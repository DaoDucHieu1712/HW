# Phần 15 — Concurrency ở quy mô hệ thống & Khả năng chịu tải

[⬅️ Về mục lục](interview.NET.md) | Tiếp theo: [Phần 16 — Transaction & Consistency ➡️](interview.NET.16-Transaction-Consistency.md)

> **Track "scale & load" (file 15–19).** Track 11–14 hỏi *"bên trong 1 process chạy ra sao"*.
> Track 15–19 hỏi *"khi có 10.000 request/giây và 8 instance thì hệ thống hỏng ở đâu, vì sao,
> và bạn sửa bằng cơ chế nào"*.
>
> Khung mỗi câu: **❓ Vấn đề gốc → ⚙️ Cơ chế bên dưới → 💻 Code/đo đạc → ⚖️ Hệ quả thực chiến**.
>
> File 13 nói về concurrency **trong 1 process** (lock, memory model, ThreadPool).
> File này nói về concurrency **giữa nhiều process/instance/máy** — nơi `lock` C# vô dụng.

---

## 🗺️ Bản đồ: một request đi qua bao nhiêu "cổ chai"

```
Client
  │  (1) rate limit / WAF / CDN
  ▼
Load balancer  ──────────► cổ chai: SNAT port, keep-alive, sticky session
  │
  ▼
App instance × N
  ├─ ThreadPool          ──► cổ chai: starvation, sync-over-async
  ├─ Semaphore/Channel   ──► cổ chai: hàng đợi không giới hạn (unbounded)
  ├─ HttpClient pool     ──► cổ chai: MaxConnectionsPerServer, socket exhaustion
  └─ DbConnection pool   ──► cổ chai: Max Pool Size (mặc định 100), pool timeout
  │
  ▼
Database / Redis / Broker
  ├─ row lock / gap lock ──► cổ chai: hot row, deadlock
  ├─ single-thread loop  ──► cổ chai: hot key, lệnh O(N)
  └─ partition           ──► cổ chai: hot partition
```

**Câu chốt phỏng vấn:** *"Chịu tải không phải là 'thêm máy'. Ở mọi tầng đều có một tài nguyên
**có giới hạn cứng** (thread, connection, row lock, partition). Scale = tìm tài nguyên đang bão hoà,
rồi hoặc **giảm thời gian giữ nó**, hoặc **chia nhỏ nó ra**, hoặc **từ chối bớt việc** (load shedding)."*

---

## CC-1. Concurrency, parallelism, throughput, latency — phân biệt và liên hệ

**❓ Vấn đề gốc**: rất nhiều người tối ưu sai chỗ vì nhầm 4 khái niệm này.

**⚙️ Cơ chế**:
- **Concurrency** = số việc đang *dở dang* cùng lúc (có thể chỉ 1 CPU).
- **Parallelism** = số việc thực sự *chạy đồng thời* (bị chặn bởi số core).
- **Throughput (λ)** = việc hoàn thành / giây.
- **Latency (W)** = thời gian một việc từ vào đến ra.

**Định luật Little** (đúng cho MỌI hệ thống ổn định, không cần giả định phân phối):

```
L = λ × W
(số request đang xử lý) = (throughput) × (latency trung bình)
```

**💻 Dùng để sizing thật:**
```text
Yêu cầu: 2.000 req/s, latency trung bình 50ms
→ L = 2000 × 0.05 = 100 request đang chạy đồng thời.
→ Nếu mỗi request giữ 1 DB connection suốt vòng đời:
   cần ≥ 100 connection → pool mặc định 100 là vừa CHẠM TRẦN → phải giảm thời gian giữ connection.
→ Nếu mỗi request chỉ giữ connection 5ms (phần còn lại là gọi API ngoài):
   L_db = 2000 × 0.005 = 10 connection là đủ.
```

**⚖️ Hệ quả**: *"Muốn tăng throughput mà không tăng tài nguyên → phải giảm **thời gian giữ**
tài nguyên khan hiếm"*. Đây là nguyên lý đằng sau: async I/O, transaction ngắn, không gọi HTTP
trong transaction, không giữ lock khi ghi log.

---

## CC-2. Vì sao thêm máy không tăng tuyến tính? (USL — Universal Scalability Law)

**❓ Vấn đề gốc**: interviewer hỏi *"tăng từ 4 lên 8 instance có gấp đôi throughput không?"*

**⚙️ Cơ chế**: USL (Gunther) mô hình hoá 2 lực cản:

```
             N
C(N) = ───────────────────────────
        1 + α(N-1) + βN(N-1)

α = contention  (phần tuần tự — Amdahl: lock, hot row, single leader)
β = coherency   (chi phí đồng bộ giữa các node — cache invalidation, 2PC, rebalance)
```

- Chỉ có α → đường cong bão hoà (Amdahl).
- Có thêm β → đường cong **đi xuống**: thêm node làm CHẬM hơn (retrograde).

**⚖️ Hệ quả thực chiến**: nếu tất cả instance cùng `UPDATE` một hàng tồn kho → α ≈ 1, thêm instance
chỉ làm tăng deadlock. Muốn scale phải **phá bỏ điểm tuần tự chung**: shard theo key, hàng đợi
theo partition, đếm theo bucket rồi cộng dồn (xem CC-11, CC-17).

---

## CC-3. Lost update — race condition ở mức hệ thống, không phải mức thread

**❓ Vấn đề gốc**: 2 request cùng đọc `Stock = 1`, cùng kiểm tra `> 0`, cùng trừ → `Stock = 0`
nhưng bán được 2 đơn. `lock` trong C# **không cứu được** vì 2 request có thể ở 2 instance khác nhau.

**⚙️ Cơ chế**: read–modify–write không nguyên tử xuyên process. Có đúng **4 cách** giải quyết:

| Cách | Cơ chế | Khi nào dùng |
|---|---|---|
| 1. **Atomic DB statement** | để DB tự làm RMW trong 1 câu lệnh | tốt nhất, luôn ưu tiên |
| 2. **Pessimistic lock** | `SELECT ... FOR UPDATE` giữ row lock | cần đọc rồi tính toán phức tạp |
| 3. **Optimistic concurrency** | `rowversion` + retry | ít tranh chấp, entity lớn |
| 4. **Distributed lock** | Redis/etcd lock | tài nguyên **ngoài** DB (file, API bên thứ 3) |

