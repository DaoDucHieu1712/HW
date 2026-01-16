using HW.Domain.Abstractions;
using HW.Domain.Abstractions.Repositories;
using HW.Infrastructure.Interceptors;
using HW.Infrastructure.Repositories;
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
        });
    }

    public static void AddInfrastructureServices(this IServiceCollection services)
    {
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
