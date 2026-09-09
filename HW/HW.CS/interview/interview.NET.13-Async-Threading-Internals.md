# Phần 13 — Async / Await & Threading Internals

[⬅️ Phần 12 — Memory & GC](interview.NET.12-Memory-GC-Internals.md) | [Về mục lục](interview.NET.md) | Tiếp theo: [Phần 14 — Framework Internals ➡️](interview.NET.14-Framework-Internals.md)

> Khung trả lời: **❓ Vấn đề gốc → ⚙️ Cơ chế bên dưới → 💻 Code/đo đạc → ⚖️ Hệ quả thực chiến**.
> Demo chạy được: `dotnet run -- 13 all` (xem `Demos/Demo13_AsyncThreadingInternals.cs`).

---

## 🗺️ Bản đồ: một `await` I/O đi qua đâu

```
await httpClient.GetAsync(url)
   │
   ├─ 1. Method của bạn đã bị compiler biến thành STATE MACHINE (struct MoveNext)
   │
   ├─ 2. Gọi xuống SocketsHttpHandler → Socket.SendAsync (overlapped / non-blocking)
   │
   ├─ 3. Đăng ký với OS:  IOCP (Windows) / epoll (Linux) / kqueue (macOS)
   │      ⇒ KHÔNG có thread nào ngồi chờ. Thread hiện tại được TRẢ VỀ ThreadPool.
   │
   ├─ 4. …dữ liệu về… OS báo completion
   │
   ├─ 5. ThreadPool (I/O completion) lấy một thread bất kỳ, gọi
   │      stateMachine.MoveNext()  → chạy tiếp phần code SAU await
   │
   └─ 6. ExecutionContext (AsyncLocal, culture…) được RESTORE trước khi chạy tiếp
```

**Câu chốt phỏng vấn:** *"`async` không làm request nhanh hơn — nó làm server **chịu tải cao hơn**,
vì trong lúc chờ I/O không có thread nào bị giam. `async` đổi *thread* lấy *state machine trên heap*."*

---

## ASY-1. Vấn đề gốc: vì sao thread lại "đắt"?

| Chi phí một thread | Con số điển hình |
|---|---|
| Stack ảo | **1 MB** (Windows mặc định) |
| Tạo/huỷ | ~ hàng trăm µs, phải gọi kernel |
| Context switch | ~1–10 µs + xả cache CPU |
| Kernel object, TLS, scheduler | thêm overhead |

Model cũ "**thread-per-request**" với 10.000 request đồng thời ⇒ 10.000 thread ⇒ ~10 GB stack ảo +
scheduler sập. Trong khi đó, 99% thời gian của web API là **chờ I/O** (DB, HTTP, disk) — thread
ngồi không mà vẫn tốn tài nguyên.

**⚙️ Giải pháp async**: khi bắt đầu I/O, lưu "phần việc còn lại" thành một **object** (state
machine) rồi **trả thread về pool**. Khi I/O xong, mượn thread bất kỳ chạy tiếp.
⇒ 10.000 request đồng thời chỉ cần **vài chục thread**.

**⚖️ Hệ quả**: `async` không giúp gì cho **CPU-bound** (không có lúc nào để trả thread về). Với
CPU-bound, dùng `Task.Run`/`Parallel` để **dùng nhiều core**, đó là bài toán khác hẳn.

---

## ASY-2. Không có thread nào chờ I/O — vậy ai chờ?

**⚙️ Cơ chế**: **OS + card mạng/đĩa** chờ, không phải thread.

| OS | Cơ chế | .NET dùng |
|---|---|---|
| Windows | **IOCP** (I/O Completion Port) — overlapped I/O | ThreadPool có "I/O completion thread" riêng |
| Linux | **epoll** (edge-triggered), io_uring (mới) | vòng lặp epoll trong runtime, đẩy completion vào ThreadPool |
| macOS | kqueue | tương tự |

Luồng: `ReadAsync` → gọi syscall non-blocking → OS trả về "pending" → runtime đăng ký completion →
**trả thread về** → khi DMA/NIC hoàn tất, OS đưa completion vào port → runtime lấy một thread pool
chạy continuation.

**⚖️ Câu chốt cực ăn điểm**: *"`await Task.Delay(1000)` không giữ thread nào — nó chỉ đăng ký một
**timer** trong timer queue. Còn `Thread.Sleep(1000)` thì giam nguyên một thread 1 giây."*

