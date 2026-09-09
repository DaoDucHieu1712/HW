# 📚 Bộ Ôn Luyện Phỏng Vấn .NET — Level Middle

> Bộ câu hỏi phỏng vấn .NET Developer trình độ Middle, chia thành nhiều file theo chủ đề.
> Mỗi câu có: **định nghĩa → điểm nhấn khi trả lời → code minh hoạ (❌ sai / ✅ đúng) → trade-off**.

## 🗂️ Mục lục các file

| # | Chủ đề | File | Số câu |
|---|--------|------|--------|
| 1 | **C# Ngôn ngữ & CLR** — value/reference type, boxing, GC, record, Span, generics | [interview.NET.01-CSharp-CLR.md](interview.NET.01-CSharp-CLR.md) | 25 + 15 đào sâu |
| 2 | **Async / Await & Multithreading** — state machine, deadlock, lock, Interlocked | [interview.NET.02-Async-Threading.md](interview.NET.02-Async-Threading.md) | 15 + 15 đào sâu |
| 3 | **Collections & LINQ** — IEnumerable vs IQueryable, deferred execution, N+1 | [interview.NET.03-Collections-LINQ.md](interview.NET.03-Collections-LINQ.md) | 12 |
| 4 | **OOP & Design Patterns (cơ bản)** — SOLID, DI, Repository, UoW | [interview.NET.04-OOP-Basics.md](interview.NET.04-OOP-Basics.md) | 13 |
| 5 | **ASP.NET Core** — middleware, filter, JWT, IOptions, HttpClientFactory | [interview.NET.05-AspNetCore.md](interview.NET.05-AspNetCore.md) | 15 |
| 6 | **Design Patterns (GoF & .NET)** — Creational/Structural/Behavioral | [interview.NET.06-DesignPatterns.md](interview.NET.06-DesignPatterns.md) | 20 |
| 7 | **Cấu trúc dữ liệu & Giải thuật** — Big-O, tree, graph, sort, DP | [interview.NET.07-DSA.md](interview.NET.07-DSA.md) | 20 |
| 8 | **Entity Framework Core & Database** — change tracking, loading, migration | [interview.NET.08-EFCore.md](interview.NET.08-EFCore.md) | 10 |
| 9 | **Kiến trúc, Testing & DevOps** — Clean Arch, CQRS, Outbox, test, CI/CD | [interview.NET.09-Architecture-Testing.md](interview.NET.09-Architecture-Testing.md) | 10 |
| 10 | **SQL** — JOIN, index, window function, transaction, injection | [interview.NET.10-SQL.md](interview.NET.10-SQL.md) | 25 |

**Tổng: ~180 câu hỏi kèm code minh hoạ.**

---

## 🔬 Track "Deep Understand" — Internals (file 11–14)

> Track 1–10 trả lời **"cái gì / khi nào dùng"** (đủ cho vòng Middle).
> Track 11–14 trả lời **"tồn tại để giải quyết vấn đề gì → bên dưới chạy ra sao → hệ quả đo được"**
> — đây là phần phân biệt Middle với Middle+/Senior, và là phần interviewer "khoan" sâu nhất.

| # | Chủ đề | File | Số câu |
|---|--------|------|--------|
| 11 | **Runtime Internals** — IL & metadata, MethodTable, dispatch, JIT/Tiering/PGO, AOT, generic sharing, boxing ở mức IL, `ref`/byref safety, reflection vs source generator | [interview.NET.11-Runtime-Internals.md](interview.NET.11-Runtime-Internals.md) | 22 |
| 12 | **Memory & GC Internals** — bố cục bộ nhớ, bump-pointer alloc, gen 0/1/2, các pha GC, roots, card table & write barrier, LOH/POH, finalization, leak & chẩn đoán, `Span`/pooling | [interview.NET.12-Memory-GC-Internals.md](interview.NET.12-Memory-GC-Internals.md) | 20 |
| 13 | **Async & Threading Internals** — IOCP/epoll, state machine, awaitable pattern, `ExecutionContext` vs `SynchronizationContext`, ThreadPool & starvation, `ValueTask`, memory model, lock internals, false sharing | [interview.NET.13-Async-Threading-Internals.md](interview.NET.13-Async-Threading-Internals.md) | 22 |
| 14 | **Framework Internals** — Host, DI engine, middleware fold, Kestrel & Pipelines, routing, MVC filter, Options, HttpClientFactory, EF Core query/change tracker/SaveChanges, MediatR chain, STJ, end-to-end request | [interview.NET.14-Framework-Internals.md](interview.NET.14-Framework-Internals.md) | 21 |

**Cộng thêm: ~85 câu internals.** Mỗi câu theo khung
**❓ Vấn đề gốc → ⚙️ Cơ chế bên dưới → 💻 Code/đo đạc → ⚖️ Hệ quả thực chiến**.


---

## ⚡ Track "Scale & Load" — hệ thống lớn & chịu tải (file 15–19)

> Track 11–14 hỏi **"bên trong 1 process chạy ra sao"**.
> Track 15–19 hỏi **"khi có 10.000 req/s và 8 instance thì hỏng ở đâu, vì sao, sửa bằng cơ chế nào"**
> — đây là phần quyết định vòng Senior / System Design.

| # | Chủ đề | File | Số câu |
|---|--------|------|--------|
| 15 | **Concurrency & Scaling** — Little's Law, USL, lost update, pessimistic/optimistic lock, distributed lock & fencing token, idempotency key, rate limiting, backpressure & load shedding, cạn pool, hot key, Polly resilience, leader election, graceful shutdown, đo tải đúng cách | [interview.NET.15-Concurrency-Scaling.md](interview.NET.15-Concurrency-Scaling.md) | 22 |
| 16 | **Transaction & Consistency** — ACID bên dưới (undo/redo/WAL), isolation & write skew, MVCC, gap lock & deadlock, ranh giới transaction, dual write, Outbox/CDC, 2PC vs Saga, exactly-once & Inbox, event sourcing, PACELC, read replica, xử lý tiền | [interview.NET.16-Transaction-Consistency.md](interview.NET.16-Transaction-Consistency.md) | 20 |
| 17 | **Caching** — toán hit ratio, cache-aside/write-through, thứ tự invalidation, stampede & singleflight, penetration/breakdown/avalanche, `IMemoryCache`, `HybridCache`, Redis internals & cấu trúc dữ liệu, HTTP caching/CDN, precomputation | [interview.NET.17-Caching-Performance.md](interview.NET.17-Caching-Performance.md) | 16 |
| 18 | **Messaging** — Queue vs Log, RabbitMQ (exchange/prefetch/ack/DLX), Kafka (partition/offset/ISR/rebalance), delivery semantics, thứ tự, retry & DLQ, consumer lag, schema evolution, saga event-sourced, observability, chọn broker | [interview.NET.18-Messaging-Scaling.md](interview.NET.18-Messaging-Scaling.md) | 15 |
| 19 | **Case study system design** — khung trả lời 6 bước, số liệu ước lượng, flash sale, đặt vé, thanh toán, news feed, sync Elasticsearch, đếm thời gian thực, áp dụng vào project HW, bảng tra nhanh | [interview.NET.19-System-Design-Cases.md](interview.NET.19-System-Design-Cases.md) | 7 case |

