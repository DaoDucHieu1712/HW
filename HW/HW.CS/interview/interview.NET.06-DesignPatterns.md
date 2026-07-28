# Phần 6 — Design Patterns (GoF & .NET)

[⬅️ Phần 5](interview.NET.05-AspNetCore.md) | [Mục lục](interview.NET.md) | [Phần 7 — DSA ➡️](interview.NET.07-DSA.md)

> Với middle: nêu **vấn đề pattern giải quyết** + **ví dụ .NET thực tế**, tránh học thuộc UML.

---

## 🏗️ Nhóm Creational (khởi tạo)

## DP-1. Factory Method vs Abstract Factory?

```csharp
// Factory Method: 1 method tạo 1 loại object
public abstract class Logistics
{
    public abstract ITransport CreateTransport(); // factory method
    public void Deliver() => CreateTransport().Deliver();
}
public class RoadLogistics : Logistics
{
    public override ITransport CreateTransport() => new Truck();
}

// Abstract Factory: tạo cả HỌ object liên quan
public interface IUIFactory
{
    IButton CreateButton();
    ICheckbox CreateCheckbox();
}
public class WindowsFactory : IUIFactory
{
    public IButton CreateButton() => new WindowsButton();
    public ICheckbox CreateCheckbox() => new WindowsCheckbox();
}
```

- **Chốt**: Factory Method = 1 sản phẩm; Abstract Factory = 1 bộ sản phẩm đồng bộ.

---

## DP-2. Builder pattern giải quyết gì?

```csharp
// ❌ Telescoping constructor - quá nhiều tham số
// var p = new Pizza("large", true, false, true, false, "thin");

// ✅ Builder - fluent, từng bước
public class PizzaBuilder
{
    private readonly Pizza _pizza = new();
    public PizzaBuilder Size(string s) { _pizza.Size = s; return this; }
    public PizzaBuilder AddCheese() { _pizza.Cheese = true; return this; }
    public PizzaBuilder AddBacon() { _pizza.Bacon = true; return this; }
    public Pizza Build() => _pizza;
}

var pizza = new PizzaBuilder().Size("large").AddCheese().AddBacon().Build();

// .NET dùng Builder khắp nơi:
var host = WebApplication.CreateBuilder(args); // WebApplicationBuilder
var sb = new StringBuilder().Append("a").Append("b");
```

---

## DP-3. Prototype pattern? Liên hệ `ICloneable`?

```csharp
public class Document
{
    public string Title { get; set; }
    public List<string> Tags { get; set; } = new();

    // Shallow copy: chung reference Tags
    public Document ShallowClone() => (Document)MemberwiseClone();

    // ✅ Deep copy: copy đệ quy
    public Document DeepClone() => new()
    {
        Title = Title,
        Tags = new List<string>(Tags) // copy list mới
    };
}

var original = new Document { Title = "A", Tags = { "x" } };
var shallow = original.ShallowClone();
shallow.Tags.Add("y");
Console.WriteLine(original.Tags.Count); // 2! (shallow chung Tags)
```

---

## DP-4. Singleton thread-safe được ưa chuộng nhất?

```csharp
// ✅ Lazy<T>: lazy + thread-safe sẵn
public sealed class Logger
{
    private static readonly Lazy<Logger> _instance = new(() => new Logger());
    public static Logger Instance => _instance.Value;
    private Logger() { }
}

// ✅ Trong app .NET: DI Singleton thay vì tự viết (dễ test)
builder.Services.AddSingleton<ILogger, Logger>();
```

---

## 🧩 Nhóm Structural (cấu trúc)

## DP-5. Adapter pattern dùng khi nào?

```csharp
// SDK bên thứ 3 với interface khác
public class ThirdPartyPayment { public void MakePayment(double amt) { } }

// Adapter: bọc SDK sau interface của mình
public interface IPaymentGateway { void Pay(decimal amount); }
public class ThirdPartyAdapter : IPaymentGateway
{
    private readonly ThirdPartyPayment _sdk = new();
    public void Pay(decimal amount) => _sdk.MakePayment((double)amount); // chuyển đổi
}
// → dễ thay nhà cung cấp mà không sửa business code
```