---

## ASY-3. State machine — compiler sinh ra chính xác cái gì?

```csharp
async Task<int> GetAsync()
{
    int local = 1;
    var data = await FetchAsync();     // điểm dừng
    return local + data.Length;
}
```

Compiler sinh (rút gọn):

```csharp
struct GetAsyncStateMachine : IAsyncStateMachine
{
    public int __state;                       // -1 = chưa chạy/đang chạy, 0.. = đang chờ await thứ N
    public AsyncTaskMethodBuilder<int> __builder;
    public int local;                         // ⚠️ biến local vượt qua await → thành FIELD
    private TaskAwaiter<Data> __awaiter;

    public void MoveNext()
    {
        try
        {
            if (__state == -1)
            {
                local = 1;
                __awaiter = FetchAsync().GetAwaiter();
                if (!__awaiter.IsCompleted)                 // ① chưa xong?
                {
                    __state = 0;
                    __builder.AwaitUnsafeOnCompleted(ref __awaiter, ref this); // ② BOX state machine lên heap
                    return;                                  // ③ TRẢ THREAD VỀ
                }
                // nếu đã xong → chạy thẳng, KHÔNG allocate, KHÔNG đổi thread
            }
            var data = __awaiter.GetResult();
            __builder.SetResult(local + data.Length);
        }
        catch (Exception e) { __builder.SetException(e); }   // exception → đặt vào Task
    }
}
```

**Những điều quan trọng suy ra được:**
1. **State machine là `struct`** trong Release. Nó chỉ **bị box lên heap** ở lần đầu gặp một
   await **thực sự chưa hoàn thành**. ⇒ `async` method mà mọi `await` đều đã xong ⇒ **0 allocation**.
2. Local **vượt qua** await trở thành field ⇒ giữ object sống lâu hơn bạn tưởng (nguồn "leak" ngầm).
3. `try/catch` bọc toàn bộ ⇒ exception được **lưu vào Task**, không ném ra call stack ngay.
4. Debug build sinh **class** thay struct (để debugger xem được) ⇒ đừng đo allocation ở Debug.

```csharp
// Đo: await Task.CompletedTask ⇒ 0 byte; await Task.Yield() ⇒ có allocation
```

---

## ASY-4. Awaitable pattern — `await` là đường cú pháp cho cái gì?

`await x` hợp lệ khi `x` có `GetAwaiter()` trả về type có:
- `bool IsCompleted { get; }`
- `void OnCompleted(Action)` (`INotifyCompletion`) — và tốt hơn: `UnsafeOnCompleted` (`ICriticalNotifyCompletion`)
- `T GetResult()`

⇒ **Bạn tự viết awaitable được**, và `await` không hề gắn cứng với `Task`:

```csharp
readonly struct DelayAwaitable(TimeSpan delay)
{
    public Awaiter GetAwaiter() => new(delay);

    public readonly struct Awaiter(TimeSpan d) : ICriticalNotifyCompletion
    {
        public bool IsCompleted => d <= TimeSpan.Zero;
        public void OnCompleted(Action k) => new Timer(_ => k(), null, d, Timeout.InfiniteTimeSpan);
        public void UnsafeOnCompleted(Action k) => OnCompleted(k);
        public void GetResult() { }
    }
}
// await new DelayAwaitable(TimeSpan.FromMilliseconds(50));
```

Chính vì thế `await` dùng được với `Task`, `ValueTask`, `Task.Yield()`, `IAsyncEnumerable`,
`SemaphoreSlim.WaitAsync()`, và cả awaitable của Unity/WinForms.

---

## ASY-5. `Task` bên dưới là gì? `TaskCompletionSource` để làm gì?

**⚙️ `Task` = 1 object chứa**: cờ trạng thái (Created/Running/RanToCompletion/Faulted/Canceled),
kết quả, exception (`AggregateException`), **danh sách continuation**, và `TaskScheduler`.

- `await` = "đăng ký một continuation vào task này".
- Khi task hoàn tất → chạy continuation. **Mặc định** continuation có thể chạy **đồng bộ ngay
  trên thread vừa hoàn tất task** (tối ưu) — điều này gây bất ngờ nguy hiểm.

