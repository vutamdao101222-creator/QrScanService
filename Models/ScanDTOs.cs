using System;
using System.Text.Json.Serialization;

namespace WebGhiHinh.Models
{
    // ==========================================
    // 1. ENUM: Xác định nguồn quét (Mode)
    // ==========================================
    public enum ScanSourceMode
    {
        Unknown = 0,
        CameraAuto = 1, // Worker tự quét camera
        ManualApi = 2   // Gọi thủ công từ API hoặc nút bấm
    }

    // ==========================================
    // 2. CLASS: Dữ liệu gửi lên API (Request)
    // Dùng trong: RecordController (POST /api/record/scan)
    // ==========================================
    public class ScanRequest
    {
        public string? QrCode { get; set; }      // Mã QR (nếu có)
        public string? RtspUrl { get; set; }     // Đường dẫn camera
        public string? StationName { get; set; } // Tên trạm
        public ScanSourceMode Mode { get; set; } // Chế độ quét
    }

    // ==========================================
    // 3. CLASS: Kết quả xử lý Logic
    // Dùng trong: QrScanWorker (Trả về kết quả cho Controller)
    // ==========================================
    public class ScanLogicResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    // ==========================================
    // 4. CLASS: Tin nhắn SignalR (Server -> Client)
    // Dùng trong: QrScanWorker (bắn đi) và LiveCameraPage (nhận về)
    // ==========================================
    public class ScanResultMessage
    {
        public string StationName { get; set; } = ""; // Để biết vẽ lên ô nào
        public string Code { get; set; } = "";        // Nội dung QR

        // Tọa độ chuẩn hóa (từ 0.0 đến 1.0)
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
    }
}