**💻 Cách 1 — luôn thử trước tiên:**
```csharp
// ❌ SAI: read-modify-write qua ChangeTracker → lost update
var p = await _repo.FindByIdAsync(id, ct);
if (p.Stock > 0) { p.Decrease(1); }   // 2 request cùng thấy Stock = 1

// ✅ ĐÚNG: một câu lệnh nguyên tử, DB tự lock row trong lúc update
var affected = await _context.Products
    .Where(p => p.Id == id && p.Stock >= qty)
    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock - qty), ct);

if (affected == 0) throw new BadRequestException("Hết hàng");
```
`affected == 0` chính là "câu trả lời" của DB: hoặc không tồn tại, hoặc không đủ hàng.
Không có khe hở giữa check và update.

**⚖️ Trade-off**: `ExecuteUpdateAsync` **bỏ qua ChangeTracker** ⇒ không raise domain event,
không chạy interceptor audit. Trong project HW: dùng cho các "counter" nóng, còn nghiệp vụ có
domain event thì dùng cách 2/3 (CC-4, CC-5).

---

## CC-4. Pessimistic locking — `SELECT ... FOR UPDATE` hoạt động thế nào?

**⚙️ Cơ chế**: `FOR UPDATE` đặt **exclusive record lock** trên các row đã đọc, giữ đến khi
COMMIT/ROLLBACK. Transaction khác đọc cùng row bằng `FOR UPDATE`/`UPDATE` sẽ **chờ**
(tới `innodb_lock_wait_timeout`, mặc định MySQL 50s → nên giảm xuống 3–5s).

- `FOR UPDATE` — chặn cả đọc-để-ghi lẫn ghi.
- `FOR SHARE` (`LOCK IN SHARE MODE`) — cho nhiều reader, chặn writer. **Rất dễ deadlock** khi
  2 transaction cùng nâng cấp S-lock → X-lock.
- `FOR UPDATE SKIP LOCKED` — bỏ qua row đang bị khoá ⇒ mẫu **hàng đợi trong DB** (job queue).
- `FOR UPDATE NOWAIT` — lỗi ngay thay vì chờ ⇒ fail fast.

**💻 Trong EF Core (không có API sẵn, phải dùng SQL thô):**
```csharp
await using var tx = await _context.Database.BeginTransactionAsync(ct);

var product = await _context.Products
    .FromSqlInterpolated($"SELECT * FROM Products WHERE Id = {id} FOR UPDATE")
    .SingleAsync(ct);              // ← row bị khoá từ đây

if (product.Stock < qty) throw new BadRequestException("Hết hàng");
product.Decrease(qty);             // domain method → raise event
await _context.SaveChangesAsync(ct);
await tx.CommitAsync(ct);          // ← lock nhả ở đây
```

**💻 Mẫu job queue bằng `SKIP LOCKED` (thay cho polling toàn bảng):**
```sql
START TRANSACTION;
SELECT * FROM OutboxMessages
 WHERE ProcessedOn IS NULL
 ORDER BY OccurredOn
 LIMIT 20
 FOR UPDATE SKIP LOCKED;     -- N worker chạy song song, không giẫm chân nhau
-- ... xử lý ...
UPDATE OutboxMessages SET ProcessedOn = NOW() WHERE Id IN (...);
COMMIT;
```

**⚖️ Hệ quả**: lock giữ **suốt transaction** ⇒ transaction dài = throughput sập.
Nguyên tắc vàng: *"khoá muộn nhất có thể, commit sớm nhất có thể, và TUYỆT ĐỐI không gọi HTTP/
gửi message/ghi file trong lúc đang giữ row lock"*.

---

## CC-5. Optimistic concurrency — `rowversion` và `DbUpdateConcurrencyException`

**❓ Vấn đề gốc**: màn hình edit của user mở 5 phút; không thể giữ DB lock 5 phút.

**⚙️ Cơ chế**: thêm cột version. Câu UPDATE sinh ra là:
```sql
UPDATE Blogs SET Title = @t, Version = @newV
 WHERE Id = @id AND Version = @oldV;      -- rows affected = 0 ⇒ ai đó đã sửa trước
```
EF Core đếm số row bị ảnh hưởng; bằng 0 ⇒ ném `DbUpdateConcurrencyException`.

**💻 Cấu hình:**
```csharp
// MySQL/MariaDB không có kiểu rowversion → dùng cột được đánh dấu ConcurrencyToken
public class Blog : AggregateRoot
{
    public string Title { get; private set; } = default!;
    public Guid ConcurrencyStamp { get; private set; } = Guid.NewGuid();  // đổi mỗi lần Update()
}

builder.Property(b => b.ConcurrencyStamp).IsConcurrencyToken();
// SQL Server: builder.Property<byte[]>("RowVersion").IsRowVersion();
// PostgreSQL: builder.UseXminAsConcurrencyToken();
```

**💻 Xử lý xung đột — 3 chiến lược, phải nói rõ chọn cái nào và vì sao:**
```csharp
try { await _context.SaveChangesAsync(ct); }
catch (DbUpdateConcurrencyException ex)
{
    var entry = ex.Entries.Single();
    var db    = await entry.GetDatabaseValuesAsync(ct);
    if (db is null) throw new NotFoundException("Bản ghi đã bị xoá");

    // (a) Store wins  → huỷ thay đổi, báo user tải lại       (an toàn nhất, mặc định nên chọn)
    // (b) Client wins → entry.OriginalValues.SetValues(db); rồi SaveChanges lại
    // (c) Merge       → gộp theo từng field (chỉ khi nghiệp vụ cho phép)
    throw new ConflictException("Dữ liệu vừa bị người khác thay đổi, vui lòng tải lại.");
}
```