```csharp
// TaskCompletionSource: cây cầu giữa callback-style và async/await
var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
//                                          ▲ GẦN NHƯ LUÔN NÊN BẬT
legacyApi.OnDone   += r => tcs.TrySetResult(r);
legacyApi.OnFailed += e => tcs.TrySetException(e);
string result = await tcs.Task;
```

**⚙️ Vì sao `RunContinuationsAsynchronously` quan trọng**: nếu không bật, khi bạn gọi
`tcs.SetResult()` **trong khi đang giữ lock** (hoặc trên một thread quan trọng như thread đọc
socket), toàn bộ continuation của người khác sẽ chạy **ngay trên thread đó, bên trong lock** →
deadlock hoặc chặn nghẽn hệ thống. Đây là bug kinh điển trong thư viện.

---

## ASY-6. `ExecutionContext` vs `SynchronizationContext` — hai thứ hoàn toàn khác nhau

| | `ExecutionContext` | `SynchronizationContext` |
|---|---|---|
| Mang gì | `AsyncLocal<T>`, security/identity, culture | **"chạy code ở đâu"** |
| Có mặt ở | mọi nơi, **luôn flow** qua await/Task.Run | UI (WinForms/WPF), ASP.NET (Framework) |
| ASP.NET Core | có | **KHÔNG có** (`SynchronizationContext.Current == null`) |
| `ConfigureAwait(false)` | **không** ảnh hưởng | **bỏ qua** nó — chạy trên thread pool |

```csharp
var al = new AsyncLocal<string>();
al.Value = "request-42";
await Task.Run(() => Console.WriteLine(al.Value));  // "request-42" — ExecutionContext flow theo
```

**⚙️ Cơ chế**: trước khi chạy continuation, runtime `ExecutionContext.Restore(capturedContext)`.
`AsyncLocal` là **copy-on-write theo nhánh**: gán giá trị mới trong nhánh con **không** ảnh hưởng
nhánh cha. Đây chính là cơ sở của `IHttpContextAccessor` (FW-5), Activity/tracing, và
`ILogger` scope.

**⚖️ Chi phí**: mỗi lần capture/restore ExecutionContext có giá. `ExecutionContext.SuppressFlow()`
hoặc `ThreadPool.UnsafeQueueUserWorkItem` bỏ được (dùng trong code hạ tầng, cẩn thận).

---

## ASY-7. `ConfigureAwait(false)` — hiểu cho đúng năm 2024+

```csharp
await SomethingAsync().ConfigureAwait(false);   // "đừng quay lại context cũ"
```

- Trong **ASP.NET Core**: không có SynchronizationContext ⇒ `ConfigureAwait(false)` **gần như
  không đổi hành vi**. Vẫn nên dùng **trong thư viện** (library) vì bạn không biết ai sẽ gọi.
- Trong **WinForms/WPF/MAUI**: rất quan trọng ở tầng dưới; **không** dùng ở code đụng vào UI.
- Trong **.NET 8**: `ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing)`,
  `.ForceYielding`, `.ContinueOnCapturedContext` — chi tiết hơn cờ bool cũ.

**⚖️ Câu trả lời "đẳng cấp middle+"**: *"`ConfigureAwait(false)` không chữa deadlock —
**không block trên async** mới chữa. Nó chỉ là cách phòng vệ ở tầng thư viện."*

---

## ASY-8. Deadlock `.Result` — cơ chế chính xác

```
[UI thread / ASP.NET Framework request thread]
  var r = GetAsync().Result;          ← BLOCK thread, giữ SynchronizationContext
        │
        └─ GetAsync() chạy tới await → capture SynchronizationContext (chính là thread đang bị block)
                 │
                 └─ I/O xong → continuation cần POST về SynchronizationContext đó
                              → nhưng thread đó đang bị .Result giam
                              ⇒ DEADLOCK vĩnh viễn
```

**ASP.NET Core không có SynchronizationContext ⇒ không deadlock kiểu này.** Nhưng `.Result` vẫn
sai vì lý do khác: nó **giam một thread pool thread** trong khi chờ ⇒ dưới tải cao gây
**thread pool starvation** (ASY-9) — triệu chứng là latency tăng theo bậc thang rất khó hiểu.

