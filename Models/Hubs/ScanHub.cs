using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;

namespace WebGhiHinh.Hubs
{
    public class ScanHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
            // Console.WriteLine($"[ScanHub] Connected: {Context.ConnectionId}");
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await base.OnDisconnectedAsync(exception);
        }

        // ================================
        // 🔥 SERVER → CLIENT (SCAN RESULT)
        // ================================
        public async Task PushScanResult(
            string station,
            string code,
            int x,
            int y,
            int w,
            int h
        )
        {
            await Clients.All.SendAsync("ScanResult", new
            {
                station,
                code,
                x,
                y,
                w,
                h
            });
        }

        // Optional: log ngược từ client
        public async Task SendLog(string message)
        {
            await Clients.All.SendAsync("ReceiveLog", message);
        }
    }
}
