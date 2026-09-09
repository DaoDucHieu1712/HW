# Phần 14 — Framework Internals: ASP.NET Core, DI, EF Core, MediatR

[⬅️ Phần 13 — Async & Threading](interview.NET.13-Async-Threading-Internals.md) | [Về mục lục](interview.NET.md) | Tiếp theo: [Phần 15 — Concurrency & Scaling ➡️](interview.NET.15-Concurrency-Scaling.md)

> Khung trả lời: **❓ Vấn đề gốc → ⚙️ Cơ chế bên dưới → 💻 Code → ⚖️ Hệ quả thực chiến**.
> Demo chạy được: `dotnet run -- 14 all` (xem `Demos/Demo14_FrameworkInternals.cs`).
> Nhiều câu ở đây soi thẳng vào chính solution HW (`HW.Api → HW.Application → HW.Infrastructure`).

---

## 🗺️ Bản đồ: một HTTP request đi qua đâu (end-to-end)

```
TCP socket
  │  Kestrel: IConnectionListener → SocketConnection → System.IO.Pipelines
  ▼
HTTP parser (HTTP/1.1 · HTTP/2 · HTTP/3) → dựng HttpContext
  │
  ▼  ── MIDDLEWARE PIPELINE (Func<RequestDelegate, RequestDelegate>) ──
  │   UseCors → UseAuthentication → UseAuthorization
  │   → ExceptionHandlingMiddleware        ← của project HW
  │   → UseRouting/endpoint
  ▼
Endpoint routing: khớp URL → Endpoint (+ metadata)
  │
  ▼  ── FILTER PIPELINE (MVC) ──
  │   Authorization → Resource → [model binding + validation] → Action → Exception → Result
  ▼
Controller (ISender)   ← HW: controller KHÔNG có business logic
  │
  ▼  ── MEDIATR PIPELINE (decorator chain) ──
  │   LoggingBehavior → ValidationBehavior → TransactionBehavior → Handler
  ▼
Handler: IEFRepository<T> (+ domain method) 
  │
  ▼  TransactionBehavior.ExecuteAsync ⇒ BEGIN TRAN
  │     EF Core: ChangeTracker.DetectChanges → command batching → SaveChanges
  │     Domain events → Outbox rows (cùng transaction)  ⇒ COMMIT
  ▼
Outbox processor (BackgroundService) → IMessageBus → RabbitMQ/Kafka
  │
  ▼
Response: output formatter (System.Text.Json) → Pipe writer → socket
```

---

## FW-1. Generic Host — ai khởi động cái gì?

**❓ Vấn đề gốc**: web app, worker, CLI đều cần cùng thứ: DI, configuration, logging, lifetime,
graceful shutdown. Generic Host gom lại một chỗ.

```csharp
var builder = WebApplication.CreateBuilder(args);
// ① Configuration: appsettings.json → appsettings.{Env}.json → env vars → CLI args (sau đè trước)
// ② Logging providers
// ③ services.Add… (đăng ký DI — mới chỉ là "công thức", chưa tạo gì)
var app = builder.Build();     // ← BuildServiceProvider: khoá container lại
// ④ app.Use… (dựng middleware pipeline)
app.Run();                     // ← chạy IHostedService, mở Kestrel, chờ shutdown signal
```

**Vòng đời:**
```
Build → IHostedService.StartAsync (theo thứ tự đăng ký)
      → ApplicationStarted
      → …phục vụ request…
      → SIGTERM / Ctrl+C → ApplicationStopping
      → IHostedService.StopAsync (NGƯỢC thứ tự) — có timeout ShutdownTimeout (mặc định 30s)
      → ApplicationStopped → dispose root ServiceProvider
```

**⚖️ Thực chiến (graceful shutdown trong K8s)**: `StopAsync` phải **thật sự** tôn trọng
`CancellationToken`; outbox processor / consumer phải dừng nhận việc mới rồi mới finish việc đang
làm. Nếu không, mỗi lần deploy sẽ mất message hoặc treo pod tới hết `terminationGracePeriod`.

---

## FW-2. DI container bên dưới hoạt động ra sao?

**⚙️ Ba giai đoạn**:

```
1. ĐĂNG KÝ    services.AddScoped<IFoo, Foo>()  →  ServiceDescriptor { ServiceType, ImplType, Lifetime }
              (chỉ là danh sách "công thức", chưa reflection gì cả)

2. BUILD      ServiceProvider dựng CALL SITE cho từng service:
                 ConstructorCallSite(Foo, [ CallSite(IBar), CallSite(IBaz) ]) — một cái CÂY

3. RESOLVE    Lần 1–2: duyệt cây bằng interpreter (chậm hơn)
              Từ lần ~3: ServiceProviderEngine COMPILE cây thành delegate
                         (ILEmit hoặc Expression.Compile) ⇒ resolve gần như gọi `new` trực tiếp
```

