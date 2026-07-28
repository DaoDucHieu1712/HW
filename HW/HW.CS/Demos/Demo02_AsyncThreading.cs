using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace HW.CS.Demos;

/// <summary>
/// Runnable demos for interview.NET.02-Async-Threading.md.
/// 15 core questions (Q26..Q40) + 15 deep-dive questions (D1..D15).
///
/// Mỗi demo là `static void` (khớp interface IDemoTopic). Phần async bên trong
/// được chạy bằng `.GetAwaiter().GetResult()` cho tiện chạy trong console.
/// Comment giải thích TỪNG BƯỚC để đọc kèm câu hỏi.
/// </summary>
public sealed class Demo02_AsyncThreading : IDemoTopic
{
    public string Title => "02 — Async / Await & Multithreading";

    public IReadOnlyList<DemoItem> Items => new DemoItem[]
    {
        new("Q26. async/await là state machine", Q26_AsyncStateMachine),
        new("Q27. Task vs Thread", Q27_TaskVsThread),
        new("Q28. Task.Run (CPU) vs async thuần (I/O)", Q28_TaskRunVsAsync),
        new("Q29. Deadlock với .Result / .Wait()", Q29_DeadlockResult),
        new("Q30. ConfigureAwait(false)", Q30_ConfigureAwait),
        new("Q31. CancellationToken + timeout", Q31_CancellationToken),
        new("Q32. Task.WhenAll vs Task.WhenAny", Q32_WhenAllWhenAny),
        new("Q33. ValueTask (cache hit đồng bộ)", Q33_ValueTask),
        new("Q34. Race condition & cách phòng", Q34_RaceCondition),
        new("Q35. lock nên lock trên gì", Q35_Lock),
        new("Q36. Interlocked (atomic)", Q36_Interlocked),
        new("Q37. SemaphoreSlim vs lock", Q37_SemaphoreSlim),
        new("Q38. ThreadPool & starvation", Q38_ThreadPool),
        new("Q39. IAsyncEnumerable + await foreach", Q39_AsyncEnumerable),
        new("Q40. Exception trong async", Q40_AsyncExceptions),

        new("D1.  await KHÔNG tạo thread mới", D1_AwaitNoNewThread),
        new("D2.  State machine allocate khi nào", D2_StateMachineAllocation),
        new("D3.  SynchronizationContext (console/ASP.NET Core = null)", D3_SynchronizationContext),
        new("D4.  Tái hiện deadlock .Result (giải thích + fix)", D4_DeadlockStepByStep),
        new("D5.  Thread pool starvation", D5_ThreadPoolStarvation),
        new("D6.  TaskCompletionSource bắc cầu callback", D6_TaskCompletionSource),
        new("D7.  async void nguy hiểm", D7_AsyncVoidDanger),
        new("D8.  Parallel.ForEachAsync (I/O song song có giới hạn)", D8_ParallelForEachAsync),
        new("D9.  volatile & memory barrier", D9_Volatile),
        new("D10. Double-checked locking / Lazy<T>", D10_DoubleCheckedLocking),
        new("D11. Interlocked.CompareExchange (lock-free)", D11_CompareExchange),
        new("D12. Giới hạn số async đồng thời", D12_LimitConcurrency),
        new("D13. Token truyền xuống nhưng không dừng", D13_CancellationNotStopping),
        new("D14. Nhiều exception trong WhenAll", D14_WhenAllAggregate),
        new("D15. ThreadLocal vs AsyncLocal", D15_ThreadLocalVsAsyncLocal),
    };

    // Tiện ích: in kèm thread id đang chạy để thấy async đổi thread.
    private static void Log(string msg) =>
        Console.WriteLine($"[thread {Environment.CurrentManagedThreadId,2}] {msg}");

