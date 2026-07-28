# Phần 9 — Kiến trúc, Testing & DevOps

[⬅️ Phần 8](interview.NET.08-EFCore.md) | [Mục lục](interview.NET.md) | [Phần 10 — SQL ➡️](interview.NET.10-SQL.md)

---

## 91. Clean Architecture / Onion Architecture là gì?

```
┌─────────────────────────────────────┐
│  Presentation (API, UI)              │  ← ngoài cùng
│  ┌───────────────────────────────┐  │
│  │  Infrastructure (EF, HTTP)    │  │
│  │  ┌─────────────────────────┐  │  │
│  │  │  Application (UseCases)  │  │  │
│  │  │  ┌───────────────────┐  │  │  │
│  │  │  │  Domain (Entities) │  │  │  │  ← core, không phụ thuộc gì
│  │  │  └───────────────────┘  │  │  │
│  │  └─────────────────────────┘  │  │
│  └───────────────────────────────┘  │
└─────────────────────────────────────┘
Dependency hướng VÀO TRONG →
```

```csharp
// Domain: thuần business, không reference Infrastructure
namespace Domain;
public class Order { public void Confirm() { /* business rule */ } }
public interface IOrderRepository { Task SaveAsync(Order o); } // interface ở Domain

// Infrastructure: implement interface của Domain
namespace Infrastructure;
public class EfOrderRepository : IOrderRepository // phụ thuộc vào trong
{
    public async Task SaveAsync(Order o) { /* EF Core */ }
}
```

- **Lợi ích**: Domain dễ test, dễ thay công nghệ hạ tầng (đổi DB/ORM không ảnh hưởng core).

---

## 92. CQRS là gì? Khi nào nên dùng?

```csharp
// Tách WRITE (Command) và READ (Query) thành model/handler riêng

// Command - thay đổi state
public record CreateOrderCommand(int ProductId, int Qty) : IRequest<int>;
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, int>
{
    public async Task<int> Handle(CreateOrderCommand cmd, CancellationToken ct)
    { /* validate + ghi DB qua domain model */ return 1; }
}

// Query - chỉ đọc (có thể query trực tiếp, projection, tối ưu riêng)
public record GetOrderQuery(int Id) : IRequest<OrderDto>;
public class GetOrderHandler : IRequestHandler<GetOrderQuery, OrderDto>
{
    public async Task<OrderDto> Handle(GetOrderQuery q, CancellationToken ct)
        => await _db.Orders.Where(o => o.Id == q.Id)
                           .Select(o => new OrderDto { /* projection */ })
                           .FirstAsync(ct);
}
```

- **Khi nào**: read/write có yêu cầu khác nhau (scale, model phức tạp). **Không lạm dụng** cho CRUD đơn giản.

---

## 93. Outbox pattern giải quyết vấn đề gì?

```csharp
// Vấn đề: dual-write - ghi DB + publish message không atomic
// ❌ Nếu publish lỗi sau khi commit DB → mất event
await _db.SaveChangesAsync();
await _messageBus.PublishAsync(orderCreatedEvent); // lỗi ở đây → event mất!

// ✅ Outbox: lưu message vào bảng outbox TRONG CÙNG transaction với business data
public async Task CreateOrderAsync(Order order)
{
    _db.Orders.Add(order);
    _db.OutboxMessages.Add(new OutboxMessage // cùng transaction
    {
        Type = "OrderCreated",
        Content = JsonSerializer.Serialize(order),
        OccurredOn = DateTime.UtcNow
    });
    await _db.SaveChangesAsync(); // atomic: cả order + message
}

// BackgroundService đọc outbox và publish
public class OutboxProcessor : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var messages = await _db.OutboxMessages
                .Where(m => m.ProcessedOn == null).Take(20).ToListAsync(ct);
            foreach (var msg in messages)
            {
                await _bus.PublishAsync(msg);
                msg.ProcessedOn = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(ct);
            await Task.Delay(5000, ct);
        }
    }
}
```

---

## 94. Unit Test vs Integration Test vs E2E Test?

```csharp
// Unit: 1 đơn vị cô lập, mock dependency, nhanh
[Fact]
public void CalculateDiscount_Over100_Returns10Percent()
{
    var calculator = new DiscountCalculator();
    var result = calculator.Calculate(200);
    Assert.Equal(20, result);
}

// Integration: nhiều component thật (DB, repo)
[Fact]
public async Task Repository_AddUser_PersistsToDatabase()
{
    using var db = new AppDbContext(_sqliteOptions);
    var repo = new UserRepository(db);
    await repo.AddAsync(new User { Name = "Test" });
    await db.SaveChangesAsync();
    Assert.Equal(1, await db.Users.CountAsync());
}

// E2E: toàn bộ luồng qua HTTP như user thật (WebApplicationFactory)
```