**⚖️ Trade-off**:
- Ít tranh chấp → optimistic **nhanh hơn nhiều** (không giữ lock, không chờ).
- Tranh chấp cao (hot row: tồn kho flash sale) → optimistic biến thành **vòng lặp retry vô ích**,
  càng nhiều instance càng tệ (β trong USL). Lúc đó **phải** dùng CC-3 cách 1 hoặc hàng đợi CC-9.
- Retry optimistic **chỉ an toàn khi thao tác idempotent** hoặc được tính lại từ đầu.

---

## CC-6. Distributed lock — vì sao Redis lock "đúng 99%" vẫn là sai

**❓ Vấn đề gốc**: cần đảm bảo chỉ 1 instance chạy một việc (import file, gọi API bên thứ 3 không
idempotent). `lock` C# chỉ có tác dụng trong 1 process.

**⚙️ Cơ chế lock tối thiểu đúng:**
```
SET lock:{key} {randomToken} NX PX 30000     -- NX: chỉ set nếu chưa có; PX: TTL bắt buộc
... làm việc ...
-- Nhả lock phải NGUYÊN TỬ: so token rồi mới xoá (nếu không sẽ xoá nhầm lock của người khác)
EVAL "if redis.call('get',KEYS[1])==ARGV[1] then return redis.call('del',KEYS[1]) else return 0 end" 1 lock:{key} {token}
```

**Ba lỗi kinh điển:**
1. **Không có TTL** → process chết = lock kẹt vĩnh viễn.
2. **Nhả lock bằng `DEL` trần** → A bị GC pause quá TTL, lock hết hạn, B lấy được, A tỉnh dậy xoá
   lock của B.
3. **Tin rằng lock đảm bảo mutual exclusion** → sai. TTL hết hạn giữa chừng (GC pause, VM freeze,
   network partition) ⇒ **2 process cùng tin mình đang giữ lock**.

**💻 Fencing token — cách duy nhất đúng (Kleppmann):**
```csharp
// Lock trả về một số tăng dần (INCR); tài nguyên đích TỪ CHỐI token cũ hơn token đã thấy
var token = await _redis.StringIncrementAsync("fence:import");   // ví dụ: 33
if (await TryAcquireAsync("lock:import", token, TimeSpan.FromSeconds(30)))
{
    // Ghi kèm token; DB chỉ chấp nhận nếu token > LastToken đã lưu
    var ok = await _context.ImportJobs
        .Where(j => j.Id == id && j.LastToken < token)
        .ExecuteUpdateAsync(s => s.SetProperty(j => j.LastToken, token), ct);
    if (ok == 0) return;   // ta là "zombie", có kẻ mới hơn đã chạy
}
```

**⚖️ Kết luận phỏng vấn**: *"Distributed lock chỉ nên dùng cho **efficiency** (tránh làm việc thừa),
không dùng cho **correctness** (tránh làm sai). Correctness phải do tài nguyên đích bảo đảm:
unique constraint, atomic update có điều kiện, hoặc fencing token."*
Redlock (nhiều node Redis) **không** sửa được vấn đề này — nó chỉ chống chết 1 node.
Muốn correctness thật: etcd/ZooKeeper lease + fencing, hoặc đơn giản là dùng chính DB
(`SELECT FOR UPDATE`, `GET_LOCK()`, `pg_advisory_lock`, `sp_getapplock`).

---

## CC-7. Idempotency key — chống double-submit và retry của client

**❓ Vấn đề gốc**: mạng timeout, client retry, người dùng bấm "Thanh toán" 2 lần ⇒ trừ tiền 2 lần.
Không có cách nào để server *đoán* được đây là retry hay là đơn thứ 2 thật.

**⚙️ Cơ chế**: client sinh khoá (`Idempotency-Key: <uuid>`), server:
1. `INSERT` khoá vào bảng có **unique constraint**, trong **cùng transaction** với nghiệp vụ.
2. Nếu trùng khoá → trả lại **response đã lưu** của lần đầu (không chạy lại nghiệp vụ).

```sql
CREATE TABLE IdempotencyRecords (
  `Key`       VARCHAR(64)  NOT NULL PRIMARY KEY,
  Endpoint    VARCHAR(200) NOT NULL,
  RequestHash CHAR(64)     NOT NULL,   -- chống dùng lại key cho payload khác
  StatusCode  INT          NULL,
  Response    JSON         NULL,
  State       TINYINT      NOT NULL,   -- 0 = InProgress, 1 = Completed
  CreatedAt   DATETIME(6)  NOT NULL,
  ExpiresAt   DATETIME(6)  NOT NULL
);
```

**💻 Behavior trong pipeline MediatR của project HW (đặt TRƯỚC TransactionBehavior):**
```csharp
public sealed class IdempotencyBehavior<TReq, TRes>(AppDbContext db) : IPipelineBehavior<TReq, TRes>
    where TReq : IBaseCommand, IHasIdempotencyKey
{
    public async Task<TRes> Handle(TReq req, RequestHandlerDelegate<TRes> next, CancellationToken ct)
    {
        var existing = await db.IdempotencyRecords.FindAsync([req.IdempotencyKey], ct);
        if (existing is { State: RecordState.Completed })
            return JsonSerializer.Deserialize<TRes>(existing.Response!)!;   // trả lại y hệt lần đầu
        if (existing is { State: RecordState.InProgress })
            throw new ConflictException("Yêu cầu đang được xử lý");         // 409 → client thử lại sau

        db.IdempotencyRecords.Add(new(req.IdempotencyKey, RecordState.InProgress));
        var result = await next();      // TransactionBehavior bọc bên trong ⇒ cùng 1 transaction
        // ... cập nhật State = Completed + Response trước khi commit
        return result;
    }
}
```

**⚖️ Chi tiết hay bị hỏi khoan:**
- Vì sao unique constraint chứ không phải "check rồi insert"? → check-then-act lại là race (CC-3).
- Vì sao lưu `RequestHash`? → chống client tái sử dụng key cho payload khác (phải trả 422).
- Ai xoá key cũ? → job dọn theo `ExpiresAt` (24h–7 ngày, nghiệp vụ tài chính thì giữ lâu hơn).
- Khác gì dedup ở consumer (MQ-7)? → cùng nguyên lý, khác vị trí: một cái ở biên HTTP,
  một cái ở biên message.