    // ── Q26 ───────────────────────────────────────────────────────────────
    public static void Q26_AsyncStateMachine()
    {
        // Compiler biến method có 'await' thành 1 state machine:
        //   - chạy đồng bộ tới chỗ 'await'
        //   - gặp await (chưa xong) → TRẢ control về caller, GIẢI PHÓNG thread
        //   - khi tác vụ xong → chạy tiếp phần sau await (continuation)
        static async Task<string> GetDataAsync()
        {
            Log("B1: bắt đầu (chạy đồng bộ tới await)");
            // Bước 2: await I/O — thread được giải phóng, KHÔNG bị block.
            await Task.Delay(100); // giả lập httpClient.GetStringAsync(...)
            // Bước 3: I/O xong → tiếp tục từ đây (có thể trên thread khác).
            Log("B3: I/O xong, chạy tiếp continuation");
            return "result";
        }

        var result = GetDataAsync().GetAwaiter().GetResult();
        Console.WriteLine($"→ kết quả = {result}");
        Console.WriteLine("Câu chốt: async GIẢI PHÓNG thread khi chờ, KHÔNG phải chạy song song.");
    }

    // ── Q27 ───────────────────────────────────────────────────────────────
    public static void Q27_TaskVsThread()
    {
        // Thread: đơn vị OS-level, tạo/huỷ đắt (~1MB stack). Tự quản lý vòng đời.
        var thread = new Thread(() => Log("Thread thủ công đang chạy"));
        thread.Start();
        thread.Join(); // chặn tới khi thread xong

        // Task: abstraction trên ThreadPool, hỗ trợ continuation/cancel/result/tổ hợp.
        int result = Task.Run(() => 1 + 1).GetAwaiter().GetResult();
        Log($"Task.Run(() => 1+1) = {result}");
        Console.WriteLine("→ Ưu tiên Task/async thay vì new Thread thủ công.");
    }

    // ── Q28 ───────────────────────────────────────────────────────────────
    public static void Q28_TaskRunVsAsync()
    {
        // CPU-bound: đẩy tính toán nặng lên thread pool bằng Task.Run
        // để không chặn thread gọi (vd UI thread / request thread).
        long sum = Task.Run(() =>
        {
            long s = 0;
            for (int i = 0; i < 50_000_000; i++) s += i; // "tính toán nặng"
            return s;
        }).GetAwaiter().GetResult();
        Log($"CPU-bound qua Task.Run → sum={sum}");

        // I/O-bound: KHÔNG cần Task.Run, chỉ await method async có sẵn.
        // (ở đây giả lập bằng Task.Delay thay cho File.ReadAllTextAsync)
        static async Task<string> ReadFileAsync()
        {
            await Task.Delay(50); // = await File.ReadAllTextAsync("data.txt")
            return "file-content";
        }
        var content = ReadFileAsync().GetAwaiter().GetResult();
        Log($"I/O-bound await trực tiếp → {content}");
        Console.WriteLine("❌ Anti-pattern: Task.Run bọc I/O trong ASP.NET → lãng phí thread pool.");
    }

    // ── Q29 ───────────────────────────────────────────────────────────────
    public static void Q29_DeadlockResult()
    {
        // Cơ chế deadlock (trong WPF/WinForms/ASP.NET classic — nơi CÓ SynchronizationContext):
        //   1. thread gọi .Result → BLOCK thread, nhưng GIỮ context
        //   2. continuation sau await cần chính thread/context đó để chạy
        //   3. thread đang bị block → continuation không chạy → task không complete → treo
        //
        // Console KHÔNG có SynchronizationContext nên bản dưới KHÔNG treo,
        // nhưng ta vẫn minh hoạ cách viết ĐÚNG: async all the way.
        static async Task<string> GetDataAsync()
        {
            await Task.Delay(50);
            return "data";
        }
        static async Task<string> GetDataProperly() => await GetDataAsync(); // ✅

        var ok = GetDataProperly().GetAwaiter().GetResult();
        Console.WriteLine($"→ async all the way, không block giữa chừng: {ok}");
        Console.WriteLine("❌ return GetDataAsync().Result trong UI/ASP.NET classic = TREO.");
    }