---

## DP-6. Decorator pattern? Khác kế thừa ở đâu?

```csharp
public interface ICoffee { decimal Cost(); string Description(); }
public class Espresso : ICoffee
{
    public decimal Cost() => 2.0m;
    public string Description() => "Espresso";
}

// Decorator: thêm hành vi ĐỘNG bằng cách bọc
public class MilkDecorator : ICoffee
{
    private readonly ICoffee _coffee;
    public MilkDecorator(ICoffee c) => _coffee = c;
    public decimal Cost() => _coffee.Cost() + 0.5m;
    public string Description() => _coffee.Description() + " + Milk";
}

ICoffee coffee = new MilkDecorator(new MilkDecorator(new Espresso()));
Console.WriteLine($"{coffee.Description()}: {coffee.Cost()}"); // Espresso+Milk+Milk: 3.0

// .NET: Stream là Decorator kinh điển
// new GZipStream(new BufferedStream(fileStream))
```

---

## DP-7. Proxy pattern gồm những loại?

```csharp
public interface IImage { void Display(); }
public class RealImage : IImage
{
    public RealImage(string file) => Console.WriteLine($"Load {file}"); // tốn kém
    public void Display() => Console.WriteLine("Display");
}

// Virtual proxy: lazy loading
public class ImageProxy : IImage
{
    private readonly string _file;
    private RealImage? _real;
    public ImageProxy(string file) => _file = file;
    public void Display()
    {
        _real ??= new RealImage(_file); // chỉ load khi cần
        _real.Display();
    }
}
```

- **Loại**: Virtual (lazy - EF lazy loading), Protection (quyền), Remote (gRPC/network), Caching.

---

## DP-8. Facade pattern giải quyết gì?

```csharp
// Ẩn hệ thống con phức tạp sau 1 interface đơn giản
public class OrderFacade
{
    private readonly IInventory _inventory;
    private readonly IPayment _payment;
    private readonly IShipping _shipping;
    private readonly INotification _notification;

    public async Task PlaceOrderAsync(Order order)
    {
        await _inventory.ReserveAsync(order);   // client chỉ gọi 1 method,
        await _payment.ChargeAsync(order);      // không cần biết 4 bước bên trong
        await _shipping.ScheduleAsync(order);
        await _notification.SendAsync(order);
    }
}
```

---

## DP-9. Composite pattern dùng cho cấu trúc nào?

```csharp
// Xử lý object đơn lẻ và nhóm ĐỒNG NHẤT - cấu trúc cây
public interface IFileSystemItem { long GetSize(); }

public class FileItem : IFileSystemItem
{
    public long Size;
    public long GetSize() => Size;
}
public class FolderItem : IFileSystemItem // vừa là item vừa chứa item
{
    private readonly List<IFileSystemItem> _children = new();
    public void Add(IFileSystemItem item) => _children.Add(item);
    public long GetSize() => _children.Sum(c => c.GetSize()); // đệ quy
}

var root = new FolderItem();
root.Add(new FileItem { Size = 100 });
var sub = new FolderItem();
sub.Add(new FileItem { Size = 50 });
root.Add(sub);
Console.WriteLine(root.GetSize()); // 150
```

---

## DP-10. Bridge pattern khác Adapter?

```csharp
// Bridge: tách abstraction khỏi implementation (thiết kế TRƯỚC)
public interface IRenderer { void RenderCircle(float radius); } // implementation
public class OpenGLRenderer : IRenderer { public void RenderCircle(float r) { } }
public class DirectXRenderer : IRenderer { public void RenderCircle(float r) { } }

public abstract class Shape // abstraction
{
    protected IRenderer Renderer;
    protected Shape(IRenderer r) => Renderer = r;
    public abstract void Draw();
}
public class Circle : Shape
{
    private float _radius;
    public Circle(IRenderer r, float radius) : base(r) => _radius = radius;
    public override void Draw() => Renderer.RenderCircle(_radius);
}
// Shape và Renderer tiến hoá ĐỘC LẬP
```