---

## CC-8. Rate limiting — 5 thuật toán và cái giá của mỗi cái

**❓ Vấn đề gốc**: bảo vệ hệ thống khỏi client hung hãn / crawler / retry storm, và bảo vệ
tài nguyên chung (DB) khỏi bị một tenant chiếm hết.

**⚙️ So sánh:**

| Thuật toán | Cơ chế | Ưu | Nhược |
|---|---|---|---|
| **Fixed window** | đếm trong khung 1 phút | rẻ nhất, 1 counter | **burst gấp đôi** ở ranh giới cửa sổ |
| **Sliding window log** | lưu timestamp từng request | chính xác tuyệt đối | tốn bộ nhớ O(số request) |
| **Sliding window counter** | nội suy 2 cửa sổ liền kề | gần chính xác, rẻ | là xấp xỉ |
| **Token bucket** | token đổ vào đều `r/s`, sức chứa `b` | **cho phép burst có kiểm soát** | cần 2 tham số |
| **Leaky bucket** | ra đều đặn, vào thì xếp hàng | làm phẳng traffic tuyệt đối | thêm latency, cần queue |

**💻 .NET 7+ có sẵn middleware:**
```csharp
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("per-user", ctx => RateLimitPartition.GetTokenBucketLimiter(
        partitionKey: ctx.User.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress!.ToString(),
        _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 100,                                  // burst
            TokensPerPeriod = 20,                              // 20 token mỗi 1s ⇒ 20 rps ổn định
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 0,                                    // 0 = từ chối ngay, không xếp hàng
            AutoReplenishment = true
        }));
    o.OnRejected = async (ctx, ct) =>
    {
        ctx.HttpContext.Response.Headers.RetryAfter = "1";     // BẮT BUỘC: nói client chờ bao lâu
        await ctx.HttpContext.Response.WriteAsync("Too many requests", ct);
    };
});
app.UseRateLimiter();
```

**💻 Rate limit **phân tán** (nhiều instance) — phải chạy trong Redis bằng Lua để nguyên tử:**
```lua
-- KEYS[1]=key, ARGV[1]=rate/s, ARGV[2]=burst, ARGV[3]=now(ms), ARGV[4]=cost
local b      = redis.call('HMGET', KEYS[1], 'tokens', 'ts')
local tokens = tonumber(b[1]) or tonumber(ARGV[2])
local ts     = tonumber(b[2]) or tonumber(ARGV[3])
local delta  = math.max(0, tonumber(ARGV[3]) - ts) / 1000 * tonumber(ARGV[1])
tokens = math.min(tonumber(ARGV[2]), tokens + delta)          -- đổ token theo thời gian trôi
if tokens < tonumber(ARGV[4]) then return 0 end
redis.call('HMSET', KEYS[1], 'tokens', tokens - tonumber(ARGV[4]), 'ts', ARGV[3])
redis.call('PEXPIRE', KEYS[1], 60000)
return 1
```

**⚖️ Hệ quả**: rate limit ở **mỗi instance** với hạn mức `X/N` thì khi 1 instance chết, tổng hạn
mức tụt. Rate limit **tập trung** thì chính Redis thành điểm chết (và thêm 1 RTT mỗi request).
Thực tế hay dùng lai: **local limiter chặn thô + Redis limiter chặn chính xác cho endpoint đắt**.

---

## CC-9. Backpressure & load shedding — vì sao "hàng đợi không giới hạn" là bug

**❓ Vấn đề gốc**: khi tải vào > khả năng xử lý, hệ thống có 3 lựa chọn: (1) xếp hàng vô hạn,
(2) từ chối bớt, (3) chậm dần rồi chết. Mặc định của code cẩu thả luôn là (1) → OOM + latency
tăng vô hạn + client đã timeout nhưng server vẫn xử lý = **làm việc vô ích 100%**.

**⚙️ Cơ chế**: hàng đợi **có giới hạn** (bounded) biến "chậm" thành "từ chối nhanh" — thứ mà
client xử lý được.

**💻 `Channel<T>` bounded — xương sống của mọi producer/consumer trong .NET:**
```csharp
var channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(1_000)
{
    FullMode = BoundedChannelFullMode.Wait,   // Wait = backpressure (producer bị chặn)
                                              // DropWrite/DropOldest = load shedding
    SingleReader = false, SingleWriter = false
});

// Producer: KHÔNG được bỏ qua kết quả TryWrite
if (!channel.Writer.TryWrite(item))
    return Results.StatusCode(503);           // shed ngay, kèm Retry-After

// Consumer: N worker song song
await Parallel.ForEachAsync(
    channel.Reader.ReadAllAsync(ct),
    new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
    async (item, token) => await ProcessAsync(item, token));
```

**💻 Shed theo "còn kịp không" — kỹ thuật hay ghi điểm:**
```csharp
// Nếu request đã nằm trong queue quá deadline của client thì VỨT, đừng xử lý
if (DateTimeOffset.UtcNow - item.EnqueuedAt > TimeSpan.FromSeconds(2)) { Metrics.Shed(); return; }

// Và luôn tôn trọng huỷ khi client ngắt kết nối:
// HttpContext.RequestAborted đã tự cancel — hãy truyền ct xuống TẬN cùng (repo, HttpClient, Redis)
```

**⚖️ Hệ quả**: Little's Law lại xuất hiện — queue dài `L` với throughput `λ` ⇒ chờ `W = L/λ`.
Queue 10.000 item, xử lý 100/s ⇒ item cuối chờ **100 giây**, chắc chắn client đã bỏ.
*"Hàng đợi dài không tăng throughput, nó chỉ biến việc mất request thành việc mất thời gian."*

---

## CC-10. Cạn tài nguyên: connection pool, ThreadPool, socket

**❓ Vấn đề gốc**: lỗi kinh điển production — `Timeout expired. The timeout period elapsed prior to
obtaining a connection from the pool.` Không phải DB chậm, mà là **pool cạn**.