**Nếu bắt buộc phải sync-over-async** (constructor, `Main` cũ, interface không async):
`Task.Run(() => AsyncMethod()).GetAwaiter().GetResult()` — vẫn tệ, chỉ là bớt tệ. Cách đúng là
async all the way (đúng như hard rule trong `CLAUDE.md` của project HW).

---

## ASY-9. ThreadPool internals — hàng đợi, work-stealing, hill climbing

```
                       ┌─────────── GLOBAL QUEUE ───────────┐
  Task.Run ─────────►  │  work item, work item, …            │
                       └──────────────┬──────────────────────┘
                                      │ lấy khi local rỗng
   Worker 1 [LOCAL QUEUE ◄──┐]  Worker 2 [LOCAL QUEUE]  Worker 3 […]
        │ push/pop LIFO      │              ▲
        └─ task con sinh ra ─┘   work-stealing: lấy từ ĐUÔI queue của worker khác
```

- **Local queue**: task tạo *từ bên trong* một task khác vào queue riêng của worker → LIFO,
  cache-friendly, không tranh chấp lock.
- **Work stealing**: worker rỗng "ăn cắp" từ đuôi queue của worker khác → cân bằng tải tự động.
- **I/O completion** đi qua đường riêng (IOCP thread trên Windows).
- **Hill climbing (thread injection)**: pool bắt đầu với `MinThreads` = số core. Nếu hàng đợi
  còn việc mà throughput không tăng, nó **thêm thread rất chậm — cỡ 1–2 thread mỗi giây** (thuật
  toán đo throughput để tìm số thread tối ưu).

**⚙️ Vì sao starvation gây "latency spike bậc thang"**: bạn block 20 thread bằng `.Result` →
pool cần 20 thread mới → mất ~10–20 **giây** để inject đủ → trong lúc đó mọi request xếp hàng.

```csharp
ThreadPool.GetAvailableThreads(out int worker, out int io);
Console.WriteLine($"ThreadCount={ThreadPool.ThreadCount}, " +
                  $"PendingWorkItems={ThreadPool.PendingWorkItemCount}, " +
                  $"CompletedItems={ThreadPool.CompletedWorkItemCount}");
```

**Dấu hiệu starvation trong production**:
`dotnet-counters monitor System.Runtime` → `threadpool-queue-length` > 0 kéo dài,
`threadpool-thread-count` tăng đều, CPU **thấp** nhưng latency **cao** (đặc trưng nhất!).

**Cách chữa**: bỏ mọi `.Result`/`.Wait()`/`Thread.Sleep` trong đường request; dùng async cho I/O;
`ThreadPool.SetMinThreads` chỉ là băng dán tạm.

---

## ASY-10. `ValueTask` & `IValueTaskSource` — tối ưu allocation

**❓ Vấn đề gốc**: method async được gọi **hàng triệu lần** mà thường trả kết quả **ngay lập tức**
(cache hit, buffer còn dữ liệu) vẫn phải cấp phát một `Task` mỗi lần.

**⚙️ `ValueTask<T>` = struct** chứa **một trong ba**: giá trị có sẵn, một `Task<T>`, hoặc một
`IValueTaskSource<T>` (object **tái sử dụng được**, có token version để phát hiện dùng sai).

```csharp
public ValueTask<User> GetAsync(int id)
    => _cache.TryGetValue(id, out var u)
        ? new ValueTask<User>(u)              // ✅ 0 allocation — đường nóng
        : new ValueTask<User>(LoadAsync(id)); // đường lạnh: bọc Task
```

**⚠️ 4 luật của `ValueTask`** (vi phạm = bug ngẫu nhiên, cực khó tìm):
1. **Chỉ await đúng một lần.**
2. Không `.Result`/`.GetAwaiter().GetResult()` khi chưa hoàn thành.
3. Không await từ nhiều thread.
4. Muốn giữ lại/await nhiều lần → `.AsTask()` trước.

```csharp
[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]  // pool cả state machine
```

**⚖️ Khi nào dùng**: chỉ khi **đã đo** và method (a) rất nóng, (b) thường hoàn thành đồng bộ.
Mặc định vẫn nên là `Task` — đơn giản và an toàn hơn.

---

## ASY-11. `IAsyncEnumerable<T>` — stream bất đồng bộ

