# Phần 2 — Async / Await & Multithreading

[⬅️ Phần 1](interview.NET.01-CSharp-CLR.md) | [Mục lục](interview.NET.md) | [Phần 3 — Collections & LINQ ➡️](interview.NET.03-Collections-LINQ.md)

> 🔬 **Đào sâu cơ chế**: state machine compiler sinh ra, IOCP/epoll, `ExecutionContext` vs
> `SynchronizationContext`, ThreadPool work-stealing & starvation, memory model, thin lock,
> false sharing → [Phần 13 — Async & Threading Internals](interview.NET.13-Async-Threading-Internals.md).

---

## 26. `async`/`await` hoạt động như thế nào?

- `async` đánh dấu method có `await`; compiler biến nó thành **state machine**. Gặp `await` → trả control về caller, không tạo thread mới.

```csharp
public async Task<string> GetDataAsync()
{
    Console.WriteLine("Bắt đầu");
    // Tại đây thread được GIẢI PHÓNG (không block) trong lúc chờ I/O
    string result = await httpClient.GetStringAsync("https://api.example.com");
    // Khi HTTP xong, tiếp tục từ đây (có thể trên thread khác)
    Console.WriteLine("Xong");
    return result;
}
```

- **Câu chốt**: "async giải phóng thread trong lúc chờ, KHÔNG phải chạy song song."

---

## 27. Sự khác nhau giữa Task và Thread?

```csharp
// ❌ Thread: đơn vị OS-level, tạo/huỷ đắt, tốn ~1MB stack
var thread = new Thread(() => Console.WriteLine("work"));
thread.Start();
thread.Join();

// ✅ Task: abstraction trên ThreadPool, hỗ trợ continuation/cancellation/result
Task<int> task = Task.Run(() => 1 + 1);
int result = await task;

// Task hỗ trợ tổ hợp
await Task.WhenAll(task1, task2, task3);
```

- **Điểm nhấn**: ưu tiên `Task`/`async` thay vì tạo `Thread` thủ công.

---

## 28. `Task.Run` khác `async/await` thuần ở điểm nào?

```csharp
// ✅ CPU-bound: dùng Task.Run đẩy lên thread pool
public async Task<long> CalculateAsync()
    => await Task.Run(() => {
        long sum = 0;
        for (int i = 0; i < 1_000_000_000; i++) sum += i; // tính toán nặng
        return sum;
    });

// ✅ I/O-bound: KHÔNG cần Task.Run, chỉ await method async có sẵn
public async Task<string> ReadFileAsync()
    => await File.ReadAllTextAsync("data.txt");

// ❌ Anti-pattern: Task.Run bọc I/O trong ASP.NET → lãng phí thread
public async Task<string> BadAsync()
    => await Task.Run(async () => await File.ReadAllTextAsync("data.txt"));
```

---

## 29. Deadlock với `.Result` / `.Wait()` xảy ra thế nào?

```csharp
// ❌ Deadlock trong context có SynchronizationContext (WPF/WinForms/ASP.NET classic)
public string GetData()
{
    // .Result BLOCK thread, giữ context; continuation của await cần chính
    // thread đó → deadlock
    return GetDataAsync().Result; // TREO!
}

// ✅ Async all the way
public async Task<string> GetDataProperly()
{
    return await GetDataAsync();
}
```

- **Cơ chế**: thread bị block giữ context → continuation không có chỗ chạy → task không complete → block vĩnh viễn.

---

## 30. `ConfigureAwait(false)` dùng để làm gì?

```csharp
// ✅ Trong LIBRARY code: không cần quay lại context gốc → tránh deadlock, nhanh hơn
public async Task<string> LibraryMethodAsync()
{
    var data = await httpClient.GetStringAsync(url).ConfigureAwait(false);
    // continuation chạy trên thread pool bất kỳ, KHÔNG cần context gốc
    return Process(data);
}
```

- **Điểm nhấn**: ASP.NET Core không có SynchronizationContext nên ít quan trọng hơn, nhưng vẫn nên dùng ở thư viện tái sử dụng.

---

## 31. `CancellationToken` dùng để làm gì?

