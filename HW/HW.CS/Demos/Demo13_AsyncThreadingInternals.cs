using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace HW.CS.Demos;

/// <summary>
/// Runnable demos cho interview.NET.13-Async-Threading-Internals.md (ASY-1..ASY-22).
///
/// Mọi demo async được lái bằng .GetAwaiter().GetResult() bên trong một `static void`
/// để giữ nguyên chữ ký `Action Run` của DemoItem. Không demo nào treo:
/// kịch bản deadlock được tái hiện bằng Wait(timeout) nên nó CHỨNG MINH được bế tắc mà không kẹt.
/// </summary>
public sealed class Demo13_AsyncThreadingInternals : IDemoTopic
{
    public string Title => "13 — Async / Await & Threading Internals";

    public IReadOnlyList<DemoItem> Items => new DemoItem[]
    {
        new("ASY1.  await KHÔNG tạo thread — ai chạy phần sau await?", ASY1_WhoRunsContinuation),
        new("ASY2.  State machine: khi nào allocate, khi nào không", ASY2_StateMachineAllocation),
        new("ASY3.  Tự viết awaitable (awaiter pattern)", ASY3_CustomAwaitable),
        new("ASY4.  TaskCompletionSource & RunContinuationsAsynchronously", ASY4_TaskCompletionSource),
        new("ASY5.  AsyncLocal (ExecutionContext) vs ThreadLocal", ASY5_AsyncLocalVsThreadLocal),
        new("ASY6.  SynchronizationContext & ConfigureAwait(false)", ASY6_SynchronizationContext),
        new("ASY7.  Tái hiện deadlock .Result (an toàn, có timeout)", ASY7_DeadlockReproduction),
        new("ASY8.  ThreadPool: hàng đợi & thread injection", ASY8_ThreadPoolStats),
        new("ASY9.  Thread pool starvation — đo độ trễ", ASY9_Starvation),
        new("ASY10. ValueTask vs Task — allocation", ASY10_ValueTask),
        new("ASY11. IAsyncEnumerable: stream + backpressure tự nhiên", ASY11_AsyncStreams),
        new("ASY12. CancellationToken hợp tác + linked source", ASY12_Cancellation),
        new("ASY13. Memory reordering thật (store-load trên x86)", ASY13_MemoryReordering),
        new("ASY14. Race condition & Interlocked", ASY14_RaceCondition),
        new("ASY15. lock: không tranh chấp vs tranh chấp", ASY15_LockContention),
        new("ASY16. False sharing (cache line 64 byte)", ASY16_FalseSharing),
        new("ASY17. ConcurrentDictionary: factory chạy nhiều lần", ASY17_ConcurrentDictionary),
        new("ASY18. Channel bounded — backpressure", ASY18_Channels),
        new("ASY19. Exception trong async & Task.WhenAll", ASY19_AsyncExceptions),
        new("ASY20. SemaphoreSlim: giới hạn số việc song song", ASY20_Throttling),
        new("ASY21. Cheat-sheet chẩn đoán async/threading", ASY21_DiagnosticsCheatSheet),
    };

    // ── ASY1 ──────────────────────────────────────────────────────────────
    public static void ASY1_WhoRunsContinuation() => Run(async () =>
    {
        Console.WriteLine($"  Thread bắt đầu                       : #{Environment.CurrentManagedThreadId} (pool? {Thread.CurrentThread.IsThreadPoolThread})");

        await Task.CompletedTask;
        Console.WriteLine($"  Sau `await Task.CompletedTask`       : #{Environment.CurrentManagedThreadId}  ← KHÔNG đổi thread: task đã xong ⇒ chạy tiếp đồng bộ");

        await Task.Yield();
        Console.WriteLine($"  Sau `await Task.Yield()`             : #{Environment.CurrentManagedThreadId}  ← ép nhường: continuation được đẩy vào ThreadPool");

        await Task.Delay(20);
        Console.WriteLine($"  Sau `await Task.Delay(20)`           : #{Environment.CurrentManagedThreadId}  ← timer hết hạn, một thread pool BẤT KỲ chạy tiếp");

        int before = Environment.CurrentManagedThreadId;
        await Task.Run(() => Console.WriteLine($"  Bên trong Task.Run                   : #{Environment.CurrentManagedThreadId}"));
        Console.WriteLine($"  Sau `await Task.Run(...)` (trước #{before}) : #{Environment.CurrentManagedThreadId}");

        Console.WriteLine();
        Console.WriteLine("→ `await` KHÔNG tạo thread. Nó đăng ký một CONTINUATION rồi TRẢ thread hiện tại về pool.");
        Console.WriteLine("→ Trong lúc chờ I/O, không thread nào bị giam: OS (IOCP trên Windows, epoll trên Linux) mới là bên chờ.");
        Console.WriteLine("→ Đó là lý do async giúp SCALE (10.000 request đồng thời ~ vài chục thread), chứ không làm 1 request nhanh hơn.");
    });

    // ── ASY2 ──────────────────────────────────────────────────────────────
    public static void ASY2_StateMachineAllocation() => Run(async () =>
    {
        // Mỗi lần đo chạy 1000 vòng để số liệu đủ rõ
        long allocSync = await AllocAsync(async () =>
        {
            for (int i = 0; i < 1000; i++) _ = await AlreadyDoneAsync();
        });

        long allocReal = await AllocAsync(async () =>
        {
            for (int i = 0; i < 1000; i++) _ = await ReallySuspendsAsync();
        });

        long allocValueTask = await AllocAsync(async () =>
        {
            for (int i = 0; i < 1000; i++) _ = await AlreadyDoneValueTaskAsync();
        });

        Console.WriteLine($"  1000 lần await một Task ĐÃ hoàn thành      : {allocSync,7:N0} byte  ({allocSync / 1000.0:F1} byte/lần)");
        Console.WriteLine($"  1000 lần await một Task THẬT SỰ dừng lại   : {allocReal,7:N0} byte  ({allocReal / 1000.0:F1} byte/lần)");
        Console.WriteLine($"  1000 lần await ValueTask hoàn thành đồng bộ : {allocValueTask,7:N0} byte  ({allocValueTask / 1000.0:F1} byte/lần)");
        Console.WriteLine();
        Console.WriteLine("→ Compiler sinh state machine dạng STRUCT. Nó chỉ bị BOX lên heap ở lần đầu gặp một");
        Console.WriteLine("  await THẬT SỰ chưa hoàn thành (lúc đó mới cần lưu trạng thái để quay lại sau).");
        Console.WriteLine("→ Async method mà mọi await đều đã xong ⇒ gần như không allocate (chỉ còn object Task).");
        Console.WriteLine("→ `ValueTask` bỏ nốt object Task cho đường nóng hoàn thành đồng bộ (xem ASY10).");
        Console.WriteLine("→ ⚠️ Ở bản DEBUG compiler sinh CLASS thay vì struct ⇒ đừng đo allocation ở Debug.");
    });

