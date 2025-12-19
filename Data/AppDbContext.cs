using Microsoft.EntityFrameworkCore;
using QrScanService.Models; // 👈 Đổi thành namespace chứa Model của Worker

namespace QrScanService.Data // 👈 Đổi namespace cho khớp với Project Worker
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // Các bảng giữ nguyên để khớp với Database
        public DbSet<Station> Stations => Set<Station>();
        public DbSet<Camera> Cameras => Set<Camera>();
        public DbSet<VideoLog> VideoLogs => Set<VideoLog>();
        public DbSet<User> Users => Set<User>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Cấu hình mối quan hệ (Copy y nguyên từ Web sang)
            modelBuilder.Entity<Station>()
                .HasOne(s => s.OverviewCamera)
                .WithMany()
                .HasForeignKey(s => s.OverviewCameraId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Station>()
                .HasOne(s => s.QrCamera)
                .WithMany()
                .HasForeignKey(s => s.QrCameraId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}