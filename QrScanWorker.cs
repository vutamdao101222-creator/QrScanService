using Microsoft.EntityFrameworkCore;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using QrScanService.Data;
using QrScanService.Models;
using System.Drawing;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ZXing;
using ZXing.Windows.Compatibility;

public class QrScanWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly JwtHelper _jwt;
    private readonly ILogger<QrScanWorker> _logger;
    private readonly IConfiguration _config;

    private readonly Dictionary<int, VideoCapture> _captures = new();
    private readonly Dictionary<int, string> _lastQrCodes = new();
    private readonly Dictionary<int, DateTime> _lastSentTimes = new();

    private List<Station> _cachedStations = new();
    private DateTime _lastDbReload = DateTime.MinValue;

    public QrScanWorker(
        IServiceScopeFactory scopeFactory,
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
        _logger.LogInformation("🚀 QR Worker Started: Bounding Box Mode");

        while (!stoppingToken.IsCancellationRequested)
        {
            if ((DateTime.Now - _lastDbReload).TotalSeconds > 10)
            {
                await ReloadStationsFromDb(stoppingToken);
            }

            var tasks = _cachedStations.Select(station => Task.Run(async () =>
            {
                await ProcessCamera(station.Id, station.Name, station.QrCamera?.RtspUrl, stoppingToken);
            }, stoppingToken));

            await Task.WhenAll(tasks);
            await Task.Delay(200, stoppingToken);
        }
    }

    private async Task ReloadStationsFromDb(CancellationToken token)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            _cachedStations = await db.Stations
                .Include(s => s.QrCamera)
                .Where(s => s.QrCamera != null && !string.IsNullOrEmpty(s.QrCamera.RtspUrl))
                .AsNoTracking()
                .ToListAsync(token);

            _lastDbReload = DateTime.Now;
        }
        catch (Exception ex)
        {
            _logger.LogError($"⚠️ DB Reload Failed: {ex.Message}");
        }
    }

    private async Task ProcessCamera(int stationId, string stationName, string? rtspUrl, CancellationToken token)
    {
        if (string.IsNullOrEmpty(rtspUrl)) return;

        try
        {
            if (!_captures.TryGetValue(stationId, out var capture) || !capture.IsOpened())
            {
                capture?.Dispose();
                capture = new VideoCapture(rtspUrl);
                _captures[stationId] = capture;
                if (!capture.IsOpened()) return;
            }

            using var mat = new Mat();
            if (!capture.Read(mat) || mat.Empty()) return;

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

                // 👇👇👇 LOGIC TÍNH TOÁN TỌA ĐỘ VẼ KHUNG 👇👇👇
                double xPct = 0, yPct = 0, wPct = 0, hPct = 0;

                var points = result.ResultPoints;
                if (points != null && points.Length > 0)
                {
                    // Tìm tọa độ min/max của các điểm góc QR
                    float minX = points.Min(p => p.X);
                    float maxX = points.Max(p => p.X);
                    float minY = points.Min(p => p.Y);
                    float maxY = points.Max(p => p.Y);

                    // Kích thước QR (theo pixel ảnh gốc)
                    float w = maxX - minX;
                    float h = maxY - minY;

                    // Chuyển sang Phần Trăm (%) so với kích thước ảnh (mat.Width/Height)
                    // Công thức: (Pixel / TổngPixel) * 100
                    xPct = (minX / mat.Width) * 100;
                    yPct = (minY / mat.Height) * 100;
                    wPct = (w / mat.Width) * 100;
                    hPct = (h / mat.Height) * 100;
                }

                // Debounce Logic
                bool isNewQr = !_lastQrCodes.ContainsKey(stationId) || _lastQrCodes[stationId] != qrText;
                bool isTimePassed = !_lastSentTimes.ContainsKey(stationId) || (DateTime.Now - _lastSentTimes[stationId]).TotalSeconds > 5;

                if (isNewQr || isTimePassed)
                {
                    _logger.LogInformation($"✅ {stationName}: {qrText} [X:{xPct:F1}% Y:{yPct:F1}%]");

                    // Gửi kèm tọa độ x, y, w, h
                    await SendToApi(qrText, stationName, xPct, yPct, wPct, hPct, token);

                    _lastQrCodes[stationId] = qrText;
                    _lastSentTimes[stationId] = DateTime.Now;
                }
            }
        }
        catch (Exception ex)
        {
            // _logger.LogWarning($"⚠️ Cam {stationName} error: {ex.Message}");
        }
    }

    // 👇 Cập nhật hàm này nhận thêm tham số tọa độ
    private async Task SendToApi(string qrText, string stationName, double x, double y, double w, double h, CancellationToken token)
    {
        try
        {
            var client = _httpFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _jwt.CreateToken());

            var payload = new
            {
                text = qrText,
                stationName = stationName,
                timestamp = DateTime.Now,
                // 👇 Gửi thêm tọa độ
                x = x,
                y = y,
                w = w,
                h = h
            };

            var apiUrl = _config["Api:Url"];
            await client.PostAsJsonAsync(apiUrl, payload, token);
        }
        catch (Exception ex)
        {
            _logger.LogError($"❌ Send API Failed: {ex.Message}");
        }
    }
}