**Lifetime = mỗi cái ứng với một "cache scope":**

| Lifetime | Cache ở đâu | Sống tới khi |
|---|---|---|
| Singleton | root provider | app tắt |
| Scoped | `IServiceScope` hiện tại (= 1 HTTP request) | scope dispose |
| Transient | **không cache** | — nhưng xem cảnh báo dưới |

**⚠️ 3 cái bẫy phải thuộc:**

```csharp
// ① Captive dependency: singleton giữ scoped ⇒ scoped bị "giam" thành singleton
class MySingleton(AppDbContext db);   // ❌ DbContext sống mãi → phình change tracker, lỗi concurrency
// ✅ đúng:
class MySingleton(IServiceScopeFactory scopeFactory)
{
    async Task Work()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }
}
// (Bật ValidateScopes/ValidateOnBuild để phát hiện lúc khởi động — Development bật sẵn)

// ② Container SỞ HỮU mọi IDisposable nó tạo — KỂ CẢ transient
services.AddTransient<IHeavy, Heavy>();     // Heavy : IDisposable
// resolve từ ROOT provider ⇒ mọi instance bị giữ tới khi app tắt = LEAK

// ③ Đăng ký nhiều lần: GetRequiredService<T>() trả về cái CUỐI CÙNG;
//    GetServices<T>() trả về TẤT CẢ (dùng cho pipeline/strategy)
services.TryAddScoped<IFoo, Foo>();         // chỉ thêm nếu chưa có
services.TryAddEnumerable(ServiceDescriptor.Scoped<IFoo, Foo2>());  // thêm vào tập hợp
```

**.NET 8 — keyed services:**
```csharp
services.AddKeyedScoped<IMessageBus, RabbitMqBus>("rabbit");
public Ctor([FromKeyedServices("rabbit")] IMessageBus bus) { }
```

**⚖️ Liên hệ HW**: `AddScoped(typeof(IEFRepository<>), typeof(EFRepository<>))` là **open generic
registration** — container tự đóng kiểu lúc resolve (`IEFRepository<Blog>` → `EFRepository<Blog>`),
không cần đăng ký từng entity. Đây cũng là lý do repository phải **scoped**: nó ôm `DbContext`.

---

## FW-3. Middleware pipeline được dựng như thế nào?

**⚙️ Cơ chế**: một middleware chỉ là `Func<RequestDelegate, RequestDelegate>` — "cho tôi phần
tiếp theo, tôi trả về phần bắt đầu". `Build()` **gấp danh sách từ CUỐI về ĐẦU**:

```csharp
RequestDelegate Build(List<Func<RequestDelegate, RequestDelegate>> components)
{
    RequestDelegate app = ctx => { ctx.Response.StatusCode = 404; return Task.CompletedTask; };
    for (int i = components.Count - 1; i >= 0; i--)   // ⚠️ NGƯỢC
        app = components[i](app);
    return app;                                        // matryoshka: A(B(C(terminal)))
}
```

⇒ Vì thế **thứ tự `Use…` chính là thứ tự chạy**, và code sau `await next()` chạy theo **thứ tự
ngược** (khi response đi ra).

```csharp
app.Use(async (ctx, next) =>
{
    // chạy TRƯỚC (đi vào)
    await next();
    // chạy SAU (đi ra) — ⚠️ response có thể ĐÃ GỬI, không sửa header được nữa
});
app.Run(ctx => ctx.Response.WriteAsync("end"));   // terminal, KHÔNG gọi next
app.Map("/admin", branch => …);                   // rẽ nhánh pipeline riêng
```

**⚠️ Middleware class là SINGLETON** (được tạo một lần lúc dựng pipeline):
```csharp
public class MyMiddleware(RequestDelegate next, IScopedThing thing)  // ❌ captive dependency!
{
    public async Task InvokeAsync(HttpContext ctx, IScopedThing scoped)  // ✅ inject vào Invoke
        => await next(ctx);
}
```

**⚖️ Liên hệ HW**: `ExceptionHandlingMiddleware` đặt **sau** auth và **trước** `MapControllers`
⇒ nó bắt được exception của controller/MediatR, nhưng exception ném ra từ middleware **đứng trước
nó** (CORS, authentication) thì không. Muốn bắt tất cả thì nó phải là middleware **đầu tiên** —
đây là trade-off có chủ đích, nên nói ra khi phỏng vấn.

**Middleware vs Filter**: middleware biết `HttpContext` (mọi request, kể cả static file);
filter biết `ActionContext` (model đã bind, action đang gọi, kết quả action) và chỉ chạy cho MVC
endpoint.

---

## FW-4. Kestrel & System.IO.Pipelines — vì sao nhanh?

**❓ Vấn đề gốc của I/O truyền thống (`Stream`)**: bạn phải tự quản buffer, tự xử lý "message bị
cắt làm đôi giữa hai lần đọc", tự copy, và không có backpressure ⇒ code parser đầy `byte[]` tạm.

