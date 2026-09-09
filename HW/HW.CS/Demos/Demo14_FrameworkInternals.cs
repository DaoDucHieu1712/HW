using System.Diagnostics;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HW.CS.Demos;

/// <summary>
/// Runnable demos cho interview.NET.14-Framework-Internals.md (FW-1..FW-21).
///
/// Nhiều demo dùng ĐÚNG các thư viện thật của ASP.NET Core (ServiceCollection,
/// ApplicationBuilder, IOptionsMonitor, ILogger) chứ không phải mô phỏng —
/// nhờ &lt;FrameworkReference Include="Microsoft.AspNetCore.App" /&gt; trong .csproj.
/// Phần EF Core / MediatR được dựng lại ở dạng thu nhỏ để nhìn rõ CƠ CHẾ bên trong.
/// </summary>
public sealed class Demo14_FrameworkInternals : IDemoTopic
{
    public string Title => "14 — Framework Internals (ASP.NET Core, DI, EF, MediatR)";

    public IReadOnlyList<DemoItem> Items => new DemoItem[]
    {
        new("FW1.  DI: 3 lifetime & scope thật sự là gì", FW1_Lifetimes),
        new("FW2.  DI: captive dependency & ValidateScopes", FW2_CaptiveDependency),
        new("FW3.  DI: container SỞ HỮU mọi IDisposable nó tạo", FW3_DisposableOwnership),
        new("FW4.  DI: open generic + keyed service (.NET 8)", FW4_OpenGenericAndKeyed),
        new("FW5.  DI: call site được compile sau vài lần resolve", FW5_ResolveCost),
        new("FW6.  Middleware pipeline THẬT: gấp từ cuối về đầu", FW6_MiddlewarePipeline),
        new("FW7.  Middleware là singleton — bẫy inject scoped", FW7_MiddlewareIsSingleton),
        new("FW8.  Options: IOptions vs IOptionsMonitor + reload", FW8_OptionsPattern),
        new("FW9.  Logging: LoggerMessage vs log thường", FW9_Logging),
        new("FW10. IHttpContextAccessor = AsyncLocal", FW10_ContextAccessor),
        new("FW11. MediatR: behavior chain (thứ tự quyết định tất cả)", FW11_MediatRPipeline),
        new("FW12. EF: ChangeTracker snapshot & DetectChanges", FW12_ChangeTracker),
        new("FW13. EF: LINQ → expression tree → SQL + query cache", FW13_QueryPipeline),
        new("FW14. System.Text.Json: 3 tầng API & allocation", FW14_SystemTextJson),
        new("FW15. HttpClientFactory giải quyết 2 vấn đề gì", FW15_HttpClientFactory),
        new("FW16. Đường đi đầy đủ của POST /api/blog", FW16_EndToEndRequest),
    };

    // ── FW1 ───────────────────────────────────────────────────────────────
    public static void FW1_Lifetimes()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SingletonService>();
        services.AddScoped<ScopedService>();
        services.AddTransient<TransientService>();

        using var provider = services.BuildServiceProvider();

        Console.WriteLine("  Trong CÙNG một scope (≈ 1 HTTP request):");
        using (var scope1 = provider.CreateScope())
        {
            var sp = scope1.ServiceProvider;
            Console.WriteLine($"    Singleton : {sp.GetRequiredService<SingletonService>().Id} | {sp.GetRequiredService<SingletonService>().Id}");
            Console.WriteLine($"    Scoped    : {sp.GetRequiredService<ScopedService>().Id} | {sp.GetRequiredService<ScopedService>().Id}   ← giống nhau");
            Console.WriteLine($"    Transient : {sp.GetRequiredService<TransientService>().Id} | {sp.GetRequiredService<TransientService>().Id}   ← khác nhau");
        }

        Console.WriteLine("  Ở scope KHÁC (request thứ 2):");
        using (var scope2 = provider.CreateScope())
        {
            var sp = scope2.ServiceProvider;
            Console.WriteLine($"    Singleton : {sp.GetRequiredService<SingletonService>().Id}   ← vẫn y hệt (cache ở ROOT provider)");
            Console.WriteLine($"    Scoped    : {sp.GetRequiredService<ScopedService>().Id}   ← MỚI (cache ở IServiceScope)");
        }

