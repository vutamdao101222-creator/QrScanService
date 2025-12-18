using Microsoft.EntityFrameworkCore;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using QrScanService.Data; // Namespace chứa AppDbContext
using System.Drawing;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ZXing;

public class QrScanWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory; // 👈 Dùng để tạo DbContext
    private readonly IHttpClientFactory _httpFactory;
    private readonly JwtHelper _jwt;
    private readonly ILogger<QrScanWorker> _logger;
    private readonly IConfiguration _config;

    // Dictionary để quản lý trạng thái quét của từng Camera (Key: StationId)
    private Dictionary<int, string> _lastQrCodes = new();
    private Dictionary<int, DateTime> _lastSentTimes = new();

    public QrScanWorker(
        IServiceScopeFactory scopeFactory, // 👈 Inject ScopeFactory
        IHttpClientFactory httpFactory,
        JwtHelper jwt,
        ILogger<QrScanWorker> logger,
        IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
        _jwt = jwt;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 QR Worker Started with Database Connection...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. Tạo Scope mới để kết nối Database
                using (var scope = _scopeFactory.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    // 2. Lấy danh sách các Trạm có cấu hình QR Camera
                    var stations = await dbContext.Stations
                        .Include(s => s.QrCamera)
                        .Where(s => s.QrCamera != null && !string.IsNullOrEmpty(s.QrCamera.RtspUrl))
                        .AsNoTracking()
                        .ToListAsync(stoppingToken);

                    if (stations.Count == 0)
                    {
                        _logger.LogWarning("⚠️ No Stations with QR Camera found in DB.");
                    }
                    else
                    {
                        // 3. Xử lý từng Camera (Có thể chạy Parallel nếu máy mạnh)
                        // Ở đây mình chạy tuần tự để test cho dễ, tránh treo máy
                        foreach (var station in stations)
                        {
                            await ProcessCamera(station.Name, station.QrCamera!.RtspUrl, station.Id, stoppingToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🔥 Error in Main Loop");
            }

            // Nghỉ 1 giây rồi quét lại danh sách DB (hoặc lâu hơn tùy nhu cầu)
            await Task.Delay(1000, stoppingToken);
        }
    }

    private async Task ProcessCamera(string stationName, string rtspUrl, int stationId, CancellationToken token)
    {
        try
        {
            // Mở luồng RTSP (Chỉ lấy 1 frame nhanh để quét)
            using var capture = new VideoCapture(rtspUrl);

            if (!capture.IsOpened())
            {
                _logger.LogWarning($"❌ Cannot connect to camera of {stationName}");
                return;
            }

            using var mat = new Mat();
            if (capture.Read(mat) && !mat.Empty())
            {
                // Đọc QR
                var reader = new BarcodeReader
                {
                    Options = new ZXing.Common.DecodingOptions
                    {
                        TryHarder = true,
                        PossibleFormats = new[] { BarcodeFormat.QR_CODE }
                    }
                };

                using var bitmap = BitmapConverter.ToBitmap(mat);
                var result = reader.Decode(bitmap);

                if (result != null && !string.IsNullOrEmpty(result.Text))
                {
                    var qrText = result.Text;

                    // Kiểm tra xem mã này đã gửi chưa (Debounce 5 giây cho mỗi trạm)
                    if (!_lastQrCodes.ContainsKey(stationId) ||
                        _lastQrCodes[stationId] != qrText ||
                        (DateTime.Now - _lastSentTimes[stationId]).TotalSeconds > 5)
                    {
                        _logger.LogInformation($"✅ {stationName} FOUND: {qrText}");

                        await SendToApi(qrText, stationName, token);

                        // Cập nhật trạng thái
                        _lastQrCodes[stationId] = qrText;
                        _lastSentTimes[stationId] = DateTime.Now;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"⚠️ Error processing {stationName}: {ex.Message}");
        }
    }

    private async Task SendToApi(string qrText, string stationName, CancellationToken token)
    {
        try
        {
            var client = _httpFactory.CreateClient();
            var jwtToken = _jwt.CreateToken();
            var apiUrl = _config["Api:Url"];

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwtToken);

            var payload = new { text = qrText, stationName = stationName, timestamp = DateTime.Now };
            var response = await client.PostAsJsonAsync(apiUrl, payload, token);

            if (!response.IsSuccessStatusCode)
                _logger.LogError($"❌ API Error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to call API");
        }
    }
}