    // ── Q30 ───────────────────────────────────────────────────────────────
    public static void Q30_ConfigureAwait()
    {
        // ConfigureAwait(false): "continuation KHÔNG cần quay lại context gốc".
        //   → tránh deadlock kiểu .Result, nhanh hơn chút.
        //   → nên dùng trong LIBRARY tái sử dụng (không biết caller có context không).
        static async Task<int> LibraryMethodAsync()
        {
            var data = await Task.FromResult(21).ConfigureAwait(false);
            // continuation chạy trên thread pool bất kỳ, không bám context gốc
            return data * 2;
        }
        var r = LibraryMethodAsync().GetAwaiter().GetResult();
        Console.WriteLine($"→ LibraryMethodAsync() = {r}");
        Console.WriteLine("ASP.NET Core không có SyncContext nên ít quan trọng, nhưng vẫn nên dùng ở thư viện.");
    }

    // ── Q31 ───────────────────────────────────────────────────────────────
    public static void Q31_CancellationToken()
    {
        // CancellationToken là hợp tác (cooperative): phải chủ động kiểm tra + truyền xuống.
        static async Task ProcessAsync(CancellationToken ct)
        {
            for (int i = 0; i < 1000; i++)
            {
                ct.ThrowIfCancellationRequested(); // kiểm tra huỷ → ném OperationCanceledException
                await Task.Delay(50, ct);           // truyền token xuống API dưới
            }
        }

        // CancellationTokenSource tự phát tín hiệu huỷ sau 200ms.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        try
        {
            ProcessAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("→ Đã huỷ sau ~200ms (cooperative cancellation).");
        }
    }

    // ── Q32 ───────────────────────────────────────────────────────────────
    public static void Q32_WhenAllWhenAny()
    {
        static async Task<int> WorkAsync(int ms, int value)
        {
            await Task.Delay(ms);
            return value;
        }

        // WhenAll: chạy SONG SONG, chờ tất cả. Tổng thời gian ~= task lâu nhất.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int[] all = Task.WhenAll(WorkAsync(150, 1), WorkAsync(150, 2), WorkAsync(150, 3))
                        .GetAwaiter().GetResult();
        sw.Stop();
        Console.WriteLine($"WhenAll: [{string.Join(",", all)}] trong {sw.ElapsedMilliseconds}ms (~150 vì song song)");

        // WhenAny: xong khi task ĐẦU TIÊN hoàn thành → làm timeout/race.
        var dataTask = WorkAsync(500, 99);
        var timeoutTask = Task.Delay(200);
        var completed = Task.WhenAny(dataTask, timeoutTask).GetAwaiter().GetResult();
        Console.WriteLine(completed == timeoutTask ? "WhenAny: Timeout! (data quá chậm)" : "WhenAny: data về trước");
    }

    // ── Q33 ───────────────────────────────────────────────────────────────
    public static void Q33_ValueTask()
    {
        var cache = new Dictionary<int, string> { [1] = "cached-1" };

        // ValueTask: tránh allocate Task khi kết quả THƯỜNG có sẵn đồng bộ (cache hit).
        ValueTask<string> GetAsync(int id)
        {
            if (cache.TryGetValue(id, out var hit))
                return new ValueTask<string>(hit);              // đồng bộ, KHÔNG allocate Task
            return new ValueTask<string>(LoadAsync(id));         // async khi cache miss
        }
        static async Task<string> LoadAsync(int id)
        {
            await Task.Delay(50);
            return $"db-{id}";
        }

        Console.WriteLine($"cache hit  (id=1): {GetAsync(1).GetAwaiter().GetResult()} (đồng bộ, 0 alloc)");
        Console.WriteLine($"cache miss (id=2): {GetAsync(2).GetAwaiter().GetResult()} (async)");
        Console.WriteLine("⚠️ ValueTask: KHÔNG await 2 lần, KHÔNG dùng lại. Chỉ tối ưu ở hot path.");
    }

