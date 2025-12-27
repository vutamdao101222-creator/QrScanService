using Microsoft.EntityFrameworkCore;
using QrScanService;
using QrScanService.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "QrScanService";
    })
    .ConfigureServices((context, services) =>
    {
        // 1. Cấu hình DB
        var connectionString = context.Configuration.GetConnectionString("DefaultConnection");
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString), ServiceLifetime.Scoped);

        // 2. 👇 SỬA LỖI BREAK TẠI ĐÂY
        // Thay vì: services.AddSingleton<SignalRClient>();
        // Hãy dùng cách này để truyền tham số string hubUrl:
        services.AddSingleton<SignalRClient>(provider =>
        {
            // Lấy Logger từ hệ thống
            var logger = provider.GetRequiredService<ILogger<SignalRClient>>();

            // Lấy URL từ appsettings.json (Nếu chưa có thì dùng mặc định)
            string hubUrl = context.Configuration["SignalRUrl"] ?? "http://192.168.1.48/scanHub";

            // Khởi tạo class thủ công với đủ tham số
            return new SignalRClient(logger, hubUrl);
        });

        // 3. Các dịch vụ khác
        services.AddHttpClient();

        // Nếu bạn có class JwtHelper, hãy đảm bảo nó không yêu cầu tham số lạ trong Constructor
        // services.AddSingleton<JwtHelper>(); 

        // 4. Đăng ký Worker
        // Lưu ý: Nếu QrScanWorker không kế thừa BackgroundService, dòng này sẽ lỗi
        // Giả sử bạn đã có class CameraWorkerWrapper như hướng dẫn trước:
        services.AddHostedService<QrScanWorker>();
    })
    .Build(); // ✅ Bây giờ dòng này sẽ chạy qua được

await host.RunAsync();