    // ── ASY3 ──────────────────────────────────────────────────────────────
    public static void ASY3_CustomAwaitable() => Run(async () =>
    {
        Console.WriteLine("  `await x` chỉ cần x có: GetAwaiter() → { bool IsCompleted; void OnCompleted(Action); T GetResult(); }");
        Console.WriteLine();

        var sw = Stopwatch.StartNew();
        await new DelayAwaitable(TimeSpan.FromMilliseconds(60));
        Console.WriteLine($"  await awaitable tự viết (60ms)  → thực tế {sw.ElapsedMilliseconds} ms, thread #{Environment.CurrentManagedThreadId}");

        int value = await new ValueAwaitable<int>(123);
        Console.WriteLine($"  await awaitable trả giá trị     → {value} (IsCompleted=true ⇒ chạy thẳng, không dừng)");

        Console.WriteLine();
        Console.WriteLine("→ Vì `await` chỉ dựa trên PATTERN (không phải interface), nó dùng được với:");
        Console.WriteLine("  Task, ValueTask, Task.Yield(), IAsyncEnumerable, SemaphoreSlim.WaitAsync(), awaitable của Unity/WinForms…");
        Console.WriteLine("→ Đây là lý do bạn có thể tự viết awaitable cho một API callback cũ (hoặc dùng TaskCompletionSource — ASY4).");
    });

    // ── ASY4 ──────────────────────────────────────────────────────────────
    public static void ASY4_TaskCompletionSource() => Run(async () =>
    {
        // (a) MẶC ĐỊNH: continuation chạy ĐỒNG BỘ ngay trên thread gọi SetResult
        var tcsSync = new TaskCompletionSource<int>();
        int continuationThreadSync = 0;
        var waiterSync = Task.Run(async () =>
        {
            await tcsSync.Task;
            continuationThreadSync = Environment.CurrentManagedThreadId;
        });
        await Task.Delay(50);
        int setterThread = Environment.CurrentManagedThreadId;
        tcsSync.SetResult(1);
        await waiterSync;

        // (b) RunContinuationsAsynchronously: continuation được đẩy sang ThreadPool
        var tcsAsync = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        int continuationThreadAsync = 0;
        var waiterAsync = Task.Run(async () =>
        {
            await tcsAsync.Task;
            continuationThreadAsync = Environment.CurrentManagedThreadId;
        });
        await Task.Delay(50);
        tcsAsync.SetResult(1);
        await waiterAsync;

        Console.WriteLine($"  Thread gọi SetResult                              : #{setterThread}");
        Console.WriteLine($"  Continuation với TCS mặc định                     : #{continuationThreadSync}  ← thường CHÍNH LÀ thread gọi SetResult!");
        Console.WriteLine($"  Continuation với RunContinuationsAsynchronously   : #{continuationThreadAsync}  ← thread pool khác");
        Console.WriteLine();
        Console.WriteLine("→ Vì sao điều này NGUY HIỂM: nếu bạn gọi SetResult() khi ĐANG GIỮ LOCK, hoặc trên một thread");
        Console.WriteLine("  quan trọng (thread đọc socket, thread UI), thì TOÀN BỘ continuation của người khác");
        Console.WriteLine("  sẽ chạy NGAY trên thread đó, BÊN TRONG lock ⇒ deadlock hoặc nghẽn cả hệ thống.");
        Console.WriteLine("→ Quy tắc: viết thư viện thì GẦN NHƯ LUÔN bật TaskCreationOptions.RunContinuationsAsynchronously.");
        Console.WriteLine("→ TCS là cây cầu từ API callback cũ sang async/await:");
        Console.WriteLine("     legacy.OnDone += r => tcs.TrySetResult(r);  →  await tcs.Task;");
    });

    // ── ASY5 ──────────────────────────────────────────────────────────────
    public static void ASY5_AsyncLocalVsThreadLocal() => Run(async () =>
    {
        _asyncLocal.Value = "request-42";
        _threadLocal.Value = "thread-42";

        Console.WriteLine($"  Trước await   — AsyncLocal={_asyncLocal.Value}, ThreadLocal={_threadLocal.Value}, thread #{Environment.CurrentManagedThreadId}");

        await Task.Run(() =>
        {
            Console.WriteLine($"  Trong Task.Run— AsyncLocal={_asyncLocal.Value ?? "(null)"}, ThreadLocal={_threadLocal.Value ?? "(null)"}, thread #{Environment.CurrentManagedThreadId}");
            _asyncLocal.Value = "bị đổi trong nhánh con";      // copy-on-write theo NHÁNH
        });

        Console.WriteLine($"  Sau Task.Run  — AsyncLocal={_asyncLocal.Value}  ← thay đổi ở nhánh con KHÔNG ảnh hưởng nhánh cha");
        Console.WriteLine();
        Console.WriteLine("→ AsyncLocal nằm trong ExecutionContext ⇒ FLOW theo mọi await / Task.Run (kể cả sang thread khác).");
        Console.WriteLine("→ ThreadLocal gắn với THREAD ⇒ mất ngay khi continuation chạy trên thread khác.");
        Console.WriteLine("→ Đây chính là cơ sở của IHttpContextAccessor, ILogger scope, Activity/tracing (correlation id).");
        Console.WriteLine("→ Chi phí: capture/restore ExecutionContext ở mỗi continuation — dùng ít thôi.");
    });

