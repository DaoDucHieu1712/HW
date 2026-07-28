# Phần 5 — ASP.NET Core

[⬅️ Phần 4](interview.NET.04-OOP-Basics.md) | [Mục lục](interview.NET.md) | [Phần 6 — Design Patterns ➡️](interview.NET.06-DesignPatterns.md)

---

## 66. Middleware pipeline hoạt động thế nào?

```csharp
// Chuỗi delegate xử lý request/response theo thứ tự đăng ký
app.Use(async (context, next) =>
{
    Console.WriteLine("Trước next");   // xử lý request
    await next();                       // chuyển tiếp middleware sau
    Console.WriteLine("Sau next");      // xử lý response (chiều ngược)
});

// Short-circuit: KHÔNG gọi next → trả response sớm
app.Use(async (context, next) =>
{
    if (!context.Request.Headers.ContainsKey("X-Api-Key"))
    {
        context.Response.StatusCode = 401;
        return; // dừng, không gọi next
    }
    await next();
});
```

- **Điểm nhấn**: thứ tự đăng ký rất quan trọng; middleware chạy như "vòng tròn" (request đi xuống, response đi lên).

---

## 67. Thứ tự middleware chuẩn trong ASP.NET Core?

```csharp
var app = builder.Build();

app.UseExceptionHandler("/error"); // 1. bắt lỗi đầu tiên
app.UseHttpsRedirection();          // 2
app.UseStaticFiles();               // 3
app.UseRouting();                   // 4. xác định endpoint
app.UseCors();                      // 5. sau routing, trước auth
app.UseAuthentication();            // 6. bạn là ai
app.UseAuthorization();             // 7. bạn được làm gì (SAU authentication!)
app.MapControllers();               // 8. thực thi endpoint

app.Run();
```

- **❌ Bẫy**: đặt `UseAuthorization` trước `UseAuthentication` → user luôn null → lỗi bảo mật.

---

## 68. `IActionResult` khác `ActionResult<T>`?

```csharp
// IActionResult: trả nhiều loại result nhưng không mô tả kiểu data
[HttpGet("{id}")]
public IActionResult GetOld(int id)
{
    var product = _repo.Get(id);
    if (product == null) return NotFound();
    return Ok(product);
}

// ✅ ActionResult<T>: vừa trả result vừa mô tả kiểu → OpenAPI/Swagger đẹp hơn
[HttpGet("{id}")]
public ActionResult<Product> Get(int id)
{
    var product = _repo.Get(id);
    if (product == null) return NotFound();
    return product; // trả trực tiếp T
}
```

---

## 69. Model binding và validation hoạt động thế nào?

```csharp
public class CreateUserDto
{
    [Required] public string Name { get; set; }
    [EmailAddress] public string Email { get; set; }
    [Range(18, 100)] public int Age { get; set; }
}

[ApiController] // ✅ tự động trả 400 khi ModelState invalid
[Route("api/users")]
public class UsersController : ControllerBase
{
    [HttpPost]
    public IActionResult Create(CreateUserDto dto) // binding từ body + validate tự động
    {
        // Không cần check ModelState.IsValid nhờ [ApiController]
        return Ok();
    }
}
```

- Model binding map: route, query, body, form, header. Validation: Data Annotations hoặc FluentValidation.

---

## 70. `[FromBody]`, `[FromQuery]`, `[FromRoute]` khác nhau?

```csharp
// GET /api/products/5/reviews?page=2&sort=date
[HttpGet("api/products/{id}/reviews")]
public IActionResult GetReviews(
    [FromRoute] int id,        // từ route: 5
    [FromQuery] int page,      // từ query: 2
    [FromQuery] string sort)   // từ query: "date"
{ return Ok(); }

// POST với body JSON
[HttpPost("api/products")]
public IActionResult Create(
    [FromBody] ProductDto dto,          // từ body JSON (chỉ 1 tham số)
    [FromHeader(Name = "X-Tenant")] string tenant, // từ header
    [FromServices] IProductRepository repo)         // từ DI container
{ return Ok(); }
```

---

## 71. Filter trong ASP.NET Core gồm những loại nào?

```csharp
// Thứ tự: Authorization → Resource → Action → Exception → Result

// Action Filter: chạy trước/sau action
public class LogActionFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext c) => Console.WriteLine("Trước action");
    public void OnActionExecuted(ActionExecutedContext c) => Console.WriteLine("Sau action");
}

// Exception Filter: bắt lỗi trong action
public class CustomExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        context.Result = new ObjectResult(new { error = context.Exception.Message })
        { StatusCode = 500 };
        context.ExceptionHandled = true;
    }
}

// Đăng ký
builder.Services.AddControllers(opt => opt.Filters.Add<LogActionFilter>());
```

- **Dùng cho**: tách cross-cutting concern (logging, caching, validation, exception).

---

## 72. Cách xử lý exception tập trung?

```csharp
// ✅ .NET 8: IExceptionHandler
public class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var problem = new ProblemDetails // RFC 7807 chuẩn
        {
            Status = ex switch
            {
                NotFoundException => 404,
                ValidationException => 400,
                _ => 500
            },
            Title = "Đã xảy ra lỗi",
            Detail = ex.Message
        };
        ctx.Response.StatusCode = problem.Status!.Value;
        await ctx.Response.WriteAsJsonAsync(problem, ct);
        return true;
    }
}

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
app.UseExceptionHandler();
```