**⚙️ Ba pool khác nhau, ba cách chết khác nhau:**

| Pool | Giới hạn mặc định | Triệu chứng cạn | Cách sửa |
|---|---|---|---|
| **DbConnection** | `Max Pool Size=100` trong connection string | pool timeout sau ~15s | transaction ngắn hơn, `AsNoTracking`, tách read replica, tăng pool **có cân nhắc phía DB** |
| **ThreadPool** | min = số core, tăng thêm ~1–2 thread/giây | latency tăng theo bậc thang, `PendingWorkItemCount` cao | bỏ `.Result`/`.Wait()`, async all the way; `SetMinThreads` chỉ là băng dán tạm |
| **Socket / SNAT** | ~28k ephemeral port, `TIME_WAIT` 240s (Windows) | `SocketException: Only one usage of each socket address` | **`IHttpClientFactory`** (tái dùng handler), keep-alive, HTTP/2 multiplexing |

**💻 Chẩn đoán nhanh — dán vào endpoint `/health/detail`:**
```csharp
ThreadPool.GetAvailableThreads(out var worker, out var io);
ThreadPool.GetMaxThreads(out var maxW, out var maxIo);
var stats = new
{
    BusyWorker     = maxW  - worker,
    BusyIo         = maxIo - io,
    QueueLength    = ThreadPool.PendingWorkItemCount,   // > 0 kéo dài ⇒ starvation
    LockContention = Monitor.LockContentionCount,       // tăng nhanh ⇒ có lock nóng
    Gen2           = GC.CollectionCount(2)
};
```
Counter nên bật cảnh báo:
`dotnet-counters monitor --counters System.Runtime,Microsoft.AspNetCore.Hosting`
→ `threadpool-queue-length`, `threadpool-thread-count`, `current-requests`.

**⚖️ Bẫy ngược**: tăng `Max Pool Size` lên 500 thường **làm chậm hơn** — DB phải context-switch giữa
500 connection, lock contention tăng. Đúng hơn: **giảm thời gian giữ connection** (CC-1),
hoặc đặt pooler trước DB (ProxySQL / PgBouncer) để nhiều app instance chia sẻ ít connection thật.

---

## CC-11. Hot key / hot partition — vì sao 1% dữ liệu làm sập 100% hệ thống

**❓ Vấn đề gốc**: sharding tưởng đã giải quyết scale, nhưng traffic không phân bố đều
(Zipf: một sản phẩm bán chạy = 40% lượt xem; một tenant lớn = 60% lượt ghi).

**⚙️ Cơ chế**: mọi hệ thống phân tán đều map `key → partition` bằng hash. Key nóng ⇒ partition nóng,
và **partition không chia nhỏ được nữa** (Redis: 1 slot thuộc 1 node; Kafka: 1 partition chỉ 1
consumer trong group; DB: 1 row chỉ 1 lock).

**💻 Ba kỹ thuật gỡ:**
```csharp
// 1) Key splitting — biến 1 key nóng thành N key nguội (đếm view, like)
var bucket = Random.Shared.Next(0, 16);
await _redis.StringIncrementAsync($"views:{postId}:{bucket}");   // ghi tản ra 16 key
// đọc: cộng 16 key lại, hoặc job gộp định kỳ về 1 con số

// 2) L1 local cache trước L2 Redis — hot key phục vụ ngay trong process, TTL ngắn 1–5s
//    (xem file 17, CA-13 — HybridCache của .NET 9 làm sẵn việc này)

// 3) Ghi theo hàng đợi thay vì ghi trực tiếp — biến contention thành throughput tuần tự
//    Mọi lệnh trừ kho của 1 SKU đi vào CÙNG 1 partition Kafka ⇒ 1 consumer xử lý tuần tự:
//    không lock, không deadlock, và ordering được đảm bảo (xem MQ-6).
```

**⚖️ Nhận diện sớm**: đo **phân phối** key, không chỉ đo trung bình.
`redis-cli --hotkeys`, Kafka `kafka-consumer-groups --describe` (lag lệch giữa partition),
DB: `performance_schema` top row-lock waits. *"Trung bình luôn nói dối; p99 và phân phối mới nói thật."*

---

## CC-12. Resilience: timeout, retry, circuit breaker, bulkhead (Polly v8)

**❓ Vấn đề gốc**: một dependency chậm 5 giây sẽ **giữ** thread + connection của bạn 5 giây
⇒ theo Little's Law, `L` tăng vọt ⇒ pool cạn ⇒ **sập lan truyền** (cascading failure).
Và retry ngây thơ biến sự cố nhỏ thành **retry storm** (tải × 3 đúng lúc hệ thống yếu nhất).

**⚙️ Thứ tự bọc đúng (từ ngoài vào trong)** — thứ tự này hay bị hỏi:
```
Total timeout  →  Retry  →  Circuit breaker  →  Attempt timeout  →  HTTP call
  (chặn trên toàn bộ)      (mở là fail-fast)    (mỗi lần thử)
```
Sai lầm phổ biến: đặt circuit breaker **ngoài** retry ⇒ breaker đếm cả các lần retry ⇒ mở quá sớm.

**💻 Polly v8 pipeline gắn vào `IHttpClientFactory`:**
```csharp
builder.Services.AddHttpClient<IPaymentGateway, PaymentGateway>(c =>
{
    c.BaseAddress = new Uri("https://pay.example.com");
    c.Timeout = TimeSpan.FromSeconds(10);                     // total timeout
})
.AddResilienceHandler("pay", b =>
{
    b.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,                                     // ❗BẮT BUỘC: chống retry đồng loạt
        Delay = TimeSpan.FromMilliseconds(200),
        ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Result is { StatusCode: HttpStatusCode.ServiceUnavailable or (HttpStatusCode)429 }
            || args.Outcome.Exception is HttpRequestException or TimeoutRejectedException)
    });
    b.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        FailureRatio = 0.5, MinimumThroughput = 20,           // ≥50% lỗi trên ≥20 request
        SamplingDuration = TimeSpan.FromSeconds(30),
        BreakDuration    = TimeSpan.FromSeconds(15)           // mở 15s rồi thử half-open
    });
    b.AddTimeout(TimeSpan.FromSeconds(3));                    // attempt timeout
});
```

