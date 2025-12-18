using Microsoft.EntityFrameworkCore; // 👈 Nhớ thêm dòng này
using QrScanService.Data; // 👈 Namespace chứa AppDbContext của bạn

IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "QrScanService";
    })
    .ConfigureServices((context, services) =>
    {
        // 1. Lấy ConnectionString từ appsettings.json
        var connectionString = context.Configuration.GetConnectionString("DefaultConnection");

        // 2. Đăng ký AppDbContext (Lưu ý: Worker dùng Singleton, nên DB phải xử lý khéo ở bước sau)
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddHttpClient();
        services.AddSingleton<JwtHelper>();
        services.AddHostedService<QrScanWorker>();
    })
    .Build();

await host.RunAsync();