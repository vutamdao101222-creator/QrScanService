using WebGhiHinh.Models;

namespace QrScanService.Models // 👈 Namespace của Worker
{
    public class Station
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        public int? OverviewCameraId { get; set; }
        public Camera? OverviewCamera { get; set; }

        public int? QrCameraId { get; set; }
        public Camera? QrCamera { get; set; }

        public int? CurrentUserId { get; set; }
        public User? CurrentUser { get; set; }
    }
}