```csharp
async IAsyncEnumerable<Order> StreamAsync([EnumeratorCancellation] CancellationToken ct = default)
{
    await using var reader = await cmd.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
        yield return Map(reader);      // trả từng phần tử, KHÔNG dồn hết vào List
}

await foreach (var o in StreamAsync().WithCancellation(ct).ConfigureAwait(false))
    Process(o);
```

**⚙️ Bên dưới**: compiler sinh state machine **kết hợp** của `async` và `yield return`, implement
`IAsyncEnumerator<T>` với `MoveNextAsync()` trả `ValueTask<bool>` và `DisposeAsync()`.

**Giải quyết vấn đề gì**: `Task<List<T>>` buộc phải có **toàn bộ** dữ liệu trong RAM trước khi
trả; `IAsyncEnumerable<T>` cho phép **backpressure tự nhiên** — consumer xử lý tới đâu, producer
đọc tới đó. Đây là cách stream 10 triệu dòng từ DB mà RAM không nổ.

**⚠️ Bẫy**: `[EnumeratorCancellation]` bắt buộc để `WithCancellation` thật sự truyền được token.

---

## ASY-12. `CancellationToken` — cơ chế hợp tác

```
CancellationTokenSource                 ← nơi RA LỆNH huỷ
  ├─ _state (chưa huỷ / đã huỷ)
  ├─ danh sách callback đã đăng ký (Register)
  └─ timer (nếu CancelAfter)
        │  token = "view chỉ đọc" của source
        ▼
CancellationToken  → ThrowIfCancellationRequested() / IsCancellationRequested / Register()
```

- **Hợp tác (cooperative)**: không ai "giết" được task của bạn. Nếu code không **kiểm tra** token
  thì huỷ **không có tác dụng** — đây là câu hỏi bẫy hay gặp.
- `CancellationTokenSource.CreateLinkedTokenSource(a, b)` — huỷ khi *một trong hai* huỷ.
  **Phải `Dispose()`**, nếu không callback vẫn treo trên source cha ⇒ leak.
- Trong ASP.NET Core: `HttpContext.RequestAborted` — client ngắt kết nối thì token huỷ.

```csharp
// ❌ token nhận vào rồi… quên truyền xuống
public async Task<List<Order>> Get(CancellationToken ct)
    => await _db.Orders.ToListAsync();          // không có ct ⇒ không huỷ được

// ✅
    => await _db.Orders.ToListAsync(ct);

// Với vòng lặp CPU-bound: tự kiểm tra
foreach (var item in items) { ct.ThrowIfCancellationRequested(); Heavy(item); }
```

---

## ASY-13. Memory model .NET — reordering, `volatile`, barrier

**❓ Vấn đề gốc**: compiler, JIT và **CPU** đều được phép **sắp xếp lại** lệnh miễn là kết quả
trong *một thread* không đổi. Với nhiều thread thì bạn thấy được sự sắp xếp lại đó.

| Kiến trúc | Mức độ "mạnh" | Bug đa luồng |
|---|---|---|
| x86/x64 | mạnh: chỉ cho phép **Store→Load** đảo | ẩn rất lâu |
| **ARM64** (Apple M-series, AWS Graviton) | yếu: đảo nhiều loại | **lộ ra ngay** |

⇒ Code đa luồng "chạy tốt nhiều năm" trên x86 có thể **hỏng khi chuyển sang ARM**. Đây là câu
chuyện rất thực tế và ăn điểm khi kể.

```csharp
// Kinh điển: cờ dừng bị JIT cache vào thanh ghi → vòng lặp không bao giờ thấy thay đổi
private bool _stop;                       // ❌
private volatile bool _stop;              // ✅ volatile read = acquire, write = release
// hoặc
Volatile.Write(ref _stop, true);
if (Volatile.Read(ref _stop)) …
```

| Công cụ | Ý nghĩa |
|---|---|
| `volatile` field | mọi đọc = acquire, mọi ghi = release (cấm đảo qua nó) |
| `Volatile.Read/Write` | như trên, nhưng chỉ tại điểm bạn muốn (rõ ràng hơn) |
| `Interlocked.*` | atomic **và** full barrier |
| `Thread.MemoryBarrier()` | full fence thủ công |
| `lock` | vào = acquire, ra = release ⇒ **đã đủ**, không cần volatile bên trong lock |

