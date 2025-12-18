// ===============================================
// FILE: Models/Dto/ForceReleaseRequest.cs
// ===============================================
using System.ComponentModel.DataAnnotations;

namespace WebGhiHinh.Models.Dto
{
    public class ForceReleaseRequest
    {
        [Required]
        [Range(1, int.MaxValue)]
        public int StationId { get; set; }
    }
}