**⚙️ Pipelines** giải quyết: một vùng buffer **chia sẻ** giữa writer và reader, dữ liệu ở dạng
`ReadOnlySequence<byte>` (nhiều mảnh, **không copy**), và **backpressure** tự động (writer bị chặn
khi reader tụt lại).

```csharp
while (true)
{
    ReadResult result = await reader.ReadAsync(ct);
    ReadOnlySequence<byte> buffer = result.Buffer;

    while (TryParseLine(ref buffer, out var line))    // parse tại chỗ, 0 copy
        Process(line);

    reader.AdvanceTo(buffer.Start, buffer.End);       // "tôi đã xem tới đây, mới dùng tới đây"
    if (result.IsCompleted) break;
}
```

**Kiến trúc Kestrel**:
```
IConnectionListener (Socket) ──► SocketConnection (PipeReader/PipeWriter)
        ▼
Http1Connection / Http2Connection  ── parse header bằng vectorized SIMD, không alloc string thừa
        ▼
HttpProtocol → dựng HttpContext (được POOL và reset giữa các request!)
        ▼
RequestDelegate (pipeline của bạn)
```

**⚖️ Hệ quả bạn phải nhớ**: `HttpContext` **được tái sử dụng** sau khi request kết thúc.
⇒ **Không bao giờ** giữ `HttpContext`, `HttpRequest`, hay `ctx.Request.Body` sang một task chạy
nền. Muốn xử lý nền thì **copy dữ liệu ra** rồi mới `Task.Run` / đẩy vào Channel.

---

## FW-5. `IHttpContextAccessor` — AsyncLocal và cái giá của nó

```csharp
// Bên trong (rút gọn):
private static readonly AsyncLocal<HttpContextHolder> _current = new();
public HttpContext? HttpContext
{
    get => _current.Value?.Context;
    set => _current.Value = new HttpContextHolder { Context = value };
}
```

- Dựa trên `AsyncLocal<T>` ⇒ **flow theo ExecutionContext** qua mọi `await` (ASY-6).
- Dùng một **holder object** để khi request kết thúc, set `Context = null` là mọi nhánh async còn
  sót cũng nhìn thấy null ⇒ không giữ `HttpContext` sống.
- **Có phí**: bật `IHttpContextAccessor` khiến mọi async continuation phải capture/restore
  ExecutionContext nặng hơn.

**⚖️ Lời khuyên**: đừng dùng `IHttpContextAccessor` trong tầng Application/Domain. Trong project HW,
thông tin user nên đi qua một abstraction (`ICurrentUser`) được điền ở tầng Api — giữ đúng
layer boundary và test được.

---

## FW-6. Endpoint routing — URL được khớp thế nào?

**❓ Vấn đề gốc (routing kiểu cũ)**: middleware chạy **trước** khi biết action nào sẽ xử lý ⇒
authorization/CORS không biết endpoint có metadata gì.

**⚙️ Endpoint routing tách 2 bước**:
```
UseRouting()      → chọn Endpoint, gắn vào HttpContext (chưa chạy)
   … middleware ở giữa đọc được ctx.GetEndpoint()?.Metadata (ví dụ [Authorize], [EnableCors])
UseEndpoints()    → thực sự chạy endpoint.RequestDelegate
```

**Cơ chế khớp**: route template được compile thành một **cây quyết định (DFA)** theo từng segment,
không phải duyệt tuyến tính từng route ⇒ O(độ dài URL), không phụ thuộc số lượng route.
Khi nhiều route cùng khớp, chọn theo **precedence**: literal > constraint > parameter > catch-all;
hoà thì dùng `Order`, vẫn hoà thì `AmbiguousMatchException`.

```csharp
[HttpGet("{id}")]                          // literal thắng parameter
[HttpGet("special")]                       // /api/blog/special luôn vào đây, không vào {id}
```

---

## FW-7. MVC pipeline: model binding, validation, filter

**Thứ tự filter (thuộc lòng):**
```
Authorization → Resource (bọc cả binding, dùng cho cache) → [MODEL BINDING] → [VALIDATION]
   → Action (before) → ACTION → Action (after) → Exception (nếu ném) → Result (before) 
   → RESULT (format) → Result (after)
```

**Model binding** lấy dữ liệu từ **value providers** theo thứ tự: Form → Route → QueryString
(có thể ép bằng `[FromBody]`, `[FromQuery]`, `[FromRoute]`, `[FromHeader]`, `[FromServices]`).

```
⚠️ [FromBody] chỉ được có MỘT trên mỗi action — body là stream đọc-một-lần.
⚠️ Kiểu phức hợp KHÔNG có attribute: mặc định bind từ body (API controller) / từ form (MVC).
```

**Validation**: sau binding, MVC chạy `ObjectModelValidator` (DataAnnotations) và gom vào
`ModelState`. Với `[ApiController]`, ModelState invalid ⇒ **tự động trả 400 ProblemDetails**
mà không vào action.