```csharp
public async Task ProcessAsync(CancellationToken cancellationToken)
{
    for (int i = 0; i < 1000; i++)
    {
        cancellationToken.ThrowIfCancellationRequested(); // kiểm tra huỷ
        await DoWorkAsync(cancellationToken);             // truyền xuống tiếp
    }
}

// Sử dụng với timeout
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
try
{
    await ProcessAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Đã huỷ sau 5s");
}
```

- **Điểm nhấn**: cancellation là **cooperative** — phải truyền token xuyên suốt và chủ động kiểm tra.

---

## 32. `Task.WhenAll` và `Task.WhenAny` khác nhau?

```csharp
// WhenAll: chờ TẤT CẢ, chạy song song
var tasks = urls.Select(url => httpClient.GetStringAsync(url));
string[] results = await Task.WhenAll(tasks); // gom kết quả

// WhenAny: hoàn thành khi task ĐẦU TIÊN xong (timeout, race)
var dataTask = GetDataAsync();
var timeoutTask = Task.Delay(TimeSpan.FromSeconds(3));
var completed = await Task.WhenAny(dataTask, timeoutTask);
if (completed == timeoutTask) Console.WriteLine("Timeout!");

// ❌ Tuần tự (chậm) vs ✅ song song
// var a = await GetA(); var b = await GetB(); // 2 lần chờ nối tiếp
var (a, b) = (GetA(), GetB());
await Task.WhenAll(a, b);                       // chờ đồng thời
```

---

## 33. `ValueTask` khác `Task`? Khi nào dùng?

```csharp
private Dictionary<int, string> _cache = new();

// ✅ ValueTask: tránh allocation khi kết quả THƯỜNG có sẵn đồng bộ (cache hit)
public ValueTask<string> GetAsync(int id)
{
    if (_cache.TryGetValue(id, out var cached))
        return new ValueTask<string>(cached); // đồng bộ, không allocate Task
    return new ValueTask<string>(LoadFromDbAsync(id)); // async khi cache miss
}

// ⚠️ Hạn chế: KHÔNG await 2 lần, KHÔNG dùng chung nhiều nơi
// ❌ var vt = GetAsync(1); await vt; await vt; // sai!
```

- **Trade-off**: chỉ tối ưu khi đo được vấn đề allocation ở hot path.

---

## 34. Race condition là gì? Cách phòng tránh?

```csharp
// ❌ Race condition: nhiều thread cùng ++ trên biến chung
int counter = 0;
Parallel.For(0, 10000, _ => counter++); // KHÔNG atomic → kết quả sai (< 10000)

// ✅ Cách 1: lock
object lockObj = new();
Parallel.For(0, 10000, _ => { lock (lockObj) counter++; });

// ✅ Cách 2: Interlocked (nhanh hơn cho phép toán đơn giản)
Parallel.For(0, 10000, _ => Interlocked.Increment(ref counter));

// ✅ Cách 3: Concurrent collection
var bag = new ConcurrentBag<int>();
Parallel.For(0, 10000, i => bag.Add(i));
```

---

## 35. `lock` hoạt động thế nào? Nên lock trên gì?

```csharp
public class BankAccount
{
    private readonly object _lock = new(); // ✅ private readonly object riêng
    private decimal _balance;

    public void Withdraw(decimal amount)
    {
        lock (_lock) // = Monitor.Enter/Exit, chỉ 1 thread vào
        {
            if (_balance >= amount) _balance -= amount;
        }
    }
}

// ❌ KHÔNG lock trên các thứ này (code ngoài có thể lock cùng → deadlock)
// lock (this) { }              // sai
// lock (typeof(BankAccount)) { } // sai
// lock ("some string") { }     // sai (string interned)
```

---

## 36. `Interlocked` để làm gì?

```csharp
int counter = 0;
long total = 0;

Interlocked.Increment(ref counter);        // ++counter atomic
Interlocked.Decrement(ref counter);        // --counter atomic
Interlocked.Add(ref total, 100);           // total += 100 atomic
long old = Interlocked.Exchange(ref total, 0); // gán + trả giá trị cũ, atomic

// CompareExchange: "nếu = comparand thì gán value", atomic
int expected = 5;
Interlocked.CompareExchange(ref counter, 10, expected); // nếu counter==5 → 10
```

