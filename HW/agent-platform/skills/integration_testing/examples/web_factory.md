public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _db = new MsSqlBuilder().Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseSqlServer(_db.GetConnectionString()));

            // Only the non-deterministic parts are replaced.
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(new FixedClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            services.RemoveAll<IEventPublisher>();
            services.AddSingleton<IEventPublisher, RecordingEventPublisher>();
        });

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public new Task DisposeAsync() => _db.DisposeAsync().AsTask();
}