**⚖️ Quy tắc sống còn:**
- **Chỉ retry thao tác idempotent.** `POST /payment` không idempotent → phải kèm idempotency key (CC-7).
- **Jitter là bắt buộc**, không phải tuỳ chọn. Không jitter = đồng bộ hoá toàn bộ client (thundering herd).
- **Bulkhead**: giới hạn concurrency riêng cho từng dependency (`SemaphoreSlim` hoặc
  `AddConcurrencyLimiter`) để một dependency chậm không nuốt hết ThreadPool của cả app.
- **Fallback có ý nghĩa**: trả dữ liệu cache cũ (stale) còn hơn trả 500 (xem CA-5).

---

## CC-13. Giới hạn concurrency khi fan-out — `SemaphoreSlim`, `Parallel.ForEachAsync`, `Channel`

**❓ Vấn đề gốc**: `await Task.WhenAll(ids.Select(FetchAsync))` với 10.000 id = 10.000 request
đồng thời → giết chính bạn và giết luôn dependency.

**💻 Ba cách, chọn theo tình huống:**
```csharp
// 1) Đơn giản nhất, .NET 6+ — dùng cho fan-out I/O
await Parallel.ForEachAsync(ids,
    new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = ct },
    async (id, token) => bag.Add(await FetchAsync(id, token)));   // gom bằng ConcurrentBag/Channel

// 2) SemaphoreSlim — khi cần chia sẻ hạn mức giữa nhiều nơi gọi khác nhau
private static readonly SemaphoreSlim _gate = new(16);
await _gate.WaitAsync(ct);
try { return await FetchAsync(id, ct); } finally { _gate.Release(); }   // finally BẮT BUỘC

// 3) Channel pipeline — khi có nhiều giai đoạn tốc độ khác nhau (đọc → biến đổi → ghi)
//    Mỗi giai đoạn một bounded channel ⇒ backpressure tự lan ngược về nguồn.
```

**⚖️ Bẫy hay bị hỏi:**
- `Parallel.For`/`Parallel.ForEach` (bản **sync**) với body async = fire-and-forget: mất exception,
  mất kiểm soát ⇒ chỉ dùng `ForEachAsync` cho async.
- `MaxDegreeOfParallelism` cho việc **CPU-bound** ≈ số core; cho **I/O-bound** thì đặt theo
  giới hạn của dependency (rate limit của họ), không phải theo core.
- `SemaphoreSlim.Wait()` (sync) trong code async ⇒ chặn ThreadPool ⇒ starvation (file 13).

---

## CC-14. Trạng thái dùng chung trong ASP.NET Core — singleton, `ConcurrentDictionary`

**❓ Vấn đề gốc**: service singleton **dùng chung cho mọi request** ⇒ mọi field mutable đều là
shared mutable state ⇒ phải thread-safe. Đây là lỗi hay gặp nhất khi "tự viết cache".

**💻 Bẫy `GetOrAdd` — factory có thể chạy nhiều lần:**
```csharp
// ❌ Với key nóng, N thread cùng miss ⇒ factory chạy N lần (N lần gọi DB)
var v = _dict.GetOrAdd(key, k => LoadExpensive(k));

// ✅ Bọc bằng Lazy: chỉ MỘT lần khởi tạo thật sự được thực hiện
private readonly ConcurrentDictionary<string, Lazy<Task<Product>>> _dict = new();
public Task<Product> GetAsync(string key) =>
    _dict.GetOrAdd(key, k => new Lazy<Task<Product>>(() => LoadAsync(k))).Value;  // xem CA-5
```
Lưu ý: `ConcurrentDictionary` chỉ nguyên tử cho **một** thao tác.
`if (dict.ContainsKey(k)) dict[k] = v;` vẫn là race. Muốn đọc-sửa-ghi nguyên tử: `AddOrUpdate`
hoặc `TryUpdate(key, newValue, comparisonValue)` (CAS).

**⚖️ Nguyên tắc**: state trong singleton chỉ nên là (a) immutable, (b) `ConcurrentDictionary`/
`Interlocked`, hoặc (c) đẩy hẳn ra ngoài (Redis) nếu chạy nhiều instance — vì state trong process
**không đồng nhất giữa các instance**, gốc rễ của rất nhiều bug "lúc được lúc không".

---

## CC-15. Read-mostly: snapshot bất biến thay vì khoá

**❓ Vấn đề gốc**: bảng cấu hình/danh mục đọc 100.000 lần/giây, ghi 1 lần/ngày. Dùng
`ReaderWriterLockSlim` vẫn phải trả chi phí đồng bộ mỗi lần đọc.

**⚙️ Copy-on-write**: reader đọc một tham chiếu bất biến; writer tạo bản mới rồi
`Interlocked.Exchange`. Reader **không khoá gì cả** và luôn thấy một snapshot nhất quán.

```csharp
private ImmutableDictionary<string, Config> _snapshot = ImmutableDictionary<string, Config>.Empty;

public Config? Get(string k) =>
    Volatile.Read(ref _snapshot).TryGetValue(k, out var v) ? v : null;   // zero lock

public void Reload(IEnumerable<Config> all) =>
    Interlocked.Exchange(ref _snapshot, all.ToImmutableDictionary(c => c.Key));   // ghi hiếm
```

**⚖️ Trade-off**: reader đang chạy vẫn thấy snapshot cũ vài mili-giây (eventual consistency ngay
trong process) — chấp nhận được cho cấu hình/danh mục, **không** chấp nhận được cho số dư tài khoản.

---

## CC-16. Sinh ID ở quy mô lớn: GUID v4 vs v7 vs Snowflake

**❓ Vấn đề gốc**: project HW dùng `Guid.NewGuid().ToString()` làm khoá chính. Ở quy mô lớn, khoá
**ngẫu nhiên** làm phân mảnh B+Tree: mỗi insert rơi vào một trang ngẫu nhiên ⇒ page split,
buffer pool miss, WAL phình.

