using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace QrScanService
{
    public class SignalRClient
    {
        private readonly HubConnection _hubConnection;
        private readonly ILogger<SignalRClient> _logger;

        public SignalRClient(ILogger<SignalRClient> logger, string hubUrl)
        {
            _logger = logger;

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.Closed += async (error) =>
            {
                await Task.Delay(2000);
                await ConnectAsync();
            };

            _ = ConnectAsync();
        }

        public async Task ConnectAsync()
        {
            if (_hubConnection.State == HubConnectionState.Disconnected)
            {
                try
                {
                    await _hubConnection.StartAsync();
                    _logger.LogInformation("✅ SignalR Connected!");
                }
                catch { }
            }
        }

        // Gửi Logic (Login/Ghi hình) -> Gọi PushScanResult
        public async Task<bool> SendScanToCloudAsync(string station, string code, double x, double y, double w, double h)
        {
            if (_hubConnection.State != HubConnectionState.Connected) return false;

            try
            {
                await _hubConnection.SendAsync("PushScanResult", station, code, x, y, w, h);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Lỗi gửi SignalR: {ex.Message}");
                return false;
            }
        }

        // Gửi Visual (Vẽ khung) -> Gọi PushVisual (ĐÃ SỬA)
        public async Task SendScanResultAsync(string station, string code, double x, double y, double w, double h)
        {
            if (_hubConnection.State == HubConnectionState.Connected)
            {
                // 🔥 SỬA: Gọi đúng tên hàm "PushVisual" trên Hub
                await _hubConnection.SendAsync("PushVisual", station, code, x, y, w, h);
            }
        }
    }
}