- **Điểm nhấn**: nhanh hơn `lock` cho phép toán đơn giản trên biến số (counter).

---

## 37. Sự khác nhau giữa `SemaphoreSlim` và `lock`?

```csharp
// lock KHÔNG await được → không dùng trong async
// ✅ SemaphoreSlim: giới hạn N thread + hỗ trợ async
private readonly SemaphoreSlim _semaphore = new(3); // tối đa 3 đồng thời

public async Task ProcessAsync()
{
    await _semaphore.WaitAsync();  // chờ slot (async, không block thread)
    try
    {
        await DoWorkAsync();
    }
    finally
    {
        _semaphore.Release();      // ✅ luôn release trong finally
    }
}
```

---

## 38. `ThreadPool` là gì? Tại sao quan trọng với async?

```csharp
// Thread pool: tập thread tái sử dụng do runtime quản lý
ThreadPool.QueueUserWorkItem(_ => Console.WriteLine("từ pool"));

// ❌ Sync-over-async CHẶN thread pool → thread starvation
public IActionResult BadEndpoint()
{
    var data = GetDataAsync().Result; // chặn 1 thread pool thread
    return Ok(data);
}

// ✅ Async giải phóng thread pool thread trong lúc chờ
public async Task<IActionResult> GoodEndpoint()
{
    var data = await GetDataAsync();
    return Ok(data);
}
```

---

## 39. `IAsyncEnumerable<T>` và `await foreach`?

```csharp
// Stream dữ liệu bất đồng bộ từng phần tử (không load hết vào bộ nhớ)
public async IAsyncEnumerable<Order> GetOrdersAsync(
    [EnumeratorCancellation] CancellationToken ct = default)
{
    await foreach (var row in dbReader.ReadAsync(ct))
        yield return Map(row); // trả từng phần tử khi có
}

// Tiêu thụ
await foreach (var order in GetOrdersAsync())
    Console.WriteLine(order.Id);
```

---

## 40. Exception trong async method được xử lý thế nào?

```csharp
// Exception lưu trong Task, ném lại khi await
public async Task<int> DivideAsync(int a, int b)
{
    await Task.Delay(10);
    return a / b; // ném khi b == 0
}

try { await DivideAsync(10, 0); }
catch (DivideByZeroException) { Console.WriteLine("Chia 0!"); }

// ❌ async void: exception KHÔNG bắt được → crash process
public async void BadFireAndForget()
{
    throw new Exception("mất tăm, crash app!"); // không catch được ở caller
}

// ✅ Dùng async Task
public async Task GoodMethod() { throw new Exception("bắt được qua await"); }
```

---

# 🔍 Câu hỏi đào sâu — Phần 2

## 2D-1. `await` có tạo thread mới không? Ai chạy phần sau await?

```csharp
public async Task DemoAsync()
{
    Console.WriteLine($"Trước: Thread {Thread.CurrentThread.ManagedThreadId}");
    await Task.Delay(100); // KHÔNG thread nào bị chặn (dựa I/O completion port)
    Console.WriteLine($"Sau: Thread {Thread.CurrentThread.ManagedThreadId}");
    // Có thể là thread KHÁC (thread pool) - không tạo thread mới, tái dùng pool
}
```

- **Câu chốt**: "async giải phóng thread trong lúc chờ, không phải chạy song song."

---

## 2D-2. Async state machine sinh ra thế nào? Có allocate không?

```csharp
// Compiler biến method này thành 1 struct IAsyncStateMachine
public async Task<int> GetAsync()
{
    var x = await FetchAsync();
    return x + 1;
}
// - Nếu FetchAsync hoàn thành ĐỒNG BỘ (task đã xong) → gần zero-allocation
// - Nếu await thật sự yield → state machine bị BOX lên heap để sống qua callback
// → Đây là lý do ValueTask giảm allocation ở hot path (câu 33)
```

---

## 2D-3. `SynchronizationContext` là gì? Tại sao ASP.NET Core không có?