- **Chốt**: Bridge là chủ đích thiết kế; Adapter là chữa cháy interface đã tồn tại.

---

## ⚙️ Nhóm Behavioral (hành vi)

## DP-11. Strategy vs State pattern — dễ nhầm?

```csharp
// Strategy: hoán đổi THUẬT TOÁN, client chọn
public interface ISortStrategy { void Sort(int[] data); }

// State: object đổi HÀNH VI khi state đổi; state tự chuyển sang state khác
public interface IOrderState { void Next(OrderContext ctx); }
public class PendingState : IOrderState
{
    public void Next(OrderContext ctx) => ctx.State = new PaidState(); // tự chuyển
}
public class PaidState : IOrderState
{
    public void Next(OrderContext ctx) => ctx.State = new ShippedState();
}
public class OrderContext
{
    public IOrderState State = new PendingState();
    public void Proceed() => State.Next(this);
}
```

- **Chốt**: UML giống nhau, khác ở **ý đồ** (Strategy = chọn thuật toán; State = máy trạng thái).

---

## DP-12. Observer pattern? Liên hệ event C#?

```csharp
// C# hỗ trợ sẵn qua event/delegate
public class Stock
{
    public event Action<decimal>? PriceChanged; // subject
    private decimal _price;
    public decimal Price
    {
        set { _price = value; PriceChanged?.Invoke(value); } // thông báo observer
    }
}

var stock = new Stock();
stock.PriceChanged += p => Console.WriteLine($"Observer 1: {p}"); // observer
stock.PriceChanged += p => Console.WriteLine($"Observer 2: {p}");
stock.Price = 100; // cả 2 observer nhận thông báo
```

---

## DP-13. Template Method pattern?

```csharp
// Khung thuật toán trong base, lớp con override MỘT SỐ bước
public abstract class DataImporter
{
    public void Import() // template method - cố định cấu trúc
    {
        var data = ReadData();
        var validated = Validate(data);
        Save(validated);
    }
    protected abstract string ReadData();          // lớp con tự lo
    protected virtual string Validate(string d) => d; // có default
    protected abstract void Save(string data);
}

public class CsvImporter : DataImporter
{
    protected override string ReadData() => "csv data";
    protected override void Save(string data) { }
}
```

---

## DP-14. Chain of Responsibility? Liên hệ middleware?

```csharp
// Chuỗi handler, mỗi cái tự xử lý hoặc chuyển tiếp
public abstract class Handler
{
    protected Handler? Next;
    public Handler SetNext(Handler next) { Next = next; return next; }
    public abstract void Handle(Request request);
}

public class AuthHandler : Handler
{
    public override void Handle(Request req)
    {
        if (!req.IsAuthenticated) { Console.WriteLine("Chặn: chưa auth"); return; }
        Next?.Handle(req); // chuyển tiếp
    }
}

// ↔ ASP.NET Core middleware & MediatR pipeline behavior chính là pattern này
public class LoggingBehavior<TReq, TRes> : IPipelineBehavior<TReq, TRes>
    where TReq : notnull
{
    public async Task<TRes> Handle(TReq req, RequestHandlerDelegate<TRes> next, CancellationToken ct)
    {
        Console.WriteLine("Trước");
        var response = await next(); // chuyển tiếp
        Console.WriteLine("Sau");
        return response;
    }
}
```

---

## DP-15. Command pattern?

