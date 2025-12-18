namespace WebGhiHinh.Shared.DTOs
{
    public class CameraMiniDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? RtspUrl { get; set; }
        public string? StreamUrl { get; set; }
    }
}
