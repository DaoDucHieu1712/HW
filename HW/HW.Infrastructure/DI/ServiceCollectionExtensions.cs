using HW.Domain.Abstractions;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;
using HW.Infrastructure.Interceptors;
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
    public static void AddMariaDbConfiguration(this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        services.AddDbContext<ApplicationDbContext>((provider, builder) =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            var auditableInterceptor = provider.GetService<AuditableEntitiesInterceptor>();
            var options = provider.GetRequiredService<IOptionsMonitor<MariaDbRetryOptions>>();

            builder
               .EnableDetailedErrors(isDevelopment)
               .EnableSensitiveDataLogging(isDevelopment)
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
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequiredLength = 3;
            options.Password.RequiredUniqueChars = 1;

            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;

            options.User.AllowedUserNameCharacters =
                "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
            options.User.RequireUniqueEmail = true;

            options.SignIn.RequireConfirmedEmail = true;
            options.SignIn.RequireConfirmedPhoneNumber = false;
        });
    }

    public static void AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped(typeof(IEFRepository<>), typeof(EFRepository<>));
        services.AddScoped<IUnitOfWork, EFUnitOfWork>();
        services.AddHostedService<Outbox.OutboxMessageProcessor>();
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