        Console.WriteLine();
        Console.WriteLine("→ Lifetime thực chất chỉ là 'CACHE Ở ĐÂU': singleton ⇒ root provider, scoped ⇒ IServiceScope, transient ⇒ không cache.");
        Console.WriteLine("→ Trong ASP.NET Core, mỗi HTTP request tạo một scope; DbContext/repository của project HW là SCOPED");
        Console.WriteLine("  ⇒ một unit of work cho một request.");
    }

    // ── FW2 ───────────────────────────────────────────────────────────────
    public static void FW2_CaptiveDependency()
    {
        var services = new ServiceCollection();
        services.AddSingleton<BadSingleton>();     // ❌ ctor nhận ScopedService
        services.AddScoped<ScopedService>();

        // ValidateScopes: Development bật sẵn — phát hiện lỗi ngay lúc khởi động
        using var strict = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = false
        });

        try
        {
            using var scope = strict.CreateScope();
            _ = scope.ServiceProvider.GetRequiredService<BadSingleton>();
            Console.WriteLine("  (không ném — kiểm tra lại cấu hình)");
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine("  ✅ ValidateScopes BẮT ĐƯỢC lỗi:");
            Console.WriteLine($"     {Clean(ex.Message.Split('\n')[0])}");
        }

        Console.WriteLine();
        Console.WriteLine("  Vì sao nguy hiểm: singleton sống suốt đời app ⇒ nó GIAM luôn ScopedService (và DbContext bên trong)");
        Console.WriteLine("  ⇒ change tracker phình vô hạn, dữ liệu cũ, lỗi 'A second operation was started on this context'.");
        Console.WriteLine();
        Console.WriteLine("  ✅ Cách đúng — singleton nhận IServiceScopeFactory rồi TỰ tạo scope mỗi lần dùng:");
        Console.WriteLine("     using var scope = _scopeFactory.CreateScope();");
        Console.WriteLine("     var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();");
        Console.WriteLine();
        Console.WriteLine("  → Đây chính là lý do BackgroundService (outbox processor của HW) phải dùng IServiceScopeFactory.");
        Console.WriteLine("  → Nhớ bật ValidateOnBuild = true để lỗi lộ ra lúc KHỞI ĐỘNG, không phải lúc 3h sáng.");
    }

    // ── FW3 ───────────────────────────────────────────────────────────────
    public static void FW3_DisposableOwnership()
    {
        var services = new ServiceCollection();
        services.AddTransient<TrackedDisposable>();

        DisposableTracker.Reset();

        var provider = services.BuildServiceProvider();

        // (a) resolve từ ROOT provider — container giữ tới khi app tắt
        for (int i = 0; i < 3; i++) _ = provider.GetRequiredService<TrackedDisposable>();
        Console.WriteLine($"  Resolve 3 transient IDisposable từ ROOT provider → đã dispose: {DisposableTracker.Disposed}/3");
        Console.WriteLine("  ⚠️ Container GIỮ chúng để dispose lúc app tắt ⇒ trong web app = LEAK có kiểm soát nhưng vẫn là leak.");

        // (b) resolve trong một scope — được dispose khi scope kết thúc
        DisposableTracker.Reset();
        using (var scope = provider.CreateScope())
        {
            for (int i = 0; i < 3; i++) _ = scope.ServiceProvider.GetRequiredService<TrackedDisposable>();
            Console.WriteLine($"  Resolve 3 transient trong SCOPE, trước khi scope kết thúc → đã dispose: {DisposableTracker.Disposed}/3");
        }
        Console.WriteLine($"  Sau khi scope dispose                                    → đã dispose: {DisposableTracker.Disposed}/3 ✅");

        provider.Dispose();
        Console.WriteLine();
        Console.WriteLine("→ Luật: container SỞ HỮU và sẽ Dispose MỌI IDisposable mà nó tạo ra — kể cả TRANSIENT.");
        Console.WriteLine("→ Hệ quả: đừng resolve transient IDisposable từ root provider trong vòng lặp dài / background loop.");
        Console.WriteLine("→ Ngoại lệ: instance bạn TỰ new rồi AddSingleton(instance) thì container KHÔNG sở hữu (bạn tự dispose).");
    }

    // ── FW4 ───────────────────────────────────────────────────────────────
    public static void FW4_OpenGenericAndKeyed()
    {
        var services = new ServiceCollection();

        // Open generic — đúng như HW: AddScoped(typeof(IEFRepository<>), typeof(EFRepository<>))
        services.AddScoped(typeof(IMiniRepository<>), typeof(MiniRepository<>));

        // Keyed services (.NET 8)
        services.AddKeyedSingleton<IBus, RabbitBus>("rabbit");
        services.AddKeyedSingleton<IBus, KafkaBus>("kafka");

        // Nhiều implementation cho cùng một interface
        services.AddSingleton<IRule, RuleA>();
        services.AddSingleton<IRule, RuleB>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        Console.WriteLine($"  Open generic  : IMiniRepository<Blog>  → {sp.GetRequiredService<IMiniRepository<Blog>>().Describe()}");
        Console.WriteLine($"                  IMiniRepository<Order> → {sp.GetRequiredService<IMiniRepository<Order>>().Describe()}");
        Console.WriteLine("                  (container tự ĐÓNG KIỂU lúc resolve — không cần đăng ký từng entity)");
        Console.WriteLine();
        Console.WriteLine($"  Keyed service : \"rabbit\" → {sp.GetRequiredKeyedService<IBus>("rabbit").Name}");
        Console.WriteLine($"                  \"kafka\"  → {sp.GetRequiredKeyedService<IBus>("kafka").Name}");
        Console.WriteLine("                  ctor: public Handler([FromKeyedServices(\"rabbit\")] IBus bus)");
        Console.WriteLine();
        Console.WriteLine($"  GetRequiredService<IRule>() → {sp.GetRequiredService<IRule>().Name}   ← chỉ trả về cái ĐĂNG KÝ CUỐI CÙNG");
        Console.WriteLine($"  GetServices<IRule>()        → {string.Join(", ", sp.GetServices<IRule>().Select(r => r.Name))}   ← TẤT CẢ (dùng cho pipeline/strategy)");
        Console.WriteLine();
        Console.WriteLine("→ TryAdd… chỉ thêm nếu chưa có; TryAddEnumerable thêm vào TẬP HỢP mà không trùng implementation.");
        Console.WriteLine("→ Chính cơ chế GetServices<T>() là thứ MediatR dùng để lấy danh sách IPipelineBehavior (FW11).");
    }

    // ── FW5 ───────────────────────────────────────────────────────────────
    public static void FW5_ResolveCost()
    {
        var services = new ServiceCollection();
        services.AddTransient<Level1>();
        services.AddTransient<Level2>();
        services.AddTransient<Level3>();
        using var provider = services.BuildServiceProvider();

        var sw = Stopwatch.StartNew();
        _ = provider.GetRequiredService<Level1>();
        double first = sw.Elapsed.TotalMicroseconds;

        sw.Restart();
        for (int i = 0; i < 5; i++) _ = provider.GetRequiredService<Level1>();
        double next5 = sw.Elapsed.TotalMicroseconds / 5;

        for (int i = 0; i < 50_000; i++) _ = provider.GetRequiredService<Level1>();   // để engine compile

        sw.Restart();
        const int N = 200_000;
        for (int i = 0; i < N; i++) _ = provider.GetRequiredService<Level1>();
        double hot = sw.Elapsed.TotalMicroseconds / N;

        Console.WriteLine($"  Lần resolve ĐẦU TIÊN (dựng call site) : {first,9:F2} µs");
        Console.WriteLine($"  5 lần tiếp theo (interpreter)         : {next5,9:F2} µs / lần");
        Console.WriteLine($"  Sau khi engine COMPILE call site      : {hot,9:F3} µs / lần");
        Console.WriteLine();
        Console.WriteLine("→ Ba giai đoạn của container:");
        Console.WriteLine("    ① AddScoped<IFoo, Foo>()  → chỉ là một ServiceDescriptor (công thức), chưa reflection gì.");
        Console.WriteLine("    ② Build()                 → dựng CÂY CALL SITE: ConstructorCallSite(Foo, [CallSite(IBar), …]).");
        Console.WriteLine("    ③ Resolve nhiều lần       → engine COMPILE cây thành delegate (ILEmit/Expression)");
        Console.WriteLine("                                 ⇒ nhanh gần bằng gọi `new` trực tiếp.");
        Console.WriteLine();
        Console.WriteLine("→ Đây là một phần lý do REQUEST ĐẦU TIÊN của API luôn chậm (cùng với JIT Tier-0 và EF model building).");
    }

    // ── FW6 ───────────────────────────────────────────────────────────────
    public static void FW6_MiddlewarePipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        using var provider = services.BuildServiceProvider();

        // ĐÂY LÀ ApplicationBuilder THẬT của ASP.NET Core, không phải mô phỏng.
        var app = new ApplicationBuilder(provider);

        app.Use(async (ctx, next) =>
        {
            Console.WriteLine("    A → vào  (giống ExceptionHandlingMiddleware của HW)");
            await next();
            Console.WriteLine($"    A ← ra   (status lúc này = {ctx.Response.StatusCode}; header ĐÃ GỬI thì không sửa được nữa)");
        });

        app.Use(async (ctx, next) =>
        {
            Console.WriteLine("    B → vào  (giống Authentication)");
            await next();
            Console.WriteLine("    B ← ra");
        });

        app.Run(async ctx =>
        {
            Console.WriteLine("    C → TERMINAL (giống MapControllers) — KHÔNG gọi next");
            ctx.Response.StatusCode = 200;
            await ctx.Response.WriteAsync("ok");
        });

        RequestDelegate pipeline = app.Build();

        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        Console.WriteLine("  Chạy pipeline với một DefaultHttpContext:");
        pipeline(context).GetAwaiter().GetResult();

        Console.WriteLine();
        Console.WriteLine("  Build() gấp danh sách từ CUỐI về ĐẦU — đó là toàn bộ 'phép màu':");
        Console.WriteLine("    RequestDelegate app = ctx => { ctx.Response.StatusCode = 404; return Task.CompletedTask; };");
        Console.WriteLine("    for (int i = components.Count - 1; i >= 0; i--)   // NGƯỢC");
        Console.WriteLine("        app = components[i](app);");
        Console.WriteLine("    return app;                                       // A(B(C(terminal)))");
        Console.WriteLine();
        Console.WriteLine("→ Vì thế thứ tự `app.Use…` CHÍNH LÀ thứ tự chạy, và code sau `await next()` chạy theo thứ tự NGƯỢC.");
        Console.WriteLine("→ Liên hệ HW: ExceptionHandlingMiddleware đặt SAU auth ⇒ bắt được lỗi của controller/MediatR,");
        Console.WriteLine("  nhưng KHÔNG bắt được lỗi ném từ CORS/Authentication đứng trước nó. Đây là trade-off có chủ đích.");
    }

    // ── FW7 ───────────────────────────────────────────────────────────────
    public static void FW7_MiddlewareIsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<ScopedService>();
        using var provider = services.BuildServiceProvider();

        var app = new ApplicationBuilder(provider);
        app.UseMiddleware<CountingMiddleware>();
        app.Run(_ => Task.CompletedTask);
        var pipeline = app.Build();

        for (int i = 0; i < 3; i++)
        {
            using var scope = provider.CreateScope();
            var ctx = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            pipeline(ctx).GetAwaiter().GetResult();
        }

        Console.WriteLine($"  Middleware được KHỞI TẠO {CountingMiddleware.ConstructorCalls} lần cho 3 request  ← nó là SINGLETON");
        Console.WriteLine($"  InvokeAsync được gọi     {CountingMiddleware.InvokeCalls} lần");
        Console.WriteLine($"  ScopedService nhận qua tham số InvokeAsync: {string.Join(", ", CountingMiddleware.ScopedIds)}  ← mỗi request một instance ✅");
        Console.WriteLine();
        Console.WriteLine("  ❌ SAI:  public MyMiddleware(RequestDelegate next, IScopedThing thing)   // captive dependency!");
        Console.WriteLine("  ✅ ĐÚNG: public Task InvokeAsync(HttpContext ctx, IScopedThing thing)    // inject vào Invoke");
        Console.WriteLine();
        Console.WriteLine("→ Middleware được tạo MỘT LẦN lúc dựng pipeline ⇒ mọi thứ inject vào constructor đều là singleton.");
        Console.WriteLine("→ Middleware vs Filter: middleware biết HttpContext (mọi request, kể cả static file);");
        Console.WriteLine("  filter biết ActionContext (model đã bind, action đang gọi, kết quả) và chỉ chạy cho endpoint MVC.");
    }

    // ── FW8 ───────────────────────────────────────────────────────────────
    public static void FW8_OptionsPattern()
    {
        var initial = new Dictionary<string, string?>
        {
            ["Messaging:Provider"] = "RabbitMq",
            ["Messaging:BatchSize"] = "10"
        };

        IConfigurationRoot config = new ConfigurationBuilder().AddInMemoryCollection(initial).Build();

        var services = new ServiceCollection();
        services.AddOptions<MessagingOptions>()
                .Bind(config.GetSection("Messaging"))
                .Validate(o => o.BatchSize > 0, "BatchSize phải > 0");
        using var provider = services.BuildServiceProvider();

        var snapshotOfIOptions = provider.GetRequiredService<IOptions<MessagingOptions>>();
        var monitor = provider.GetRequiredService<IOptionsMonitor<MessagingOptions>>();

        monitor.OnChange(o => Console.WriteLine($"    [OnChange] cấu hình đổi → BatchSize = {o.BatchSize}"));

        Console.WriteLine($"  Ban đầu     : IOptions.BatchSize = {snapshotOfIOptions.Value.BatchSize}, IOptionsMonitor.BatchSize = {monitor.CurrentValue.BatchSize}");

        config["Messaging:BatchSize"] = "50";
        config.Reload();                                    // giả lập file appsettings.json đổi
        Thread.Sleep(50);

        Console.WriteLine($"  Sau reload  : IOptions.BatchSize = {snapshotOfIOptions.Value.BatchSize}  ← KHÔNG đổi (đọc một lần rồi cache)");
        Console.WriteLine($"                IOptionsMonitor   = {monitor.CurrentValue.BatchSize}  ← đã cập nhật ✅");
        Console.WriteLine();
        Console.WriteLine("  IOptions<T>          Singleton  đọc 1 lần            → cấu hình tĩnh");
        Console.WriteLine("  IOptionsSnapshot<T>  SCOPED     mỗi request 1 lần    → cấu hình đổi theo request");
        Console.WriteLine("  IOptionsMonitor<T>   Singleton  + OnChange callback  → singleton / background service");
        Console.WriteLine();
        Console.WriteLine("  ⚠️ Inject IOptionsSnapshot<T> (scoped) vào singleton = captive dependency (FW2).");
        Console.WriteLine("  ✅ .ValidateDataAnnotations().ValidateOnStart() ⇒ config sai thì CHẾT LÚC KHỞI ĐỘNG.");
        Console.WriteLine();
        Console.WriteLine("→ Thứ tự provider (sau đè trước): appsettings.json → appsettings.{Env}.json → User Secrets → ENV → CLI args.");
        Console.WriteLine("  Trong biến môi trường dùng `__` thay `:`  ⇒  Messaging__BatchSize=50");
    }

    // ── FW9 ───────────────────────────────────────────────────────────────
    public static void FW9_Logging()
    {
        using var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));  // Information bị TẮT
        ILogger logger = factory.CreateLogger("Demo");

        const int N = 20_000;
        string orderId = "ORD-1", userId = "U-9";

        long allocInterpolated = Alloc(() =>
        {
            for (int i = 0; i < N; i++)
                logger.LogInformation($"Order {orderId} created for {userId}");   // ❌ nội suy TRƯỚC khi kiểm tra level
        });

        long allocTemplate = Alloc(() =>
        {
            for (int i = 0; i < N; i++)
                logger.LogInformation("Order {OrderId} created for {UserId}", orderId, userId);  // ⚠️ box params
        });

        long allocSourceGen = Alloc(() =>
        {
            for (int i = 0; i < N; i++)
                logger.OrderCreated(orderId, userId);                              // ✅ LoggerMessage
        });

        Console.WriteLine($"  {N:N0} log ở level ĐANG BỊ TẮT:");
        Console.WriteLine($"    Nội suy chuỗi  $\"...\"          : {allocInterpolated,9:N0} byte");
        Console.WriteLine($"    Message template + params      : {allocTemplate,9:N0} byte");
        Console.WriteLine($"    LoggerMessage (source-gen-style): {allocSourceGen,9:N0} byte");
        Console.WriteLine();
        Console.WriteLine("→ `{OrderId}` KHÔNG phải format string — nó là TÊN TRƯỜNG được serialize ra JSON");
        Console.WriteLine("  ⇒ query được trong Seq/ELK/Loki. Nội suy chuỗi phá huỷ structured logging (và tốn RAM).");
        Console.WriteLine("→ LoggerMessage.Define / [LoggerMessage] source generator: kiểm tra level TRƯỚC, không box, không format thừa.");
        Console.WriteLine("→ Scope (logger.BeginScope) dựa trên AsyncLocal ⇒ tự gắn CorrelationId cho mọi log trong request (FW10).");
    }

    // ── FW10 ──────────────────────────────────────────────────────────────
    public static void FW10_ContextAccessor()
    {
        var accessor = new MiniContextAccessor();

        Fw14Helpers.RunTwoRequests(accessor).GetAwaiter().GetResult();

        Console.WriteLine();
        Console.WriteLine("  Bên trong IHttpContextAccessor (rút gọn):");
        Console.WriteLine("    private static readonly AsyncLocal<Holder> _current = new();");
        Console.WriteLine("    public HttpContext? HttpContext {");
        Console.WriteLine("        get => _current.Value?.Context;");
        Console.WriteLine("        set => _current.Value = new Holder { Context = value };  // holder để reset được");
        Console.WriteLine("    }");
        Console.WriteLine();
        Console.WriteLine("→ AsyncLocal flow theo ExecutionContext ⇒ mọi await trong request đều thấy đúng context của MÌNH,");
        Console.WriteLine("  kể cả khi continuation chạy trên thread khác, và hai request song song KHÔNG lẫn nhau.");
        Console.WriteLine("→ Dùng HOLDER object để khi request kết thúc, set Context = null là mọi nhánh async còn sót cũng thấy null");
        Console.WriteLine("  ⇒ không giữ HttpContext sống (nhớ: HttpContext được POOL và tái sử dụng — FW16).");
        Console.WriteLine("→ ⚠️ Có phí: bật IHttpContextAccessor làm mọi continuation phải capture/restore ExecutionContext nặng hơn.");
        Console.WriteLine("→ ⚠️ Đừng dùng nó ở tầng Application/Domain của HW — truyền qua abstraction (ICurrentUser) điền từ tầng Api.");
    }

    // ── FW11 ──────────────────────────────────────────────────────────────
    public static void FW11_MediatRPipeline()
    {
        Console.WriteLine("  Pipeline của project HW: Logging → Validation → Transaction → Handler");
        Console.WriteLine();

        var behaviors = new IMiniBehavior[]
        {
            new MiniLoggingBehavior(),
            new MiniValidationBehavior(),
            new MiniTransactionBehavior()
        };

        Console.WriteLine("  ① Command HỢP LỆ:");
        MiniMediator.Send(behaviors, new CreateBlogCommand("Clean Architecture"));

        Console.WriteLine();
        Console.WriteLine("  ② Command KHÔNG hợp lệ (title rỗng):");
        MiniMediator.Send(behaviors, new CreateBlogCommand(""));

        Console.WriteLine();
        Console.WriteLine("  ③ Nếu ĐỔI thứ tự: Transaction TRƯỚC Validation:");
        var wrongOrder = new IMiniBehavior[]
        {
            new MiniLoggingBehavior(),
            new MiniTransactionBehavior(),
            new MiniValidationBehavior()
        };
        MiniMediator.Send(wrongOrder, new CreateBlogCommand(""));
        Console.WriteLine("     → đã MỞ transaction rồi mới phát hiện input sai ⇒ tốn connection + rollback vô ích.");

        Console.WriteLine();
        Console.WriteLine("  Cách MediatR gấp behavior (giống hệt middleware — FW6):");
        Console.WriteLine("    RequestHandlerDelegate<T> next = () => handler.Handle(request, ct);");
        Console.WriteLine("    foreach (var b in behaviors.Reverse()) { var cur = next; next = () => b.Handle(request, cur, ct); }");
        Console.WriteLine("    return next();");
        Console.WriteLine();
        Console.WriteLine("→ ISender.Send: tra Dictionary<Type, RequestHandlerWrapper> (reflection MỘT LẦN rồi cache),");
        Console.WriteLine("  resolve handler + GetServices<IPipelineBehavior<,>>() từ DI, rồi gấp ngược.");
        Console.WriteLine("→ Chi phí mỗi Send ≈ 1 dictionary lookup + N lần resolve DI — không đáng kể so với một query DB,");
        Console.WriteLine("  nhưng ĐÁNG KỂ nếu gọi trong vòng lặp hàng chục nghìn lần (lúc đó gọi thẳng handler).");
    }

    // ── FW12 ──────────────────────────────────────────────────────────────
    public static void FW12_ChangeTracker()
    {
        var tracker = new MiniChangeTracker();

        var blog = new Blog { Id = "b1", Title = "Tiêu đề cũ", Author = "Hieu", Views = 10 };
        tracker.Attach(blog);                       // giống khi load entity có TRACKING

        Console.WriteLine($"  State ngay sau khi load  : {tracker.GetState(blog)}");

        blog.Title = "Tiêu đề MỚI";                 // sửa 1 property
        blog.Views = 11;                            // sửa thêm 1 property

        var changes = tracker.DetectChanges();
        Console.WriteLine($"  State sau khi sửa        : {tracker.GetState(blog)}");
        Console.WriteLine($"  Cột thay đổi             : {string.Join(", ", changes[blog])}");
        Console.WriteLine($"  SQL sinh ra              : {MiniChangeTracker.BuildUpdate(blog, changes[blog])}");
        Console.WriteLine("                             ← chỉ UPDATE cột THAY ĐỔI, không phải cả bảng");
        Console.WriteLine();

        // Chi phí DetectChanges tỉ lệ với (số entity × số property)
        foreach (int count in new[] { 1_000, 10_000, 50_000 })
        {
            var t = new MiniChangeTracker();
            for (int i = 0; i < count; i++)
                t.Attach(new Blog { Id = $"b{i}", Title = "t", Author = "a", Views = i });

            var sw = Stopwatch.StartNew();
            t.DetectChanges();
            Console.WriteLine($"  DetectChanges với {count,6:N0} entity tracked : {sw.Elapsed.TotalMilliseconds,7:F2} ms");
        }

        Console.WriteLine();
        Console.WriteLine("→ Cơ chế SNAPSHOT: khi entity được tracked, EF lưu một bản chụp giá trị gốc.");
        Console.WriteLine("  SaveChanges gọi DetectChanges: duyệt MỌI entity × MỌI property, so với snapshot.");
        Console.WriteLine("  ⇒ Chi phí O(entity × property) — tracking 50.000 entity thì mỗi SaveChanges đã rất đắt.");
        Console.WriteLine("→ Vì thế query CHỈ ĐỂ ĐỌC phải .AsNoTracking() (đúng như FindAll() của HW),");
        Console.WriteLine("  còn FindByIdAsync/FindSingleAsync thì tracked để entity.Update() sinh UPDATE đúng cột.");
        Console.WriteLine("→ Identity map: trong một DbContext, một khoá chính ↔ MỘT instance ⇒ Find() kiểm tra cache trước khi ra DB");
        Console.WriteLine("  (khác FirstOrDefault() — luôn ra DB).");
    }

    // ── FW13 ──────────────────────────────────────────────────────────────
    public static void FW13_QueryPipeline()
    {
        string author = "Hieu";
        int minViews = 100;

        // Đây là EXPRESSION TREE — dữ liệu mô tả code, không phải code đã compile
        Expression<Func<Blog, bool>> predicate = b => b.Author == author && b.Views > minViews;

        Console.WriteLine($"  LINQ (C#)         : b => b.Author == author && b.Views > minViews");
        Console.WriteLine($"  Expression tree   : {predicate.Body.NodeType} ⇒ {predicate.Body}");
        Console.WriteLine($"  Dịch sang SQL     : SELECT * FROM Blogs WHERE {MiniSqlTranslator.Translate(predicate.Body)}");
        Console.WriteLine("                      ← closure variable được PARAMETER HOÁ (@p0, @p1), không nhúng thẳng giá trị");
        Console.WriteLine();

        // Chi phí compile expression — vì sao EF phải CACHE query
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 200; i++) _ = predicate.Compile();
        double compileMs = sw.Elapsed.TotalMilliseconds / 200;

        var compiled = predicate.Compile();
        var blog = new Blog { Author = "Hieu", Views = 500 };
        sw.Restart();
        for (int i = 0; i < 200_000; i++) _ = compiled(blog);
        double invokeUs = sw.Elapsed.TotalMicroseconds / 200_000;

        Console.WriteLine($"  Expression.Compile()  : {compileMs,8:F3} ms MỖI LẦN  ← rất đắt");
        Console.WriteLine($"  Gọi delegate đã compile: {invokeUs,8:F5} µs mỗi lần  ← rất rẻ");
        Console.WriteLine($"  → chênh nhau khoảng {compileMs * 1000 / Math.Max(invokeUs, 0.00001):N0} lần");
        Console.WriteLine();
        Console.WriteLine("  Query pipeline của EF Core:");
        Console.WriteLine("    LINQ expression tree");
        Console.WriteLine("      ▼ QueryTranslationPreprocessor      chuẩn hoá, xử lý navigation");
        Console.WriteLine("      ▼ QueryableMethodTranslatingVisitor LINQ → SelectExpression (cây SQL)");
        Console.WriteLine("      ▼ ShapedQueryExpression             SQL + 'shaper' (DbDataReader → entity)");
        Console.WriteLine("      ▼ Expression.Compile()              → delegate được CACHE theo hình dạng tree");
        Console.WriteLine("      ▼ DbCommand + parameter");
        Console.WriteLine();
        Console.WriteLine("→ Vì compile đắt như trên, EF CACHE theo query cache key = hình dạng expression tree.");
        Console.WriteLine("→ Client evaluation bị CẤM từ EF Core 3: dịch không được thì NÉM lỗi, thay vì âm thầm kéo cả bảng về RAM.");
        Console.WriteLine("→ Mọi thứ SAU .AsEnumerable()/.ToList() đều chạy trên client — đó là ranh giới IQueryable ↔ IEnumerable.");
    }

    // ── FW14 ──────────────────────────────────────────────────────────────
    public static void FW14_SystemTextJson()
    {
        var dto = new BlogResponseDto("b1", "Clean Architecture", "Hieu", 1200);
        string json = JsonSerializer.Serialize(dto);
        byte[] utf8 = Encoding.UTF8.GetBytes(json);

        Console.WriteLine($"  JSON: {json}");
        Console.WriteLine();

        const int N = 5_000;
        long allocSerializer = Alloc(() => { for (int i = 0; i < N; i++) _ = JsonSerializer.Deserialize<BlogResponseDto>(utf8); });
        long allocDocument = Alloc(() =>
        {
            for (int i = 0; i < N; i++)
            {
                using var doc = JsonDocument.Parse(utf8);
                _ = doc.RootElement.GetProperty("Title").GetString();
            }
        });
        long allocReaderString = Alloc(() => { for (int i = 0; i < N; i++) _ = MiniJson.ReadTitle(utf8); });
        long allocReaderSpan = Alloc(() => { for (int i = 0; i < N; i++) _ = MiniJson.TitleLength(utf8); });

        Console.WriteLine($"  {N:N0} lần đọc field 'Title':");
        Console.WriteLine($"    JsonSerializer.Deserialize<T> : {allocSerializer,9:N0} byte  (dựng cả object)");
        Console.WriteLine($"    JsonDocument.Parse            : {allocDocument,9:N0} byte  (DOM, buffer được pool)");
        Console.WriteLine($"    Utf8JsonReader → GetString()  : {allocReaderString,9:N0} byte  (chỉ còn CHÍNH chuỗi kết quả)");
        Console.WriteLine($"    Utf8JsonReader → đọc trên span: {allocReaderSpan,9:N0} byte  ← 0 allocation thật sự");
        Console.WriteLine();
        Console.WriteLine("→ STJ làm việc TRỰC TIẾP trên UTF-8 byte, không chuyển sang UTF-16 string ⇒ nhanh & ít rác hơn Newtonsoft.");
        Console.WriteLine("→ Utf8JsonReader là `ref struct` ⇒ KHÔNG dùng được trong async (luật byref-safety, xem RT-15/MEM-14);");
        Console.WriteLine("  vì thế JsonSerializer có đường async riêng đọc theo chunk.");
        Console.WriteLine("→ Source generator ([JsonSerializable] + JsonSerializerContext): 0 reflection, AOT-safe, nhanh hơn.");
        Console.WriteLine();
        Console.WriteLine("  ⚠️ Khác Newtonsoft hay cắn: STJ mặc định CASE-SENSITIVE khi deserialize");
        Console.WriteLine("     (PropertyNameCaseInsensitive), không xử lý vòng lặp tham chiếu (ReferenceHandler.Preserve),");
        Console.WriteLine("     và không serialize field (IncludeFields).");
    }

    // ── FW15 ──────────────────────────────────────────────────────────────
    public static void FW15_HttpClientFactory()
    {
        Console.WriteLine("  ❓ Vấn đề 1 — SOCKET EXHAUSTION");
        Console.WriteLine("     `new HttpClient()` mỗi request ⇒ mỗi cái một connection pool riêng.");
        Console.WriteLine("     Dispose xong socket còn ở TIME_WAIT ~240s ⇒ cạn cổng ⇒ SocketException dưới tải.");
        Console.WriteLine();
        Console.WriteLine("  ❓ Vấn đề 2 — DNS CŨ");
        Console.WriteLine("     Một `static HttpClient` dùng mãi ⇒ giữ connection cũ, KHÔNG thấy DNS đổi");
        Console.WriteLine("     (blue/green deploy, failover) ⇒ vẫn gọi vào IP đã chết.");
        Console.WriteLine();
        Console.WriteLine("  ⚙️ Cơ chế factory: HttpClient thì RẺ và stateless; cái ĐẮT là HttpMessageHandler.");
        Console.WriteLine("     Factory POOL handler và xoay vòng chúng mỗi 2 phút (mặc định):");
        Console.WriteLine();
        Console.WriteLine("       HttpClient (tạo mới mỗi lần, rẻ) ──► handler pool");
        Console.WriteLine("                                            ├─ handler A (active, hết hạn sau 2 phút)");
        Console.WriteLine("                                            └─ handler cũ → chờ request xong → dispose");
        Console.WriteLine();
        Console.WriteLine("  ✅ services.AddHttpClient<IPaymentApi, PaymentApi>(c => { c.BaseAddress = …; c.Timeout = …; })");
        Console.WriteLine("             .SetHandlerLifetime(TimeSpan.FromMinutes(2))");
        Console.WriteLine("             .AddPolicyHandler(retryPolicy);     // DelegatingHandler chain");
        Console.WriteLine();
        Console.WriteLine("  ✅ Không dùng factory? Ít nhất phải:");
        Console.WriteLine("     new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) }  // chữa DNS");
        Console.WriteLine();
        Console.WriteLine("→ DelegatingHandler CHÍNH LÀ middleware pipeline phía client (cùng ý tưởng gấp hàm ở FW6):");
        Console.WriteLine("  retry, logging, auth header, correlation id đều nên là handler, không nhét vào từng call site.");
    }

    // ── FW16 ──────────────────────────────────────────────────────────────
    public static void FW16_EndToEndRequest()
    {
        string[] steps =
        {
            "①  TCP socket → Kestrel: PipeReader đọc byte, parse HTTP (SIMD, không alloc string thừa)",
            "    dựng HttpContext — object này ĐƯỢC POOL và reset giữa các request",
            "②  Middleware: CORS → Authentication (đọc JWT → ClaimsPrincipal) → Authorization",
            "    → ExceptionHandlingMiddleware (bọc phần còn lại) → routing",
            "③  Endpoint routing: cây quyết định (DFA) khớp POST /api/blog → BlogController.Create",
            "④  Model binding: đọc body qua Utf8JsonReader → CreateBlogRequestDto (record)",
            "⑤  Controller: dto.Adapt<CreateBlogCommand>() rồi ISender.Send(...) — KHÔNG nghiệp vụ",
            "⑥  MediatR chain: Logging → Validation (FluentValidation) → Transaction (BEGIN TRAN)",
            "⑦  Handler: new Blog(...) TRONG handler → domain method → RaiseDomainEvent(BlogCreated)",
            "    _repository.Add(entity) — mới chỉ đánh dấu Added trong ChangeTracker, CHƯA chạm DB",
            "⑧  SaveChanges: DetectChanges → batch INSERT; interceptor điền CreatedAt/CreatedBy;",
            "    domain events → OUTBOX ROWS trong CÙNG transaction → COMMIT",
            "⑨  Outbox processor (BackgroundService): đọc row, route theo payload type",
            "    (IDomainEvent → MediatR, [Message] → broker), publish, đánh dấu đã xử lý",
            "⑩  Response: ApiResponseFactory → System.Text.Json ghi UTF-8 thẳng vào PipeWriter → socket",
            "⑪  Kết thúc: DI scope dispose → DbContext dispose → connection về pool;",
            "    HttpContext được reset và trả về pool"
        };
        foreach (var s in steps) Console.WriteLine("  " + s);

        Console.WriteLine();
        Console.WriteLine("  ❓ BA CÂU HỎI NGƯỢC interviewer hay hỏi tiếp — chuẩn bị sẵn:");
        Console.WriteLine();
        Console.WriteLine("  1) \"Commit xong mà app chết trước khi publish thì sao?\"");
        Console.WriteLine("     → Outbox row vẫn còn; processor publish lại khi khởi động ⇒ AT-LEAST-ONCE");
        Console.WriteLine("       ⇒ consumer BẮT BUỘC idempotent (bảng inbox unique MessageId, hoặc UPSERT).");
        Console.WriteLine();
        Console.WriteLine("  2) \"Vì sao không publish thẳng trong handler?\"");
        Console.WriteLine("     → Không thể atomic với DB transaction. Publish rồi rollback = MESSAGE MA");
        Console.WriteLine("       (consumer xử lý một sự kiện chưa từng xảy ra). Đó chính là lý do Outbox tồn tại.");
        Console.WriteLine();
        Console.WriteLine("  3) \"Vì sao validation không đặt ở middleware/filter?\"");
        Console.WriteLine("     → Command là biên NGHIỆP VỤ, không phải biên HTTP. Message consumer và saga");
        Console.WriteLine("       cũng cần được validate — đặt ở MediatR pipeline thì cả ba đường đều được bảo vệ.");
        Console.WriteLine();
        Console.WriteLine("→ Trả lời trọn vẹn được câu này = bạn đã ghép được cả track 11–14 vào một mạch:");
        Console.WriteLine("  bộ nhớ (pooling HttpContext), runtime (JIT lần đầu), async (không giam thread),");
        Console.WriteLine("  và framework (DI scope, pipeline, transaction, outbox).");
    }

    // ── Helper ────────────────────────────────────────────────────────────

    /// <summary>Bỏ phần mangling của type file-local (`&lt;File&gt;HASH__Ten`) cho dễ đọc.</summary>
    internal static string Clean(string text)
        => System.Text.RegularExpressions.Regex.Replace(text, @"<[A-Za-z0-9_]+>[0-9A-F]{20,}__", "");

    private static long Alloc(Action action)
    {
        action();
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

}