    // ── ASY6 ──────────────────────────────────────────────────────────────
    public static void ASY6_SynchronizationContext()
    {
        Console.WriteLine($"  SynchronizationContext trong console app : {SynchronizationContext.Current?.ToString() ?? "null"}");
        Console.WriteLine("  ASP.NET Core cũng là null ⇒ continuation luôn chạy trên ThreadPool.");
        Console.WriteLine("  WinForms/WPF thì KHÁC: continuation bị POST về đúng UI thread.");
        Console.WriteLine();

        var ctx = new PumpingSyncContext();
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(ctx);
            ctx.RunOnCurrentThread(async () =>
            {
                int uiThread = Environment.CurrentManagedThreadId;
                Console.WriteLine($"  [ngữ cảnh giả lập UI] thread ban đầu       : #{uiThread}");

                await Task.Delay(30);                       // capture context
                Console.WriteLine($"  [ngữ cảnh giả lập UI] sau await thường     : #{Environment.CurrentManagedThreadId}  ← QUAY VỀ đúng 'UI thread'");

                await Task.Delay(30).ConfigureAwait(false); // bỏ qua context
                Console.WriteLine($"  [ngữ cảnh giả lập UI] sau ConfigureAwait(false): #{Environment.CurrentManagedThreadId}  ← KHÔNG quay về, chạy trên pool");
            });
        }) { IsBackground = true };
        thread.Start();
        thread.Join(3000);

        Console.WriteLine();
        Console.WriteLine("→ SynchronizationContext trả lời câu hỏi 'chạy continuation Ở ĐÂU'.");
        Console.WriteLine("→ ConfigureAwait(false) = 'đừng quay lại context cũ'. Nó KHÔNG ảnh hưởng ExecutionContext (AsyncLocal vẫn flow).");
        Console.WriteLine("→ Trong ASP.NET Core nó gần như không đổi hành vi, nhưng vẫn nên dùng TRONG THƯ VIỆN vì bạn không biết ai gọi mình.");
        Console.WriteLine("→ .NET 8 có ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing / .ForceYielding) — chi tiết hơn cờ bool cũ.");
    }

    // ── ASY7 ──────────────────────────────────────────────────────────────
    public static void ASY7_DeadlockReproduction()
    {
        Console.WriteLine("  Kịch bản: UI/ASP.NET Framework — thread DUY NHẤT vừa phải chạy continuation, vừa bị .Result giam.");
        Console.WriteLine();

        var ctx = new PumpingSyncContext();
        bool completed = false;
        int pendingAfterTimeout = -1;

        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(ctx);

            Task<int> task = WorkThenReturnToContextAsync();   // await Task.Delay ⇒ capture ctx

            // .Result / .Wait() sẽ BLOCK chính thread này — thread duy nhất có thể chạy continuation.
            // Dùng Wait(timeout) để CHỨNG MINH bế tắc mà không treo demo.
            completed = task.Wait(TimeSpan.FromMilliseconds(700));
            pendingAfterTimeout = ctx.PendingCount;
        }) { IsBackground = true };

        thread.Start();
        thread.Join(3000);

        Console.WriteLine($"  Task hoàn thành trong 700ms?            : {completed}   (task chỉ cần 50ms!)");
        Console.WriteLine($"  Số continuation đang XẾP HÀNG trong ctx : {pendingAfterTimeout}   ← không ai chạy được vì thread bị block");
        Console.WriteLine();
        Console.WriteLine("  Chuỗi sự kiện:");
        Console.WriteLine("    1. Thread A gọi task.Wait() ⇒ A bị BLOCK, nhưng vẫn GIỮ SynchronizationContext.");
        Console.WriteLine("    2. Task xong I/O ⇒ continuation cần được POST về context đó (tức là về thread A).");
        Console.WriteLine("    3. Thread A đang bị block bởi chính Wait() ⇒ không bao giờ chạy continuation.");
        Console.WriteLine("    4. Task không bao giờ hoàn thành ⇒ Wait() không bao giờ trả về  ⇒ DEADLOCK.");
        Console.WriteLine();
        Console.WriteLine("→ ASP.NET Core KHÔNG có SynchronizationContext ⇒ không dính deadlock kiểu này.");
        Console.WriteLine("  NHƯNG .Result vẫn sai: nó GIAM một thread pool thread ⇒ dưới tải cao gây STARVATION (ASY9).");
        Console.WriteLine("→ ConfigureAwait(false) chỉ VÁ được triệu chứng. Cách chữa thật: async all the way.");
    }

    // ── ASY8 ──────────────────────────────────────────────────────────────
    public static void ASY8_ThreadPoolStats() => Run(async () =>
    {
        ThreadPool.GetMinThreads(out int minW, out int minIo);
        ThreadPool.GetMaxThreads(out int maxW, out int maxIo);
        Console.WriteLine($"  MinThreads : worker={minW}, IO={minIo}   (mặc định = số core: {Environment.ProcessorCount})");
        Console.WriteLine($"  MaxThreads : worker={maxW:N0}, IO={maxIo:N0}");
        Console.WriteLine($"  Hiện tại   : ThreadCount={ThreadPool.ThreadCount}, PendingWorkItems={ThreadPool.PendingWorkItemCount}, Completed={ThreadPool.CompletedWorkItemCount:N0}");
        Console.WriteLine();

        // đo throughput của work item ngắn
        const int Items = 200_000;
        int done = 0;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < Items; i++)
            ThreadPool.UnsafeQueueUserWorkItem(_ =>
            {
                if (Interlocked.Increment(ref done) == Items) tcs.TrySetResult();
            }, null);
        await tcs.Task;
        sw.Stop();

        Console.WriteLine($"  Chạy {Items:N0} work item cực ngắn : {sw.ElapsedMilliseconds} ms ⇒ {Items / Math.Max(sw.Elapsed.TotalSeconds, 0.001) / 1_000_000:F2} triệu item/giây");
        Console.WriteLine($"  ThreadCount sau đó : {ThreadPool.ThreadCount} (không hề tăng vọt — work item ngắn không cần thêm thread)");
        Console.WriteLine();
        Console.WriteLine("→ Cấu trúc: GLOBAL QUEUE + một LOCAL QUEUE cho mỗi worker.");
        Console.WriteLine("  Task sinh ra TỪ BÊN TRONG một task khác vào local queue (LIFO, thân thiện cache, không tranh lock).");
        Console.WriteLine("  Worker rỗng thì WORK-STEALING: lấy việc từ ĐUÔI queue của worker khác.");
        Console.WriteLine("→ I/O completion đi đường riêng (IOCP trên Windows).");
        Console.WriteLine("→ HILL CLIMBING: nếu hàng đợi còn việc mà throughput không tăng, pool thêm thread RẤT CHẬM (~1–2 thread/giây).");
    });

    // ── ASY9 ──────────────────────────────────────────────────────────────
    public static void ASY9_Starvation() => Run(async () =>
    {
        const int Batch = 2000;

        // (a) pool rảnh — đo thời gian hoàn thành một lô việc rất nhẹ
        double idleMs = await RunBatchAsync(Batch);

        // (b) làm nghẽn pool bằng công việc BLOCKING (mô phỏng .Result / I/O đồng bộ)
        int blockers = Environment.ProcessorCount * 8;
        Console.WriteLine($"  Đẩy {blockers} công việc BLOCKING (Thread.Sleep 1s) vào ThreadPool…");
        for (int i = 0; i < blockers; i++)
            ThreadPool.UnsafeQueueUserWorkItem(_ => Thread.Sleep(1000), null);

        await Task.Delay(150);
        int queued = (int)ThreadPool.PendingWorkItemCount;
        int threadsWhileBlocked = ThreadPool.ThreadCount;

        double starvedMs = await RunBatchAsync(Batch);

        Console.WriteLine();
        Console.WriteLine($"  Hoàn thành {Batch:N0} việc nhẹ khi pool RẢNH   : {idleMs,8:F1} ms");
        Console.WriteLine($"  Hoàn thành {Batch:N0} việc nhẹ khi pool NGHẼN  : {starvedMs,8:F1} ms   ({starvedMs / Math.Max(idleMs, 0.01):F0}× chậm hơn)");
        Console.WriteLine($"  Lúc nghẽn : PendingWorkItemCount = {queued}, ThreadCount = {threadsWhileBlocked}");

        var drain = Stopwatch.StartNew();
        while (ThreadPool.PendingWorkItemCount > 0 && drain.ElapsedMilliseconds < 15000)
            await Task.Delay(100);
        Console.WriteLine($"  Sau khi rút hết hàng đợi : ThreadCount = {ThreadPool.ThreadCount} (pool đã phải inject thêm thread)");

        Console.WriteLine();
        Console.WriteLine("→ Đây chính là THREAD POOL STARVATION: worker bị giam bởi công việc BLOCKING,");
        Console.WriteLine("  việc mới phải xếp hàng, và pool chỉ inject thêm thread ~1–2 thread/giây (hill climbing)");
        Console.WriteLine("  ⇒ độ trễ tăng theo BẬC THANG, kéo dài hàng chục giây dưới tải thật.");
        Console.WriteLine();
        Console.WriteLine("  ℹ️ Tinh tế: vài primitive (Monitor, SemaphoreSlim…) BÁO cho pool biết mình đang block ⇒ inject nhanh hơn.");
        Console.WriteLine("     Thread.Sleep / I/O đồng bộ / .Result thì KHÔNG báo — đó là trường hợp tệ nhất.");
        Console.WriteLine();
        Console.WriteLine("  🔍 DẤU HIỆU đặc trưng trong production (rất hay được hỏi):");
        Console.WriteLine("     CPU THẤP + latency CAO + threadpool-queue-length > 0 kéo dài + thread count tăng đều.");
        Console.WriteLine("  💊 CHỮA: bỏ hết .Result/.Wait()/Thread.Sleep trong đường request; async all the way.");
        Console.WriteLine("     ThreadPool.SetMinThreads() chỉ là băng dán tạm, không phải cách chữa.");
    });

    /// <summary>Chạy một lô work item rất nhẹ và trả về thời gian hoàn thành (ms).</summary>
    private static async Task<double> RunBatchAsync(int count)
    {
        int done = 0;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
            ThreadPool.UnsafeQueueUserWorkItem(_ =>
            {
                if (Interlocked.Increment(ref done) == count) tcs.TrySetResult();
            }, null);
        await tcs.Task;
        return sw.Elapsed.TotalMilliseconds;
    }

    // ── ASY10 ─────────────────────────────────────────────────────────────
    public static void ASY10_ValueTask() => Run(async () =>
    {
        var cache = new Dictionary<int, string> { [1] = "cached" };
        var service = new CachedService(cache);

        long allocTask = await AllocAsync(async () =>
        {
            for (int i = 0; i < 10_000; i++) _ = await service.GetAsTaskAsync(1);
        });
        long allocValueTask = await AllocAsync(async () =>
        {
            for (int i = 0; i < 10_000; i++) _ = await service.GetAsValueTaskAsync(1);
        });

        Console.WriteLine($"  10.000 lần cache-hit trả Task<string>      : {allocTask,7:N0} byte");
        Console.WriteLine($"  10.000 lần cache-hit trả ValueTask<string> : {allocValueTask,7:N0} byte");
        Console.WriteLine();
        Console.WriteLine("→ ValueTask<T> là STRUCT chứa MỘT trong ba: giá trị có sẵn / một Task<T> / một IValueTaskSource<T>.");
        Console.WriteLine("  Đường nóng hoàn thành đồng bộ ⇒ 0 allocation.");
        Console.WriteLine();
        Console.WriteLine("  ⚠️ 4 LUẬT của ValueTask (vi phạm = bug ngẫu nhiên cực khó tìm):");
        Console.WriteLine("     1. Chỉ await ĐÚNG MỘT LẦN.");
        Console.WriteLine("     2. Không .Result/.GetAwaiter().GetResult() khi chưa hoàn thành.");
        Console.WriteLine("     3. Không await từ nhiều thread.");
        Console.WriteLine("     4. Muốn giữ lại / await nhiều lần ⇒ .AsTask() trước.");
        Console.WriteLine("→ Chỉ dùng khi ĐÃ ĐO và method (a) rất nóng, (b) thường hoàn thành đồng bộ. Mặc định vẫn nên là Task.");
    });

    // ── ASY11 ─────────────────────────────────────────────────────────────
    public static void ASY11_AsyncStreams() => Run(async () =>
    {
        using var cts = new CancellationTokenSource();
        int processed = 0;

        Console.WriteLine("  Nguồn sinh 5 phần tử, mỗi phần tử mất 30ms — consumer xử lý ngay khi có:");
        var sw = Stopwatch.StartNew();
        await foreach (var item in GenerateAsync(5, cts.Token).WithCancellation(cts.Token))
        {
            processed++;
            Console.WriteLine($"    nhận '{item}' tại {sw.ElapsedMilliseconds,4} ms (thread #{Environment.CurrentManagedThreadId})");
        }

        Console.WriteLine();
        Console.WriteLine("  Huỷ giữa chừng:");
        using var cts2 = new CancellationTokenSource();
        int got = 0;
        try
        {
            await foreach (var item in GenerateAsync(100, cts2.Token).WithCancellation(cts2.Token))
            {
                if (++got == 3) cts2.Cancel();
            }
        }
        catch (OperationCanceledException) { Console.WriteLine($"    đã huỷ sau {got} phần tử (nguồn dừng sinh ngay, không chạy hết 100)"); }

        Console.WriteLine();
        Console.WriteLine($"→ `Task<List<T>>` buộc phải có TOÀN BỘ dữ liệu trong RAM rồi mới trả ({processed} phần tử ⇒ chờ hết).");
        Console.WriteLine("  `IAsyncEnumerable<T>` trả từng phần tử ⇒ BACKPRESSURE tự nhiên: consumer xử lý tới đâu, producer đọc tới đó.");
        Console.WriteLine("  Đây là cách stream 10 triệu dòng từ DB mà RAM không nổ.");
        Console.WriteLine("→ ⚠️ Bắt buộc gắn [EnumeratorCancellation] lên tham số token thì WithCancellation mới thật sự truyền được.");
    });

    // ── ASY12 ─────────────────────────────────────────────────────────────
    public static void ASY12_Cancellation() => Run(async () =>
    {
        // (a) không kiểm tra token ⇒ huỷ KHÔNG có tác dụng
        using var cts1 = new CancellationTokenSource(30);
        var sw = Stopwatch.StartNew();
        long ignored = IgnoresToken(cts1.Token);
        Console.WriteLine($"  Vòng lặp KHÔNG kiểm tra token : chạy đủ {sw.ElapsedMilliseconds} ms rồi mới xong (huỷ vô tác dụng) [={ignored}]");

        // (b) có kiểm tra token
        using var cts2 = new CancellationTokenSource(30);
        sw.Restart();
        try
        {
            RespectsToken(cts2.Token);
            Console.WriteLine($"  Vòng lặp CÓ kiểm tra token    : chạy xong trước khi kịp huỷ ({sw.ElapsedMilliseconds} ms)");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"  Vòng lặp CÓ kiểm tra token    : dừng sau {sw.ElapsedMilliseconds} ms ✅");
        }

        // (c) linked token source
        using var userCts = new CancellationTokenSource();
        using var timeoutCts = new CancellationTokenSource(80);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(userCts.Token, timeoutCts.Token);
        sw.Restart();
        try { await Task.Delay(5000, linked.Token); }
        catch (OperationCanceledException) { Console.WriteLine($"  Linked source (user OR timeout): huỷ sau {sw.ElapsedMilliseconds} ms bởi timeout"); }

        // (d) callback đăng ký
        using var cbCts = new CancellationTokenSource();
        using (cbCts.Token.Register(() => Console.WriteLine("  Callback Register() chạy khi huỷ ✅")))
        {
            cbCts.Cancel();
        }

        Console.WriteLine();
        Console.WriteLine("→ Huỷ là HỢP TÁC: không ai 'giết' được task của bạn. Không kiểm tra token = không huỷ được.");
        Console.WriteLine("→ Lỗi phổ biến nhất: nhận CancellationToken rồi… quên truyền xuống tầng dưới (ToListAsync(), HttpClient…).");
        Console.WriteLine("→ Linked source PHẢI Dispose, nếu không callback vẫn treo trên source cha ⇒ leak.");
        Console.WriteLine("→ Trong ASP.NET Core: HttpContext.RequestAborted huỷ khi client ngắt kết nối.");
    });

    // ── ASY13 ─────────────────────────────────────────────────────────────
    public static void ASY13_MemoryReordering()
    {
        const int Iterations = 500_000;
        int anomalies = 0;
        var barrier = new Barrier(2);

        var t1 = new Thread(() =>
        {
            for (int i = 0; i < Iterations; i++)
            {
                barrier.SignalAndWait();
                _x = 1;              // ghi
                _r1 = _y;            // rồi đọc biến kia
                barrier.SignalAndWait();
            }
        }) { IsBackground = true };

        var t2 = new Thread(() =>
        {
            for (int i = 0; i < Iterations; i++)
            {
                barrier.SignalAndWait();
                _y = 1;
                _r2 = _x;
                barrier.SignalAndWait();

                // Nếu CPU không đảo lệnh thì KHÔNG THỂ cả hai cùng đọc ra 0.
                if (_r1 == 0 && _r2 == 0) anomalies++;
                _x = _y = 0;
            }
        }) { IsBackground = true };

        var sw = Stopwatch.StartNew();
        t1.Start(); t2.Start();
        t1.Join(); t2.Join();

        Console.WriteLine($"  Thí nghiệm kinh điển (Dekker): T1 ghi x rồi đọc y | T2 ghi y rồi đọc x");
        Console.WriteLine($"  Chạy {Iterations:N0} vòng trong {sw.ElapsedMilliseconds} ms trên {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"  Số lần CẢ HAI cùng đọc ra 0 : {anomalies:N0}  ({100.0 * anomalies / Iterations:F3}% số vòng)");
        Console.WriteLine();
        Console.WriteLine("  Về mặt logic tuần tự, kết quả (0,0) là KHÔNG THỂ. Nhưng nó xảy ra thật, vì:");
        Console.WriteLine("    CPU có STORE BUFFER — lệnh ghi nằm chờ trong buffer, lệnh đọc sau đó vượt lên trước");
        Console.WriteLine("    ⇒ StoreLoad reordering (đây là kiểu đảo DUY NHẤT mà x86/x64 cho phép).");
        Console.WriteLine();
        Console.WriteLine("→ x86/x64 có memory model MẠNH. ARM64 (Apple M-series, AWS Graviton) YẾU hơn nhiều:");
        Console.WriteLine("  code đa luồng 'chạy tốt nhiều năm' trên x86 có thể HỎNG khi chuyển sang ARM.");
        Console.WriteLine("→ Công cụ: volatile (acquire/release) · Volatile.Read/Write · Interlocked (atomic + full barrier)");
        Console.WriteLine("  · Thread.MemoryBarrier() · lock (vào = acquire, ra = release ⇒ trong lock KHÔNG cần volatile).");
        Console.WriteLine("→ Lời khuyên thật lòng: đừng tự viết lock-free. Dùng lock / Interlocked / System.Collections.Concurrent.");
    }

    // ── ASY14 ─────────────────────────────────────────────────────────────
    public static void ASY14_RaceCondition()
    {
        const int PerThread = 200_000;
        int threads = Math.Max(4, Environment.ProcessorCount);

        int unsafeCounter = 0, interlockedCounter = 0, lockedCounter = 0;
        object gate = new();

        Parallel.For(0, threads, _ =>
        {
            for (int i = 0; i < PerThread; i++)
            {
                unsafeCounter++;                              // ❌ đọc-sửa-ghi KHÔNG atomic
                Interlocked.Increment(ref interlockedCounter); // ✅ lock-free (lệnh CPU lock xadd)
                lock (gate) { lockedCounter++; }               // ✅ nhưng nặng hơn
            }
        });

        int expected = threads * PerThread;
        Console.WriteLine($"  Kỳ vọng                    : {expected:N0}");
        Console.WriteLine($"  counter++ trần             : {unsafeCounter,10:N0}  ← MẤT {expected - unsafeCounter:N0} lần tăng!");
        Console.WriteLine($"  Interlocked.Increment      : {interlockedCounter,10:N0}  ✅");
        Console.WriteLine($"  lock(gate) counter++       : {lockedCounter,10:N0}  ✅");
        Console.WriteLine();
        Console.WriteLine("→ `counter++` là BA thao tác: đọc → cộng → ghi. Hai thread xen kẽ ⇒ mất kết quả của nhau.");
        Console.WriteLine("→ Interlocked dùng lệnh CPU nguyên tử (lock xadd / cmpxchg), vừa atomic vừa là full memory barrier.");
        Console.WriteLine();
        Console.WriteLine("  Pattern CAS-loop (cập nhật bất kỳ theo kiểu lock-free):");
        Console.WriteLine("    do { snapshot = Volatile.Read(ref target); newVal = f(snapshot); }");
        Console.WriteLine("    while (Interlocked.CompareExchange(ref target, newVal, snapshot) != snapshot);");
        Console.WriteLine("  (Chú ý ABA problem khi target là reference: giá trị A→B→A, CAS tưởng không ai đụng.)");
    }

    // ── ASY15 ─────────────────────────────────────────────────────────────
    public static void ASY15_LockContention()
    {
        const int N = 3_000_000;
        object gate = new();
        int counter = 0;

        for (int i = 0; i < 100_000; i++) lock (gate) counter++;   // warm-up

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < N; i++) lock (gate) counter++;
        double uncontended = sw.Elapsed.TotalMilliseconds;

        counter = 0;
        int threads = Math.Max(4, Environment.ProcessorCount);
        int perThread = N / threads;
        sw.Restart();
        Parallel.For(0, threads, _ => { for (int i = 0; i < perThread; i++) lock (gate) counter++; });
        double contended = sw.Elapsed.TotalMilliseconds;

        Console.WriteLine($"  {N:N0} lần lock KHÔNG tranh chấp (1 thread)     : {uncontended,7:F0} ms  ({uncontended * 1_000_000 / N:F1} ns/lần)");
        Console.WriteLine($"  {N:N0} lần lock CÓ tranh chấp ({threads} thread)      : {contended,7:F0} ms  ({contended * 1_000_000 / N:F1} ns/lần)");
        Console.WriteLine($"  → chậm hơn {contended / Math.Max(uncontended, 0.01):F1} lần dù TỔNG công việc y hệt");
        Console.WriteLine();
        Console.WriteLine("→ Bên dưới: (1) chưa tranh chấp ⇒ THIN LOCK, chỉ ghi thread-id vào object header bằng một lệnh CAS — rất rẻ.");
        Console.WriteLine("            (2) có tranh chấp ⇒ SPIN một lúc, hy vọng chủ lock nhả sớm.");
        Console.WriteLine("            (3) vẫn không được ⇒ INFLATE thành SyncBlock + block ở kernel (context switch, đắt gấp nghìn lần).");
        Console.WriteLine();
        Console.WriteLine("  ❌ KHÔNG BAO GIỜ lock lên: `this` (code ngoài cũng lock được), `typeof(Foo)` (dùng chung toàn app),");
        Console.WriteLine("     hoặc string literal (đã intern ⇒ dùng chung toàn PROCESS).");
        Console.WriteLine("  ⚠️ Không await được trong lock (Monitor có thread affinity) ⇒ dùng SemaphoreSlim(1,1).WaitAsync().");
        Console.WriteLine("  ℹ️ .NET 9 có type System.Threading.Lock + EnterScope() được tối ưu hơn.");
    }

    // ── ASY16 ─────────────────────────────────────────────────────────────
    public static void ASY16_FalseSharing()
    {
        const int N = 30_000_000;
        int threads = Math.Max(4, Environment.ProcessorCount);

        // (a) các counter nằm sát nhau ⇒ chung cache line
        var shared = new long[threads];
        var sw = Stopwatch.StartNew();
        Parallel.For(0, threads, t => { for (int i = 0; i < N; i++) shared[t]++; });
        double falseSharing = sw.Elapsed.TotalMilliseconds;

        // (b) mỗi counter một cache line riêng (padding 128 byte)
        var padded = new PaddedCounter[threads];
        sw.Restart();
        Parallel.For(0, threads, t => { for (int i = 0; i < N; i++) padded[t].Value++; });
        double noSharing = sw.Elapsed.TotalMilliseconds;

        Console.WriteLine($"  {threads} thread, mỗi thread tăng biến RIÊNG của mình {N:N0} lần:");
        Console.WriteLine($"    counter nằm sát nhau (long[])   : {falseSharing,7:F0} ms  ← FALSE SHARING");
        Console.WriteLine($"    counter padding 128 byte        : {noSharing,7:F0} ms");
        Console.WriteLine($"    → nhanh hơn {falseSharing / Math.Max(noSharing, 0.01):F1} lần, dù KHÔNG có lock nào, logic hoàn toàn độc lập");
        Console.WriteLine();
        Console.WriteLine("→ CPU đồng bộ bộ nhớ theo CACHE LINE 64 byte. Hai biến khác nhau nằm chung một line:");
        Console.WriteLine("  hai core sẽ liên tục GIÀNH QUYỀN SỞ HỮU line đó (cache coherence ping-pong).");
        Console.WriteLine("→ Dấu hiệu nhận biết: tăng số thread mà KHÔNG nhanh hơn, trong đoạn code không hề có lock.");
        Console.WriteLine("→ Đây là câu hỏi 'cực deep' mà interviewer senior rất thích.");
    }

    // ── ASY17 ─────────────────────────────────────────────────────────────
    public static void ASY17_ConcurrentDictionary()
    {
        var dict = new ConcurrentDictionary<int, string>();
        int factoryCalls = 0;

        Parallel.For(0, Math.Max(8, Environment.ProcessorCount * 2), _ =>
        {
            dict.GetOrAdd(1, key =>
            {
                Interlocked.Increment(ref factoryCalls);
                Thread.SpinWait(50_000);          // giả lập factory "đắt"
                return $"value-{key}";
            });
        });

        Console.WriteLine($"  Số lần valueFactory ĐƯỢC GỌI : {factoryCalls}  ← có thể > 1!");
        Console.WriteLine($"  Số phần tử trong dictionary  : {dict.Count} (chỉ MỘT kết quả được giữ lại)");
        Console.WriteLine();
        Console.WriteLine("→ Bên dưới: mảng bucket + STRIPED LOCK (nhiều lock nhỏ) cho ghi; ĐỌC thì lock-free (volatile read).");
        Console.WriteLine("→ Vì thế GetOrAdd KHÔNG bảo đảm factory chạy đúng một lần ⇒ ĐỪNG đặt side-effect trong factory");
        Console.WriteLine("  (gọi HTTP, ghi DB, mở connection). Nếu cần 'đúng một lần' ⇒ dùng Lazy<T>:");
        Console.WriteLine("     dict.GetOrAdd(key, k => new Lazy<T>(() => Expensive(k))).Value");
        Console.WriteLine("→ ConcurrentQueue<T> thì khác: danh sách các SEGMENT mảng, thao tác bằng CAS.");
        Console.WriteLine("→ ⚠️ .Count trên ConcurrentDictionary phải khoá TẤT CẢ stripe ⇒ đừng gọi trong hot path.");
    }

    // ── ASY18 ─────────────────────────────────────────────────────────────
    public static void ASY18_Channels() => Run(async () =>
    {
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(5)
        {
            FullMode = BoundedChannelFullMode.Wait      // ✅ backpressure
        });

        var sw = Stopwatch.StartNew();
        var producer = Task.Run(async () =>
        {
            for (int i = 1; i <= 20; i++)
            {
                await channel.Writer.WriteAsync(i);      // BỊ CHẶN khi channel đầy
                if (i % 5 == 0) Console.WriteLine($"    producer đã ghi tới {i,2} tại {sw.ElapsedMilliseconds,4} ms");
            }
            channel.Writer.Complete();
        });

        int consumed = 0;
        await foreach (var item in channel.Reader.ReadAllAsync())
        {
            consumed++;
            await Task.Delay(10);                        // consumer CHẬM hơn producer
        }
        await producer;

        Console.WriteLine($"  Tiêu thụ {consumed} phần tử trong {sw.ElapsedMilliseconds} ms với sức chứa chỉ 5.");
        Console.WriteLine();
        Console.WriteLine("→ Producer bị CHẶN (async, không giam thread) khi channel đầy ⇒ tốc độ tự cân bằng theo consumer.");
        Console.WriteLine("→ Unbounded channel = rủi ro OOM khi producer nhanh hơn consumer.");
        Console.WriteLine("→ Thay thế BlockingCollection<T> (vốn BLOCK thread) bằng phiên bản async.");
        Console.WriteLine("→ Liên hệ project HW: nếu tách 'đọc outbox' và 'publish broker' bằng Channel thì PHẢI bounded,");
        Console.WriteLine("  nếu không một sự cố broker sẽ làm phình RAM cho tới khi OOM.");
    });

    // ── ASY19 ─────────────────────────────────────────────────────────────
    public static void ASY19_AsyncExceptions() => Run(async () =>
    {
        // (1) exception được LƯU vào Task, chỉ ném khi await
        Task faulted = FailAsync("lỗi #1");
        Console.WriteLine($"  Gọi async method ném exception → chưa ném gì cả. Task.Status = {faulted.Status}");
        try { await faulted; }
        catch (InvalidOperationException ex) { Console.WriteLine($"  Ném ra đúng lúc await          : {ex.Message}"); }

        // (2) .Wait() bọc trong AggregateException
        try { FailAsync("lỗi #2").Wait(); }
        catch (AggregateException ae) { Console.WriteLine($"  .Wait() ⇒ {ae.GetType().Name} bọc {ae.InnerExceptions.Count} lỗi: {ae.InnerException!.Message}"); }

        // (3) WhenAll: await chỉ ném lỗi ĐẦU TIÊN
        var all = Task.WhenAll(FailAsync("A"), FailAsync("B"), FailAsync("C"));
        try { await all; }
        catch (Exception first)
        {
            Console.WriteLine($"  await Task.WhenAll ⇒ chỉ thấy 1 lỗi : {first.Message}");
            Console.WriteLine($"  Lấy ĐỦ qua all.Exception.InnerExceptions : {string.Join(", ", all.Exception!.InnerExceptions.Select(e => e.Message))}");
        }

        // (4) async void
        Console.WriteLine();
        Console.WriteLine("  async void: KHÔNG có Task để chứa exception ⇒ ném thẳng lên SynchronizationContext");
        Console.WriteLine("              ⇒ CRASH process, try/catch bên ngoài VÔ DỤNG. (không demo — nó sẽ giết process)");
        Console.WriteLine("              ⇒ chỉ dùng cho event handler UI, và bên trong phải try/catch toàn bộ.");
        Console.WriteLine();
        Console.WriteLine("→ Task bị fault mà không ai await ⇒ TaskScheduler.UnobservedTaskException (chỉ nên dùng để LOG).");
        Console.WriteLine("→ BackgroundService.ExecuteAsync ném ⇒ từ .NET 6 mặc định làm SẬP HOST");
        Console.WriteLine("  (BackgroundServiceExceptionBehavior) ⇒ nên tự bọc try/catch + log + retry.");
    });

    // ── ASY20 ─────────────────────────────────────────────────────────────
    public static void ASY20_Throttling() => Run(async () =>
    {
        const int Jobs = 30;
        const int MaxParallel = 5;

        int running = 0, peak = 0;
        using var gate = new SemaphoreSlim(MaxParallel);

        var sw = Stopwatch.StartNew();
        var tasks = Enumerable.Range(1, Jobs).Select(async _ =>
        {
            await gate.WaitAsync();                       // async, KHÔNG giam thread
            try
            {
                InterlockedMax(ref peak, Interlocked.Increment(ref running));
                await Task.Delay(40);                     // giả lập I/O
            }
            finally
            {
                Interlocked.Decrement(ref running);
                gate.Release();                           // ⚠️ PHẢI ở finally
            }
        });
        await Task.WhenAll(tasks);

        Console.WriteLine($"  {Jobs} việc, giới hạn {MaxParallel} song song ⇒ đỉnh thực tế = {peak}, tổng {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"  (không giới hạn thì {Jobs} việc sẽ cùng đập vào DB/API một lúc)");
        Console.WriteLine();
        Console.WriteLine("→ SemaphoreSlim là 'lock' dùng được trong async (WaitAsync) và là cách chuẩn để giới hạn đồng thời.");
        Console.WriteLine("→ .NET 6+ còn có Parallel.ForEachAsync(source, new ParallelOptions { MaxDegreeOfParallelism = N }, …).");
        Console.WriteLine();
        Console.WriteLine("  Chọn primitive:");
        Console.WriteLine("    lock/Monitor        đoạn code ngắn, đồng bộ            SpinLock          critical section CỰC ngắn");
        Console.WriteLine("    Interlocked         một biến                          SemaphoreSlim     async lock / giới hạn N");
        Console.WriteLine("    ReaderWriterLockSlim đọc nhiều ghi ít (đo trước!)      Channel<T>        producer/consumer");
    });

    // ── ASY21 ─────────────────────────────────────────────────────────────
    public static void ASY21_DiagnosticsCheatSheet()
    {
        Console.WriteLine("  ① dotnet-counters monitor -p <pid> System.Runtime");
        Console.WriteLine("       threadpool-thread-count          ↑ đều          = đang inject vì thread bị block");
        Console.WriteLine("       threadpool-queue-length          > 0 kéo dài    = ★ STARVATION");
        Console.WriteLine("       threadpool-completed-items-count / s            = throughput thật");
        Console.WriteLine("       monitor-lock-contention-count    ↑              = tranh chấp lock");
        Console.WriteLine();
        Console.WriteLine("  ② dotnet-stack report -p <pid>     → stack của MỌI thread; thấy ngay ai đang .Result/.Wait()");
        Console.WriteLine();
        Console.WriteLine("  ③ dotnet-dump collect -p <pid> → dotnet-dump analyze");
        Console.WriteLine("       dumpasync         mọi state machine async đang treo + nó đang chờ AI");
        Console.WriteLine("       syncblk           object nào đang bị lock và thread nào đang giữ");
        Console.WriteLine("       clrstack -all     toàn bộ stack managed");
        Console.WriteLine("       threads           danh sách thread + trạng thái");
        Console.WriteLine();
        Console.WriteLine("  ④ BỘ BA TRIỆU CHỨNG ↔ NGUYÊN NHÂN (thuộc lòng để trả lời phỏng vấn):");
        Console.WriteLine("       CPU thấp  + latency cao + queue dài   ⇒ thread pool starvation (sync-over-async)");
        Console.WriteLine("       CPU cao   + throughput thấp           ⇒ lock contention hoặc false sharing");
        Console.WriteLine("       RAM tăng  + nhiều async state machine ⇒ task không bao giờ hoàn thành (thiếu timeout)");
        Console.WriteLine();
        Console.WriteLine("  ⑤ PHÒNG BỆNH: async all the way · luôn truyền CancellationToken · luôn có timeout");
        Console.WriteLine("     · ConfigureAwait(false) trong thư viện · RunContinuationsAsynchronously cho TCS");
        Console.WriteLine("     · giới hạn đồng thời bằng SemaphoreSlim/Channel bounded.");
    }

    // ── Helper ────────────────────────────────────────────────────────────

    private static readonly AsyncLocal<string?> _asyncLocal = new();
    private static readonly ThreadLocal<string?> _threadLocal = new();

    // Dùng cho ASY13 — CỐ Ý không volatile để quan sát reordering.
    private static int _x, _y, _r1, _r2;

    /// <summary>Chạy logic async trong một demo có chữ ký `Action`.</summary>
    private static void Run(Func<Task> body) => body().GetAwaiter().GetResult();

    private static async Task<long> AllocAsync(Func<Task> action)
    {
        await action();                                   // warm-up (JIT + cache)
        long before = GC.GetAllocatedBytesForCurrentThread();
        await action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static async Task<int> AlreadyDoneAsync()
    {
        await Task.CompletedTask;                         // không bao giờ dừng ⇒ state machine không bị box
        return 1;
    }

    private static async ValueTask<int> AlreadyDoneValueTaskAsync()
    {
        await Task.CompletedTask;
        return 1;
    }

    private static async Task<int> ReallySuspendsAsync()
    {
        await Task.Yield();                               // luôn dừng ⇒ box state machine + tạo Task
        return 1;
    }

    private static async Task<int> WorkThenReturnToContextAsync()
    {
        await Task.Delay(50);                             // capture SynchronizationContext hiện tại
        return 42;
    }

    private static async IAsyncEnumerable<string> GenerateAsync(
        int count,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (int i = 1; i <= count; i++)
        {
            await Task.Delay(30, ct);
            yield return $"item-{i}";
        }
    }

    private static long IgnoresToken(CancellationToken ct)
    {
        long acc = 0;
        for (int i = 0; i < 400_000_000; i++) acc += i;   // không hề nhìn tới ct
        return acc;
    }

    private static void RespectsToken(CancellationToken ct)
    {
        long acc = 0;
        for (int i = 0; i < 400_000_000; i++)
        {
            if ((i & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            acc += i;
        }
    }

    private static async Task FailAsync(string message)
    {
        await Task.Yield();
        throw new InvalidOperationException(message);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int snapshot;
        do
        {
            snapshot = Volatile.Read(ref target);
            if (value <= snapshot) return;
        }
        while (Interlocked.CompareExchange(ref target, value, snapshot) != snapshot);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Helper types
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Awaitable tự viết — chứng minh `await` chỉ dựa trên PATTERN, không phải interface.</summary>
file readonly struct DelayAwaitable(TimeSpan delay)
{
    public Awaiter GetAwaiter() => new(delay);

    public readonly struct Awaiter(TimeSpan delay) : ICriticalNotifyCompletion
    {
        private readonly TimeSpan _delay = delay;
        public bool IsCompleted => _delay <= TimeSpan.Zero;
        public void OnCompleted(Action continuation) => UnsafeOnCompleted(continuation);
        public void UnsafeOnCompleted(Action continuation)
        {
            Timer? timer = null;
            timer = new Timer(_ => { timer!.Dispose(); continuation(); }, null, _delay, Timeout.InfiniteTimeSpan);
        }
        public void GetResult() { }
    }
}

/// <summary>Awaitable đã có sẵn giá trị ⇒ IsCompleted = true ⇒ await chạy thẳng, không dừng.</summary>
file readonly struct ValueAwaitable<T>(T value)
{
    public Awaiter GetAwaiter() => new(value);

    public readonly struct Awaiter(T value) : INotifyCompletion
    {
        private readonly T _value = value;
        public bool IsCompleted => true;
        public void OnCompleted(Action continuation) => continuation();
        public T GetResult() => _value;
    }
}

/// <summary>
/// SynchronizationContext một-thread, giống UI thread: mọi continuation được POST vào hàng đợi
/// và chỉ chạy khi thread đó "bơm" (pump). Dùng để tái hiện deadlock ở ASY7.
/// </summary>
file sealed class PumpingSyncContext : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

    public int PendingCount => _queue.Count;

    public override void Post(SendOrPostCallback d, object? state) => _queue.TryAdd((d, state));

    public override void Send(SendOrPostCallback d, object? state) => d(state);

    /// <summary>Chạy một async method rồi bơm hàng đợi cho tới khi nó hoàn tất.</summary>
    public void RunOnCurrentThread(Func<Task> body)
    {
        Task task = body();
        task.ContinueWith(_ => _queue.CompleteAdding(), TaskScheduler.Default);

        foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            callback(state);
    }
}

file sealed class CachedService(Dictionary<int, string> cache)
{
    public Task<string> GetAsTaskAsync(int id)
        => cache.TryGetValue(id, out var v) ? Task.FromResult(v) : LoadAsync(id);

    public ValueTask<string> GetAsValueTaskAsync(int id)
        => cache.TryGetValue(id, out var v) ? new ValueTask<string>(v) : new ValueTask<string>(LoadAsync(id));

    private static async Task<string> LoadAsync(int id)
    {
        await Task.Delay(1);
        return $"loaded-{id}";
    }
}

/// <summary>Mỗi counter chiếm trọn 2 cache line ⇒ hai core không giành nhau (xem ASY16).</summary>
[StructLayout(LayoutKind.Explicit, Size = 128)]
file struct PaddedCounter
{
    [FieldOffset(0)] public long Value;
}