**⚖️ Lời khuyên thật lòng**: đừng tự viết lock-free. Dùng `lock`, `Interlocked`, hoặc collection
trong `System.Collections.Concurrent`. `volatile` đúng chỗ khó hơn mọi người tưởng.

---

## ASY-14. `lock` bên dưới: thin lock → sync block

**⚙️ Cơ chế (CoreCLR)**:

```
1. Chưa tranh chấp: CLR ghi thread-id vào OBJECT HEADER (thin lock)
                    → chỉ một lệnh CAS, cực rẻ, không có kernel object
2. Có tranh chấp:   SPIN một lúc (vài vòng, hy vọng chủ lock nhả sớm)
3. Vẫn chưa được:   INFLATE → cấp phát SyncBlock (bảng riêng), block thread ở kernel
                    → context switch, đắt gấp hàng nghìn lần
```

```csharp
private readonly object _gate = new();     // ✅ private, chỉ dùng để lock
lock (_gate) { … }                          // = Monitor.Enter/Exit trong try/finally

// ❌ KHÔNG BAO GIỜ lock trên:
lock (this)            { }   // code ngoài cũng lock được object của bạn
lock (typeof(Foo))     { }   // toàn app dùng chung → deadlock chéo
lock ("some string")   { }   // string interned → dùng chung toàn process (!)
```

- **`lock` không await được** (`await` trong `lock` là lỗi compile) vì Monitor có **thread
  affinity** — phải nhả trên đúng thread đã lấy. Async cần `SemaphoreSlim(1,1).WaitAsync()`.
- **.NET 9** thêm type `System.Threading.Lock` với `lock` được tối ưu hơn và `EnterScope()`.

**⚖️ Ba luật giữ cho lock không thành deadlock**:
1. Giữ lock **ngắn nhất có thể**; không I/O, không gọi code lạ (callback, event) bên trong lock.
2. Nếu phải lấy nhiều lock, **luôn cùng thứ tự** ở mọi nơi.
3. Không block trên task/lock khác khi đang giữ lock.

---

## ASY-15. Chọn primitive đồng bộ nào?

| Primitive | User/Kernel | Async? | Dùng khi |
|---|---|---|---|
| `lock`/`Monitor` | user → kernel khi tranh chấp | ❌ | mặc định cho đoạn code ngắn |
| `Interlocked` | user, lock-free | — | tăng/giảm/CAS trên một biến |
| `SpinLock` | user (spin) | ❌ | critical section **cực ngắn**, tranh chấp thấp |
| `SemaphoreSlim(1,1)` | lai | ✅ `WaitAsync` | "lock" trong code async, hoặc giới hạn N đồng thời |
| `ReaderWriterLockSlim` | lai | ❌ | đọc nhiều ghi ít (đo trước — thường thua `lock` + copy) |
| `ManualResetEventSlim` | lai | ❌ | báo hiệu một-lần/nhiều-lần |
| `Barrier` | lai | ❌ | nhiều thread đồng bộ theo "pha" |
| `Channel<T>` | — | ✅ | producer/consumer + backpressure (ASY-19) |

```csharp
// Giới hạn số việc chạy song song — pattern hay dùng nhất
var gate = new SemaphoreSlim(10);
var tasks = urls.Select(async url =>
{
    await gate.WaitAsync(ct);
    try { return await http.GetStringAsync(url, ct); }
    finally { gate.Release(); }        // ⚠️ finally, nếu không sẽ cạn semaphore
});
var all = await Task.WhenAll(tasks);
```

---

## ASY-16. `Interlocked` & CAS — nền của mọi thứ lock-free

```csharp
Interlocked.Increment(ref _counter);                 // atomic ++
Interlocked.Add(ref _total, 5);
Interlocked.Exchange(ref _current, newValue);        // set và trả về giá trị cũ
Interlocked.CompareExchange(ref _x, newV, expected); // CAS: nếu _x == expected thì gán

// Pattern CAS-loop: cập nhật bất kỳ theo kiểu lock-free
static void Max(ref int target, int value)
{
    int snapshot;
    do
    {
        snapshot = Volatile.Read(ref target);
        if (value <= snapshot) return;
    }
    while (Interlocked.CompareExchange(ref target, value, snapshot) != snapshot);
}
```

- **CAS** (compare-and-swap) là một lệnh CPU (`lock cmpxchg`) — atomic + full barrier.
- **ABA problem**: giá trị đổi A→B→A giữa chừng, CAS tưởng "không ai đụng". Với reference thì
  nguy hiểm; giải bằng version counter.