// ═══════════════════════════════════════════════════════════════════════════
//  Helper types — DI
// ═══════════════════════════════════════════════════════════════════════════

file sealed class SingletonService { public string Id { get; } = $"S-{Guid.NewGuid().ToString()[..4]}"; }
file sealed class ScopedService { public string Id { get; } = $"C-{Guid.NewGuid().ToString()[..4]}"; }
file sealed class TransientService { public string Id { get; } = $"T-{Guid.NewGuid().ToString()[..4]}"; }

file sealed class BadSingleton(ScopedService scoped)
{
    public ScopedService Scoped { get; } = scoped;   // ❌ singleton giữ scoped = captive dependency
}

file static class DisposableTracker
{
    public static int Disposed;
    public static void Reset() => Disposed = 0;
}

file sealed class TrackedDisposable : IDisposable
{
    public void Dispose() => Interlocked.Increment(ref DisposableTracker.Disposed);
}

file interface IMiniRepository<TEntity> { string Describe(); }
file sealed class MiniRepository<TEntity> : IMiniRepository<TEntity>
{
    public string Describe() => $"MiniRepository<{Demo14_FrameworkInternals.Clean(typeof(TEntity).Name)}>";
}

file interface IBus { string Name { get; } }
file sealed class RabbitBus : IBus { public string Name => "RabbitMQ"; }
file sealed class KafkaBus : IBus { public string Name => "Kafka"; }

