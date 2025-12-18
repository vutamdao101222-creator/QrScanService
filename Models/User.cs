// FILE: Models/User.cs
using System.ComponentModel.DataAnnotations;

namespace WebGhiHinh.Models
{
    public class User
    {
        public int Id { get; set; }

        [Required, MaxLength(60)]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [MaxLength(120)]
        public string FullName { get; set; } = string.Empty;

        // QR nhân viên nên encode đúng EmployeeCode này
        [MaxLength(50)]
        public string EmployeeCode { get; set; } = string.Empty;

        [MaxLength(200)]
        public string Address { get; set; } = string.Empty;

        [MaxLength(30)]
        public string Role { get; set; } = "user";
    }
}
