# Phần 4 — OOP & Design Patterns (cơ bản)

[⬅️ Phần 3](interview.NET.03-Collections-LINQ.md) | [Mục lục](interview.NET.md) | [Phần 5 — ASP.NET Core ➡️](interview.NET.05-AspNetCore.md)

---

## 53. 4 tính chất OOP là gì?

```csharp
// Encapsulation: ẩn state, expose qua public API
public class BankAccount
{
    private decimal _balance;                       // ẩn
    public decimal Balance => _balance;             // chỉ đọc
    public void Deposit(decimal amt)                // kiểm soát thay đổi
    {
        if (amt <= 0) throw new ArgumentException();
        _balance += amt;
    }
}

// Inheritance: tái sử dụng
public class Animal { public string Name; }
public class Dog : Animal { public void Bark() { } }

// Polymorphism: cùng interface, hành vi khác
public abstract class Shape { public abstract double Area(); }
public class Circle : Shape { public double R; public override double Area() => Math.PI * R * R; }
public class Square : Shape { public double S; public override double Area() => S * S; }

// Abstraction: ẩn phức tạp qua interface
public interface IRepository<T> { T GetById(int id); }
```

---

## 54. `abstract class` khác `interface` như thế nào?

```csharp
// abstract class: có field, constructor, implementation dùng chung; kế thừa 1
public abstract class Repository
{
    protected readonly DbContext Db;               // field
    protected Repository(DbContext db) => Db = db; // constructor
    public void Save() => Db.SaveChanges();        // implementation dùng chung
    public abstract void Delete(int id);           // bắt buộc override
}

// interface: hợp đồng "can-do", implement NHIỀU; C# 8+ có default method
public interface ILogger
{
    void Log(string msg);
    void LogError(string msg) => Log($"ERROR: {msg}"); // default implementation
}

public class Service : Repository, ILogger, IDisposable // 1 class + nhiều interface
{
    public Service(DbContext db) : base(db) { }
    public override void Delete(int id) { }
    public void Log(string msg) { }
    public void Dispose() { }
}
```

- **Khi nào**: quan hệ "is-a" + code dùng chung → abstract class; khả năng "can-do" → interface.

---

## 55. `virtual`, `override`, `new`, `sealed` khác nhau?

```csharp
public class Base
{
    public virtual void Show() => Console.WriteLine("Base");
}

public class Derived : Base
{
    public override void Show() => Console.WriteLine("Derived"); // đa hình
}

public class Hider : Base
{
    public new void Show() => Console.WriteLine("Hider");        // che, KHÔNG đa hình
}

Base b1 = new Derived();
b1.Show(); // "Derived" (override → đa hình theo object thật)

Base b2 = new Hider();
b2.Show(); // "Base" (new → theo kiểu biến, không đa hình!)

// sealed: chặn override tiếp
public class Final : Base
{
    public sealed override void Show() => Console.WriteLine("Final");
}
```

---

## 56. Method overloading khác overriding?

```csharp
// Overloading: cùng tên, khác signature, quyết định COMPILE-TIME
public class Calculator
{
    public int Add(int a, int b) => a + b;
    public double Add(double a, double b) => a + b;      // overload
    public int Add(int a, int b, int c) => a + b + c;    // overload
}

// Overriding: lớp con định nghĩa lại virtual, quyết định RUNTIME
public class Base { public virtual string Name() => "Base"; }
public class Child : Base { public override string Name() => "Child"; }
```

---

## 57. Nguyên lý SOLID gồm những gì?

```csharp
// S - Single Responsibility: mỗi class 1 lý do thay đổi
// ❌ class làm cả lưu DB + gửi email
// ✅ tách UserRepository và EmailService

// O - Open/Closed: mở để mở rộng, đóng để sửa
public abstract class Discount { public abstract decimal Apply(decimal p); }
public class BlackFridayDiscount : Discount { public override decimal Apply(decimal p) => p * 0.5m; }
// thêm loại giảm giá mới KHÔNG sửa code cũ

// L - Liskov: lớp con thay được lớp cha mà không phá hành vi
// D - Dependency Inversion: phụ thuộc abstraction
public class OrderService
{
    private readonly IPaymentGateway _gateway; // interface, không concrete
    public OrderService(IPaymentGateway g) => _gateway = g;
}
```

---

## 58. Dependency Injection là gì? Lợi ích?

```csharp
// ❌ Không DI: tự new → coupling chặt, khó test
public class OrderService
{
    private readonly SqlOrderRepository _repo = new(); // cứng!
}

// ✅ DI: nhận qua constructor
public class OrderServiceGood
{
    private readonly IOrderRepository _repo;
    public OrderServiceGood(IOrderRepository repo) => _repo = repo; // inject
}

// Đăng ký trong container
builder.Services.AddScoped<IOrderRepository, SqlOrderRepository>();

// Test: dễ mock
var mockRepo = new Mock<IOrderRepository>();
var service = new OrderServiceGood(mockRepo.Object);
```

- **Lợi ích**: giảm coupling, dễ test, dễ thay implementation, tuân DIP.

---

## 59. 3 service lifetime trong .NET DI?