- `ConcurrentDictionary` bên dưới = mảng bucket + **striped locks** (nhiều lock nhỏ) cho ghi,
  đọc **lock-free** (volatile read). ⇒ `GetOrAdd(key, factory)` có thể gọi `factory` **nhiều lần**
  (chỉ một kết quả được giữ) — đừng đặt side-effect trong factory.
- `ConcurrentQueue<T>` = danh sách các **segment** mảng, thao tác bằng CAS.

---

## ASY-17. False sharing — bug hiệu năng "vô hình"

**❓ Vấn đề gốc**: CPU đồng bộ bộ nhớ theo **cache line 64 byte**. Hai biến khác nhau nằm chung
một cache line: hai core ghi hai biến đó sẽ **liên tục giành quyền sở hữu cache line** ⇒ chậm
thảm hại dù logic hoàn toàn độc lập.

```csharp
// ❌ 8 counter nằm sát nhau → cùng 1–2 cache line
long[] counters = new long[8];
Parallel.For(0, 8, i => { for (int k = 0; k < 10_000_000; k++) counters[i]++; });

// ✅ padding: mỗi counter một cache line riêng
[StructLayout(LayoutKind.Explicit, Size = 128)]
struct PaddedLong { [FieldOffset(0)] public long Value; }
```

**⚖️ Nhận biết**: nhiều core mà scaling gần như bằng 0 (8 thread không nhanh hơn 1 thread) trong
đoạn code không hề có lock. Đây là câu hỏi "cực deep" mà interviewer senior rất thích.

---

## ASY-18. Exception trong async — luật đầy đủ

```csharp
// 1) Trong async Task → lưu vào Task, chỉ ném khi AWAIT
Task t = ThrowAsync();          // KHÔNG ném ở đây
await t;                        // ném ở đây

// 2) .Result/.Wait() → bọc trong AggregateException
try { t.Wait(); } catch (AggregateException ae) { … }   // phải InnerException

// 3) Task.WhenAll: await chỉ ném exception ĐẦU TIÊN
var all = Task.WhenAll(t1, t2, t3);
try { await all; }
catch { foreach (var e in all.Exception!.InnerExceptions) Log(e); }  // ✅ lấy đủ

// 4) async void: KHÔNG có Task để chứa exception
//    → ném thẳng lên SynchronizationContext ⇒ CRASH process, try/catch bên ngoài vô dụng
async void Bad() => throw new Exception();      // ❌ chỉ dùng cho event handler
try { Bad(); } catch { /* KHÔNG bao giờ chạy */ }

// 5) Task bị fault mà không ai await → TaskScheduler.UnobservedTaskException (chỉ để log)
```

**⚖️ Quy tắc**: `async void` **chỉ** cho event handler UI, và bên trong phải `try/catch` toàn bộ.
Mọi chỗ khác dùng `async Task`. `BackgroundService.ExecuteAsync` ném ⇒ từ .NET 6 mặc định làm
**sập host** (`BackgroundServiceExceptionBehavior`) — nên tự bọc try/catch.

---

## ASY-19. `Channel<T>` — producer/consumer đúng cách

```csharp
var channel = Channel.CreateBounded<Order>(new BoundedChannelOptions(1000)
{
    FullMode = BoundedChannelFullMode.Wait   // ✅ BACKPRESSURE: producer chờ khi đầy
});

// Producer
await channel.Writer.WriteAsync(order, ct);
channel.Writer.Complete();

// Consumer (nhiều consumer song song được)
await foreach (var order in channel.Reader.ReadAllAsync(ct))
    await ProcessAsync(order, ct);
```

- **Bounded** (có backpressure) vs **Unbounded** (rủi ro OOM — producer nhanh hơn consumer).
- Thay thế `BlockingCollection<T>` (vốn block thread) bằng phiên bản **async, không giam thread**.
- Đây là nền của pipeline xử lý message, batch writer, outbox processor.

**⚖️ Liên hệ project HW**: outbox processor đọc row → publish qua `IMessageBus`; nếu dùng Channel
để tách "đọc DB" và "publish" thì phải là **bounded**, nếu không một sự cố broker sẽ làm phình RAM.

---

