using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using QrScanService.Data;
using QrScanService.Models;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QrScanService
{
    public class QrScanWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<QrScanWorker> _logger;
        private readonly SignalRClient _signalR;
        private readonly ConcurrentDictionary<int, CameraWorker> _workers = new();

        public QrScanWorker(IServiceScopeFactory scopeFactory, ILogger<QrScanWorker> logger, SignalRClient signalR)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _signalR = signalR;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Ép OpenCV sử dụng TCP để luồng video RTSP ổn định, không bị vỡ hình gây lỗi Empty Frame
            Environment.SetEnvironmentVariable("OPENCV_FFMPEG_CAPTURE_OPTIONS", "rtsp_transport;tcp");

            _logger.LogInformation("🚀 QR SCAN SERVICE STARTED (YOLOv8 + ZXing)");

            try { await _signalR.ConnectAsync(); }
            catch (Exception ex) { _logger.LogError($"❌ SignalR Connection Error: {ex.Message}"); }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var stations = await db.Stations
                        .Include(x => x.QrCamera)
                        .Where(x => x.QrCamera != null && !string.IsNullOrEmpty(x.QrCamera.RtspUrl))
                        .AsNoTracking()
                        .ToListAsync(stoppingToken);

                    foreach (var s in stations)
                    {
                        if (_workers.ContainsKey(s.Id)) continue;

                        _logger.LogInformation($"🆕 New Camera Found: [{s.Name}] - {s.QrCamera.RtspUrl}");
                        var worker = new CameraWorker(s, _signalR, _logger);

                        if (_workers.TryAdd(s.Id, worker))
                        {
                            _ = Task.Run(async () => {
                                try { await worker.RunAsync(stoppingToken); }
                                catch (Exception ex) { _logger.LogError($"🔥 Worker Error [{s.Name}]: {ex.Message}"); }
                                finally { _workers.TryRemove(s.Id, out _); }
                            }, stoppingToken);
                        }
                    }
                }
                catch (Exception ex) { _logger.LogError(ex, "⚠️ Manager Loop Error"); }

                await Task.Delay(10000, stoppingToken);
            }
        }
    }
}