```csharp
// WPF/WinForms: continuation quay về UI thread (để update UI an toàn)
// ASP.NET classic: quay về request context
// ASP.NET Core: KHÔNG có SynchronizationContext
//   → continuation chạy trên thread pool bất kỳ
//   → không deadlock kiểu .Result, ConfigureAwait(false) ít tác dụng hơn

// Kiểm tra
Console.WriteLine(SynchronizationContext.Current?.ToString() ?? "null (ASP.NET Core)");
```

---

## 2D-4. Tái hiện deadlock với `.Result` — từng bước

```csharp
// Trong WinForms/WPF:
private void Button_Click(object sender, EventArgs e)
{
    // Bước 1: UI thread gọi .Result → BLOCK, giữ UI context
    var data = LoadAsync().Result; // TREO
}

private async Task<string> LoadAsync()
{
    // Bước 2: await xong, continuation cần quay lại UI context
    await Task.Delay(1000);
    // Bước 3: nhưng UI thread đang bị .Result block → continuation không chạy
    //         → task không complete → block vĩnh viễn
    return "data";
}

// ✅ Fix: async all the way HOẶC ConfigureAwait(false)
private async Task<string> LoadFixed()
{
    await Task.Delay(1000).ConfigureAwait(false); // không cần context
    return "data";
}
```

---

## 2D-5. Thread pool starvation xảy ra thế nào? Dấu hiệu?

```csharp
// ❌ Nhiều request cùng block bằng sync-over-async → hết thread rảnh
public IActionResult Endpoint()
{
    // Mỗi request chiếm 1 thread pool thread và BLOCK nó
    var result = _service.GetDataAsync().Result;
    return Ok(result);
}
// Thread pool tăng thread rất chậm (~1-2/giây) → app "đơ" dù CPU thấp
```

- **Dấu hiệu**: latency tăng đột biến, queue request dồn, **CPU không cao**. **Fix**: bỏ sync-over-async (async all the way).

---

## 2D-6. `TaskCompletionSource` dùng để làm gì?

```csharp
// Cầu nối callback/event-based API → async/await
public Task<string> WaitForMessageAsync()
{
    var tcs = new TaskCompletionSource<string>(
        TaskCreationOptions.RunContinuationsAsynchronously); // ✅ tránh deadlock inline

    _messageBus.OnMessage += msg => tcs.TrySetResult(msg); // hoàn thành khi có event
    _messageBus.OnError += ex => tcs.TrySetException(ex);

    return tcs.Task; // caller await task này
}
```

---

## 2D-7. Tại sao `async void` nguy hiểm? Ngoại lệ nào được phép?

```csharp
// ❌ async void: không await được, exception crash process, không test được
public async void ProcessData() // NGUY HIỂM
{
    await Task.Delay(100);
    throw new Exception("crash toàn app, không catch được ở caller");
}

// ✅ Ngoại lệ DUY NHẤT: event handler (buộc phải async void do signature)
private async void Button_Click(object sender, EventArgs e)
{
    try // phải try/catch bên trong
    {
        await DoWorkAsync();
    }
    catch (Exception ex) { ShowError(ex); }
}
```

---

## 2D-8. `Parallel.ForEach` / PLINQ khác `async/await`? Khi nào dùng?

```csharp
// ✅ CPU-bound: Parallel/PLINQ chia việc lên nhiều core
Parallel.ForEach(images, img => ResizeImage(img)); // xử lý ảnh nặng
var results = numbers.AsParallel().Select(n => HeavyCompute(n)).ToList();

// ❌ Parallel.ForEach với body async KHÔNG await được
Parallel.ForEach(urls, async url => await DownloadAsync(url)); // SAI

// ✅ I/O song song có giới hạn: Parallel.ForEachAsync (.NET 6+)
await Parallel.ForEachAsync(urls,
    new ParallelOptions { MaxDegreeOfParallelism = 5 },
    async (url, ct) => await DownloadAsync(url, ct));
```

---

## 2D-9. `volatile` và memory barrier để làm gì?

```csharp
public class Worker
{
    private volatile bool _stop; // ✅ volatile: đọc/ghi không bị reorder, không cache thanh ghi

    public void Run()
    {
        while (!_stop) { /* work */ } // thấy _stop mới nhất từ thread khác
    }
    public void Stop() => _stop = true;
}

// ⚠️ volatile KHÔNG đảm bảo atomic cho phép toán phức hợp
// volatile int x; x++; // vẫn race! → dùng Interlocked
```

