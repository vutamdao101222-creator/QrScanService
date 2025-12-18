using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebGhiHinh.Models;

namespace WebGhiHinh.Models
{
    public class VideoLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string QrCode { get; set; } = string.Empty; // Mã sản phẩm

        [Required]
        public string FilePath { get; set; } = string.Empty; // Đường dẫn file .mp4

        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime? EndTime { get; set; }

        // Lưu cứng tên người/trạm tại thời điểm ghi (phòng khi user bị xóa sau này)
        public string? StationName { get; set; }
        public string? RecordedBy { get; set; }

        // Khóa ngoại: Video này quay từ Camera nào
        public int? CameraId { get; set; }
        [ForeignKey("CameraId")]
        public Camera? Camera { get; set; }
    }
}