using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using QrScanService.Models;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZXing;
using ZXing.Windows.Compatibility;

namespace QrScanService
{
    public class CameraWorker
    {
        private readonly Station _station;
        private readonly SignalRClient _signalR;
        private readonly ILogger _logger;

        private VideoCapture? _cap;

        // ===== BUFFER =====
        private readonly ConcurrentQueue<Mat> _frameBuffer = new();
        private const int BUFFER_SIZE = 3;

        // ===== DECODE =====
        private readonly QRCodeDetector _opencvQr = new();
        private readonly BarcodeReader _zxing;

        // ===== CONTROL =====
        private readonly ConcurrentDictionary<string, DateTime> _cooldown = new();
        private readonly ConcurrentDictionary<string, int> _voteMap = new();

        private const int EVENT_COOLDOWN_MS = 3000;
        private const int VOTE_THRESHOLD = 2;

        private readonly string _debugDir;

        public CameraWorker(Station station, SignalRClient signalR, ILogger logger)
        {
            _station = station;
            _signalR = signalR;
            _logger = logger;

            // Tạo đường dẫn lưu ảnh debug (Sẽ được xử lý tên folder ở hàm SaveDebug)
            _debugDir = "debug";

            _zxing = new BarcodeReader
            {
                AutoRotate = true,
                Options = new ZXing.Common.DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = new[] { BarcodeFormat.QR_CODE }
                }
            };

            Cv2.SetLogLevel(0);
            _logger.LogInformation($"[{_station.Name}] 🚀 OpenCV QR Multi-frame READY");
        }

        public async Task RunAsync(CancellationToken token)
        {
            _ = Task.Run(() => DecodeLoopAsync(token), token);

            using var frame = new Mat();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_cap == null || !_cap.IsOpened())
                    {
                        try { OpenCamera(); }
                        catch
                        {
                            await Task.Delay(5000, token);
                            continue;
                        }
                    }

                    if (!_cap!.Read(frame) || frame.Empty())
                    {
                        _logger.LogWarning($"[{_station.Name}] ⚠️ Mất tín hiệu.");
                        ResetCamera();
                        await Task.Delay(1000, token);
                        continue;
                    }