    // ── Q34 ───────────────────────────────────────────────────────────────
    public static void Q34_RaceCondition()
    {
        const int n = 100_000;

        // ❌ counter++ KHÔNG atomic (đọc-tăng-ghi) → nhiều thread ghi đè nhau → sai.
        int bad = 0;
        Parallel.For(0, n, _ => bad++);
        Console.WriteLine($"❌ counter++      : {bad} (kỳ vọng {n}, thường THIẾU)");

        // ✅ lock: tuần tự hoá critical section.
        int withLock = 0;
        object gate = new();
        Parallel.For(0, n, _ => { lock (gate) withLock++; });
        Console.WriteLine($"✅ lock           : {withLock}");

        // ✅ Interlocked: atomic, nhanh hơn lock cho phép toán đơn giản.
        int interlocked = 0;
        Parallel.For(0, n, _ => Interlocked.Increment(ref interlocked));
        Console.WriteLine($"✅ Interlocked    : {interlocked}");

        // ✅ Concurrent collection.
        var bag = new ConcurrentBag<int>();
        Parallel.For(0, n, i => bag.Add(i));
        Console.WriteLine($"✅ ConcurrentBag  : {bag.Count}");
    }

    // ── Q35 ───────────────────────────────────────────────────────────────
    public static void Q35_Lock()
    {
        var account = new BankAccount(1000m);
        // 100 lần rút song song, mỗi lần 10 → còn 0 nếu lock đúng.
        Parallel.For(0, 100, _ => account.Withdraw(10m));
        Console.WriteLine($"→ Balance sau 100 lần rút song song = {account.Balance} (đúng nhờ lock)");
        Console.WriteLine("❌ KHÔNG lock(this) / lock(typeof(X)) / lock(\"string\") — code ngoài lock cùng → deadlock.");
    }

    // ── Q36 ───────────────────────────────────────────────────────────────
    public static void Q36_Interlocked()
    {
        int counter = 0;
        long total = 0;

        Interlocked.Increment(ref counter);           // ++ atomic
        Interlocked.Increment(ref counter);
        Interlocked.Decrement(ref counter);           // -- atomic
        Interlocked.Add(ref total, 100);              // += atomic
        long old = Interlocked.Exchange(ref total, 0); // gán + trả giá trị cũ

        Console.WriteLine($"counter={counter}, Exchange trả về old total={old}, total hiện tại={total}");

        // CompareExchange: "nếu counter == comparand thì gán value" (atomic).
        int prev = Interlocked.CompareExchange(ref counter, 10, comparand: 1); // counter đang =1 → thành 10
        Console.WriteLine($"CompareExchange: prev={prev}, counter={counter}");
    }

    // ── Q37 ───────────────────────────────────────────────────────────────
    public static void Q37_SemaphoreSlim()
    {
        // lock KHÔNG await được. SemaphoreSlim giới hạn N + có WaitAsync().
        using var semaphore = new SemaphoreSlim(3); // tối đa 3 đồng thời
        int current = 0, peak = 0;

        async Task WorkAsync(int id)
        {
            await semaphore.WaitAsync();              // chờ slot (async, không block thread)
            try
            {
                int now = Interlocked.Increment(ref current);
                InterlockedMax(ref peak, now);        // theo dõi đỉnh đồng thời
                await Task.Delay(80);
            }
            finally
            {
                Interlocked.Decrement(ref current);
                semaphore.Release();                  // ✅ luôn release trong finally
            }
        }

        var tasks = Enumerable.Range(0, 9).Select(WorkAsync);
        Task.WhenAll(tasks).GetAwaiter().GetResult();
        Console.WriteLine($"→ 9 task, giới hạn 3 → số đồng thời cao nhất quan sát được = {peak} (≤ 3)");
    }

