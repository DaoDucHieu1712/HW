using HW.Domain.Abstractions;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Infrastructure.Interceptors;
using HW.Infrastructure.MultiTenant;
using HW.Infrastructure.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static HW.Infrastructure.DI.Options;

namespace HW.Infrastructure.DI;

public static class ServiceCollectionExtensions
{
    public static void AddMariaDbConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>((provider, builder) =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            var auditableInterceptor = provider.GetService<AuditableEntitiesInterceptor>();

            var options = provider.GetRequiredService<IOptionsMonitor<MariaDbRetryOptions>>();

            builder
               .EnableDetailedErrors(true)
               .EnableSensitiveDataLogging(true)
               .UseLazyLoadingProxies(true)
               .UseMySql(
                    connectionString: connectionString,
                    ServerVersion.AutoDetect(connectionString),
                    mySqlOptionsAction: optionsBuilder
                        => optionsBuilder.ExecutionStrategy(
                                dependencies => new MySqlRetryingExecutionStrategy(
                                    dependencies: dependencies,
                                    maxRetryCount: options.CurrentValue.MaxRetryCount,
                                    maxRetryDelay: options.CurrentValue.MaxRetryDelay,
                                    errorNumbersToAdd: options.CurrentValue.ErrorNumbersToAdd))
                            .MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name))
            .AddInterceptors(auditableInterceptor);
        }).AddIdentity<AppUser, AppRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<IdentityOptions>(options =>
        {
            // Thiết lập về Password
            options.Password.RequireDigit = false; // Không bắt phải có số
            options.Password.RequireLowercase = false; // Không bắt phải có chữ thường
            options.Password.RequireNonAlphanumeric = false; // Không bắt ký tự đặc biệt
            options.Password.RequireUppercase = false; // Không bắt buộc chữ in
            options.Password.RequiredLength = 3; // Số ký tự tối thiểu của password
            options.Password.RequiredUniqueChars = 1; // Số ký tự riêng biệt

            // Cấu hình Lockout - khóa user
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(0); // Khóa 5 phút
            options.Lockout.MaxFailedAccessAttempts = 10000; // Thất bại 5 lầ thì khóa
            options.Lockout.AllowedForNewUsers = true;

            // Cấu hình về User.
            options.User.AllowedUserNameCharacters = // các ký tự đặt tên user
                "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
            options.User.RequireUniqueEmail = true;  // Email là duy nhất

            // Cấu hình đăng nhập.
            options.SignIn.RequireConfirmedEmail = true;            // Cấu hình xác thực địa chỉ email (email phải tồn tại)
            options.SignIn.RequireConfirmedPhoneNumber = false;     // Xác thực số điện thoại
        });

    }

    public static void AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddScoped<UserInfo>();
        services.AddScoped(typeof(IEFRepository<>), typeof(EFRepository<>));
        services.AddScoped<IUnitOfWork, EFUnitOfWork>();
    }

    public static void AddInterceptorDbContext(this IServiceCollection services)
    {
        services.AddSingleton<AuditableEntitiesInterceptor>();
    }

    public static OptionsBuilder<MariaDbRetryOptions> ConfigureMariaDbRetryOptions(this IServiceCollection services, IConfigurationSection section)
            => services
                .AddOptions<MariaDbRetryOptions>()
                .Bind(section)
                .ValidateDataAnnotations()
                .ValidateOnStart();
}
