# Skill: ef-dapper-setup

MariaDB/Pomelo configuration, retry strategy, migrations, and Dapper query setup.

---

## MariaDB DI config — `HW.Infrastructure/DI/ServiceCollectionExtensions.cs`

```csharp
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
                mySqlOptionsAction: o => o
                    .ExecutionStrategy(deps => new MySqlRetryingExecutionStrategy(
                        deps,
                        options.CurrentValue.MaxRetryCount,
                        options.CurrentValue.MaxRetryDelay,
                        options.CurrentValue.ErrorNumbersToAdd))
                    .MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name))
            .AddInterceptors(auditableInterceptor);
    });
}
```

## Retry options — `HW.Infrastructure/DI/Options.cs`
```csharp
using System.ComponentModel.DataAnnotations;

namespace HW.Infrastructure.DI;

public static class Options
{
    public class MariaDbRetryOptions
    {
        [Required] public int MaxRetryCount { get; set; } = 5;
        [Required] public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
        public ICollection<int>? ErrorNumbersToAdd { get; set; }
    }
}
```

## appsettings.json sections
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "server=localhost,3306;database=hw;User=root;Password="
  },
  "MariaDbRetryOptions": {
    "MaxRetryCount": 5,
    "MaxRetryDelay": "00:00:05",
    "ErrorNumbersToAdd": []
  }
}
```

## Program.cs wiring
```csharp
builder.Services.AddInterceptorDbContext();
builder.Services.ConfigureMariaDbRetryOptions(
    builder.Configuration.GetSection(nameof(MariaDbRetryOptions)));
builder.Services.AddMariaDbConfiguration(builder.Configuration, builder.Environment.IsDevelopment());
```

## EF Core migration commands (from solution root)
```bash
# Create migration
dotnet ef migrations add <MigrationName> --project HW.Infrastructure --startup-project HW.Api

# Apply to DB
dotnet ef database update --project HW.Infrastructure --startup-project HW.Api

# Undo last migration (before applying)
dotnet ef migrations remove --project HW.Infrastructure --startup-project HW.Api

# Rollback to specific migration
dotnet ef database update <PreviousMigrationName> --project HW.Infrastructure --startup-project HW.Api

# Generate SQL script
dotnet ef migrations script --project HW.Infrastructure --startup-project HW.Api -o migration.sql

# List applied migrations
dotnet ef migrations list --project HW.Infrastructure --startup-project HW.Api
```

## Dapper for read-heavy queries (optional)

Add `Dapper` package to `HW.Infrastructure`:
```bash
dotnet add HW.Infrastructure package Dapper
```

Create query service alongside EF repositories:
```csharp
// HW.Infrastructure/QueryServices/{Feature}QueryService.cs
using Dapper;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace HW.Infrastructure.QueryServices;

public interface I{Feature}QueryService
{
    Task<IEnumerable<{Feature}SummaryDto>> GetSummariesAsync();
}

public class {Feature}QueryService : I{Feature}QueryService
{
    private readonly string _connectionString;

    public {Feature}QueryService(IConfiguration configuration)
        => _connectionString = configuration.GetConnectionString("DefaultConnection")!;

    public async Task<IEnumerable<{Feature}SummaryDto>> GetSummariesAsync()
    {
        using var conn = new MySqlConnection(_connectionString);
        const string sql = """
            SELECT id, name, created_at
            FROM {feature}s
            WHERE is_delete = 0
            ORDER BY created_at DESC
            """;
        return await conn.QueryAsync<{Feature}SummaryDto>(sql);
    }
}
```

Register in `AddInfrastructureServices()`:
```csharp
services.AddScoped<I{Feature}QueryService, {Feature}QueryService>();
```

## snake_case column naming (optional)

To use snake_case column names globally, override `OnModelCreating` in `ApplicationDbContext`:
```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    foreach (var entity in modelBuilder.Model.GetEntityTypes())
    {
        // Table name: snake_case
        entity.SetTableName(entity.GetTableName()?.ToSnakeCase());

        // Column names: snake_case
        foreach (var prop in entity.GetProperties())
            prop.SetColumnName(prop.GetColumnName().ToSnakeCase());

        // Keys and indexes
        foreach (var key in entity.GetKeys())
            key.SetName(key.GetName()?.ToSnakeCase());

        foreach (var fk in entity.GetForeignKeys())
            fk.SetConstraintName(fk.GetConstraintName()?.ToSnakeCase());
    }
}
```

Add the extension:
```csharp
public static class StringExtensions
{
    public static string ToSnakeCase(this string str)
        => string.Concat(str.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? "_" + char.ToLower(c) : char.ToLower(c).ToString()));
}
```