---

## 2D-10. Double-checked locking đúng cách?

```csharp
public sealed class Singleton
{
    private static Singleton? _instance;
    private static readonly object _lock = new();

    public static Singleton Instance
    {
        get
        {
            if (_instance == null)           // check 1 (không lock, nhanh)
            {
                lock (_lock)
                {
                    if (_instance == null)   // check 2 (trong lock, an toàn)
                        _instance = new Singleton();
                }
            }
            return _instance;
        }
    }
}

// ✅ .NET hiện đại: ưu tiên Lazy<T> cho gọn và an toàn
private static readonly Lazy<Singleton> _lazy = new(() => new Singleton());
public static Singleton Better => _lazy.Value;
```

---

## 2D-11. `Interlocked.CompareExchange` dùng khi nào?

```csharp
// Lock-free: khởi tạo lazy an toàn không cần lock
private object? _value;
public object GetValue()
{
    if (_value == null)
    {
        var created = new object();
        // "nếu _value đang null thì gán created", atomic
        Interlocked.CompareExchange(ref _value, created, null);
    }
    return _value;
}

// Lock-free counter với logic phức tạp
int current, updated;
do
{
    current = _state;
    updated = Compute(current);
} while (Interlocked.CompareExchange(ref _state, updated, current) != current);
```

---

## 2D-12. Giới hạn số tác vụ async chạy đồng thời?

```csharp
// ✅ Cách 1: SemaphoreSlim
var semaphore = new SemaphoreSlim(5); // tối đa 5 đồng thời
var tasks = urls.Select(async url =>
{
    await semaphore.WaitAsync();
    try { return await DownloadAsync(url); }
    finally { semaphore.Release(); }
});
await Task.WhenAll(tasks);

// ✅ Cách 2: Parallel.ForEachAsync với MaxDegreeOfParallelism (câu 2D-8)
```

---

## 2D-13. `CancellationToken` truyền xuống nhưng không dừng — vì sao?

```csharp
// ❌ Không kiểm tra token → vẫn chạy tiếp
public async Task BadLoop(CancellationToken ct)
{
    while (true) { await Task.Delay(1000); } // bỏ qua ct → không dừng
}

// ✅ Chủ động kiểm tra + truyền token xuống API dưới
public async Task GoodLoop(CancellationToken ct)
{
    while (true)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Delay(1000, ct); // truyền token vào Delay
    }
}
```

- **Điểm nhấn**: cancellation là "tín hiệu", không phải "kill switch".

---

## 2D-14. Nhiều exception trong `Task.WhenAll` — bắt đủ thế nào?

```csharp
var tasks = new[] { FailAsync("A"), FailAsync("B"), FailAsync("C") };

// ❌ await chỉ ném exception ĐẦU TIÊN
try { await Task.WhenAll(tasks); }
catch (Exception ex) { Console.WriteLine(ex.Message); } // chỉ thấy 1

// ✅ Lấy HẾT qua AggregateException
var all = Task.WhenAll(tasks);
try { await all; }
catch
{
    foreach (var inner in all.Exception!.InnerExceptions)
        Console.WriteLine(inner.Message); // A, B, C
}
```

---

## 2D-15. `ThreadLocal<T>` và `AsyncLocal<T>` khác nhau?

```csharp
// ThreadLocal: mỗi THREAD một bản riêng
var threadLocal = new ThreadLocal<int>(() => 0);

// AsyncLocal: dữ liệu "chảy" theo logical async context (xuyên qua await, đổi thread vẫn giữ)
private static readonly AsyncLocal<string> _correlationId = new();

public async Task HandleRequestAsync(string id)
{
    _correlationId.Value = id;          // set
    await SomeDeepAsyncCall();           // đổi thread nhưng _correlationId.Value vẫn = id
    Console.WriteLine(_correlationId.Value);
}
```

- **Dùng cho**: ambient data như correlation ID, user context trong async pipeline.

---

[⬅️ Phần 1](interview.NET.01-CSharp-CLR.md) | [Mục lục](interview.NET.md) | [Phần 3 — Collections & LINQ ➡️](interview.NET.03-Collections-LINQ.md)