**Cộng thêm: ~80 câu + 7 case study.** Cùng khung
**❓ Vấn đề gốc → ⚙️ Cơ chế bên dưới → 💻 Code/đo đạc → ⚖️ Hệ quả thực chiến**.

### 🧭 Lộ trình học đề xuất
```
Tuần 1  file 01 + 02        →  nền tảng ngôn ngữ & async (biết "cái gì")
Tuần 2  file 11 + 12        →  runtime + memory (hiểu "vì sao & bên dưới")
Tuần 3  file 13             →  async/threading internals (phần hay bị hỏi khoan nhất)
Tuần 4  file 05 + 08 + 14   →  framework: ASP.NET Core + EF Core + internals
Tuần 5  file 03/04/06/07/09/10 →  quét phần còn lại + FW-21 (kể end-to-end request)
Tuần 6  file 15 + 16        →  concurrency phân tán + transaction/consistency
Tuần 7  file 17 + 18        →  caching + messaging
Tuần 8  file 19             →  luyện system design bằng 7 case (nói to, vẽ ra giấy)
```

### ▶️ Demo chạy được
Mọi câu trong track internals đều có demo in ra số liệu thật (allocation, gen, thread id, thời gian):
```bash
cd HW.CS
dotnet run                 # menu tương tác: chọn topic → chọn câu
dotnet run -- 11 all       # chạy toàn bộ topic 11
dotnet run -- 12 5         # chạy câu số 5 của topic 12
```
> Track 15–19 (scale & load) là track **thiết kế hệ thống** — code trong đó là mẫu để đọc
> và thảo luận khi phỏng vấn, không có demo runner tương ứng.

---

## 🤖 Track "AI Engineer" — LLM, Agent, MCP, Harness (file AI-01 → AI-07)

> Track riêng, độc lập với 19 file .NET ở trên. Cùng khung
> **❓ Vấn đề gốc → ⚙️ Cơ chế bên dưới → 💻 Code/đo đạc → ⚖️ Hệ quả thực chiến**.

| # | Chủ đề | File |
|---|--------|------|
| AI-01 | **Nền tảng LLM** — token/BPE, KV cache, prefill vs decode, sampling, hallucination, context rot, thinking & effort, embedding, RAG, structured output, prompt caching, cost, eval, prompt injection | [interview.AI.01-LLM-Foundations.md](interview.AI.01-LLM-Foundations.md) |
| AI-02 | **Prompt & Context Engineering** — tool description, prompt cruft, compaction vs context editing vs memory, progressive disclosure, versioning, hill-climbing | [interview.AI.02-Prompt-Context.md](interview.AI.02-Prompt-Context.md) |
| AI-03 | **Agentic Loop & Loop Engineer** — workflow vs agent, manual loop, verify, điều kiện dừng, task budget, chế độ hỏng, observability, Loop Agentic | [interview.AI.03-Agentic-Loop.md](interview.AI.03-Agentic-Loop.md) |
| AI-04 | **MCP chuyên sâu** — JSON-RPC & lifecycle, transport, primitive, scope, chi phí context, auth, 6 rủi ro bảo mật, thiết kế server | [interview.AI.04-MCP.md](interview.AI.04-MCP.md) |
| AI-05 | **Harness** — hooks, settings & precedence, permissions, slash commands, skills, subagents, plugins | [interview.AI.05-Harness-Config.md](interview.AI.05-Harness-Config.md) |
| AI-06 | **Multi-agent & Agent Team** — topology, kinh tế học token, handoff, task list, bảo mật giữa agent, khi nào KHÔNG dùng | [interview.AI.06-MultiAgent-Teams.md](interview.AI.06-MultiAgent-Teams.md) |
| AI-07 | **Kiến trúc triển khai** — LLM gateway, Clean Architecture, background job, resilience, cost governance, tracing, đa tenant, rollout, áp dụng vào HW | [interview.AI.07-Impl-Architecture.md](interview.AI.07-Impl-Architecture.md) |

**➡️ Mục lục đầy đủ của track AI: [interview.AI.md](interview.AI.md)** (~116 câu + 3 case study)

---

## 🎯 Mẹo trả lời phỏng vấn Middle
- **Trả lời có cấu trúc**: định nghĩa → khi nào dùng → ví dụ thực tế → trade-off.
- **Nêu trade-off**: middle được đánh giá cao khi biết "tại sao" và "khi nào KHÔNG dùng", không chỉ "cái gì".
- **Liên hệ dự án thực tế**: gắn câu trả lời với kinh nghiệm cá nhân (đã tối ưu N+1, xử lý deadlock async…).
- **Thành thật khi không biết**: nói hướng tiếp cận thay vì bịa.
- **Code sạch khi lên bảng**: đặt tên rõ, xử lý edge case (null, empty), nói độ phức tạp Big-O.

---

# 📕 Bảng thuật ngữ (Glossary)

> Tổng hợp & giải thích ngắn gọn toàn bộ thuật ngữ xuất hiện trong 10 file. Nhóm theo chủ đề để dễ tra cứu.

## 1️⃣ C# Ngôn ngữ & CLR