## ASY-20. `Parallel` / PLINQ vs `async` — đừng lẫn hai bài toán

| | I/O-bound | CPU-bound |
|---|---|---|
| Công cụ | `async/await`, `Task.WhenAll`, `Parallel.ForEachAsync` | `Parallel.For/ForEach`, PLINQ |
| Mục tiêu | **không giam thread** | **dùng hết core** |
| Số "worker" hợp lý | rất nhiều (hàng nghìn) | ≈ số core |

```csharp
// I/O: đừng dùng Parallel.ForEach (nó block thread pool thread)
await Parallel.ForEachAsync(urls, new ParallelOptions { MaxDegreeOfParallelism = 10, CancellationToken = ct },
    async (url, token) => await http.GetStringAsync(url, token));   // ✅ .NET 6+

// CPU: PLINQ
var primes = numbers.AsParallel().WithDegreeOfParallelism(4).Where(IsPrime).ToList();
```

**⚠️ Trên web server**: `Parallel`/PLINQ trong request handler thường **có hại** — server đã xử lý
song song ở mức request rồi; chiếm hết core cho một request làm các request khác đói.

---

## ASY-21. Timer, `Task.Delay`, `PeriodicTimer`

```csharp
// ❌ vòng lặp lệch nhịp (thời gian xử lý cộng dồn) + khó test
while (!ct.IsCancellationRequested) { await DoWork(); await Task.Delay(1000, ct); }

// ✅ PeriodicTimer (.NET 6+): nhịp ổn định, huỷ sạch, không alloc timer mỗi vòng
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
while (await timer.WaitForNextTickAsync(ct)) await DoWork();
```

- `Task.Delay` **không** giam thread: nó đăng ký vào **timer queue** của runtime; khi hết hạn,
  một thread pool thread chạy continuation.
- Độ phân giải timer ~15ms trên Windows ⇒ `Task.Delay(1)` thực tế có thể ~15ms.
- `System.Threading.Timer`: callback chạy trên thread pool và **có thể chồng lấn** nếu công việc
  lâu hơn chu kỳ — nguồn bug kinh điển của background job.

---

## ASY-22. Chẩn đoán async/threading trong production

```bash
dotnet-counters monitor -p <pid> System.Runtime
#   threadpool-thread-count       ↑ đều = đang inject vì bị block
#   threadpool-queue-length       > 0 kéo dài = STARVATION
#   threadpool-completed-items-count / s = throughput thật
#   monitor-lock-contention-count  ↑ = tranh chấp lock

dotnet-stack report -p <pid>          # stack của mọi thread — thấy ngay ai đang .Result
dotnet-dump collect -p <pid>
#   trong analyze:  dumpasync         → mọi state machine async đang treo + đang chờ ai
#                   syncblk           → object nào đang bị lock và thread nào giữ
#                   clrstack -all
```

**Bộ ba triệu chứng ↔ nguyên nhân**:
- CPU thấp + latency cao + queue length cao ⇒ **thread pool starvation** (sync-over-async).
- CPU cao + throughput thấp ⇒ **lock contention** hoặc **false sharing**.
- RAM tăng + nhiều async state machine trong dump ⇒ **task không bao giờ hoàn thành** (thiếu timeout).

---

## ✅ Checklist tự kiểm tra — Phần 13

- [ ] Giải thích được "await không tạo thread" và ai chạy phần code sau await.
- [ ] Vẽ được state machine + nói rõ khi nào nó bị box lên heap (và khi nào 0 allocation).
- [ ] Phân biệt `ExecutionContext` (flow) vs `SynchronizationContext` (chạy ở đâu).
- [ ] Mô tả cơ chế deadlock `.Result`, và vì sao ASP.NET Core không deadlock nhưng vẫn cấm.
- [ ] Vẽ ThreadPool: global queue, local queue, work stealing, hill-climbing injection.
- [ ] Kể 4 luật của `ValueTask` và khi nào nên dùng.
- [ ] Giải thích thin lock → sync block, và 3 object không bao giờ được lock lên.
- [ ] Nói được memory reordering + vì sao bug đa luồng "lộ" trên ARM64.
- [ ] Nhận diện starvation qua `dotnet-counters` (CPU thấp, queue dài).

➡️ Tiếp: [Phần 14 — Framework Internals](interview.NET.14-Framework-Internals.md)
