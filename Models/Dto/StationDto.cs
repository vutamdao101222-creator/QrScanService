namespace WebGhiHinh.DTOs
{
    // ==========================================
    // 1. DTOs HIỂN THỊ DỮ LIỆU (VIEW MODELS)
    // ==========================================

    // Class đại diện cho thông tin rút gọn của Camera
    public sealed class CameraMiniDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string RtspUrl { get; set; } = "";
        public string? Description { get; set; }
    }

    // Class đại diện cho thông tin Trạm (Station)
    public sealed class StationDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        public int? CurrentUserId { get; set; }
        public string? CurrentUsername { get; set; }

        public int? OverviewCameraId { get; set; }
        public CameraMiniDto? OverviewCamera { get; set; }

        public int? QrCameraId { get; set; }
        public CameraMiniDto? QrCamera { get; set; }
    }

    // ==========================================
    // 2. DTOs NHẬN DỮ LIỆU TỪ CLIENT (REQUEST MODELS)
    // ==========================================

    // 👇 Class này bị thiếu -> Gây lỗi ở hàm CreateStation
    public sealed class CreateStationRequest
    {
        public string Name { get; set; } = "";
    }

    // Class dùng để gửi request cập nhật Camera cho Trạm
    public sealed class SetStationCamerasRequest
    {
        public int StationId { get; set; }
        public int? OverviewCameraId { get; set; }
        public int? QrCameraId { get; set; }
    }

    // 👇 Class này bị thiếu -> Gây lỗi ở hàm Occupy và Release
    public sealed class StationActionDto
    {
        public int StationId { get; set; }
    }

    // 👇 Class này bị thiếu -> Gây lỗi ở hàm ForceRelease
    public sealed class ForceReleaseRequest
    {
        public int StationId { get; set; }
    }
}