file interface IRule { string Name { get; } }
file sealed class RuleA : IRule { public string Name => "RuleA"; }
file sealed class RuleB : IRule { public string Name => "RuleB"; }

file sealed class Level3 { }
file sealed class Level2(Level3 l3) { public Level3 Inner { get; } = l3; }
file sealed class Level1(Level2 l2, Level3 l3) { public Level2 Inner { get; } = l2; public Level3 Other { get; } = l3; }

// ═══════════════════════════════════════════════════════════════════════════
//  Helper types — middleware / options / context
// ═══════════════════════════════════════════════════════════════════════════

file sealed class CountingMiddleware
{
    public static int ConstructorCalls;
    public static int InvokeCalls;
    public static readonly List<string> ScopedIds = new();

    private readonly RequestDelegate _next;

    public CountingMiddleware(RequestDelegate next)
    {
        _next = next;
        Interlocked.Increment(ref ConstructorCalls);
    }

    // ✅ scoped service inject vào InvokeAsync, KHÔNG phải constructor
    public async Task InvokeAsync(HttpContext context, ScopedService scoped)
    {
        Interlocked.Increment(ref InvokeCalls);
        lock (ScopedIds) ScopedIds.Add(scoped.Id);
        await _next(context);
    }
}

file sealed class MessagingOptions
{
    public string Provider { get; set; } = "None";
    public int BatchSize { get; set; }
}

