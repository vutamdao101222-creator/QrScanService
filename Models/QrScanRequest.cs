using WebGhiHinh.Models; // Nếu dùng chung Enum

namespace WebGhiHinh.Models
{
    public class QrScanRequest
    {
        public string? QrCode { get; set; }
        public string? StationName { get; set; }
        public string? RtspUrl { get; set; }

        // Bạn có thể dùng int hoặc Enum ScanSourceMode đều được
        public int Mode { get; set; }
    }
}