```csharp
// Đóng gói yêu cầu thành object → queue, log, undo
public interface ICommand { void Execute(); void Undo(); }

public class AddTextCommand : ICommand
{
    private readonly Document _doc;
    private readonly string _text;
    public AddTextCommand(Document doc, string text) => (_doc, _text) = (doc, text);
    public void Execute() => _doc.Text += _text;
    public void Undo() => _doc.Text = _doc.Text[..^_text.Length];
}

// ↔ CQRS Command + MediatR IRequest chính là biến thể pattern này
```

---

## DP-16. Mediator? Vì sao MediatR phổ biến?

```csharp
// Component giao tiếp qua mediator, không gọi trực tiếp nhau
public record GetUserQuery(int Id) : IRequest<UserDto>;

public class GetUserHandler : IRequestHandler<GetUserQuery, UserDto>
{
    public async Task<UserDto> Handle(GetUserQuery q, CancellationToken ct)
        => await _repo.GetDtoAsync(q.Id);
}

// Controller chỉ Send, không biết handler nào → tách rời, dễ test
public class UsersController : ControllerBase
{
    private readonly IMediator _mediator;
    public UsersController(IMediator m) => _mediator = m;

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
        => Ok(await _mediator.Send(new GetUserQuery(id)));
}
```

---

## 🎯 Nhóm đặc thù .NET / Enterprise

## DP-17. Options pattern trong .NET?

```csharp
public class EmailOptions
{
    public string SmtpHost { get; set; } = "";
    public int Port { get; set; }
}

// Bind + validate
builder.Services.AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection("Email"))
    .ValidateDataAnnotations()
    .Validate(o => o.Port > 0, "Port phải > 0");

public class EmailService
{
    private readonly EmailOptions _options;
    public EmailService(IOptions<EmailOptions> opt) => _options = opt.Value;
}
```

---

## DP-18. Result pattern (thay vì exception)?

```csharp
// Thay throw cho lỗi nghiệp vụ DỰ KIẾN
public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? Error { get; }
    private Result(bool ok, T? val, string? err) => (IsSuccess, Value, Error) = (ok, val, err);
    public static Result<T> Success(T val) => new(true, val, null);
    public static Result<T> Failure(string err) => new(false, default, err);
}

public Result<User> GetUser(int id)
{
    var user = _repo.Find(id);
    return user is null
        ? Result<User>.Failure("Không tìm thấy user") // không throw
        : Result<User>.Success(user);
}

var result = GetUser(1);
if (result.IsSuccess) Console.WriteLine(result.Value!.Name);
else Console.WriteLine(result.Error);
```

- **Trade-off**: luồng lỗi tường minh, tránh exception đắt; nhưng dài dòng hơn. Thư viện: `FluentResults`, `ErrorOr`.

---

## DP-19. Specification pattern?

```csharp
// Đóng gói điều kiện query thành object tái sử dụng, kết hợp được
public class ActiveUsersSpec
{
    public Expression<Func<User, bool>> ToExpression()
        => user => user.IsActive && user.LastLogin > DateTime.Now.AddDays(-30);
}

// Dùng với EF Core
var spec = new ActiveUsersSpec();
var users = await _db.Users.Where(spec.ToExpression()).ToListAsync();
```

---

## DP-20. Khi nào KHÔNG nên dùng pattern? (over-engineering)

```csharp
// ❌ Over-engineering: abstract factory + strategy + repository cho 1 CRUD đơn giản
// public interface IUserFactoryStrategyProvider { ... } // thừa!

// ✅ CRUD đơn giản thì code thẳng
[HttpGet("{id}")]
public async Task<IActionResult> Get(int id)
    => Ok(await _db.Users.FindAsync(id)); // đủ rồi
```

- **Chốt**: "Tôi dùng X vì gặp vấn đề Y; nếu chỉ CRUD thì không cần." Ưu tiên **YAGNI** & **KISS**.

---

[⬅️ Phần 5](interview.NET.05-AspNetCore.md) | [Mục lục](interview.NET.md) | [Phần 7 — DSA ➡️](interview.NET.07-DSA.md)