**⚙️ So sánh:**

| Loại | Sắp theo thời gian | Kích thước | Ghi chú |
|---|---|---|---|
| `Guid.NewGuid()` (v4) | ❌ ngẫu nhiên | 16B (36 ký tự nếu lưu string!) | phân mảnh index nặng |
| **UUID v7** (`Guid.CreateVersion7()`, .NET 9) | ✅ tăng dần theo ms | 16B | **mặc định nên chọn hiện nay** |
| ULID | ✅ | 16B / 26 ký tự | tương đương v7, dễ đọc |
| **Snowflake** (64-bit: time + nodeId + seq) | ✅ | 8B | nhỏ nhất, phải cấp phát nodeId |
| Auto-increment | ✅ | 4–8B | tốt nhất cho DB đơn; **hỏng** khi sharding + lộ sản lượng |

**💻**
```csharp
// .NET 9+
public Entity() { Id = Guid.CreateVersion7().ToString("N"); }   // "N": 32 ký tự, bỏ dấu '-'
// Quan trọng hơn cả: lưu BINARY(16) hoặc CHAR(32), KHÔNG lưu VARCHAR(36) có dấu gạch
```

**⚖️ Hệ quả đo được**: đổi v4 → v7 trên bảng vài chục triệu dòng giảm rõ rệt page split và kích
thước index. Ngược lại, ID tuần tự **lộ thông tin nghiệp vụ** (đối thủ đếm được số đơn/ngày)
⇒ nếu ID xuất hiện trong URL công khai, dùng v7 nội bộ + slug/hashid đối ngoại.

---

## CC-17. Đếm ở quy mô lớn: vì sao `UPDATE counter SET n = n + 1` là phản mẫu

**❓ Vấn đề gốc**: đếm view/like/tồn kho trên **một hàng** = mọi transaction xếp hàng trên đúng một
row lock. Throughput trần ≈ 1 / (thời gian giữ lock) — thường vài nghìn/s là kịch.

**⚙️ Bốn cách, tăng dần độ phức tạp:**
1. **Redis `INCR`** — nguyên tử, ~100k ops/s/node; flush xuống DB định kỳ. Mất vài giây dữ liệu nếu Redis chết.
2. **Sharded counter** — `N` hàng `counter_{id}_{bucket}`, ghi ngẫu nhiên bucket, đọc `SUM`. Giảm contention ~N lần.
3. **Append-only + rollup** — ghi sự kiện (`ViewLogged`), job gộp mỗi phút. Chính xác tuyệt đối, đọc qua bảng tổng hợp.
4. **Kafka partition theo key + 1 consumer** — biến ghi song song tranh chấp thành ghi tuần tự không tranh chấp.

**⚖️ Chọn theo yêu cầu chính xác**: lượt xem sai 0,1% không sao → (1).
Tồn kho/số dư sai 1 đơn vị là sự cố → (2) không đủ, phải (4) hoặc atomic có điều kiện (CC-3).

---

## CC-18. Leader election — chạy background job khi có N instance

**❓ Vấn đề gốc**: `BackgroundService` (ví dụ Outbox processor của project HW) chạy trên **mọi**
instance ⇒ 8 instance = 8 lần xử lý cùng một outbox row.

**⚙️ Ba cách:**
1. **Không cần leader** — job idempotent + `FOR UPDATE SKIP LOCKED` (CC-4). Mỗi worker lấy phần
   khác nhau ⇒ vừa an toàn vừa **scale ngang**. ✅ Ưu tiên cách này cho outbox.
2. **Lock có TTL + gia hạn** (Redis / DB advisory lock). Cần fencing token nếu job không idempotent (CC-6).
3. **Hạ tầng lo** — K8s `StatefulSet` replica = 1, Lease API, hoặc CronJob.

**💻 Mẫu 1 an toàn nhất cho Outbox — "claim rồi mới xử lý":**
```sql
UPDATE OutboxMessages
   SET LockedBy = @instanceId, LockedUntil = NOW() + INTERVAL 60 SECOND
 WHERE ProcessedOn IS NULL
   AND (LockedUntil IS NULL OR LockedUntil < NOW())
 ORDER BY OccurredOn LIMIT 20;      -- claim nhanh rồi COMMIT ⇒ không giữ transaction dài khi publish
```

**⚖️ Điểm ăn tiền**: nói được *"tôi không cần leader nếu job idempotent và có cơ chế claim —
leader election là công cụ cuối cùng vì bản thân nó tạo ra một điểm tuần tự chung (α trong USL)"*.

---

## CC-19. Sticky session, SignalR backplane và cái giá của state trong process

**❓ Vấn đề gốc**: scale ngang giả định instance **stateless**. Mọi state trong process
(session in-memory, cache in-memory, WebSocket connection) đều phá vỡ giả định đó.

**⚙️ Hệ quả cụ thể:**
- Session in-memory + round-robin LB ⇒ user "bị đăng xuất ngẫu nhiên". Sửa: JWT stateless hoặc
  distributed session (Redis).
- **Sticky session** vá được nhưng phá cân bằng tải, và khi instance chết thì mất hết session của nó.
- SignalR: connection nằm ở **1 instance**. Instance A không thể gửi cho client nối vào B
  ⇒ cần **backplane** (Redis pub/sub / Azure SignalR).
- In-memory cache: 8 instance = 8 bản cache **không đồng nhất** ⇒ user F5 thấy 2 kết quả khác nhau (CA-8).

**⚖️ Quy tắc**: *"State chỉ được nằm ở 3 nơi: DB (bền), cache phân tán (chia sẻ), hoặc trong process
nhưng CHẤP NHẬN ĐƯỢC việc không nhất quán (cache TTL ngắn, snapshot cấu hình)."*

---

## CC-20. Graceful shutdown & draining — mất request lúc deploy

**❓ Vấn đề gốc**: rolling deploy giết pod trong khi nó còn 30 request đang chạy và 1 message đã
nhận nhưng chưa ack ⇒ 502 cho user, message xử lý dở.