                    EnqueueFrame(frame);
                    await Task.Delay(30, token);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[{_station.Name}] Lỗi Capture Loop.");
                    ResetCamera();
                    await Task.Delay(2000, token);
                }
            }
        }

        private void OpenCamera()
        {
            Environment.SetEnvironmentVariable("OPENCV_FFMPEG_CAPTURE_OPTIONS", "rtsp_transport;tcp");
            _cap = new VideoCapture(_station.QrCamera.RtspUrl, VideoCaptureAPIs.FFMPEG);
            _cap.Set(VideoCaptureProperties.BufferSize, 0);

            if (!_cap.IsOpened()) throw new Exception("Connect Fail");

            for (int i = 0; i < 5; i++) _cap.Grab();
            _logger.LogInformation($"[{_station.Name}] 🎥 Camera connected.");
        }

        private void EnqueueFrame(Mat src)
        {
            var clone = src.Clone();
            _frameBuffer.Enqueue(clone);
            while (_frameBuffer.Count > BUFFER_SIZE)
            {
                if (_frameBuffer.TryDequeue(out var old)) old.Dispose();
            }
        }

        private async Task DecodeLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (!_frameBuffer.TryDequeue(out var frame))
                {
                    await Task.Delay(10, token);
                    continue;
                }

                try
                {
                    await ProcessFrameAsync(frame);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[{_station.Name}] Lỗi Decode: {ex.Message}");
                }
                finally
                {
                    frame.Dispose();
                }
            }
        }

        private async Task ProcessFrameAsync(Mat frame)
        {
            using var gray = new Mat();
            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);

            string text = "";
            Point2f[]? points = null;

            // 1. OpenCV
            try { text = _opencvQr.DetectAndDecode(gray, out points); } catch { }

            // 2. ZXing
            if (string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    using var bmp = BitmapConverter.ToBitmap(gray);
                    var result = _zxing.Decode(bmp);

                    if (result != null)
                    {
                        text = result.Text;
                        if (result.ResultPoints != null && result.ResultPoints.Length > 0)
                        {
                            points = result.ResultPoints.Select(p => new Point2f(p.X, p.Y)).ToArray();
                        }
                    }
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                await VoteResultAsync(text, frame, points);
            }
        }

        private async Task VoteResultAsync(string text, Mat frame, Point2f[]? points)
        {
            int count = _voteMap.AddOrUpdate(text, 1, (_, v) => v + 1);
            _logger.LogInformation($"[{_station.Name}] 👁 Phát hiện: {text} (Vote: {count}/{VOTE_THRESHOLD})");

            if (count < VOTE_THRESHOLD) return;

            if (_cooldown.TryGetValue(text, out var last) && (DateTime.Now - last).TotalMilliseconds < EVENT_COOLDOWN_MS)
                return;

            _cooldown[text] = DateTime.Now;
            _voteMap.Clear();

            // Tính toán tọa độ
            double px = 0, py = 0, pw = 0, ph = 0;
            if (points != null && points.Length >= 2)
            {
                float minX = points.Min(p => p.X);
                float minY = points.Min(p => p.Y);
                float maxX = points.Max(p => p.X);
                float maxY = points.Max(p => p.Y);

                if (minX < 0) minX = 0; if (minY < 0) minY = 0;

                px = (minX / frame.Width) * 100.0;
                py = (minY / frame.Height) * 100.0;
                pw = ((maxX - minX) / frame.Width) * 100.0;
                ph = ((maxY - minY) / frame.Height) * 100.0;
            }

            DrawOverlay(frame, text, points);
            SaveDebug(frame, "scan_success");

            _logger.LogInformation($"[{_station.Name}] ✅ KẾT QUẢ: {text} | Box: {px:F1}% {py:F1}%");

            try
            {
                // 1. Gửi visual (Khung xanh) - Cái này cho vui, không cần check kỹ
                await _signalR.SendScanResultAsync(_station.Name, text, px, py, pw, ph);

                // 2. 🔥 Gửi logic (Quan trọng) - SỬA ĐOẠN NÀY
                bool isSent = await _signalR.SendScanToCloudAsync(_station.Name, text, px, py, pw, ph);

                if (isSent)
                {
                    // Chỉ hiện dòng này khi thực sự gửi được
                    _logger.LogInformation($"[{_station.Name}] 📤 GỬI THÀNH CÔNG lên WebGhiHinh.");
                }
                else
                {
                    // Nếu không gửi được -> Báo động đỏ
                    _logger.LogError($"[{_station.Name}] ❌ GỬI THẤT BẠI! Kiểm tra lại IP/Port trong appsettings.json");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{_station.Name}] ❌ Worker Error: {ex.Message}");
            }
        }

        private void DrawOverlay(Mat frame, string text, Point2f[]? pts)
        {
            if (pts != null && pts.Length == 4)
            {
                for (int i = 0; i < 4; i++)
                    Cv2.Line(frame, (Point)pts[i], (Point)pts[(i + 1) % 4], Scalar.Lime, 2);
            }
            Cv2.PutText(frame, text, new Point(20, 50), HersheyFonts.HersheySimplex, 1, Scalar.Red, 2);
        }

        // 🔥 FIX LỖI IMWRITE (Màu đỏ trong log cũ của bạn)
        private void SaveDebug(Mat img, string tag)
        {
            // Kiểm tra ảnh rỗng thì bỏ qua
            if (img.Empty()) return;

            // 🔥 BƯỚC 1: CLONE ẢNH NGAY LẬP TỨC (Trên luồng chính)
            // Việc này tạo ra vùng nhớ mới, tách biệt hoàn toàn với biến 'img' sắp bị Dispose bên ngoài.
            Mat clone = img.Clone();

            // Chạy task riêng để ghi đĩa (không làm lag camera)
            _ = Task.Run(() =>
            {
                try
                {
                    // Làm sạch tên máy (Loại bỏ dấu tiếng Việt để tạo folder)
                    string safeName = string.Join("", _station.Name.Where(c => char.IsLetterOrDigit(c)));

                    string dir = Path.Combine(_debugDir, safeName);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    string path = Path.Combine(dir, $"{DateTime.Now:HHmmss}_{tag}.jpg");

                    // 🔥 BƯỚC 2: Dùng bản CLONE để xử lý
                    bool success = Cv2.ImEncode(".jpg", clone, out byte[] buf);
                    if (success)
                    {
                        File.WriteAllBytes(path, buf);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SaveDebug Error: {ex.Message}");
                }
                finally
                {
                    // 🔥 BƯỚC 3: QUAN TRỌNG - GIẢI PHÓNG BẢN CLONE
                    // Vì mình Clone thủ công nên phải Dispose thủ công khi dùng xong
                    clone.Dispose();
                }
            });
        }

        private void ResetCamera()
        {
            if (_cap != null) { _cap.Release(); _cap.Dispose(); _cap = null; }
        }
    }
}