---

## 73. CORS là gì? Cấu hình thế nào?

```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
        policy.WithOrigins("https://myapp.com")  // ❌ tránh AllowAnyOrigin trong prod
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

var app = builder.Build();
app.UseRouting();
app.UseCors("AllowFrontend"); // sau UseRouting, trước UseAuthorization
app.UseAuthorization();
```

- **CORS**: cơ chế trình duyệt cho phép/chặn request từ origin khác.

---

## 74. Phân biệt Authentication và Authorization?

```csharp
// Authentication: xác thực "bạn là ai"
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => { /* validate token */ });

// Authorization: kiểm tra "bạn được làm gì"
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireRole("Admin"));
    options.AddPolicy("Over18", p => p.RequireClaim("age", "18"));
});

[Authorize(Roles = "Admin")]           // authorization theo role
[Authorize(Policy = "Over18")]         // authorization theo policy
public IActionResult DeleteUser(int id) => Ok();
```

---

## 75. JWT hoạt động thế nào?

```csharp
// Token = Header.Payload.Signature (base64url)
// Tạo token
var claims = new[]
{
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
    new Claim(ClaimTypes.Role, "Admin")
};
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
var token = new JwtSecurityToken(
    claims: claims,
    expires: DateTime.UtcNow.AddMinutes(15), // ✅ nên có expiry ngắn + refresh token
    signingCredentials: creds);
var jwt = new JwtSecurityTokenHandler().WriteToken(token);

// Client gửi: Authorization: Bearer <jwt>
// Server verify chữ ký (stateless - không lưu session)
```

---

## 76. `IConfiguration` và cấu hình theo môi trường?

```csharp
// Thứ tự ưu tiên (nguồn sau GHI ĐÈ nguồn trước):
// appsettings.json → appsettings.{Env}.json → env vars → command line → user secrets

// Đọc trực tiếp
var connStr = builder.Configuration.GetConnectionString("Default");
var timeout = builder.Configuration.GetValue<int>("Api:TimeoutSeconds");

// ✅ Bind section thành strongly-typed
public class ApiOptions { public string BaseUrl { get; set; } public int Timeout { get; set; } }
builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection("Api"));
```

---

## 77. `IOptions`, `IOptionsSnapshot`, `IOptionsMonitor` khác nhau?

```csharp
// IOptions<T>: singleton, đọc 1 lần lúc khởi động (config không đổi)
public class ServiceA
{
    public ServiceA(IOptions<ApiOptions> opt) { var o = opt.Value; }
}

// IOptionsSnapshot<T>: scoped, đọc lại mỗi request (hỗ trợ reload)
public class ServiceB
{
    public ServiceB(IOptionsSnapshot<ApiOptions> opt) { var o = opt.Value; }
}

// IOptionsMonitor<T>: singleton + callback OnChange (real-time)
public class ServiceC
{
    public ServiceC(IOptionsMonitor<ApiOptions> monitor)
    {
        var o = monitor.CurrentValue;
        monitor.OnChange(newOpt => Console.WriteLine("Config đổi!"));
    }
}
```

---

## 78. Minimal API khác Controller-based API?

```csharp
// Minimal API: ít boilerplate, hợp microservice nhỏ
app.MapGet("/products/{id}", async (int id, IProductRepo repo) =>
{
    var p = await repo.GetAsync(id);
    return p is null ? Results.NotFound() : Results.Ok(p);
});

// Controller: cấu trúc rõ, filter/binding đầy đủ, hợp app lớn
[ApiController, Route("products")]
public class ProductsController : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<ActionResult<Product>> Get(int id) { /*...*/ return Ok(); }
}
```

---

## 79. `HttpClient` — tại sao không `new` mỗi lần? `IHttpClientFactory`?

```csharp
// ❌ new HttpClient() liên tục → socket exhaustion (TIME_WAIT)
public async Task<string> Bad()
{
    using var client = new HttpClient(); // mỗi request 1 socket, cạn kiệt!
    return await client.GetStringAsync(url);
}

// ✅ IHttpClientFactory: quản lý pool handler, hỗ trợ Polly
builder.Services.AddHttpClient("api", c => c.BaseAddress = new Uri("https://api.com"))
    .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(3, _ => TimeSpan.FromSeconds(1)));

public class ApiClient
{
    private readonly IHttpClientFactory _factory;
    public ApiClient(IHttpClientFactory f) => _factory = f;
    public async Task<string> Get()
    {
        var client = _factory.CreateClient("api");
        return await client.GetStringAsync("/data");
    }
}
```

---

## 80. Hosted Service / BackgroundService dùng để làm gì?

```csharp
// Chạy tác vụ nền dài hạn (queue processing, scheduled job, outbox processor)
public class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    public OutboxProcessor(IServiceScopeFactory f) => _scopeFactory = f;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope(); // ✅ tạo scope cho DbContext
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // ... đọc outbox, publish message
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}

builder.Services.AddHostedService<OutboxProcessor>();
```

---

[⬅️ Phần 4](interview.NET.04-OOP-Basics.md) | [Mục lục](interview.NET.md) | [Phần 6 — Design Patterns ➡️](interview.NET.06-DesignPatterns.md)