**⚙️ Trình tự đúng:**
```
1. K8s gửi SIGTERM + đồng thời gỡ pod khỏi Endpoints
   (hai việc này KHÔNG đồng bộ ⇒ cần preStop sleep 5–10s, nếu không vẫn mất request)
2. Readiness probe fail ⇒ LB ngừng gửi request MỚI
3. App ngừng nhận request mới, xử lý nốt request đang chạy (ShutdownTimeout)
4. BackgroundService nhận stoppingToken ⇒ dừng vòng lặp, nack/không ack message đang xử lý dở
5. Flush log/metric/trace, đóng connection, thoát
```

**💻**
```csharp
builder.Services.Configure<HostOptions>(o =>
{
    o.ShutdownTimeout = TimeSpan.FromSeconds(30);   // mặc định chỉ 5s — thường quá ngắn
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
});

public class OutboxProcessor(...) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessBatchAsync(stoppingToken);          // truyền token xuống TẬN cùng
            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }    // huỷ là bình thường, đừng log Error
        }
        // Không ack message chưa xử lý xong ⇒ broker giao lại (at-least-once)
    }
}
```

**⚖️ Đừng quên**: `terminationGracePeriodSeconds` của K8s phải **lớn hơn** `ShutdownTimeout`,
nếu không K8s SIGKILL trước khi app kịp dọn.

---

## CC-21. Đo tải cho đúng: p99, tail amplification, coordinated omission

**❓ Vấn đề gốc**: "trung bình 50ms" là con số vô nghĩa nhất trong đo hiệu năng.

**⚙️ Ba khái niệm phải nói được:**
1. **Tail latency amplification**: 1 request gọi 10 service song song, mỗi service p99 = 100ms
   ⇒ xác suất **ít nhất một** cái chậm = `1 − 0.99¹⁰ ≈ 9.6%` ⇒ p90 của request tổng ≈ p99 của
   service con. *Càng fan-out rộng, p99 của bạn càng bị chi phối bởi p99 của người khác.*
2. **Coordinated omission**: công cụ đo gửi request rồi *chờ* phản hồi mới gửi tiếp ⇒ khi hệ thống
   nghẽn, tool tự giảm tải ⇒ **giấu mất** phần latency tệ nhất. Phải dùng mô hình
   **open-loop / constant arrival rate** (k6, wrk2, Gatling), không phải closed-loop.
3. **Phần trăm không cộng được**: không có "p99 trung bình". Phải gộp **histogram** (HDR /
   `histogram_quantile` của Prometheus), không phải lấy trung bình các con số p99.

**💻 Kịch bản k6 đúng chuẩn:**
```js
export const options = {
  scenarios: { load: {
    executor: 'constant-arrival-rate',   // ⬅️ mấu chốt: giữ ĐỀU tốc độ tới, không phụ thuộc phản hồi
    rate: 2000, timeUnit: '1s', duration: '5m',
    preAllocatedVUs: 500, maxVUs: 3000,
  }},
  thresholds: { http_req_duration: ['p(99)<300'], http_req_failed: ['rate<0.01'] },
};
```

**⚖️ Quy trình test tải nên nói ra**: baseline → tăng dần tới khi p99 vỡ (**knee point**) →
xác định tài nguyên bão hoà → sửa → đo lại. Kèm **soak test** (2–4h, phát hiện leak) và
**spike test** (phát hiện thiếu backpressure).

---

## CC-22. Ba câu hỏi ngược kinh điển về concurrency (chuẩn bị sẵn)

**1. *"Hai user cùng đặt vé cuối cùng, bạn xử lý sao?"***
> Không dùng lock ứng dụng. Dùng nguyên tử ở DB:
> `UPDATE seats SET status='HELD', holder=@u WHERE id=@id AND status='FREE'`
> — `affected = 1` là thắng, `0` là thua. Rồi giữ chỗ có TTL bằng cột `HeldUntil` + job giải phóng.
> Không có khe hở nào giữa kiểm tra và ghi.

**2. *"Vì sao bạn không dùng `lock` C#?"***
> `lock` chỉ có phạm vi 1 process; 8 instance = 8 monitor khác nhau. Và ngay trong 1 process,
> giữ `lock` khi gọi I/O sẽ chặn ThreadPool. Ranh giới nguyên tử đúng của hệ phân tán là
> **transaction của DB** hoặc **lệnh nguyên tử của Redis**, không phải monitor của CLR.

**3. *"Hệ thống chịu được bao nhiêu req/s?"***
> Trả lời bằng phương pháp, không bằng con số: xác định tài nguyên bão hoà trước (thường là DB
> connection hoặc row lock, hiếm khi là CPU), tính bằng Little's Law, đo bằng open-loop load test
> tới knee point, và nêu rõ **hành vi khi quá tải** (429 + `Retry-After`, không phải timeout 30s).

---

## ✅ Checklist tự kiểm tra — Phần 15

- [ ] Viết công thức Little's Law và dùng nó để tính số connection cần thiết.
- [ ] Nêu 4 cách chống lost update và biết khi nào dùng cái nào.
- [ ] Giải thích vì sao Redis lock không đảm bảo correctness, và fencing token sửa được gì.
- [ ] Thiết kế bảng idempotency key + giải thích vai trò của unique constraint.
- [ ] So sánh token bucket vs sliding window; viết được rate limiter phân tán bằng Lua.
- [ ] Phân biệt backpressure vs load shedding; giải thích vì sao unbounded queue là bug.
- [ ] Chẩn đoán 3 kiểu cạn tài nguyên: DB pool, ThreadPool, SNAT port.
- [ ] Nêu 3 cách xử lý hot key / hot partition.
- [ ] Sắp đúng thứ tự timeout → retry → circuit breaker → attempt timeout và giải thích vì sao.
- [ ] Giải thích coordinated omission và tail latency amplification.

⬅️ Quay lại [Mục lục](interview.NET.md) | Tiếp: [Phần 16 — Transaction & Consistency ➡️](interview.NET.16-Transaction-Consistency.md)