```csharp
builder.Services.AddTransient<ITransient, MyService>(); // mới mỗi lần resolve
builder.Services.AddScoped<IScoped, MyService>();       // 1 instance / HTTP request
builder.Services.AddSingleton<ISingleton, MyService>(); // 1 instance toàn app

// ❌ BẪY: inject Scoped/Transient vào Singleton (captive dependency)
public class BadSingleton // đăng ký Singleton
{
    private readonly IScoped _scoped; // ⚠️ scoped bị "kẹt" sống mãi như singleton!
    public BadSingleton(IScoped s) => _scoped = s;
}

// ✅ Fix: inject IServiceScopeFactory rồi tạo scope khi cần
public class GoodSingleton
{
    private readonly IServiceScopeFactory _factory;
    public GoodSingleton(IServiceScopeFactory f) => _factory = f;
    public void Work()
    {
        using var scope = _factory.CreateScope();
        var scoped = scope.ServiceProvider.GetRequiredService<IScoped>();
    }
}
```

---

## 60. Repository pattern giải quyết vấn đề gì?

```csharp
public interface IProductRepository
{
    Task<Product?> GetByIdAsync(int id);
    Task AddAsync(Product product);
}

public class ProductRepository : IProductRepository
{
    private readonly AppDbContext _db;
    public ProductRepository(AppDbContext db) => _db = db;
    public async Task<Product?> GetByIdAsync(int id) => await _db.Products.FindAsync(id);
    public async Task AddAsync(Product p) => await _db.Products.AddAsync(p);
}
```

- **Lưu ý**: với EF Core, `DbContext` + `DbSet` đã là Repository/UoW → lạm dụng thêm 1 lớp repo có thể thừa. Cân nhắc theo bối cảnh.

---

## 61. Unit of Work pattern là gì?

```csharp
// DbContext.SaveChanges() CHÍNH LÀ Unit of Work
public async Task TransferMoneyAsync(int fromId, int toId, decimal amount)
{
    var from = await _db.Accounts.FindAsync(fromId);
    var to = await _db.Accounts.FindAsync(toId);
    from.Balance -= amount;
    to.Balance += amount;

    await _db.SaveChangesAsync(); // ✅ cả 2 thay đổi trong 1 transaction (all-or-nothing)
}

// Nhiều SaveChanges cần transaction thủ công
using var tx = await _db.Database.BeginTransactionAsync();
try { /* nhiều SaveChanges */ await tx.CommitAsync(); }
catch { await tx.RollbackAsync(); throw; }
```

---

## 62. Factory pattern dùng khi nào?

```csharp
// Khi việc tạo object phức tạp / quyết định concrete type lúc runtime
public interface INotification { void Send(string msg); }
public class EmailNotification : INotification { public void Send(string m) { } }
public class SmsNotification : INotification { public void Send(string m) { } }

public class NotificationFactory
{
    public INotification Create(string type) => type switch
    {
        "email" => new EmailNotification(),
        "sms" => new SmsNotification(),
        _ => throw new ArgumentException($"Unknown: {type}")
    };
}
```

---

## 63. Singleton pattern và cách thread-safe?

```csharp
// ✅ Cách ưa chuộng: Lazy<T> (lazy + thread-safe sẵn)
public sealed class Config
{
    private static readonly Lazy<Config> _instance = new(() => new Config());
    public static Config Instance => _instance.Value;
    private Config() { }
}

// ✅ Trong app .NET: dùng DI Singleton thay vì tự viết (dễ test hơn)
builder.Services.AddSingleton<IConfig, Config>();
```

- **Điểm nhấn**: Singleton thủ công là anti-pattern (global state, khó mock) → ưu tiên DI.

---

## 64. Strategy pattern là gì?

```csharp
public interface IShippingStrategy { decimal Calculate(decimal weight); }
public class StandardShipping : IShippingStrategy { public decimal Calculate(decimal w) => w * 1.0m; }
public class ExpressShipping : IShippingStrategy { public decimal Calculate(decimal w) => w * 2.5m; }

public class ShippingCalculator
{
    private readonly IShippingStrategy _strategy;
    public ShippingCalculator(IShippingStrategy strategy) => _strategy = strategy; // chọn runtime
    public decimal GetCost(decimal weight) => _strategy.Calculate(weight);
}

var calc = new ShippingCalculator(new ExpressShipping());
Console.WriteLine(calc.GetCost(10)); // 25
```

---

## 65. Mediator pattern và MediatR giải quyết gì?

```csharp
// Giảm coupling: component giao tiếp qua mediator thay vì gọi trực tiếp nhau

// Command
public record CreateOrderCommand(int ProductId, int Qty) : IRequest<int>;

// Handler
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, int>
{
    private readonly AppDbContext _db;
    public CreateOrderHandler(AppDbContext db) => _db = db;
    public async Task<int> Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        var order = new Order { ProductId = cmd.ProductId, Qty = cmd.Qty };
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        return order.Id;
    }
}

// Controller chỉ Send, không biết handler nào
[HttpPost]
public async Task<IActionResult> Create(CreateOrderCommand cmd)
    => Ok(await _mediator.Send(cmd));
```

- **Vì sao phổ biến**: tách rời controller ↔ logic, dễ test, hỗ trợ pipeline behavior (validation, logging, transaction) → trụ cột CQRS + Clean Architecture.

---

[⬅️ Phần 3](interview.NET.03-Collections-LINQ.md) | [Mục lục](interview.NET.md) | [Phần 5 — ASP.NET Core ➡️](interview.NET.05-AspNetCore.md)