**⚖️ Liên hệ HW**: project dùng **FluentValidation trong MediatR pipeline** (`ValidationBehavior`)
chứ không dùng filter/middleware validation. Ưu điểm: validate **command**, không phải DTO của
web ⇒ áp dụng được cho cả message consumer và saga, không phụ thuộc HTTP. Trả lời được điểm này
là ăn điểm kiến trúc.

---

## FW-8. Minimal API — vì sao nhanh hơn controller?

**⚙️ `RequestDelegateFactory`**: phân tích signature của lambda **một lần** lúc khởi động, sinh
sẵn code đọc tham số + gọi + ghi kết quả ⇒ bỏ toàn bộ tầng
`ActionInvoker → filter → model binder → result executor` của MVC.
Từ .NET 7 còn có **RequestDelegateGenerator** (source generator) sinh code lúc **compile** ⇒
AOT-friendly, bỏ luôn reflection lúc khởi động.

```csharp
app.MapPost("/blog", async (CreateBlogRequestDto dto, ISender sender)
    => Results.Ok(await sender.Send(dto.Adapt<CreateBlogCommand>())));
```

| | Controller | Minimal API |
|---|---|---|
| Overhead/request | cao hơn (filter, invoker) | thấp |
| Filter, model validation tự động | đầy đủ | có `AddEndpointFilter` (.NET 7+), ít hơn |
| Hợp với | API lớn nhiều convention | endpoint nhỏ, high-throughput, AOT |

**⚖️ Nói cho đúng**: khác biệt throughput chỉ đáng kể khi handler **rất nhẹ**. Nếu handler đụng DB
thì chênh lệch bị lu mờ. Chọn theo **tổ chức code**, đừng chọn theo benchmark hello-world.

---

## FW-9. Configuration & Options — `IOptions` vs `Snapshot` vs `Monitor`

**⚙️ Configuration** = danh sách provider xếp chồng, key phẳng dạng `"Messaging:UseOutbox"`;
provider sau **đè** provider trước:
```
appsettings.json → appsettings.{Env}.json → User Secrets (Dev) → Environment Variables → CLI args
```
(Env var dùng `__` thay `:` — `Messaging__UseOutbox=true`.)

| Interface | Lifetime | Đọc lại khi file đổi? | Dùng khi |
|---|---|---|---|
| `IOptions<T>` | Singleton | ❌ (đọc một lần) | cấu hình tĩnh |
| `IOptionsSnapshot<T>` | **Scoped** | ✅ mỗi request một lần | cấu hình đổi theo request |
| `IOptionsMonitor<T>` | Singleton | ✅ + `OnChange` callback | singleton/background service |

```csharp
services.AddOptions<MessagingOptions>()
        .Bind(config.GetSection("Messaging"))
        .ValidateDataAnnotations()
        .ValidateOnStart();          // ✅ sai config thì CHẾT LÚC KHỞI ĐỘNG, không phải lúc 3h sáng
```

**⚠️ Bẫy**: inject `IOptionsSnapshot<T>` (scoped) vào một singleton ⇒ captive dependency (FW-2).
Trong singleton **luôn** dùng `IOptionsMonitor<T>`.

---

## FW-10. `IHttpClientFactory` — nó thật sự giải quyết hai vấn đề

**❓ Vấn đề 1 — socket exhaustion**: `new HttpClient()` mỗi request ⇒ mỗi cái mở connection pool
riêng; `Dispose` xong socket còn ở trạng thái `TIME_WAIT` ~240s ⇒ cạn cổng.
**❓ Vấn đề 2 — DNS cũ**: một `static HttpClient` dùng mãi ⇒ giữ connection cũ, **không thấy** DNS
đổi (blue/green, failover) ⇒ gọi vào IP đã chết.

**⚙️ Cơ chế factory**: `HttpClient` thì rẻ và **stateless**; cái đắt là `HttpMessageHandler`.
Factory **pool handler** và xoay vòng chúng mỗi **2 phút** (mặc định):

```
HttpClient (tạo mới mỗi lần, rẻ)  ──►  HandlerLifetime pool
                                        ├─ handler A (đang active, hết hạn sau 2 phút)
                                        └─ handler cũ → chờ mọi request xong → dispose
```

```csharp
services.AddHttpClient<IPaymentApi, PaymentApi>(c =>
        {
            c.BaseAddress = new Uri("https://pay.example.com");
            c.Timeout = TimeSpan.FromSeconds(10);
        })
        .SetHandlerLifetime(TimeSpan.FromMinutes(2))
        .AddPolicyHandler(retryPolicy);        // DelegatingHandler chain — giống middleware!

// Không dùng factory? Ít nhất phải:
new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) };  // ✅ chữa DNS
```

