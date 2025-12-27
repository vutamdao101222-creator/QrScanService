using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace QrScanService
{
    public class SignalRClient
    {
        private readonly HubConnection _hubConnection;
        private readonly ILogger<SignalRClient> _logger;

        // 👇 SỬA CONSTRUCTOR: Nhận thêm string hubUrl
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

            // Tự động kết nối ngay khi khởi tạo
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
                catch { /* Bỏ qua lỗi connection ban đầu */ }
            }
        }

        // 👇 Hàm gửi thông tin quét về Server (Server sẽ tự xử lý logic Ghi hình/Login)
        // File: QrScanService/SignalRClient.cs

        // 👇 Đổi từ Task sang Task<bool>
        public async Task<bool> SendScanToCloudAsync(string station, string code, double x, double y, double w, double h)
        {
            // Kiểm tra kết nối trước
            if (_hubConnection.State != HubConnectionState.Connected)
            {
                _logger.LogWarning($"⚠️ SignalR chưa kết nối! (Status: {_hubConnection.State})");
                return false; // Báo thất bại
            }

            try
            {
                await _hubConnection.SendAsync("PushScanResult", station, code, x, y, w, h);
                return true; // Gửi thành công
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Lỗi gửi SignalR: {ex.Message}");
                return false; // Báo thất bại do lỗi mạng
            }
        }

        // Hàm này để vẽ khung xanh (giữ nguyên nếu cần)
        public async Task SendScanResultAsync(string station, string code, double x, double y, double w, double h)
        {
            if (_hubConnection.State == HubConnectionState.Connected)
            {
                // Gọi hàm vẽ khung xanh (Visual only)
                await _hubConnection.SendAsync("ScanResult", station, code, x, y, w, h);
            }
        }
    }
}