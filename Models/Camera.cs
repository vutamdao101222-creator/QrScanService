// FILE: Models/Camera.cs
namespace QrScanService.Models
{
    public class Camera
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string RtspUrl { get; set; } = "";
        public string? Description { get; set; }
    }
}