**⚖️ `DelegatingHandler` chính là middleware pipeline phía client** — retry, logging, auth header,
correlation id đều nên là handler, không nhét vào từng call site.

---

## FW-11. Logging: vì sao `LoggerMessage` / source generator?

```csharp
// ❌ mỗi lần log: box tham số vào object[], format string ngay cả khi level bị tắt
_logger.LogInformation("Order {OrderId} created for {UserId}", orderId, userId);

// ✅ source-generated: 0 allocation khi level tắt, không reflection
public static partial class Log
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
                   Message = "Order {OrderId} created for {UserId}")]
    public static partial void OrderCreated(ILogger logger, string orderId, string userId);
}
```

- **Structured logging**: `{OrderId}` không phải là format string — nó là **tên trường** được
  serialize ra JSON ⇒ query được trong Seq/ELK/Loki. **Đừng** nội suy chuỗi (`$"..."`) vào log.
- **Scope** (`_logger.BeginScope`) dựa trên `AsyncLocal` ⇒ tự gắn `CorrelationId` cho mọi log
  trong request.
- Filter theo level được kiểm tra **trước** khi format: nhưng tham số vẫn bị box ở overload thường
  → đó chính là lý do có source generator.

---

## FW-12. EF Core — model, query pipeline & cache

**⚙️ Khởi động (một lần)**: convention + `OnModelCreating` + data annotations → `IModel` (bất biến).
Model building khá đắt (~vài trăm ms) ⇒ `.NET 6+` có **compiled model** (`dotnet ef dbcontext optimize`)
cho model lớn.

**⚙️ Mỗi query**:
```
LINQ expression tree
   ▼  QueryTranslationPreprocessor        (chuẩn hoá, xử lý navigation)
   ▼  QueryableMethodTranslatingVisitor   (LINQ → SelectExpression — cây SQL)
   ▼  ShapedQueryExpression               (SQL + "shaper": DbDataReader → entity)
   ▼  Expression.Compile()                → delegate được CACHE
   ▼  DbCommand + parameter
```

- **Query cache key** = hình dạng expression tree + provider + tracking mode. Vì thế:

```csharp
// ❌ giá trị nhúng thẳng vào tree ⇒ mỗi id là một cache entry + một plan SQL khác
var q = ctx.Blogs.Where(b => b.Id == "abc");   // EF vẫn parameter hoá được cái này…
// ⚠️ nhưng cái này thì KHÔNG:
var sql = $"SELECT * FROM Blogs WHERE Id = '{id}'";   // raw string → injection + không cache

// ✅ closure variable ⇒ EF parameter hoá tự động
var id = dto.Id;
var q2 = ctx.Blogs.Where(b => b.Id == id);     // → WHERE Id = @p0
```

- **Client evaluation bị cấm** (từ EF Core 3): nếu không dịch được sang SQL, EF **ném exception**
  thay vì âm thầm kéo cả bảng về RAM như EF Core 2. Đây là thay đổi cứu rất nhiều production.
- `IQueryable` vs `IEnumerable`: mọi thứ **sau** `.AsEnumerable()`/`.ToList()` chạy trên client.

**⚖️ Liên hệ HW**: `FindAll()` trả `IQueryable` **AsNoTracking**, còn `FindByIdAsync`/
`FindSingleAsync` là **tracked** — đúng ý đồ: query đọc thì no-tracking (nhanh, ít RAM), lệnh ghi
thì tracked (để `Update()` sinh UPDATE đúng cột).

---

## FW-13. Change Tracker — `DetectChanges` làm gì và tốn gì?

**⚙️ Cơ chế snapshot**: khi entity được **tracked**, EF lưu một **bản chụp giá trị gốc**.
`SaveChanges` gọi `DetectChanges`: duyệt **mọi entity × mọi property**, so sánh với snapshot →
suy ra `Added/Modified/Deleted/Unchanged` và **danh sách cột thay đổi**.

```
Chi phí ≈ O(số entity tracked × số property)   ⇒ tracking 10.000 entity = mỗi SaveChanges rất đắt
```

```csharp
ctx.Entry(blog).State;                      // xem state
ctx.Entry(blog).Property(b => b.Title).IsModified;
ctx.ChangeTracker.Entries().Count();        // bao nhiêu entity đang bị theo dõi?

// Tối ưu khi load nhiều để ĐỌC:
await ctx.Blogs.AsNoTracking().ToListAsync();                 // ✅ không snapshot, không identity map
await ctx.Blogs.AsNoTrackingWithIdentityResolution().ToListAsync(); // khi cần dedupe object trùng
```

- **Identity map**: trong một `DbContext`, một khoá chính chỉ ứng với **một** instance ⇒ `Find()`
  kiểm tra cache trước khi ra DB (khác `FirstOrDefault()` — luôn ra DB).
- **Change-tracking proxies** (notification-based): entity kế thừa proxy báo thay đổi ngay khi set
  property ⇒ không cần `DetectChanges` quét — đổi lại mọi property phải `virtual`.

