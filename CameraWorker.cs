using Microsoft.Extensions.Logging;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using QrScanService.Models;
using System;
using System.Collections.Concurrent;
using System.Drawing; // Lưu ý: Bitmap cần System.Drawing.Common
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
        private readonly ConcurrentQueue<Mat> _frameBuffer = new();
        private const int BUFFER_SIZE = 3;

        private readonly QRCodeDetector _opencvQr = new();
        private readonly BarcodeReader _zxing;

        private readonly ConcurrentDictionary<string, DateTime> _cooldown = new();
        private readonly ConcurrentDictionary<string, int> _voteMap = new();

        // ⚙️ Cấu hình Logic QR
        private const int EVENT_COOLDOWN_MS = 2000; // 2 giây / 1 QR logic
        private const int VOTE_THRESHOLD = 1;       // Chỉ cần 1 frame là nhận

        // 🔄 Cấu hình Re-connect (Exponential Backoff)
        private int _reconnectAttempts = 0;
        private const int INITIAL_RECONNECT_DELAY_MS = 2000; // Bắt đầu đợi 2s
        private const int MAX_RECONNECT_DELAY_MS = 30000;    // Đợi tối đa 30s

        public CameraWorker(Station station, SignalRClient signalR, ILogger logger)
        {
            _station = station;
            _signalR = signalR;
            _logger = logger;

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
            _logger.LogInformation($"[{_station.Name}] 🚀 Worker READY (OpenCV + ZXing)");
        }

        public async Task RunAsync(CancellationToken token)
        {
            // 1. Chạy luồng giải mã song song
            _ = Task.Run(() => DecodeLoopAsync(token), token);

            using var frame = new Mat();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 2. Kiểm tra trạng thái kết nối
                    // Nếu chưa khởi tạo hoặc đã bị đóng -> Thử kết nối lại
                    if (_cap == null || !_cap.IsOpened())
                    {
                        try
                        {
                            // Chỉ log warning nếu không phải lần đầu tiên chạy (để tránh spam log lúc khởi động)
                            if (_reconnectAttempts > 0)
                                _logger.LogWarning($"[{_station.Name}] 🔄 Đang thử kết nối camera... (Lần {_reconnectAttempts + 1})");

                            OpenCamera();

                            // ✅ Kết nối thành công: Reset bộ đếm
                            _reconnectAttempts = 0;
                            _logger.LogInformation($"[{_station.Name}] ✅ Kết nối thành công.");
                        }
                        catch (Exception ex)
                        {
                            _reconnectAttempts++;

                            // Tính toán thời gian đợi: min(2 * 2^(n-1), 30000)
                            // Ví dụ: 2s -> 4s -> 8s -> 16s -> 30s -> 30s...
                            int delay = Math.Min(INITIAL_RECONNECT_DELAY_MS * (int)Math.Pow(2, _reconnectAttempts - 1), MAX_RECONNECT_DELAY_MS);

                            _logger.LogError($"[{_station.Name}] ❌ Lỗi kết nối: {ex.Message}. Thử lại sau {delay / 1000} giây.");

                            // Đợi theo thời gian tính toán trước khi thử lại
                            await Task.Delay(delay, token);
                            continue; // Quay lại đầu vòng lặp while
                        }
                    }

                    // 3. Đọc dữ liệu từ luồng RTSP
                    // Nếu đọc thất bại hoặc frame rỗng
                    if (!_cap!.Read(frame) || frame.Empty())
                    {
                        _logger.LogWarning($"[{_station.Name}] ⚠️ Luồng video rỗng hoặc mất tín hiệu.");

                        // Reset để giải phóng ffmpeg buffer, ép kết nối lại ở vòng lặp sau
                        ResetCamera();

                        // Đợi cứng 2s trước khi loop lại để tránh spam CPU khi mạng chập chờn
                        await Task.Delay(2000, token);
                        continue;
                    }

                    // 4. Đẩy vào hàng đợi xử lý nếu frame tốt
                    EnqueueFrame(frame);

                    // Đợi một khoảng rất nhỏ để nhường CPU cho luồng Decode và hệ điều hành
                    await Task.Delay(5, token);
                }
                catch (Exception ex)
                {
                    // Catch các lỗi không mong muốn khác trong quá trình chạy (ví dụ lỗi bộ nhớ OpenCV)
                    _logger.LogError(ex, $"[{_station.Name}] 💥 Lỗi Capture Loop (Unexpected).");
                    ResetCamera();
                    await Task.Delay(5000, token);
                }
            }
        }

        private void OpenCamera()
        {
            // Thiết lập transport TCP để ổn định hơn UDP
            Environment.SetEnvironmentVariable("OPENCV_FFMPEG_CAPTURE_OPTIONS", "rtsp_transport;tcp");

            _cap = new VideoCapture(_station.QrCamera.RtspUrl, VideoCaptureAPIs.FFMPEG);
            _cap.Set(VideoCaptureProperties.BufferSize, 0); // Giảm độ trễ tối đa

            if (!_cap.IsOpened())
                throw new Exception("Không thể mở luồng RTSP");

            // Đọc bỏ vài frame đầu để làm nóng decoder và tránh frame rác
            for (int i = 0; i < 5; i++)
            {
                if (!_cap.Grab()) break;
            }
        }

        private void EnqueueFrame(Mat src)
        {
            // Clone frame vì Mat gốc sẽ bị ghi đè ở vòng lặp tiếp theo
            var clone = src.Clone();
            _frameBuffer.Enqueue(clone);

            // Giữ buffer size nhỏ để đảm bảo realtime, drop frame cũ nếu xử lý không kịp
            while (_frameBuffer.Count > BUFFER_SIZE)
            {
                if (_frameBuffer.TryDequeue(out var old))
                    old.Dispose();
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
                    // Luôn luôn giải phóng Mat sau khi xử lý xong
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

            // 1. Thử detect bằng OpenCV (Nhanh)
            try { text = _opencvQr.DetectAndDecode(gray, out points); } catch { }

            // 2. Nếu OpenCV thất bại, thử bằng ZXing (Mạnh hơn nhưng chậm hơn)
            if (string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    using var bmp = BitmapConverter.ToBitmap(gray);
                    var result = _zxing.Decode(bmp);

                    if (result != null)
                    {
                        text = result.Text;
                        if (result.ResultPoints?.Length > 0)
                            points = result.ResultPoints.Select(p => new Point2f(p.X, p.Y)).ToArray();
                    }
                }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(text))
                await VoteResultAsync(text, frame, points);
        }

        private async Task VoteResultAsync(string text, Mat frame, Point2f[]? points)
        {
            try
            {
                // Cơ chế Vote: Đảm bảo độ tin cậy (ở đây threshold = 1 tức là tin ngay lập tức)
                int count = _voteMap.AddOrUpdate(text, 1, (_, v) => v + 1);
                if (count < VOTE_THRESHOLD) return;

                double px = 0, py = 0, pw = 0, ph = 0;

                // Tính toán tọa độ hiển thị lên Dashboard
                if (points != null && points.Length >= 2)
                {
                    float minX = points.Min(p => p.X);
                    float minY = points.Min(p => p.Y);
                    float maxX = points.Max(p => p.X);
                    float maxY = points.Max(p => p.Y);

                    minX = Math.Max(0, minX);
                    minY = Math.Max(0, minY);
                    maxX = Math.Min(frame.Width, maxX);
                    maxY = Math.Min(frame.Height, maxY);

                    px = (minX / frame.Width) * 100.0;
                    py = (minY / frame.Height) * 100.0;
                    pw = ((maxX - minX) / frame.Width) * 100.0;
                    ph = ((maxY - minY) / frame.Height) * 100.0;

                    // Xử lý tỷ lệ khung hình (Aspect Ratio Correction)
                    double aspectSrc = (double)frame.Width / frame.Height;
                    double aspectDst = 16.0 / 9.0;

                    if (aspectDst > aspectSrc)
                    {
                        double fix = aspectDst / aspectSrc;
                        py /= fix;
                        ph /= fix;
                    }
                    else
                    {
                        double fix = aspectSrc / aspectDst;
                        px /= fix;
                        pw /= fix;
                    }

                    // Auto margin logic
                    double areaPercent = (pw * ph) / 100.0;
                    double margin;

                    if (areaPercent < 1.5) margin = 0.35;
                    else if (areaPercent < 4) margin = 0.25;
                    else margin = 0.12;

                    px = Math.Max(0, px - pw * margin);
                    py = Math.Max(0, py - ph * margin);
                    pw = Math.Min(100, pw * (1 + margin * 2));
                    ph = Math.Min(100, ph * (1 + margin * 2));
                }

                _logger.LogInformation($"[{_station.Name}] 🎯 DRAW: {text} | X:{px:F1}% Y:{py:F1}% W:{pw:F1}% H:{ph:F1}%");

                // Gửi về FE để vẽ
                await _signalR.SendScanResultAsync(_station.Name, text, px, py, pw, ph);

                // Kiểm tra Cooldown để tránh gửi logic trùng lặp
                if (_cooldown.TryGetValue(text, out var last) &&
                    (DateTime.Now - last).TotalMilliseconds < EVENT_COOLDOWN_MS)
                    return;

                _cooldown[text] = DateTime.Now;
                _voteMap.Clear();

                // Gửi Logic lên Cloud/Server
                _logger.LogInformation($"[{_station.Name}] 🚀 Gửi tín hiệu xử lý Logic: {text}");
                bool isSent = await _signalR.SendScanToCloudAsync(_station.Name, text, px, py, pw, ph);

                if (isSent)
                    _logger.LogInformation($"[{_station.Name}] 📤 Gửi Logic thành công.");
                else
                    _logger.LogError($"[{_station.Name}] ❌ Gửi Logic thất bại.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{_station.Name}] ❌ Lỗi VoteResultAsync: {ex.Message}");
            }
        }

        private void ResetCamera()
        {
            if (_cap != null)
            {
                try
                {
                    _cap.Release();
                    // Dispose() giải phóng object wrapper C#
                    _cap.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Lỗi khi Dispose Camera: {ex.Message}");
                }
                finally
                {
                    _cap = null;
                }
            }
        }
    }
}