    // ── Q38 ───────────────────────────────────────────────────────────────
    public static void Q38_ThreadPool()
    {
        // Thread pool: tập thread tái sử dụng do runtime quản lý.
        ThreadPool.GetMinThreads(out int minW, out _);
        ThreadPool.GetMaxThreads(out int maxW, out _);
        ThreadPool.GetAvailableThreads(out int availW, out _);
        Console.WriteLine($"worker threads: min={minW}, max={maxW}, available≈{availW}");

        var done = new ManualResetEventSlim();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Log("chạy trên 1 thread pool thread");
            done.Set();
        });
        done.Wait();
        Console.WriteLine("❌ sync-over-async (.Result) CHẶN thread pool thread → thread starvation.");
        Console.WriteLine("✅ async all the way → thread được trả về pool khi chờ.");
    }

    // ── Q39 ───────────────────────────────────────────────────────────────
    public static void Q39_AsyncEnumerable()
    {
        // Stream từng phần tử bất đồng bộ, không load hết vào RAM.
        static async IAsyncEnumerable<int> GetOrdersAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            for (int i = 1; i <= 3; i++)
            {
                await Task.Delay(50, ct);  // giả lập đọc từng dòng từ DB reader
                yield return i * 10;        // trả phần tử NGAY khi có
            }
        }

        static async Task ConsumeAsync()
        {
            await foreach (var order in GetOrdersAsync())
                Console.WriteLine($"nhận order = {order}");
        }
        ConsumeAsync().GetAwaiter().GetResult();
    }

    // ── Q40 ───────────────────────────────────────────────────────────────
    public static void Q40_AsyncExceptions()
    {
        // Exception được LƯU trong Task, ném lại khi await.
        static async Task<int> DivideAsync(int a, int b)
        {
            await Task.Delay(10);
            return a / b; // ném khi b == 0
        }

        try
        {
            _ = DivideAsync(10, 0).GetAwaiter().GetResult();
        }
        catch (DivideByZeroException)
        {
            Console.WriteLine("→ Bắt được DivideByZeroException qua await.");
        }

        // ❌ async void: exception KHÔNG bắt được ở caller → crash process.
        //    (không demo chạy thật vì sẽ làm sập app) — chỉ dùng cho event handler.
        Console.WriteLine("❌ async void → dùng 'async Task' để exception bắt được qua await.");
    }

    // ── D1 ────────────────────────────────────────────────────────────────
    public static void D1_AwaitNoNewThread()
    {
        static async Task DemoAsync()
        {
            Log("Trước await");
            await Task.Delay(100); // KHÔNG thread nào bị chặn (dựa I/O completion port)
            Log("Sau await (có thể là thread pool KHÁC — không tạo thread mới)");
        }
        DemoAsync().GetAwaiter().GetResult();
        Console.WriteLine("Câu chốt: async giải phóng thread khi chờ, không phải chạy song song.");
    }

    // ── D2 ────────────────────────────────────────────────────────────────
    public static void D2_StateMachineAllocation()
    {
        // Nếu await hoàn thành ĐỒNG BỘ → gần zero-allocation.
        // Nếu await THỰC SỰ yield → state machine bị box lên heap để sống qua callback.
        static async Task CompletesSyncAsync()
        {
            for (int i = 0; i < 1000; i++) await Task.CompletedTask; // luôn xong ngay
        }
        static async Task ReallyYieldsAsync()
        {
            for (int i = 0; i < 1000; i++) await Task.Yield();       // ép nhường → yield thật
        }

        long a0 = GC.GetAllocatedBytesForCurrentThread();
        CompletesSyncAsync().GetAwaiter().GetResult();
        long a1 = GC.GetAllocatedBytesForCurrentThread();
        ReallyYieldsAsync().GetAwaiter().GetResult();
        long a2 = GC.GetAllocatedBytesForCurrentThread();

        Console.WriteLine($"await đồng bộ (CompletedTask): ~{a1 - a0} bytes cấp phát");
        Console.WriteLine($"await yield thật (Task.Yield): ~{a2 - a1} bytes cấp phát (nhiều hơn hẳn)");
        Console.WriteLine("→ Đây là lý do ValueTask giảm allocation ở hot path (Q33).");
    }

    // ── D3 ────────────────────────────────────────────────────────────────
    public static void D3_SynchronizationContext()
    {
        // WPF/WinForms: continuation quay về UI thread.
        // ASP.NET classic: quay về request context.
        // Console app & ASP.NET Core: KHÔNG có → continuation chạy trên thread pool bất kỳ.
        Console.WriteLine($"SynchronizationContext.Current = {SynchronizationContext.Current?.ToString() ?? "null (console / ASP.NET Core)"}");
        Console.WriteLine("→ Không có context nên không deadlock kiểu .Result; ConfigureAwait(false) ít tác dụng.");
    }

    // ── D4 ────────────────────────────────────────────────────────────────
    public static void D4_DeadlockStepByStep()
    {
        Console.WriteLine("Kịch bản treo (WPF/WinForms), từng bước:");
        Console.WriteLine("  B1: UI thread gọi LoadAsync().Result → BLOCK, GIỮ UI context");
        Console.WriteLine("  B2: bên trong await Task.Delay xong, continuation cần quay lại UI context");
        Console.WriteLine("  B3: UI thread đang bị .Result block → continuation không chạy → TREO vĩnh viễn");

        // ✅ Fix: async all the way HOẶC ConfigureAwait(false).
        static async Task<string> LoadFixedAsync()
        {
            await Task.Delay(50).ConfigureAwait(false); // không cần context
            return "data";
        }
        Console.WriteLine($"✅ Bản fix chạy bình thường: {LoadFixedAsync().GetAwaiter().GetResult()}");
    }

    // ── D5 ────────────────────────────────────────────────────────────────
    public static void D5_ThreadPoolStarvation()
    {
        // Nhiều request cùng block bằng sync-over-async → mỗi request chiếm+chặn 1 thread pool thread.
        // Thread pool chỉ tăng thread rất chậm (~1-2/giây) → app "đơ" DÙ CPU THẤP.
        ThreadPool.GetAvailableThreads(out int avail, out _);
        Console.WriteLine($"worker threads còn rảnh ≈ {avail}");
        Console.WriteLine("Dấu hiệu starvation: latency tăng vọt, request dồn hàng, CPU KHÔNG cao.");
        Console.WriteLine("Fix: bỏ sync-over-async (.Result/.Wait()) → async all the way.");
    }

    // ── D6 ────────────────────────────────────────────────────────────────
    public static void D6_TaskCompletionSource()
    {
        // TaskCompletionSource: bắc cầu API kiểu callback/event → async/await.
        var bus = new MiniMessageBus();

        Task<string> WaitForMessageAsync()
        {
            // RunContinuationsAsynchronously: tránh chạy continuation inline (dễ deadlock).
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            bus.OnMessage += msg => tcs.TrySetResult(msg);   // hoàn thành khi có event
            bus.OnError += ex => tcs.TrySetException(ex);
            return tcs.Task;
        }

        var waiter = WaitForMessageAsync();
        bus.Publish("hello-from-callback"); // event bắn → task complete
        Console.WriteLine($"→ await được kết quả từ callback: {waiter.GetAwaiter().GetResult()}");
    }

    // ── D7 ────────────────────────────────────────────────────────────────
    public static void D7_AsyncVoidDanger()
    {
        // ❌ async void: không await được, exception không bắt được ở caller, khó test.
        // ✅ async Task: exception bắt được qua await.
        static async Task GoodAsync() => throw new InvalidOperationException("bắt được qua await");
        try
        {
            GoodAsync().GetAwaiter().GetResult();
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"→ async Task: bắt được exception: \"{ex.Message}\"");
        }
        Console.WriteLine("Ngoại lệ DUY NHẤT cho async void: event handler (phải try/catch bên trong).");
    }

    // ── D8 ────────────────────────────────────────────────────────────────
    public static void D8_ParallelForEachAsync()
    {
        // ❌ Parallel.ForEach với body async KHÔNG await được (fire-and-forget).
        // ✅ Parallel.ForEachAsync (.NET 6+): I/O song song CÓ giới hạn.
        var urls = Enumerable.Range(1, 6).Select(i => $"url-{i}").ToArray();
        int current = 0, peak = 0;

        Parallel.ForEachAsync(urls,
            new ParallelOptions { MaxDegreeOfParallelism = 2 },
            async (url, ct) =>
            {
                int now = Interlocked.Increment(ref current);
                InterlockedMax(ref peak, now);
                await Task.Delay(60, ct); // giả lập DownloadAsync(url)
                Interlocked.Decrement(ref current);
            }).GetAwaiter().GetResult();

        Console.WriteLine($"→ {urls.Length} url, MaxDegreeOfParallelism=2 → đồng thời cao nhất = {peak} (≤ 2)");
    }

    // ── D9 ────────────────────────────────────────────────────────────────
    public static void D9_Volatile()
    {
        // volatile: đọc/ghi không bị reorder, không cache vào thanh ghi
        //   → thread khác thấy giá trị mới nhất của _stop.
        var worker = new Worker();
        var run = Task.Run(worker.Run);   // vòng lặp while(!_stop)
        Thread.Sleep(100);
        worker.Stop();                    // set _stop = true từ thread khác
        run.GetAwaiter().GetResult();
        Console.WriteLine($"→ Worker dừng nhờ volatile flag, đã lặp {worker.Iterations:N0} lần.");
        Console.WriteLine("⚠️ volatile KHÔNG đảm bảo atomic cho x++ → dùng Interlocked.");
    }

    // ── D10 ───────────────────────────────────────────────────────────────
    public static void D10_DoubleCheckedLocking()
    {
        // Double-checked locking: check ngoài (nhanh, không lock) + check trong lock (an toàn).
        var a = LazySingleton.Instance;
        var b = LazySingleton.Instance;
        Console.WriteLine($"double-checked: cùng 1 instance = {ReferenceEquals(a, b)}");

        // .NET hiện đại: ưu tiên Lazy<T> cho gọn + thread-safe sẵn.
        var c = LazySingleton.Better;
        Console.WriteLine($"Lazy<T>       : cùng 1 instance = {ReferenceEquals(c, LazySingleton.Better)}");
    }

    // ── D11 ───────────────────────────────────────────────────────────────
    public static void D11_CompareExchange()
    {
        // Lock-free lazy init: "nếu _value đang null thì gán created", atomic.
        object? value = null;
        object created = new();
        Interlocked.CompareExchange(ref value, created, null);
        Console.WriteLine($"CompareExchange lazy init: gán thành công = {ReferenceEquals(value, created)}");

        // Lock-free update loop cho logic phức tạp (retry tới khi CAS thành công).
        int state = 0;
        for (int t = 0; t < 5; t++)
        {
            int current, updated;
            do
            {
                current = Volatile.Read(ref state);
                updated = current + 10;                 // Compute(current)
            } while (Interlocked.CompareExchange(ref state, updated, current) != current);
        }
        Console.WriteLine($"lock-free CAS loop: state = {state}");
    }

    // ── D12 ───────────────────────────────────────────────────────────────
    public static void D12_LimitConcurrency()
    {
        // Giới hạn số tác vụ async đồng thời bằng SemaphoreSlim (xem thêm Q37, D8).
        using var semaphore = new SemaphoreSlim(4);
        int current = 0, peak = 0;

        var tasks = Enumerable.Range(0, 12).Select(async i =>
        {
            await semaphore.WaitAsync();
            try
            {
                int now = Interlocked.Increment(ref current);
                InterlockedMax(ref peak, now);
                await Task.Delay(40);
            }
            finally { Interlocked.Decrement(ref current); semaphore.Release(); }
        });
        Task.WhenAll(tasks).GetAwaiter().GetResult();
        Console.WriteLine($"→ 12 task, semaphore(4) → đồng thời cao nhất = {peak} (≤ 4)");
    }

    // ── D13 ───────────────────────────────────────────────────────────────
    public static void D13_CancellationNotStopping()
    {
        // ❌ Không kiểm tra/không truyền token → vẫn chạy tiếp dù đã huỷ.
        // ✅ Chủ động kiểm tra + truyền token xuống API dưới (Task.Delay(ms, ct)).
        static async Task GoodLoopAsync(CancellationToken ct)
        {
            int i = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(30, ct); // truyền token → huỷ được ngay
                i++;
            }
        }

        using var cts = new CancellationTokenSource(120);
        try { GoodLoopAsync(cts.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { Console.WriteLine("→ Dừng đúng lúc vì token được TRUYỀN + KIỂM TRA."); }
        Console.WriteLine("Điểm nhấn: cancellation là 'tín hiệu', không phải 'kill switch'.");
    }

    // ── D14 ───────────────────────────────────────────────────────────────
    public static void D14_WhenAllAggregate()
    {
        static async Task FailAsync(string tag)
        {
            await Task.Delay(20);
            throw new InvalidOperationException($"lỗi {tag}");
        }

        var all = Task.WhenAll(FailAsync("A"), FailAsync("B"), FailAsync("C"));
        try
        {
            all.GetAwaiter().GetResult();
        }
        catch
        {
            // ❌ await/GetResult chỉ ném exception ĐẦU TIÊN.
            // ✅ Lấy HẾT qua task.Exception.InnerExceptions (AggregateException).
            Console.WriteLine("Tất cả exception trong WhenAll:");
            foreach (var inner in all.Exception!.InnerExceptions)
                Console.WriteLine($"  - {inner.Message}");
        }
    }

    // ── D15 ───────────────────────────────────────────────────────────────
    public static void D15_ThreadLocalVsAsyncLocal()
    {
        // ThreadLocal<T>: mỗi THREAD một bản riêng.
        using var threadLocal = new ThreadLocal<int>(() => Environment.CurrentManagedThreadId);
        int main = threadLocal.Value;
        int other = Task.Run(() => threadLocal.Value).GetAwaiter().GetResult();
        Console.WriteLine($"ThreadLocal: main={main}, thread khác={other} (khác nhau)");

        // AsyncLocal<T>: dữ liệu "chảy" theo logical async context, xuyên qua await dù đổi thread.
        var correlationId = new AsyncLocal<string>();
        async Task HandleAsync(string id)
        {
            correlationId.Value = id;               // set
            await Task.Delay(50);                   // có thể đổi thread...
            Console.WriteLine($"AsyncLocal sau await (thread {Environment.CurrentManagedThreadId}): {correlationId.Value} (vẫn giữ)");
        }
        HandleAsync("req-123").GetAwaiter().GetResult();
        Console.WriteLine("→ AsyncLocal dùng cho ambient data: correlation id, user context...");
    }

    // Tiện ích: cập nhật giá trị lớn nhất theo kiểu atomic (cho các demo đo đỉnh đồng thời).
    private static void InterlockedMax(ref int target, int value)
    {
        int snapshot;
        do
        {
            snapshot = Volatile.Read(ref target);
            if (value <= snapshot) return;
        } while (Interlocked.CompareExchange(ref target, value, snapshot) != snapshot);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Helper types
// ═══════════════════════════════════════════════════════════════════════════

file sealed class BankAccount(decimal initial)
{
    private readonly object _lock = new();   // ✅ private readonly object riêng để lock
    private decimal _balance = initial;

    public decimal Balance { get { lock (_lock) return _balance; } }

    public void Withdraw(decimal amount)
    {
        lock (_lock) // = Monitor.Enter/Exit — chỉ 1 thread vào critical section
        {
            if (_balance >= amount) _balance -= amount;
        }
    }
}

file sealed class Worker
{
    private volatile bool _stop; // volatile: thấy giá trị mới nhất từ thread khác
    public long Iterations { get; private set; }

    public void Run()
    {
        while (!_stop) Iterations++; // đọc _stop mới nhất, không bị cache thanh ghi
    }

    public void Stop() => _stop = true;
}

file sealed class LazySingleton
{
    private static LazySingleton? _instance;
    private static readonly object _lock = new();
    private LazySingleton() { }

    public static LazySingleton Instance
    {
        get
        {
            if (_instance == null)            // check 1: không lock, nhanh
            {
                lock (_lock)
                {
                    if (_instance == null)    // check 2: trong lock, an toàn
                        _instance = new LazySingleton();
                }
            }
            return _instance;
        }
    }

    // ✅ Cách gọn & an toàn hơn trong .NET hiện đại.
    private static readonly Lazy<LazySingleton> _lazy = new(() => new LazySingleton());
    public static LazySingleton Better => _lazy.Value;
}

file sealed class MiniMessageBus
{
    public event Action<string>? OnMessage;
    public event Action<Exception>? OnError;
    public void Publish(string msg) => OnMessage?.Invoke(msg);
    public void Fail(Exception ex) => OnError?.Invoke(ex);
}