**⚖️ Liên hệ HW**: lazy-loading proxies yêu cầu navigation property `virtual` (Castle
DynamicProxy sinh lớp con override để nạp dữ liệu khi truy cập lần đầu). Trade-off: rất dễ tạo
**N+1 query** một cách vô hình ⇒ khi cần đọc danh sách, dùng `includeProperties` của
`FindAll(...)` (eager) chứ đừng dựa vào lazy loading.

---

## FW-14. `SaveChanges` bên dưới: batching, transaction, concurrency

```
SaveChanges()
  ① DetectChanges
  ② Sắp xếp lệnh theo phụ thuộc khoá ngoại (parent trước child)
  ③ Gom thành ModificationCommandBatch  (SQL Server/MySQL: nhiều lệnh trong 1 round-trip)
  ④ Nếu chưa có transaction ⇒ TỰ MỞ một transaction bao trọn (all-or-nothing)
  ⑤ Đọc lại giá trị DB sinh ra (identity, computed, rowversion)
  ⑥ Cập nhật state = Unchanged, làm mới snapshot
```

```csharp
// Optimistic concurrency
public byte[] RowVersion { get; set; }      // [Timestamp] / IsRowVersion()
try { await ctx.SaveChangesAsync(ct); }
catch (DbUpdateConcurrencyException ex) { /* ai đó đã sửa trước — reload/merge/báo lỗi */ }

// Bulk không qua change tracker (EF Core 7+) — nhanh, nhưng KHÔNG raise domain event
await ctx.Blogs.Where(b => b.IsDelete == true).ExecuteDeleteAsync(ct);
```

**⚖️ Liên hệ HW (rất quan trọng)**: `TransactionBehavior.ExecuteAsync()` mở transaction bao quanh
handler ⇒ **domain event → outbox row** và dữ liệu nghiệp vụ được commit **cùng một transaction**.
Đó chính là lý do Outbox pattern tồn tại: không thể "vừa ghi DB vừa publish broker" atomically,
nên ta ghi **cả hai** vào DB, rồi một background processor mới publish. Hệ quả: publish từ đường
nào **không** gọi `SaveChanges` thì message **biến mất lặng lẽ** — đúng như cảnh báo trong `CLAUDE.md`.

---

## FW-15. `DbContext`: lifetime, pooling, thread-safety

- **Không thread-safe**: hai `await` song song trên cùng context ⇒
  `InvalidOperationException: A second operation was started on this context`.
  ⇒ Trong `Task.WhenAll`, **mỗi task một scope/context riêng**.
- **Scoped** là mặc định đúng: một context / một request / một unit of work.
- **`AddDbContextPool`**: tái sử dụng instance (reset state giữa các lần) → bớt chi phí khởi tạo.
  ⚠️ Không dùng được nếu context có state riêng (inject `ICurrentUser` vào ctor rồi giữ trong field).
- **`IDbContextFactory<T>`**: dành cho background service, Blazor, hoặc khi cần nhiều context song song.

```csharp
// ❌ chia sẻ context giữa các task song song
await Task.WhenAll(ids.Select(id => ctx.Blogs.FirstAsync(b => b.Id == id)));
// ✅
await Task.WhenAll(ids.Select(async id =>
{
    using var scope = scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    return await db.Blogs.FirstAsync(b => b.Id == id);
}));
```

---

## FW-16. MediatR bên dưới: pipeline behavior là decorator chain

**⚙️ Cơ chế `ISender.Send(request)`**:
```
1. Lấy type của request → tra cache Dictionary<Type, RequestHandlerWrapper>
   (chưa có thì Activator.CreateInstance một wrapper generic — reflection MỘT LẦN, rồi cache)
2. Wrapper resolve IRequestHandler<TRequest, TResponse> từ DI
3. Lấy IEnumerable<IPipelineBehavior<TRequest,TResponse>> từ DI (đúng THỨ TỰ ĐĂNG KÝ)
4. GẤP NGƯỢC danh sách behavior quanh handler — y hệt middleware (FW-3):
```
```csharp
RequestHandlerDelegate<TResponse> next = () => handler.Handle(request, ct);
foreach (var behavior in behaviors.Reverse())
{
    var current = next;
    next = () => behavior.Handle(request, current, ct);
}
return next();
```

**⚖️ Liên hệ HW** — pipeline thực tế của project:
```
LoggingBehavior      → log request/response, đo thời gian
ValidationBehavior   → FluentValidation; fail ⇒ ném trước khi mở transaction (tiết kiệm DB)
TransactionBehavior  → chỉ với IBaseCommand: IUnitOfWork.ExecuteAsync ⇒ BEGIN/COMMIT + outbox
Handler              → nghiệp vụ thuần
```
**Thứ tự đăng ký = thứ tự bọc** ⇒ Validation **phải** đứng trước Transaction (validate xong mới mở
transaction), và Logging ngoài cùng để đo cả hai. Đây là câu hỏi rất hay bị hỏi ngược:
*"Nếu đổi thứ tự Validation và Transaction thì sao?"* → mở transaction rồi mới phát hiện input sai,
tốn connection + rollback vô ích.

