using FluentValidation;
using HW.Api.DI;
using HW.Api.Logging;
using HW.Api.Middlewares;
using HW.Application.DI;
using HW.Infrastructure.AI;
using HW.Infrastructure.DI;
using Serilog;
using static HW.Infrastructure.DI.Options;

// Covers the window before configuration has been read, so a failure while building the host is
// reported instead of vanishing. Replaced by the configured logger inside AddSerilogLogging.
SerilogHostExtensions.CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    var isDev = builder.Environment.IsDevelopment();

    builder.Host.AddSerilogLogging(builder.Services);

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddValidatorsFromAssembly(HW.Application.AssemblyReference.Assembly);

    builder.Services.AddInterceptorDbContext();
    builder.Services.ConfigureMariaDbRetryOptions(builder.Configuration.GetSection(nameof(MariaDbRetryOptions)));
    builder.Services.AddMariaDbConfiguration(builder.Configuration, isDev);
    builder.Services.AddAuthentication(builder.Configuration);
    builder.Services.AddCaching(builder.Configuration);
    builder.Services.AddOrderSagaMessaging();
    builder.Services.AddMessaging(builder.Configuration);
    builder.Services.AddInfrastructureServices();
    builder.Services.AddApplicationServices();
    builder.Services.AddAiAgents(builder.Configuration);
    builder.Services.AddSwaggerDocumentation();

    var app = builder.Build();

    // First in the pipeline: everything logged after this point, by any layer, carries the id.
    app.UseMiddleware<CorrelationIdMiddleware>();

    // Then the request summary, so its stopwatch covers the whole pipeline below it.
    app.UseRequestLogging();

    app.UseSwagger();
    app.UseSwaggerUI();

    app.UseHttpsRedirection();
    app.UseCors(opt => opt.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
    app.UseAuthentication();

    // After authentication — this is the first point at which there is a user to attribute logs to.
    app.UseMiddleware<UserContextLoggingMiddleware>();

    app.UseAuthorization();
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.MapControllers();

    Log.Information("[Startup] {Application} starting in {Environment}",
        builder.Environment.ApplicationName, builder.Environment.EnvironmentName);

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // HostAbortedException is excluded deliberately: it is how the EF Core design-time tools stop
    // the host after building it, and reporting that as a crash would make every `dotnet ef`
    // command look like a failure.
    Log.Fatal(ex, "[Startup] Host terminated unexpectedly");
    throw;
}
finally
{
    // Sinks are asynchronous and batched. Without this the last events before a shutdown — which
    // include whatever caused it — are still in the buffer when the process exits.
    Log.CloseAndFlush();
}

public partial class Program { }
