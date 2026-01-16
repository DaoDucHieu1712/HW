using HW.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HW.Application.DI;

public static class ServiceCollectionExtensions
{
    public static void AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IBlogService, BlogService>();
    }
}