**Chi phí MediatR**: mỗi `Send` có ~1 dictionary lookup + N lần resolve DI + N delegate. Không đáng
kể so với một query DB, nhưng **đáng kể** nếu bạn gọi MediatR trong vòng lặp hàng chục nghìn lần —
lúc đó gọi thẳng handler.

---

## FW-17. `System.Text.Json` internals

**⚙️ Ba tầng API**, chọn theo nhu cầu:

| API | Allocation | Dùng khi |
|---|---|---|
| `Utf8JsonReader` (**ref struct**) | ~0 | parse thủ công, hiệu năng tối đa |
| `JsonDocument` (IDisposable!) | pool buffer | đọc DOM một lần rồi bỏ |
| `JsonSerializer` | POCO | mặc định |

- Làm việc **trực tiếp trên UTF-8 byte**, không chuyển sang UTF-16 string ⇒ nhanh hơn Newtonsoft
  đáng kể và ít rác hơn nhiều.
- `Utf8JsonReader` là `ref struct` ⇒ không dùng trong `async` (RT-15/MEM-14) — đó là lý do
  `JsonSerializer` có đường async riêng đọc theo chunk.
- **Source generator**:
```csharp
[JsonSerializable(typeof(BlogResponseDto))]
internal partial class AppJsonContext : JsonSerializerContext { }
JsonSerializer.Serialize(dto, AppJsonContext.Default.BlogResponseDto);  // 0 reflection, AOT-safe
```

**⚠️ Khác biệt với Newtonsoft hay cắn**: STJ mặc định **case-sensitive** khi deserialize (bật
`PropertyNameCaseInsensitive`), không tự xử lý vòng lặp tham chiếu (`ReferenceHandler.Preserve`),
và không serialize field (`IncludeFields`).

---

## FW-18. Caching: `IMemoryCache` vs `IDistributedCache` vs output cache

| | Ở đâu | Vấn đề |
|---|---|---|
| `IMemoryCache` | RAM của **từng instance** | scale-out ⇒ dữ liệu không nhất quán giữa các pod |
| `IDistributedCache` (Redis) | ngoài process | mọi thứ phải serialize; thêm network hop |
| `HybridCache` (.NET 9) | L1 memory + L2 Redis | có sẵn chống **cache stampede** |
| Output caching / Response caching | cả response HTTP | chỉ hợp GET không phụ thuộc user |

**⚠️ Ba bài toán cache phải biết tên**:
1. **Stampede** — cache miss lúc cao điểm ⇒ 1000 request cùng gọi DB. Chữa: khoá theo key
   (`SemaphoreSlim` per key) hoặc `HybridCache`.
2. **Invalidation** — dữ liệu đổi mà cache chưa hết hạn. Chữa: xoá key ngay trong command handler
   (hoặc qua domain event).
3. **`IMemoryCache` không giới hạn** ⇒ phải `SetSize` + `SizeLimit`, nếu không là leak có kiểm soát.

---

## FW-19. Message bus & idempotency (liên hệ pattern 09/10 của HW)

```
Command handler ──PublishAsync──► Outbox row (cùng transaction)  ✅ atomic với dữ liệu
                                        │  (BackgroundService, poll ≤ ~10s)
                                        ▼
                             IMessageBus → RabbitMQ / Kafka
                                        ▼
                             IMessageHandler<T> ở consumer
```

- **At-least-once**: broker có thể giao **trùng** (ack mất, retry, rebalance) ⇒ handler **bắt buộc
  idempotent**: bảng inbox `(MessageId)` unique, hoặc thao tác tự nhiên idempotent (`UPSERT`).
- **Ordering**: chỉ được bảo đảm trong một partition (Kafka) / một queue+consumer (Rabbit).
  Muốn theo thứ tự per-aggregate ⇒ partition key = aggregate id.
- **Poison message** ⇒ retry có giới hạn + **DLQ**, đừng retry vô hạn.
- **Saga** (pattern 10 của HW): trạng thái là **event stream**, `Apply()` là fold thuần
  (không clock, không id, không `Send`) để **replay** luôn cho ra cùng kết quả. Vì vậy **không bao
  giờ** được xoá/đổi tên một `ISagaEvent` — stream cũ vẫn phải replay được (đây chính là bài toán
  **schema versioning** của event sourcing).

---

## FW-20. Startup performance — request đầu tiên chậm vì những gì?