file static class Fw14Helpers
{
    public static async Task RunTwoRequests(MiniContextAccessor accessor)
    {
        async Task HandleRequest(string id)
        {
            accessor.Current = new MiniRequestContext(id);
            Console.WriteLine($"  [{id}] bắt đầu, thread #{Environment.CurrentManagedThreadId}, accessor.Current = {accessor.Current!.Id}");
            await Task.Delay(Random.Shared.Next(20, 60));
            Console.WriteLine($"  [{id}] sau await, thread #{Environment.CurrentManagedThreadId}, accessor.Current = {accessor.Current!.Id}  ← vẫn ĐÚNG context của mình");
            accessor.Current = null;
        }

        await Task.WhenAll(HandleRequest("request-A"), HandleRequest("request-B"));
        Console.WriteLine($"  Sau khi cả hai xong, accessor.Current = {accessor.Current?.Id ?? "null"}");
    }
}

file sealed class MiniRequestContext(string id)
{
    public string Id { get; } = id;
}

/// <summary>Bản thu nhỏ của IHttpContextAccessor — cùng cơ chế AsyncLocal + holder.</summary>
file sealed class MiniContextAccessor
{
    private sealed class Holder { public MiniRequestContext? Context; }

    private static readonly AsyncLocal<Holder?> _current = new();

    public MiniRequestContext? Current
    {
        get => _current.Value?.Context;
        set
        {
            var holder = _current.Value;
            if (holder is not null) holder.Context = null;      // cắt mọi nhánh async còn sót
            if (value is not null) _current.Value = new Holder { Context = value };
        }
    }
}

