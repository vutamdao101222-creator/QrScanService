using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace WebGhiHinh.Models
{
    public class Station
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        // ===== CAMERAS =====
        public int? OverviewCameraId { get; set; }
        public int? QrCameraId { get; set; }

        [ForeignKey(nameof(OverviewCameraId))]
        [JsonIgnore] // ❗ tránh vòng lặp JSON
        public Camera? OverviewCamera { get; set; }

        [ForeignKey(nameof(QrCameraId))]
        [JsonIgnore]
        public Camera? QrCamera { get; set; }

        // ===== CURRENT USER =====
        public int? CurrentUserId { get; set; }

        [ForeignKey(nameof(CurrentUserId))]
        [JsonIgnore]
        public User? CurrentUser { get; set; }
    }
}