```
① Load assembly + JIT Tier-0 mọi thứ trên đường đi          → R2R giúp
② DI: build call site + compile delegate lần đầu            → ValidateOnBuild để lộ lỗi sớm
③ EF Core: model building lần đầu (~100–500ms)              → compiled model
④ Kestrel bind socket, TLS handshake                        
⑤ Mapster: TypeAdapterConfig compile lần đầu                → Compile() lúc khởi động
⑥ Kết nối DB đầu tiên (pool rỗng) + JIT của provider
```

```csharp
// Warm-up chủ động lúc khởi động (rất đáng làm cho container/K8s readiness)
TypeAdapterConfig.GlobalSettings.Compile();
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.CanConnectAsync();
```

**⚖️ Đo bằng gì**: `dotnet-trace collect --profile startup`, hoặc log timestamp quanh từng bước
`Build()`/`Run()`. Trong K8s, tách **liveness** và **readiness** để pod chỉ nhận traffic sau warm-up.

---

## FW-21. Câu hỏi tổng hợp: "Kể đường đi của `POST /api/blog` trong hệ thống của bạn"

Đây là câu hỏi **đắt giá nhất** ở vòng senior — nó kiểm tra toàn bộ track 11–14 cùng lúc.
Khung trả lời cho project HW:

1. **Socket → Kestrel**: `PipeReader` đọc byte, parse HTTP không alloc thừa, dựng `HttpContext`
   (object **được pool**).
2. **Middleware**: CORS → Authentication (đọc JWT, dựng `ClaimsPrincipal`) → Authorization →
   `ExceptionHandlingMiddleware` (bọc phần còn lại) → routing.
3. **Endpoint routing**: DFA khớp `POST /api/blog` → action `BlogController.Create`.
4. **Model binding**: đọc body qua `Utf8JsonReader` → `CreateBlogRequestDto` (record).
5. **Controller**: chỉ `dto.Adapt<CreateBlogCommand>()` rồi `ISender.Send(...)` — không nghiệp vụ.
6. **MediatR pipeline** (decorator chain, FW-16): Logging → Validation (FluentValidation) →
   Transaction (`IUnitOfWork.ExecuteAsync` ⇒ BEGIN TRAN).
7. **Handler**: `new Blog(...)` bên trong handler (không phải controller), gọi domain method →
   `RaiseDomainEvent(BlogCreatedDomainEvent)`; `_repository.Add(entity)` — mới chỉ đánh dấu
   `Added` trong ChangeTracker, **chưa** chạm DB.
8. **`SaveChanges`**: `DetectChanges` → batch INSERT; interceptor điền `CreatedAt/CreatedBy`;
   domain events được chuyển thành **outbox rows trong cùng transaction** → COMMIT.
9. **Sau commit**: outbox processor (BackgroundService) đọc row, route theo payload type
   (`IDomainEvent` → MediatR, `[Message]` → broker), publish, đánh dấu đã xử lý.
   Delivery **at-least-once**, độ trễ ≤ ~10s.
10. **Response**: `ApiResponseFactory` → `System.Text.Json` ghi thẳng UTF-8 vào `PipeWriter` → socket.
11. **Kết thúc request**: DI scope dispose ⇒ `DbContext` dispose ⇒ connection trả về pool;
    `HttpContext` được reset và trả về pool.

**Ba câu hỏi ngược mà interviewer hay hỏi tiếp** (chuẩn bị sẵn):
- *"Nếu commit xong mà app chết trước khi publish thì sao?"* → outbox row vẫn còn, processor
  publish lại khi khởi động ⇒ at-least-once, nên consumer phải idempotent.
- *"Vì sao không publish thẳng trong handler?"* → không atomic được với DB transaction;
  publish rồi rollback = message ma.
- *"Vì sao validation không đặt ở middleware?"* → command là biên nghiệp vụ, không phải HTTP;
  message consumer và saga cũng cần được validate (FW-7).

---

## ✅ Checklist tự kiểm tra — Phần 14

- [ ] Giải thích DI: descriptor → call site → compiled delegate; và 3 bẫy lifetime.
- [ ] Viết được vòng lặp `Build()` gấp middleware từ cuối về đầu, giải thích thứ tự.
- [ ] Nói được Kestrel + `System.IO.Pipelines` giải quyết vấn đề gì, và vì sao không giữ `HttpContext`.
- [ ] Phân biệt middleware vs filter, và thứ tự filter pipeline.
- [ ] Mô tả EF query pipeline + query cache key + vì sao cấm client evaluation.
- [ ] Giải thích `DetectChanges` tốn O(entity × property) và khi nào dùng `AsNoTracking`.
- [ ] Vẽ MediatR behavior chain và giải thích vì sao Validation đứng trước Transaction.
- [ ] Kể trọn đường đi `POST /api/blog` + trả lời 3 câu hỏi ngược ở FW-21.

⬅️ Quay lại [Mục lục](interview.NET.md) | Tiếp: [Phần 15 — Concurrency & Scaling ➡️](interview.NET.15-Concurrency-Scaling.md)