file static class LoggerMessageDefinitions
{
    private static readonly Action<ILogger, string, string, Exception?> _orderCreated =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(1001, nameof(OrderCreated)),
            "Order {OrderId} created for {UserId}");

    public static void OrderCreated(this ILogger logger, string orderId, string userId)
        => _orderCreated(logger, orderId, userId, null);
}

// ═══════════════════════════════════════════════════════════════════════════
//  Helper types — MediatR thu nhỏ
// ═══════════════════════════════════════════════════════════════════════════

file sealed record CreateBlogCommand(string Title);

file interface IMiniBehavior
{
    string Name { get; }
    string Handle(CreateBlogCommand request, Func<string> next);
}

file sealed class MiniLoggingBehavior : IMiniBehavior
{
    public string Name => "Logging";
    public string Handle(CreateBlogCommand request, Func<string> next)
    {
        Console.WriteLine("     Logging   → vào");
        var sw = Stopwatch.StartNew();
        try { return next(); }
        finally { Console.WriteLine($"     Logging   ← ra ({sw.Elapsed.TotalMilliseconds:F1} ms)"); }
    }
}

file sealed class MiniValidationBehavior : IMiniBehavior
{
    public string Name => "Validation";
    public string Handle(CreateBlogCommand request, Func<string> next)
    {
        Console.WriteLine("     Validation→ kiểm tra");
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            Console.WriteLine("     Validation✖ FAIL — ném trước khi mở transaction ✅");
            throw new ArgumentException("Title là bắt buộc");
        }
        return next();
    }
}