---

## 95. Mock, Stub, Fake khác nhau?

```csharp
// Stub: trả dữ liệu định sẵn, KHÔNG kiểm tra tương tác
var stub = new Mock<IUserRepository>();
stub.Setup(r => r.GetById(1)).Returns(new User { Name = "A" });

// Mock: KIỂM TRA hành vi (method có được gọi không, mấy lần)
var mock = new Mock<IEmailService>();
var service = new OrderService(mock.Object);
await service.PlaceOrderAsync(order);
mock.Verify(e => e.SendAsync(It.IsAny<string>()), Times.Once); // ✅ verify

// Fake: implementation thật nhưng đơn giản (in-memory)
public class FakeUserRepository : IUserRepository
{
    private readonly List<User> _users = new();
    public User? GetById(int id) => _users.FirstOrDefault(u => u.Id == id);
    public void Add(User u) => _users.Add(u);
}
```

---

## 96. Nguyên tắc AAA trong viết test?

```csharp
[Fact]
public async Task Withdraw_InsufficientFunds_ThrowsException()
{
    // Arrange - chuẩn bị
    var account = new BankAccount(balance: 100);

    // Act - thực thi
    var act = () => account.Withdraw(150);

    // Assert - kiểm chứng
    await Assert.ThrowsAsync<InsufficientFundsException>(act);
}
```

---

## 97. Cách test code có dependency (DB, HTTP)?

```csharp
// ✅ SQLite in-memory cho integration test EF
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlite("DataSource=:memory:")
    .Options;

// ✅ WebApplicationFactory - test ASP.NET Core end-to-end
public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public ApiTests(WebApplicationFactory<Program> factory)
        => _client = factory.CreateClient();

    [Fact]
    public async Task GetUsers_Returns200()
    {
        var response = await _client.GetAsync("/api/users");
        response.EnsureSuccessStatusCode();
    }
}

// ✅ Mock HttpClient qua IHttpClientFactory hoặc WireMock cho HTTP bên ngoài
```

---

## 98. Logging trong .NET nên làm thế nào? Structured logging?

```csharp
public class OrderService
{
    private readonly ILogger<OrderService> _logger;
    public OrderService(ILogger<OrderService> logger) => _logger = logger;

    public async Task ProcessAsync(int orderId, int userId)
    {
        // ✅ Structured logging: giữ placeholder, KHÔNG nối chuỗi
        _logger.LogInformation("Processing order {OrderId} for user {UserId}",
            orderId, userId);

        // ❌ Không làm: nối chuỗi (mất structured data để query)
        // _logger.LogInformation($"Processing order {orderId}");

        // ❌ KHÔNG log dữ liệu nhạy cảm
        // _logger.LogInformation("Password: {Pwd}", password);
    }
}
```

- Serilog/OpenTelemetry để ship log; dùng đúng log level (Trace/Debug/Info/Warning/Error/Critical).

---

## 99. CI/CD pipeline cơ bản cho .NET?

```yaml
# GitHub Actions
name: CI
on: [push, pull_request]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - run: dotnet restore
      - run: dotnet build --no-restore -c Release
      - run: dotnet test --no-build -c Release --collect:"XPlat Code Coverage"
      - run: dotnet publish -c Release -o ./publish
      # - build Docker image, deploy...
```

- Các bước: `restore → build → test → publish → docker → deploy`. Chạy test + quality gate mỗi PR.

---

## 100. Cách giám sát & chẩn đoán ứng dụng .NET production?

```csharp
// Health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>()
    .AddUrlGroup(new Uri("https://external-api.com/health"));
app.MapHealthChecks("/health");

// Metrics + tracing (OpenTelemetry)
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddEntityFrameworkCoreInstrumentation())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation());
```

```bash
# Chẩn đoán runtime
dotnet-counters monitor --process-id 1234   # CPU, GC, memory real-time
dotnet-trace collect --process-id 1234       # trace hiệu năng
dotnet-dump collect --process-id 1234        # dump phân tích memory leak
```

- **Bộ ba quan sát**: logs (structured), metrics (Prometheus), traces (OpenTelemetry) + APM (App Insights/Datadog).

---

[⬅️ Phần 8](interview.NET.08-EFCore.md) | [Mục lục](interview.NET.md) | [Phần 10 — SQL ➡️](interview.NET.10-SQL.md)