| Thuật ngữ | Giải thích ngắn gọn |
|-----------|---------------------|
| Value type | Kiểu lưu trực tiếp dữ liệu (struct, int, bool, DateTime); gán = copy giá trị. |
| Reference type | Kiểu lưu tham chiếu trỏ đến heap (class, array, string); gán = copy địa chỉ. |
| Stack | Vùng nhớ LIFO cho biến local, con trỏ; cấp phát/thu hồi nhanh. |
| Heap | Vùng nhớ cấp phát động cho object reference type; do GC quản lý. |
| Boxing | Chuyển value type → `object` (cấp phát heap + copy). |
| Unboxing | Chuyển `object` → value type (ép kiểu tường minh). |
| `struct` | Value type, không kế thừa, thường immutable, nên nhỏ. |
| `class` | Reference type, hỗ trợ kế thừa, có thể null. |
| Immutable | Không thể thay đổi sau khi tạo (như `string`, `record`). |
| String interning | CLR gộp string literal giống nhau về 1 object trong intern pool. |
| `StringBuilder` | Buffer sửa chuỗi tại chỗ, O(n); tránh tạo object rác khi nối nhiều lần. |
| `const` | Hằng số compile-time, inline vào nơi dùng, ngầm `static`. |
| `readonly` | Gán 1 lần ở khai báo/constructor (runtime), mỗi instance có thể khác. |
| `ref` | Truyền tham chiếu 2 chiều; biến phải khởi tạo trước. |
| `out` | Truyền ra; không cần khởi tạo trước, bắt buộc gán trong method. |
| `in` | Truyền tham chiếu chỉ đọc; tối ưu struct lớn (tránh copy). |
| `var` | Suy luận kiểu compile-time; vẫn static typing. |
| `dynamic` | Bỏ kiểm tra kiểu lúc biên dịch, resolve runtime (qua DLR). |
| DLR | Dynamic Language Runtime — hạ tầng cho `dynamic`. |
| `IEnumerable<T>` | Interface duyệt tuần tự (foreach); deferred execution. |
| `ICollection<T>` | Thêm Count, Add, Remove, Contains. |
| `IList<T>` | Thêm truy cập theo index, Insert, RemoveAt. |
| Garbage Collector (GC) | Cơ chế tự thu hồi object không còn tham chiếu trên heap. |
| Gen 0 / 1 / 2 | Các thế hệ GC: Gen 0 object mới (thu nhanh), Gen 2 sống lâu (thu tốn kém). |
| LOH | Large Object Heap — object ≥ 85KB, thuộc Gen 2, mặc định không nén. |
| Workstation / Server GC | Chế độ GC: 1 heap (độ trễ thấp) vs nhiều heap+thread (throughput). |
| Concurrent/Background GC | Thu Gen 2 song song với app thread để giảm pause. |
| `ArrayPool<T>` | Pool tái sử dụng mảng lớn, tránh cấp phát LOH liên tục. |
| Managed / Unmanaged resource | Tài nguyên do CLR quản lý vs OS (file, socket) cần Dispose. |
| `IDisposable` / `Dispose()` | Interface giải phóng unmanaged resource chủ động. |
| `using` | Đảm bảo `Dispose()` được gọi kể cả khi exception. |
| Finalizer (`~Class`) | Method GC gọi để dọn unmanaged; thời điểm không xác định. |
| Dispose Pattern | Mẫu `Dispose(bool)` + `SuppressFinalize` chuẩn. |
| `GC.SuppressFinalize` | Bỏ object khỏi finalization queue (tránh chạy finalizer 2 lần). |
| `Equals()` / `==` | So sánh bằng giá trị/tham chiếu; `==` overload được. |
| `GetHashCode()` | Sinh hash cho collection hash-based; phải nhất quán với Equals. |
| `IEquatable<T>` | So sánh type-safe, tránh boxing trong collection. |
| Extension method | Method static với `this` ở tham số đầu, "thêm" method vào type có sẵn. |
| Delegate | Con trỏ hàm type-safe; truyền method như tham số. |
| `Func` / `Action` / `Predicate` | Delegate dựng sẵn: có trả về / void / trả bool. |
| Multicast delegate | Delegate gộp nhiều method (`+=`). |
| Event | Lớp bao quanh delegate: ngoài chỉ `+=`/`-=`, không invoke/gán. |
| Lambda | Hàm ẩn danh ngắn gọn (`x => x*2`). |
| Closure | Lambda "bắt" (capture) biến từ scope ngoài (capture biến, không phải giá trị). |
| Generic constraint | Ràng buộc `where T : ...` cho generic. |
| Covariance (`out T`) | Cho gán Derived→Base ở output (IEnumerable). |
| Contravariance (`in T`) | Cho gán Base→Derived ở input (Action). |
| Nullable reference types | `string?` vs `string`; compiler cảnh báo null (C# 8). |
| null-forgiving (`!`) | Khẳng định "không null", tắt cảnh báo. |
| `??` / `?.` / `??=` | null-coalescing / null-conditional / null-coalescing assignment. |
| `record` | Reference type có value-based equality + `with` expression (C# 9). |
| `with` expression | Tạo bản sao có sửa đổi (non-destructive mutation). |
| Pattern matching | `is`, switch expression, property/relational/list pattern. |
| Stack trace | Dấu vết chuỗi lời gọi khi exception; `throw;` giữ, `throw ex;` reset. |
| `Span<T>` | View zero-allocation trên vùng nhớ liên tục; là ref struct. |
| `Memory<T>` | Như Span nhưng dùng được trong async/heap. |
| `ref struct` | Struct bắt buộc chỉ sống trên stack (không box/async/field class). |
| `is` / `as` | Kiểm tra kiểu (+ gán) / ép kiểu trả null nếu thất bại. |
| Static constructor | Chạy 1 lần, thread-safe, trước lần đầu dùng type. |
| ValueTuple `(a,b)` | Tuple value type, đặt tên field, deconstruction. |
| `Nullable<T>` (`int?`) | Struct `HasValue`+`Value`; box `null` khi HasValue=false. |
| `checked`/`unchecked` | Bật/tắt kiểm tra tràn số nguyên (overflow). |
| Integer overflow | Tràn số; mặc định wrap-around âm thầm. |
| JIT | Just-In-Time — biên dịch IL→machine code lúc runtime. |
| AOT / Native AOT | Ahead-Of-Time — biên dịch native lúc build; khởi động nhanh, ít reflection. |
| ReadyToRun | Dạng lai pre-JIT một phần. |

## 2️⃣ Async / Await & Multithreading

| Thuật ngữ | Giải thích ngắn gọn |
|-----------|---------------------|
| `async` / `await` | Đánh dấu + chờ tác vụ async; compiler sinh state machine, không tạo thread mới. |
| State machine | Cấu trúc compiler sinh ra để "tạm dừng/tiếp tục" method async. |
| `Task` | Abstraction đơn vị công việc async trên ThreadPool. |
| `Thread` | Đơn vị OS-level, tạo/huỷ đắt (~1MB stack). |
| ThreadPool | Tập thread tái sử dụng do runtime quản lý. |
| `Task.Run` | Đẩy công việc CPU-bound lên thread pool. |
| CPU-bound / I/O-bound | Nghẽn do tính toán / do chờ nhập-xuất (DB, HTTP, file). |
| Deadlock | 2 bên chờ tài nguyên của nhau vĩnh viễn (vd `.Result` + context). |
| `.Result` / `.Wait()` | Chặn thread chờ Task xong (sync-over-async, dễ deadlock). |
| SynchronizationContext | "Post continuation về đúng chỗ" (UI thread); ASP.NET Core không có. |
| `ConfigureAwait(false)` | Continuation không cần quay lại context gốc → tránh deadlock. |
| `CancellationToken` | Tín hiệu huỷ cooperative giữa các tác vụ. |
| `CancellationTokenSource` | Nguồn phát tín hiệu huỷ (`.Cancel()`). |
| `Task.WhenAll` | Chờ tất cả task hoàn thành (song song). |
| `Task.WhenAny` | Hoàn thành khi task đầu tiên xong (timeout, race). |
| `ValueTask` | Struct tránh allocation khi kết quả thường có sẵn đồng bộ. |
| Race condition | Nhiều thread truy cập/sửa dữ liệu chung không đồng bộ → sai. |
| `lock` / `Monitor` | Đảm bảo chỉ 1 thread vào critical section. |
| `Interlocked` | Thao tác atomic (Increment, Exchange, CompareExchange) không cần lock. |
| `SemaphoreSlim` | Giới hạn N thread đồng thời; có `WaitAsync()` (dùng được async). |
| Thread starvation | Hết thread rảnh do sync-over-async chặn thread pool. |
| `IAsyncEnumerable<T>` | Stream dữ liệu async từng phần tử (`await foreach`). |
| `async void` | Anti-pattern (trừ event handler); exception crash process. |
| `TaskCompletionSource` | Tự điều khiển khi nào Task complete; cầu nối callback→async. |
| `Parallel.ForEach` / PLINQ | Data parallelism cho CPU-bound (chia việc lên nhiều core). |
| `volatile` | Đọc/ghi biến không bị reorder, không cache thanh ghi. |
| Memory barrier | Rào chắn ngăn CPU/compiler sắp xếp lại thứ tự lệnh. |
| Double-checked locking | Kiểm tra null 2 lần (ngoài + trong lock) để lazy init an toàn. |
| `CompareExchange` | So sánh-và-đổi atomic; nền tảng lock-free. |
| `ThreadLocal<T>` | Mỗi thread một bản dữ liệu riêng. |
| `AsyncLocal<T>` | Dữ liệu "chảy" theo logical async context (xuyên await). |
| `AggregateException` | Gom nhiều exception (từ `Task.WhenAll`). |
| I/O Completion Port | Cơ chế OS cho async I/O không chặn thread. |
| Continuation | Phần code chạy tiếp sau `await`. |
| Cooperative cancellation | Huỷ chỉ có tác dụng khi code chủ động kiểm tra token. |

## 3️⃣ Collections & LINQ

| Thuật ngữ | Giải thích ngắn gọn |
|-----------|---------------------|
| `List<T>` | Danh sách có thứ tự, index O(1), tìm kiếm O(n); mảng động bên trong. |
| `Dictionary<K,V>` | Tra cứu theo key O(1) trung bình (hash table). |
| `HashSet<T>` | Tập hợp phần tử duy nhất, check tồn tại O(1). |
| Deferred execution | Query LINQ chỉ chạy khi enumerate (foreach/ToList/Count). |
| `IQueryable<T>` | Build expression tree, dịch sang SQL (filter tại DB). |
| Expression tree | Cây biểu thức mô tả code; EF dịch sang SQL. |
| LINQ-to-Objects | LINQ thực thi in-memory trên IEnumerable. |
| `First` / `FirstOrDefault` | Phần tử đầu; throw nếu rỗng / trả default. |
| `Single` / `SingleOrDefault` | Đúng 1 phần tử; throw nếu ≠1 / trả default nếu 0-1. |
| `Select` / `SelectMany` | Projection 1-1 / flatten collection lồng thành phẳng. |
| `GroupBy` / `IGrouping` | Gom nhóm; mỗi group có Key + các phần tử. |
| `ToList()` | Thực thi query ngay, materialize về memory. |
| `AsEnumerable()` | Chuyển sang LINQ-to-Objects (thao tác sau chạy in-memory). |
| `yield return` | Tạo iterator lazy, trả từng phần tử, tạm dừng state. |
| Iterator | Đối tượng duyệt tuần tự sinh bởi `yield`. |
| N+1 problem | 1 query cha + N query con (do lazy load mỗi phần tử). |
| `Include()` | Eager loading — load related data cùng query. |
| Projection | Chỉ chọn field cần (`Select` vào DTO) để giảm dữ liệu. |
| `Any()` | Kiểm tra tồn tại, dừng sớm; EF dịch thành EXISTS (nhanh hơn Count). |

## 4️⃣ OOP & Design Patterns (cơ bản)

| Thuật ngữ | Giải thích ngắn gọn |
|-----------|---------------------|
| Encapsulation | Đóng gói — ẩn state, expose qua public API. |
| Inheritance | Kế thừa — tái sử dụng, mở rộng. |
| Polymorphism | Đa hình — cùng interface, hành vi khác nhau. |
| Abstraction | Trừu tượng — mô hình hoá bản chất, ẩn phức tạp. |
| `abstract class` | Có field/constructor/implementation dùng chung; kế thừa 1. |
| `interface` | Hợp đồng "can-do"; implement nhiều; C# 8+ có default method. |
| `virtual`/`override`/`new`/`sealed` | Cho override / ghi đè / che (không đa hình) / chặn override. |
| Overloading | Nạp chồng — cùng tên khác signature, quyết định compile-time. |
| Overriding | Ghi đè — lớp con định nghĩa lại virtual, quyết định runtime. |
| SOLID | 5 nguyên lý thiết kế hướng đối tượng. |
| SRP / OCP / LSP / ISP / DIP | Single Responsibility / Open-Closed / Liskov / Interface Segregation / Dependency Inversion. |
| Dependency Injection (DI) | Cung cấp dependency từ ngoài thay vì tự `new`. |
| DI container | `IServiceCollection` quản lý đăng ký & resolve service. |
| Transient / Scoped / Singleton | Lifetime: mới mỗi lần / mỗi request / toàn app. |
| Captive dependency | Bug inject Scoped/Transient vào Singleton (bị "kẹt" sống mãi). |
| Repository | Trừu tượng hoá tầng truy cập dữ liệu. |
| Unit of Work | Nhóm thao tác thành 1 transaction (`SaveChanges`). |

## 5️⃣ ASP.NET Core

| Thuật ngữ | Giải thích ngắn gọn |
|-----------|---------------------|
| Middleware | Delegate xử lý request/response theo chuỗi (pipeline). |
| Pipeline | Chuỗi middleware; thứ tự đăng ký quan trọng. |
| `next()` / Short-circuit | Gọi middleware sau / dừng sớm không gọi next. |
| `IActionResult` | Trả nhiều loại result (Ok, NotFound…). |
| `ActionResult<T>` | Vừa trả result vừa mô tả kiểu data (OpenAPI). |
| Model binding | Map dữ liệu request (route/query/body…) vào tham số action. |
| Validation | Kiểm tra dữ liệu qua Data Annotations / FluentValidation. |
| Data Annotations | Attribute validation (`[Required]`, `[Range]`). |
| `ModelState` | Trạng thái kết quả validation. |
| `[ApiController]` | Tự trả 400 khi ModelState invalid + binding thông minh. |
| `[FromBody]`/`[FromQuery]`/`[FromRoute]` | Nguồn bind: body JSON / query string / route. |
| `[FromHeader]`/`[FromServices]` | Bind từ header / DI container. |
| Filter | Cross-cutting concern (Authorization/Resource/Action/Exception/Result). |
| `IExceptionHandler` | Xử lý exception tập trung (.NET 8). |
| `ProblemDetails` | Định dạng lỗi chuẩn RFC 7807. |
| CORS | Cross-Origin Resource Sharing — cho/chặn request khác origin. |
| Authentication | Xác thực "bạn là ai" (JWT, cookie, OAuth). |
| Authorization | Kiểm tra "bạn được làm gì" (role, policy, claim). |
| JWT | JSON Web Token: Header.Payload.Signature; stateless. |
| Claims | Thông tin (identity/quyền) chứa trong token. |
| Bearer token | Cách gửi token qua header `Authorization: Bearer`. |
| `IConfiguration` | Đọc cấu hình từ nhiều nguồn theo thứ tự ưu tiên. |
| User secrets | Nguồn cấu hình bí mật khi dev (không commit). |
| `IOptions<T>` | Config strongly-typed, đọc 1 lần (singleton). |
| `IOptionsSnapshot<T>` | Scoped, đọc lại mỗi request (reload). |
| `IOptionsMonitor<T>` | Singleton + callback OnChange (real-time). |
| Minimal API | Định nghĩa endpoint trực tiếp trong Program.cs, ít boilerplate. |
| `HttpClient` | Client gọi HTTP; không `new` liên tục (socket exhaustion). |
| `IHttpClientFactory` | Quản lý pool handler, hỗ trợ named/typed client + Polly. |
| Socket exhaustion | Cạn kiệt socket do tạo HttpClient liên tục (TIME_WAIT). |
| Polly | Thư viện resilience: retry, circuit breaker, timeout. |
| Circuit breaker | Ngắt gọi service lỗi liên tục để tránh domino. |
| `BackgroundService` / Hosted Service | Chạy tác vụ nền dài hạn (queue, job, outbox). |
| `IServiceScopeFactory` | Tạo scope thủ công (dùng Scoped trong Singleton/BackgroundService). |

## 6️⃣ Design Patterns (GoF & .NET)

| Thuật ngữ | Giải thích ngắn gọn |
|-----------|---------------------|
| Factory Method | 1 method (lớp con) tạo 1 loại object. |
| Abstract Factory | Tạo cả họ object liên quan đồng bộ. |
| Builder | Dựng object phức tạp từng bước (fluent), tránh telescoping constructor. |
| Telescoping constructor | Constructor quá nhiều tham số (anti-pattern). |
| Prototype | Tạo object mới bằng clone object mẫu. |
| Shallow / Deep copy | Copy tham chiếu (chung con) / copy đệ quy toàn bộ. |
| `MemberwiseClone` | Method tạo shallow copy. |
| `Lazy<T>` | Khởi tạo lười + thread-safe (dùng cho Singleton). |
| Singleton | Chỉ 1 instance toàn app. |
| Adapter | Chuyển interface không tương thích cho làm việc với nhau. |
| Decorator | Thêm hành vi động bằng cách bọc object cùng interface (vd Stream). |
| Proxy | Object đại diện kiểm soát truy cập (Virtual/Protection/Remote/Caching). |
| Facade | 1 interface đơn giản che hệ thống con phức tạp. |
| Composite | Xử lý object đơn & nhóm đồng nhất qua cây (đệ quy). |
| Bridge | Tách abstraction khỏi implementation để tiến hoá độc lập. |
| Strategy | Hoán đổi thuật toán độc lập lúc runtime. |
| State | Đổi hành vi khi state nội tại đổi (máy trạng thái). |
| Observer | Subject đổi → thông báo mọi observer (event/IObservable). |
| Template Method | Khung thuật toán ở base, lớp con override vài bước. |
| Chain of Responsibility | Chuỗi handler tự xử lý hoặc chuyển tiếp (↔ middleware). |
| `IPipelineBehavior` | Pipeline behavior của MediatR (logging/validation/transaction). |
| Command | Đóng gói yêu cầu thành object (queue, undo, ↔ CQRS Command). |
| Mediator | Giao tiếp qua trung gian thay vì gọi trực tiếp (MediatR). |
| MediatR | Thư viện Mediator: Send Command/Query qua handler. |
| `IRequest` / `IRequestHandler` | Message + handler tương ứng trong MediatR. |
| CQRS | Tách model ghi (Command) và đọc (Query). |
| Options pattern | Bind config thành strongly-typed class. |
| Result pattern | Trả `Result<T>` thay throw cho lỗi nghiệp vụ dự kiến. |
| FluentResults / ErrorOr | Thư viện Result pattern. |
| Specification pattern | Đóng gói điều kiện query tái sử dụng (`Expression<Func<T,bool>>`). |
| YAGNI / KISS | "You Aren't Gonna Need It" / "Keep It Simple" — chống over-engineering. |
| Over-engineering | Áp pattern khi chưa có vấn đề → phức tạp thừa. |
| Fluent API | Chuỗi method trả `this` để gọi nối tiếp. |

## 7️⃣ Cấu trúc dữ liệu & Giải thuật

| Thuật ngữ | Giải thích ngắn gọn |
|-----------|---------------------|
| Big-O | Ký hiệu mô tả tốc độ tăng thời gian/bộ nhớ theo n. |
| Time / Space complexity | Độ phức tạp thời gian / bộ nhớ phụ. |
| Amortized complexity | Chi phí trung bình trên chuỗi thao tác (vd `List.Add` O(1)). |
| Capacity vs Count | Kích thước mảng nội bộ vs số phần tử thực của List. |
| Array / Linked List | Bộ nhớ liên tục (index O(1)) / node+con trỏ (chèn đầu O(1)). |
| Stack (LIFO) | Vào sau ra trước (undo, DFS, kiểm tra ngoặc). |
| Queue (FIFO) | Vào trước ra trước (task queue, BFS). |
| Hash table | Cấu trúc hash(key)→bucket, tra cứu O(1) trung bình. |
| Collision | 2 key cùng bucket; xử lý bằng chaining/open addressing. |
| Chaining | Xử lý collision bằng linked list trong bucket (.NET Dictionary). |
| Binary Tree / BST | Cây ≤2 con / BST: trái<node<phải, tìm O(log n) nếu cân bằng. |
| Self-balancing tree | Cây tự cân bằng (AVL, Red-Black). |
| Red-Black Tree | Cây cân bằng dùng trong SortedDictionary/SortedSet. |
| `SortedDictionary` / `SortedList` | Red-Black tree O(log n) / mảng sắp (tra O(log n), chèn O(n)). |
| Heap / min-heap | Cây thoả heap property; lấy min O(1), chèn/xoá O(log n). |
| `PriorityQueue` | Hàng đợi ưu tiên (.NET 6+). |
| Graph | Đồ thị; biểu diễn adjacency list/matrix. |
| Adjacency list / matrix | Danh sách kề (thưa) / ma trận kề (O(1) check cạnh). |
| BFS / DFS | Duyệt theo tầng (queue) / theo chiều sâu (stack/đệ quy). |
| Topological sort | Sắp thứ tự phụ thuộc trên DAG. |
| QuickSort / MergeSort / HeapSort | Sort O(n log n); Quick worst O(n²), Merge stable, Heap O(1) space. |
| Introsort | Sort lai (Quick+Heap+Insertion) — `Array.Sort` dùng. |
| Stable sort | Giữ thứ tự phần tử bằng nhau (LINQ `OrderBy`). |
| Binary Search | Chia đôi trên mảng đã sắp, O(log n). |
| Recursion | Đệ quy; sâu quá → StackOverflowException (C# không tối ưu tail-call). |
| Tail-call | Lời gọi đệ quy cuối method; C# không tối ưu. |
| Dynamic Programming (DP) | Chia bài con chồng lặp, lưu kết quả (memoization/tabulation). |
| Memoization / Tabulation | Lưu kết quả top-down / bottom-up. |
| Divide & Conquer | Chia bài con độc lập (MergeSort, QuickSort). |
| Greedy | Chọn tối ưu cục bộ mỗi bước; không luôn đúng. |
| Two pointers | 2 con trỏ trên mảng đã sắp (two-sum O(n)). |
| Sliding window | Cửa sổ co giãn trên mảng/chuỗi (substring O(n)). |
| Floyd's cycle detection | Rùa & thỏ (slow/fast pointer) phát hiện chu trình, O(1) space. |
| `ConcurrentDictionary` | Dictionary thread-safe cho đa luồng. |

## 8️⃣ Entity Framework Core & Database

| Thuật ngữ | Giải thích ngắn gọn |
|-----------|---------------------|
| Change Tracking | DbContext theo dõi trạng thái entity để sinh SQL. |
| `EntityState` | Added/Modified/Deleted/Unchanged/Detached. |
| `AsNoTracking()` | Bỏ tracking cho query read-only (nhanh hơn). |
| Eager / Lazy / Explicit loading | `Include` cùng query / khi truy cập navigation / thủ công. |
| Proxy (lazy loading) | Object trung gian trigger load navigation property. |
| Migration | Version hoá schema DB từ model code. |
| Snapshot | Trạng thái model hiện tại để so sánh sinh migration. |
| `SaveChanges` | Lưu thay đổi trong 1 transaction (Unit of Work). |
| Optimistic concurrency | Dùng RowVersion, phát hiện xung đột lúc save. |
| Pessimistic concurrency | Khoá bản ghi khi đọc (cần raw SQL). |
| `RowVersion` / `[Timestamp]` | Concurrency token phát hiện thay đổi đồng thời. |
| `DbUpdateConcurrencyException` | Ném khi optimistic concurrency phát hiện xung đột. |
| `Find()` | Tìm theo PK, kiểm tra cache trước khi query DB. |
| `DbContext` / `DbSet` | Đơn vị làm việc EF (Scoped) / tập entity ánh xạ bảng. |
| `DbContextPool` | Pool tái sử dụng DbContext (throughput cao). |
| Split query (`AsSplitQuery`) | Tách nhiều Include thành nhiều query (tránh cartesian explosion). |
| Compiled query | Query biên dịch sẵn cho lời gọi lặp nhiều. |
| `FromSqlInterpolated` / `FromSqlRaw` | Raw SQL parameterized (an toàn) / thô. |
| Cartesian explosion | Nổ số dòng khi nhiều Include collection cùng lúc. |
| Client-side evaluation | Phần query không dịch được SQL, chạy ở app (chậm). |

## 9️⃣ Kiến trúc, Testing & DevOps

| Thuật ngữ | Giải thích ngắn gọn |
|-----------|---------------------|
| Clean / Onion Architecture | Chia tầng, dependency hướng vào trong (Domain là core). |
| Domain / Application / Infrastructure / Presentation | Các tầng: business core / use case / hạ tầng / giao diện. |
| CQRS | Tách Command (ghi) và Query (đọc). |
| Outbox pattern | Lưu message cùng transaction DB rồi publish sau (chống mất event). |
| Dual-write problem | Ghi DB + publish message không atomic → mất dữ liệu. |
| Unit / Integration / E2E Test | Test đơn vị cô lập / nhiều component thật / toàn luồng. |
| Mock / Stub / Fake | Kiểm tra tương tác / trả dữ liệu định sẵn / implement đơn giản. |
| Moq / NSubstitute | Thư viện tạo mock/stub. |
| AAA | Arrange-Act-Assert — cấu trúc viết test. |
| `WebApplicationFactory` | Test ASP.NET Core end-to-end trong bộ nhớ. |
| SQLite in-memory | DB in-memory cho integration test EF. |
| WireMock | Giả lập HTTP server bên ngoài khi test. |
| Structured logging | Log giữ placeholder (`{UserId}`), query được. |
| `ILogger<T>` | Interface logging chuẩn .NET. |
| Serilog / OpenTelemetry | Thư viện ship log / chuẩn observability (trace+metric+log). |
| Log level | Trace/Debug/Information/Warning/Error/Critical. |
| CI/CD | Continuous Integration/Delivery: restore→build→test→publish→deploy. |
| GitHub Actions | Nền tảng CI/CD chạy pipeline theo YAML. |
| Health checks | Endpoint `/health` báo tình trạng app. |
| Metrics / Tracing | Số liệu (Prometheus) / dấu vết request (distributed tracing). |
| Distributed tracing | Theo dõi request xuyên nhiều service. |
| `dotnet-counters`/`-trace`/`-dump` | Công cụ chẩn đoán CPU-GC / hiệu năng / memory dump. |
| APM | Application Performance Monitoring (App Insights, Datadog). |

## 🔟 SQL

| Thuật ngữ | Giải thích ngắn gọn |
|-----------|---------------------|
| INNER / LEFT / RIGHT / FULL OUTER JOIN | Khớp cả 2 / giữ trái / giữ phải / giữ cả hai bảng. |
| CROSS JOIN / SELF JOIN | Tích Descartes / join bảng với chính nó. |
| Anti-join | LEFT JOIN + `WHERE ... IS NULL` tìm dòng không khớp. |
| `WHERE` / `HAVING` | Lọc từng dòng trước nhóm / lọc sau GROUP BY (aggregate). |
| `GROUP BY` | Gom nhóm để aggregate (COUNT, SUM…). |
| `UNION` / `UNION ALL` | Gộp loại trùng / gộp giữ trùng (nhanh hơn). |
| `IN` / `EXISTS` / `NOT EXISTS` | Trong danh sách / có tồn tại (dừng sớm) / không tồn tại. |
| Subquery / CTE | Query lồng / tập kết quả tạm đặt tên (`WITH`), hỗ trợ đệ quy. |
| Recursive CTE | CTE tự tham chiếu để duyệt cây/phân cấp. |
| Window function | Tính trên cửa sổ dòng không gom nhóm (`OVER`). |
| `PARTITION BY` | Chia cửa sổ theo nhóm trong window function. |
| `ROW_NUMBER`/`RANK`/`DENSE_RANK` | Đánh số duy nhất / hạng bỏ số / hạng không bỏ số. |
| `LAG` / `LEAD` | Lấy giá trị dòng trước / sau. |
| Running total | Tổng luỹ kế qua các dòng (`SUM() OVER`). |
| Index | Cấu trúc (B-tree) tăng tốc tìm kiếm, tránh full scan. |
| B-tree | Cây cân bằng dùng cho index; tìm O(log n). |
| Full table scan | Quét toàn bảng O(n) khi không dùng index. |
| Clustered index | Quyết định thứ tự vật lý bảng; mỗi bảng 1 (thường PK). |
| Non-clustered index | Cấu trúc riêng + con trỏ tới dòng; mỗi bảng nhiều. |
| Composite index | Index nhiều cột; chỉ dùng được theo leftmost prefix. |
| Leftmost prefix | Quy tắc: query phải lọc từ cột đầu của composite index. |
| Covering index | Index chứa đủ cột query cần (`INCLUDE`) → không key lookup. |
| Key lookup | Truy ngược từ index về bảng lấy cột thiếu (tốn kém). |
| Sargable | Điều kiện dùng được index (không bọc hàm lên cột). |
| Execution plan / `EXPLAIN` | Kế hoạch DB thực thi query (seek vs scan). |
| Statistics | Thống kê phân bố dữ liệu giúp optimizer chọn plan. |
| ACID | Atomicity, Consistency, Isolation, Durability. |
| Isolation Level | Mức cô lập transaction (Read Uncommitted → Serializable). |
| Dirty read | Đọc dữ liệu chưa commit (có thể rollback). |
| Non-repeatable read | Đọc cùng dòng 2 lần khác giá trị (bị UPDATE). |
| Phantom read | Chạy cùng query 2 lần khác số dòng (bị INSERT). |
| Deadlock (DB) | 2 transaction giữ+chờ khoá của nhau; DB kill victim. |
| Optimistic / Pessimistic locking | Không khoá dùng version / khoá bản ghi khi đọc (`UPDLOCK`). |
| Normalization (1NF/2NF/3NF) | Chuẩn hoá giảm trùng lặp: nguyên tử / bỏ partial / bỏ transitive dependency. |
| Denormalization | Cố ý phá chuẩn để tăng tốc đọc. |
| Primary / Foreign key | Định danh duy nhất không NULL / tham chiếu PK bảng khác. |
| Unique constraint | Đảm bảo duy nhất nhưng cho phép NULL. |
| Referential integrity | Toàn vẹn tham chiếu qua foreign key. |
| `CASCADE` | Xoá/cập nhật lan truyền theo foreign key. |
| `DELETE` / `TRUNCATE` / `DROP` | Xoá dòng có điều kiện / xoá hết nhanh / xoá cả bảng. |
| `NULL` | "Không biết"; dùng `IS NULL`, không `= NULL`. |
| `COALESCE` / `NULLIF` / `ISNULL` | Giá trị non-null đầu / NULL nếu bằng / thay NULL. |
| SQL Injection | Tấn công nối chuỗi input độc vào SQL. |
| Parameterized query / Prepared statement | Tham số hoá input → chống injection. |
| Stored procedure | Thủ tục SQL lưu sẵn trong DB, có tham số. |
| Least privilege | Cấp quyền tối thiểu cho DB account. |

---

## 🔬 Thuật ngữ Internals (file 11–14)

### Runtime / Type system
| Thuật ngữ | Giải thích ngắn gọn |
|---|---|
| IL / metadata | Bytecode + bảng mô tả type; nền của GC chính xác, reflection, JIT theo máy đích. |
| Metadata token | Định danh 4 byte của một hàng trong bảng metadata (`0x06…` = MethodDef). |
| MethodTable / type handle | Cấu trúc runtime của một closed type: vtable, interface map, GC layout. |
| Object header (sync block) | 8 byte trước object: lock, hash code, GC bit. |
| Virtual Stub Dispatch | Cơ chế gọi interface method: stub tra interface map rồi cache. |
| Devirtualization | JIT biến virtual call thành direct call (nhờ `sealed` hoặc PGO). |
| Tiered compilation | Tier-0 (JIT nhanh) → Tier-1 (tối ưu) sau ~30 lần gọi; OSR cho vòng lặp dài. |
| Dynamic PGO | Thu profile ở Tier-0 để tối ưu ở Tier-1 (mặc định bật từ .NET 8). |
| ReadyToRun (R2R) | Pre-JIT lúc build, vẫn rejit lên Tier-1 lúc chạy. |
| Native AOT | Compile hẳn ra native lúc build; startup/RAM tốt nhất, mất dynamic code. |
| `__Canon` | Type ẩn dùng để **chia sẻ code** cho mọi generic instantiation kiểu reference. |
| `constrained.` prefix | Cho phép gọi interface method trên struct **không boxing**. |
| Defensive copy | Bản sao ẩn compiler tạo khi gọi member trên struct không `readonly`. |
| Byref / managed pointer | Con trỏ GC hiểu được; nền của `ref`, `ref struct`, `Span<T>`. |
| Source generator | Sinh code lúc compile để thay reflection lúc runtime (AOT/trim-safe). |

### Memory / GC
| Thuật ngữ | Giải thích ngắn gọn |
|---|---|
| Allocation context | Lát Gen 0 riêng của mỗi thread → cấp phát bằng bump pointer, không lock. |
| Generational hypothesis | "Hầu hết object chết trẻ" — cơ sở của gen 0/1/2. |
| GC roots | Stack + thanh ghi, static field, GC handle, f-reachable queue, interop. |
| Safe point / GC poll | Điểm thread có thể bị dừng an toàn để GC chạy. |
| Card table / write barrier | Đánh dấu vùng vừa ghi reference → GC Gen 0 không phải quét cả Gen 2. |
| SOH / LOH / POH | Small Object Heap / Large (≥85.000 B, không nén) / Pinned Object Heap. |
| Background GC | Thu Gen 2 song song với app để giảm pause. |
| F-reachable queue | Hàng đợi giữ object có finalizer sống thêm ít nhất một vòng GC. |
| Mid-life crisis | Object sống vừa đủ để bị promote rồi mới chết — kiểu rác tệ nhất. |
| Pinning / fragmentation | Ghim object khiến GC không dồn được → heap thủng lỗ. |
| `gcroot` | Lệnh trong dotnet-dump trả lời "ai đang giữ object này sống". |

### Async / Threading
| Thuật ngữ | Giải thích ngắn gọn |
|---|---|
| IOCP / epoll | Cơ chế OS báo I/O hoàn tất — lý do "không thread nào chờ I/O". |
| Async state machine | Struct compiler sinh ra; chỉ box lên heap khi await thật sự chưa xong. |
| Awaitable pattern | `GetAwaiter()` + `IsCompleted` + `OnCompleted` + `GetResult`. |
| `ExecutionContext` | Mang `AsyncLocal`, culture… và **luôn flow** qua await. |
| `SynchronizationContext` | "Chạy continuation ở đâu"; ASP.NET Core **không có**. |
| Work stealing | Worker rỗng lấy việc từ đuôi hàng đợi của worker khác. |
| Hill climbing | Thuật toán inject thread của ThreadPool (~1–2 thread/giây). |
| Thread pool starvation | Block thread pool ⇒ CPU thấp nhưng latency cao, queue dài. |
| `IValueTaskSource` | Object tái sử dụng đứng sau `ValueTask` (chỉ await một lần!). |
| Memory reordering | Compiler/JIT/CPU đảo lệnh; x86 mạnh, ARM64 yếu → bug lộ trên ARM. |
| Thin lock → sync block | `lock` rẻ khi không tranh chấp; inflate ra kernel object khi tranh chấp. |
| False sharing | Hai biến chung cache line 64B → nhiều core tranh nhau, scaling = 0. |
| Backpressure | Producer bị chặn khi consumer tụt lại (`Channel` bounded, Pipelines). |

### Framework
| Thuật ngữ | Giải thích ngắn gọn |
|---|---|
| Call site (DI) | Cây khởi tạo được container compile thành delegate sau vài lần resolve. |
| Captive dependency | Singleton giữ scoped → scoped sống mãi (lỗi hay gặp nhất với DbContext). |
| `Func<RequestDelegate, RequestDelegate>` | Hình dạng thật của một middleware; pipeline gấp từ cuối về đầu. |
| `System.IO.Pipelines` | Buffer chia sẻ + backpressure, nền của Kestrel; dữ liệu là `ReadOnlySequence<byte>`. |
| Endpoint routing | Chọn endpoint **trước**, chạy **sau** → middleware ở giữa đọc được metadata. |
| Query cache key (EF) | Hình dạng expression tree; closure variable được parameter hoá tự động. |
| `DetectChanges` | So snapshot để suy ra cột thay đổi — O(entity × property). |
| Identity map | Trong một DbContext, một khoá chính ↔ một instance. |
| Behavior chain (MediatR) | Decorator gấp ngược danh sách behavior quanh handler (giống middleware). |
| Outbox | Ghi message vào DB cùng transaction nghiệp vụ; processor publish sau ⇒ at-least-once. |
| Cache stampede | Nhiều request cùng miss một key → dồn tải xuống DB. |

---

## 📖 Cách dùng bộ tài liệu
1. Đọc theo thứ tự file 1 → 10, hoặc nhảy vào chủ đề yếu.
2. Với mỗi câu: che phần trả lời, tự trả lời trước, rồi đối chiếu.
3. Gõ lại code minh hoạ để nhớ lâu — đặc biệt các ví dụ ❌/✅.
4. Tra nhanh khái niệm ở [Bảng thuật ngữ](#-bảng-thuật-ngữ-glossary) bên trên.
5. **Track internals (11–14)**: đọc xong mỗi file thì chạy demo tương ứng
   (`dotnet run -- 11 all`) và **đối chiếu số liệu in ra** với phần giải thích — nhớ lâu hơn đọc
   gấp nhiều lần. Cuối mỗi file có **checklist tự kiểm tra**: tự nói thành lời, nếu ấp úng chỗ nào
   thì quay lại đúng câu đó.

_Chúc bạn phỏng vấn thành công! 🚀_