file sealed class MiniTransactionBehavior : IMiniBehavior
{
    public string Name => "Transaction";
    public string Handle(CreateBlogCommand request, Func<string> next)
    {
        Console.WriteLine("     Transaction→ BEGIN TRAN");
        try
        {
            string result = next();
            Console.WriteLine("     Transaction← COMMIT (+ domain events → outbox rows cùng transaction)");
            return result;
        }
        catch
        {
            Console.WriteLine("     Transaction← ROLLBACK");
            throw;
        }
    }
}

file static class MiniMediator
{
    public static void Send(IMiniBehavior[] behaviors, CreateBlogCommand request)
    {
        Func<string> next = () =>
        {
            Console.WriteLine("     Handler   ✔ new Blog(...) + repository.Add()");
            return "blog-id-1";
        };

        // GẤP NGƯỢC danh sách behavior quanh handler — y hệt ApplicationBuilder.Build()
        foreach (var behavior in behaviors.Reverse())
        {
            var current = next;
            var b = behavior;
            next = () => b.Handle(request, current);
        }

        try { Console.WriteLine($"     ⇒ kết quả: {next()}"); }
        catch (ArgumentException ex) { Console.WriteLine($"     ⇒ lỗi: {ex.Message}"); }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Helper types — EF thu nhỏ
// ═══════════════════════════════════════════════════════════════════════════

file sealed class Blog
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public int Views { get; set; }
}

file sealed class Order { }

file sealed record BlogResponseDto(string Id, string Title, string Author, int Views);

/// <summary>Bản thu nhỏ của EF ChangeTracker: snapshot giá trị gốc rồi so sánh khi DetectChanges.</summary>
file sealed class MiniChangeTracker
{
    private readonly Dictionary<Blog, (string Title, string Author, int Views)> _snapshots = new();
    private readonly Dictionary<Blog, string> _states = new();

    public void Attach(Blog blog)
    {
        _snapshots[blog] = (blog.Title, blog.Author, blog.Views);   // ← SNAPSHOT
        _states[blog] = "Unchanged";
    }

    public string GetState(Blog blog) => _states.TryGetValue(blog, out var s) ? s : "Detached";

    public Dictionary<Blog, List<string>> DetectChanges()
    {
        var result = new Dictionary<Blog, List<string>>();
        foreach (var (blog, snapshot) in _snapshots)          // O(entity × property)
        {
            var changed = new List<string>();
            if (blog.Title != snapshot.Title) changed.Add(nameof(Blog.Title));
            if (blog.Author != snapshot.Author) changed.Add(nameof(Blog.Author));
            if (blog.Views != snapshot.Views) changed.Add(nameof(Blog.Views));

            _states[blog] = changed.Count > 0 ? "Modified" : "Unchanged";
            result[blog] = changed;
        }
        return result;
    }

    public static string BuildUpdate(Blog blog, List<string> changedColumns)
        => changedColumns.Count == 0
            ? "(không có gì để update)"
            : $"UPDATE Blogs SET {string.Join(", ", changedColumns.Select((c, i) => $"{c} = @p{i}"))} WHERE Id = @id";
}

/// <summary>Dịch expression tree sang SQL — chính là việc mà QueryableMethodTranslatingVisitor của EF làm.</summary>
file static class MiniSqlTranslator
{
    private static int _paramIndex;

    public static string Translate(Expression expression)
    {
        _paramIndex = 0;
        return Visit(expression);
    }

    private static string Visit(Expression e) => e switch
    {
        BinaryExpression b => b.NodeType switch
        {
            ExpressionType.AndAlso => $"({Visit(b.Left)} AND {Visit(b.Right)})",
            ExpressionType.OrElse => $"({Visit(b.Left)} OR {Visit(b.Right)})",
            ExpressionType.Equal => $"{Visit(b.Left)} = {Visit(b.Right)}",
            ExpressionType.GreaterThan => $"{Visit(b.Left)} > {Visit(b.Right)}",
            ExpressionType.LessThan => $"{Visit(b.Left)} < {Visit(b.Right)}",
            _ => $"<{b.NodeType}>"
        },
        // property của entity → tên cột
        MemberExpression m when m.Expression is ParameterExpression => m.Member.Name,
        // biến closure → PARAMETER (đây là lý do EF parameter hoá tự động)
        MemberExpression => $"@p{_paramIndex++}",
        ConstantExpression c => c.Value is string s ? $"'{s}'" : $"{c.Value}",
        _ => e.ToString()
    };
}

file static class MiniJson
{
    /// <summary>Đọc một field bằng Utf8JsonReader — ref struct, gần như không allocate.</summary>
    public static string? ReadTitle(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("Title"u8))
            {
                reader.Read();
                return reader.GetString();
            }
        }
        return null;
    }

    /// <summary>Đọc mà KHÔNG tạo string — thao tác thẳng trên UTF-8 span ⇒ 0 allocation.</summary>
    public static int TitleLength(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("Title"u8))
            {
                reader.Read();
                return reader.ValueSpan.Length;
            }
        }
        return -1;
    }
}
