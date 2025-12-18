// ===============================
// FILE: Data/AppDbContext.cs  (chỉ phần liên quan)
// ===============================
using Microsoft.EntityFrameworkCore;
using WebGhiHinh.Models;

namespace WebGhiHinh.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Station> Stations => Set<Station>();
        public DbSet<Camera> Cameras => Set<Camera>();
        public DbSet<VideoLog> VideoLogs => Set<VideoLog>();
        public DbSet<User> Users => Set<User>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

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
