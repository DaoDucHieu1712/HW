# Phần 16 — Transaction & Consistency: từ ACID tới Saga

[⬅️ Phần 15 — Concurrency & Scaling](interview.NET.15-Concurrency-Scaling.md) | [⬅️ Về mục lục](interview.NET.md) | Tiếp theo: [Phần 17 — Caching ➡️](interview.NET.17-Caching-Performance.md)

> Khung mỗi câu: **❓ Vấn đề gốc → ⚙️ Cơ chế bên dưới → 💻 Code/đo đạc → ⚖️ Hệ quả thực chiến**.
>
> File 10 (SQL) và 08 (EF Core) trả lời *"transaction là gì, dùng thế nào"*.
> File này trả lời *"bên dưới InnoDB/Postgres làm gì để có ACID, vì sao isolation level ảnh hưởng
> throughput, và khi nghiệp vụ vượt ra khỏi 1 database thì lấy gì thay transaction"*.

---

## 🗺️ Bản đồ: tính nhất quán ở 3 tầng

```
┌── Tầng 1: MỘT database ────────────────────────────────────────┐
│  ACID thật.  Công cụ: transaction, isolation level, lock, MVCC │
│  Giới hạn: chỉ trong phạm vi 1 connection / 1 DB                │
└────────────────────────────────────────────────────────────────┘
                    │ khi nghiệp vụ vượt ra ngoài 1 DB
                    ▼
┌── Tầng 2: DB + hệ thống ngoài (broker, cache, API) ────────────┐
│  KHÔNG có atomic. Công cụ: Outbox / Inbox / CDC                │
│  Bảo đảm: at-least-once + idempotent = "exactly-once hiệu dụng"│
└────────────────────────────────────────────────────────────────┘
                    │ khi nghiệp vụ trải qua nhiều service
                    ▼
┌── Tầng 3: nhiều service ───────────────────────────────────────┐
│  KHÔNG có rollback toàn cục. Công cụ: Saga + compensation      │
│  Bảo đảm: eventual consistency + semantic lock                 │
└────────────────────────────────────────────────────────────────┘
```

**Câu chốt phỏng vấn:** *"Càng ra xa khỏi một database, bạn càng phải đánh đổi 'atomic' lấy
'eventual + idempotent'. Kỹ năng ở đây không phải là cố giữ ACID bằng mọi giá (2PC), mà là
**thiết kế nghiệp vụ chấp nhận được trạng thái trung gian** và luôn hội tụ về đúng."*

---

## TX-1. ACID thực sự được cài đặt bằng gì?

**⚙️ Cơ chế từng chữ:**

| Chữ | Cài đặt bên dưới (InnoDB) | Hệ quả bạn thấy được |
|---|---|---|
| **A**tomicity | **undo log** — ghi ảnh cũ trước khi sửa; rollback = áp ngược undo | rollback chậm hơn commit rất nhiều |
| **C**onsistency | ràng buộc (PK/FK/UNIQUE/CHECK) + logic app | đây là phần **ứng dụng** chịu trách nhiệm, không phải DB |
| **I**solation | **lock + MVCC** (undo log dùng lại để dựng snapshot) | isolation cao = throughput thấp |
| **D**urability | **redo log (WAL)** + `fsync` khi commit | `innodb_flush_log_at_trx_commit` quyết định "bền tới đâu" |

**⚙️ Đường đi của một COMMIT (rất hay bị hỏi khoan):**
```
1. Sửa page trong buffer pool (RAM)         ← nhanh
2. Ghi undo log (để rollback / dựng MVCC snapshot)
3. Ghi redo log vào log buffer
4. COMMIT → flush redo log ra đĩa + fsync   ← ĐÂY là chỗ tốn (Write-Ahead Logging)
5. Trả OK cho client
6. Page bẩn được ghi xuống data file SAU (background flush / checkpoint)
```
Điểm cốt lõi: **durability đến từ redo log tuần tự, không phải từ việc ghi data file**.
Ghi tuần tự nhanh hơn ghi ngẫu nhiên hàng chục lần → đó là lý do mọi DB đều có WAL.

**💻 Ba mức đánh đổi durability (biết cái này = biết vì sao DB "chậm"):**
```ini
innodb_flush_log_at_trx_commit = 1   # fsync mỗi commit — ACID đầy đủ (mặc định)
                               = 2   # ghi OS cache, fsync mỗi giây — mất ≤1s khi máy sập, OS crash-safe
                               = 0   # 1s ghi + fsync — nhanh nhất, mất dữ liệu khi process chết
sync_binlog = 1                      # cần cho replication an toàn
```
**Group commit**: nhiều transaction commit gần nhau được gom vào **một** `fsync` ⇒ throughput commit
tăng theo tải. Đây là lý do "commit 1.000 transaction nhỏ" không tệ gấp 1.000 lần "commit 1 lần".

**⚖️ Hệ quả**: nếu commit là cổ chai, giải pháp thường là **gom batch** (ít transaction hơn),
không phải "tăng CPU". Ngược lại batch quá to lại giữ lock lâu (TX-6) — luôn có điểm cân bằng
(thường 100–1.000 row/transaction).

---

## TX-2. Isolation level và các dị thường — bảng phải thuộc lòng

**⚙️ Dị thường (anomaly) theo mức isolation:**

| Anomaly | Read Uncommitted | Read Committed | Repeatable Read | Serializable |
|---|---|---|---|---|
| Dirty read (đọc dữ liệu chưa commit) | ❌ có | ✅ không | ✅ không | ✅ không |
| Non-repeatable read (đọc 2 lần khác nhau) | ❌ có | ❌ có | ✅ không | ✅ không |
| Phantom read (xuất hiện row mới) | ❌ có | ❌ có | ✅ InnoDB chặn (gap lock)<br>❌ chuẩn ANSI vẫn có | ✅ không |
| **Lost update** | ❌ | ❌ | ❌ *(vẫn xảy ra!)* | ✅ |
| **Write skew** | ❌ | ❌ | ❌ *(vẫn xảy ra!)* | ✅ |

**Hai hàng cuối là chỗ phân biệt ứng viên senior.** Snapshot isolation (RR của InnoDB, RC/RR của
Postgres) **không** chống được lost update và write skew — vì cả hai transaction đều đọc snapshot
hợp lệ rồi ghi vào **các row khác nhau**.

**💻 Write skew — ví dụ kinh điển "phải luôn có ≥1 bác sĩ trực":**
```sql
-- T1                                        -- T2 (đồng thời)
SELECT COUNT(*) FROM oncall WHERE on=1;      SELECT COUNT(*) FROM oncall WHERE on=1;
-- = 2, ok, mình xin nghỉ được                -- = 2, ok, mình xin nghỉ được
UPDATE oncall SET on=0 WHERE id='alice';     UPDATE oncall SET on=0 WHERE id='bob';
COMMIT;                                       COMMIT;
-- Kết quả: 0 bác sĩ trực. Cả hai transaction đều "hợp lệ" theo snapshot isolation.
```
**Cách sửa** (theo thứ tự ưu tiên):
1. **Ràng buộc trong DB** (`CHECK`, unique index trên "materialized conflict") — mạnh nhất.
2. `SELECT ... FOR UPDATE` trên các row liên quan (biến đọc thành khoá).
3. `SERIALIZABLE` (Postgres SSI sẽ abort 1 transaction ⇒ app phải retry).
4. Gom quyết định về một nơi tuần tự (1 partition, 1 aggregate).

**⚖️ Mặc định thực tế**: MySQL/InnoDB = **REPEATABLE READ**; PostgreSQL & SQL Server = **READ COMMITTED**.
Khác biệt này gây bug khi port code giữa 2 DB — phải nêu được khi phỏng vấn.

---

## TX-3. MVCC — vì sao "đọc không chặn ghi"

**❓ Vấn đề gốc**: nếu reader phải khoá row thì hệ thống đọc-nhiều sẽ đứng hình.

**⚙️ Cơ chế MVCC (multi-version concurrency control)**: mỗi row có nhiều **phiên bản**;
mỗi transaction đọc phiên bản phù hợp với "thời điểm" của mình.

| DB | Lưu version ở đâu | "Thời điểm" xác định bằng | Chi phí dọn |
|---|---|---|---|
| **InnoDB** | undo log (rollback segment) | `read view` = tập trx_id đang mở | purge thread; undo phình nếu có transaction dài |
| **PostgreSQL** | ngay trong heap (tuple cũ nằm cùng bảng) + `xmin`/`xmax` | snapshot xmin/xmax | **VACUUM**; bloat bảng nếu vacuum không kịp |
| **SQL Server** (RCSI/SI) | **tempdb** version store | timestamp | tempdb phình |

**⚙️ Hệ quả chung, rất hay bị hỏi:** *"một transaction đọc mở 30 phút gây hại gì?"*
→ Nó **giữ snapshot cũ** ⇒ DB **không được phép** dọn các version cũ hơn ⇒
undo log / bloat / tempdb phình ⇒ toàn hệ thống chậm dần. Ở Postgres còn chặn `VACUUM` freeze
⇒ nguy cơ transaction-ID wraparound.

**💻 Phát hiện:**
```sql
-- MySQL: transaction đang mở lâu nhất
SELECT trx_id, trx_started, TIMESTAMPDIFF(SECOND, trx_started, NOW()) AS secs, trx_query
  FROM information_schema.innodb_trx ORDER BY trx_started;

-- PostgreSQL
SELECT pid, state, now() - xact_start AS age, query
  FROM pg_stat_activity WHERE xact_start IS NOT NULL ORDER BY age DESC;
```

**⚖️ Quy tắc EF Core**: `DbContext` sống theo **scope của request**. Nếu bạn `BeginTransaction`
rồi đi gọi HTTP 3 giây, bạn vừa giữ một snapshot MVCC + row lock trong 3 giây. Đây là lỗi
nghiêm trọng nhất mà mọi hệ thống "chậm không rõ lý do" đều mắc.

---

## TX-4. Lock của InnoDB: record, gap, next-key — nguồn gốc của deadlock kỳ lạ

**⚙️ Bốn loại lock phải phân biệt:**

| Loại | Khoá cái gì | Khi nào xuất hiện |
|---|---|---|
| **Record lock** | đúng một index record | `UPDATE ... WHERE id = 5` (có index unique) |
| **Gap lock** | khoảng trống *giữa* 2 index record | REPEATABLE READ, khoá phạm vi để chặn phantom |
| **Next-key lock** | record + gap phía trước nó | mặc định của InnoDB ở RR khi quét phạm vi |
| **Insert intention** | ý định chèn vào một gap | `INSERT` — xung đột với gap lock của người khác |

**💻 Deadlock kinh điển do gap lock (2 INSERT không hề đụng nhau vẫn deadlock):**
```sql
-- Bảng có UNIQUE(email). T1 và T2 insert 2 email KHÁC nhau nhưng rơi vào cùng một gap.
-- T1: INSERT ... 'b@x.com'   → giữ insert intention trong gap (a, c)
-- T2: INSERT ... 'bb@x.com'  → cùng gap → chờ
-- Cộng thêm một SELECT ... FOR UPDATE ở giữa là đủ tạo chu trình chờ → DEADLOCK
```
**Bốn nguyên nhân deadlock hay gặp nhất:**
1. Hai transaction cập nhật cùng tập row nhưng **theo thứ tự khác nhau** → luôn sắp xếp
   (`ORDER BY id`) trước khi update nhiều row.
2. Nâng cấp lock: `SELECT` (S-lock) rồi `UPDATE` (X-lock) → dùng thẳng `FOR UPDATE` ngay từ đầu.
3. Gap lock ở RR với secondary index → cân nhắc `READ COMMITTED` (Postgres/MySQL RC không có gap lock).
4. Quét thiếu index ⇒ khoá **toàn bộ** row đã quét (không phải chỉ row khớp) → thêm index.

**💻 Chẩn đoán & xử lý:**
```sql
SHOW ENGINE INNODB STATUS;         -- mục LATEST DETECTED DEADLOCK: 2 transaction, lock nào, câu SQL nào
SET innodb_lock_wait_timeout = 5;  -- fail fast thay vì chờ 50s
```
```csharp
// Deadlock là lỗi TẠM THỜI ⇒ retry được, nhưng phải retry NGUYÊN CẢ transaction
optionsBuilder.UseMySql(cs, sv, o => o.EnableRetryOnFailure(
    maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(2), errorNumbersToAdd: null));
```

**⚖️ Bẫy EF Core cực hay bị hỏi**: khi bật `EnableRetryOnFailure` mà bạn tự
`BeginTransaction()` → EF ném `InvalidOperationException` ("configured to use retrying execution
strategy…"). Phải bọc bằng execution strategy:
```csharp
var strategy = _context.Database.CreateExecutionStrategy();
await strategy.ExecuteAsync(async () =>
{
    await using var tx = await _context.Database.BeginTransactionAsync(ct);
    ...                                  // toàn bộ transaction phải nằm TRONG delegate
    await tx.CommitAsync(ct);
});
```
Lý do: retry phải chạy lại **cả** transaction, không thể chỉ chạy lại một câu lệnh giữa chừng.

---

## TX-5. Ranh giới transaction trong ứng dụng — `TransactionBehavior` của project HW

**❓ Vấn đề gốc**: đặt `BeginTransaction` ở đâu? Trong controller? Trong handler? Trong repository?

**⚙️ Nguyên tắc**: **một use case = một transaction = một aggregate được sửa**.
Trong project HW, `TransactionBehavior` bọc mọi `IBaseCommand`, gọi `IUnitOfWork.ExecuteAsync`:

```csharp
public sealed class TransactionBehavior<TReq, TRes>(IUnitOfWork uow) : IPipelineBehavior<TReq, TRes>
    where TReq : IBaseCommand
{
    public async Task<TRes> Handle(TReq req, RequestHandlerDelegate<TRes> next, CancellationToken ct)
    {
        TRes response = default!;
        await uow.ExecuteAsync(async () => { response = await next(); });  // BEGIN → next() → COMMIT
        return response;
    }
}
```
Vì thế **handler không được có `_uow`** (rule trong CLAUDE.md) — nếu handler tự commit thì
transaction bị chia đôi và tính nguyên tử biến mất.

**⚖️ Bốn quy tắc vàng cho ranh giới transaction:**
1. **Không I/O ngoài trong transaction** — không gọi HTTP, không `PublishAsync` trực tiếp tới broker,
   không gửi email. Chúng không rollback được, và làm transaction dài (TX-3).
2. **Query (`IQuery<T>`) không cần transaction** — chỉ command mới cần. Một `SELECT` đơn lẻ đã
   nguyên tử sẵn.
3. **Validation trước transaction** — `ValidationBehavior` đứng trước `TransactionBehavior` để
   không mở transaction rồi rollback vì thiếu field.
4. **Một transaction chỉ sửa một aggregate** (nguyên tắc DDD). Muốn sửa nhiều aggregate ⇒ domain
   event + eventual consistency (TX-12), không phải transaction to hơn.

---

## TX-6. Transaction dài — kẻ giết hệ thống thầm lặng

**❓ Vấn đề gốc**: transaction 5 giây trông vô hại trên máy dev nhưng giết production.

**⚙️ Nó giữ đồng thời 5 tài nguyên khan hiếm:**
1. **1 DB connection** trong pool (100 cái) — CC-10.
2. **Row/gap lock** — mọi transaction khác đụng row đó phải chờ.
3. **Snapshot MVCC** — chặn purge/vacuum toàn cục (TX-3).
4. **Undo/redo chưa được dọn** — đĩa phình.
5. **Replication lag** — replica chỉ áp dụng được sau khi primary commit ⇒ transaction lớn tạo
   một "cục" binlog làm replica tụt lại (và làm hỏng read-after-write, TX-16).

**💻 Chống bằng thiết kế, không chỉ bằng lời hứa:**
```csharp
// ❌ SAI: gọi API trong transaction
await uow.ExecuteAsync(async () =>
{
    order.Confirm();
    await _paymentApi.ChargeAsync(...);       // 2s, không rollback được, giữ lock 2s
});

// ✅ ĐÚNG: transaction chỉ ghi DB + ghi outbox; việc ngoài do processor làm sau khi commit
await uow.ExecuteAsync(async () =>
{
    order.Confirm();                          // raise OrderConfirmedDomainEvent
    // ExecuteAsync tự chuyển domain event → outbox row TRONG cùng transaction
});
```

**💻 Batch lớn thì chia nhỏ (chunking):**
```csharp
const int ChunkSize = 500;
foreach (var chunk in items.Chunk(ChunkSize))
{
    await uow.ExecuteAsync(async () => { /* xử lý 500 dòng */ });   // mỗi chunk 1 transaction ngắn
    ct.ThrowIfCancellationRequested();
}
// Đánh đổi: mất tính nguyên tử toàn cục ⇒ job PHẢI idempotent + có checkpoint để chạy lại được
```

**⚖️ Ngưỡng thực tế**: đặt cảnh báo cho transaction > 1s và cho `innodb_trx` mở > 5s.
*"Transaction là tài nguyên toàn cục, không phải phạm vi cục bộ của bạn."*

---

## TX-7. Dual write problem — vì sao "lưu DB rồi publish message" luôn sai

**❓ Vấn đề gốc**: đây là câu hỏi **quan trọng nhất** của phần messaging + transaction.

```csharp
await _context.SaveChangesAsync(ct);          // (1) COMMIT thành công
await _bus.PublishAsync(new OrderCreated());  // (2) app chết ngay đây ⇒ message KHÔNG BAO GIỜ được gửi
```
Đảo thứ tự cũng sai:
```csharp
await _bus.PublishAsync(new OrderCreated());  // (1) gửi rồi
await _context.SaveChangesAsync(ct);          // (2) rollback ⇒ MESSAGE MA: consumer xử lý đơn không tồn tại
```

**⚙️ Vì sao không có cách thứ 3?** Vì DB và broker là **2 hệ thống khác nhau**, không có commit
chung. Chỉ có 2 lối thoát: (a) 2PC/XA — chậm và dễ kẹt (TX-9); (b) **Outbox** — chuẩn công nghiệp.

**⚙️ Outbox pattern**: ghi message vào **một bảng trong chính DB đó**, cùng transaction với nghiệp vụ.
Một process riêng đọc bảng và publish.

```
┌── Transaction (nguyên tử) ──┐
│ INSERT INTO Orders ...      │
│ INSERT INTO OutboxMessages  │  ← message chỉ là một dòng dữ liệu bình thường
└── COMMIT ───────────────────┘
          │  (bất đồng bộ, ≤ ~10s trong project HW)
          ▼
   OutboxProcessor  → publish tới broker → đánh dấu ProcessedOn
```

**💻 Bảng outbox (đúng như project HW đang dùng):**
```sql
CREATE TABLE OutboxMessages (
  Id          CHAR(36) PRIMARY KEY,
  Type        VARCHAR(300) NOT NULL,     -- AssemblyQualifiedName để deserialize
  Content     JSON         NOT NULL,     -- Newtonsoft với TypeNameHandling
  OccurredOn  DATETIME(6)  NOT NULL,
  ProcessedOn DATETIME(6)  NULL,
  Error       TEXT         NULL,
  RetryCount  INT          NOT NULL DEFAULT 0,
  INDEX ix_unprocessed (ProcessedOn, OccurredOn)   -- index chỉ phục vụ processor
);
```

**⚖️ Ba hệ quả bắt buộc phải nói ra:**
1. **At-least-once**: processor có thể publish rồi chết trước khi đánh dấu ⇒ gửi lại ⇒
   **consumer phải idempotent** (TX-11).
2. **Có độ trễ**: message tới sau commit vài giây ⇒ nghiệp vụ phải chấp nhận eventual consistency.
3. **Publish từ nơi không `SaveChanges` sẽ mất im lặng** — đúng như rule trong CLAUDE.md:
   *"A publish from a path that never calls SaveChanges is silently discarded."*

---

## TX-8. Outbox: polling vs CDC, và bài toán thứ tự

**⚙️ Hai cách đọc outbox:**

| | **Polling** (project HW) | **CDC / Debezium** (đọc binlog) |
|---|---|---|
| Cơ chế | `SELECT ... WHERE ProcessedOn IS NULL` mỗi N giây | đọc redo/binlog của DB, stream sang Kafka |
| Độ trễ | 1–10s | ~ms |
| Tải DB | thêm query định kỳ (có index thì rẻ) | gần như 0 (đọc log, không đọc bảng) |
| Vận hành | đơn giản, không thêm hạ tầng | thêm Kafka Connect + quản lý offset/schema |
| Thứ tự | theo `OccurredOn` + `LIMIT` | **đúng thứ tự commit** tự nhiên |

**💻 Polling đúng cách (kết hợp CC-18):**
```csharp
// 1) Claim bằng SKIP LOCKED ⇒ nhiều instance chạy song song an toàn
// 2) Publish NGOÀI transaction claim ⇒ không giữ lock khi gọi broker
// 3) Đánh dấu ProcessedOn sau khi broker ack
// 4) Backoff luỹ thừa theo RetryCount; quá N lần ⇒ chuyển sang bảng OutboxDeadLetter + cảnh báo
```

**⚙️ Thứ tự message — cái bẫy tinh vi**: hai outbox row cùng aggregate có thể bị 2 worker publish
song song ⇒ tới broker **sai thứ tự** (`OrderUpdated` trước `OrderCreated`).
Ba cách xử lý:
1. **Không cần thứ tự** — thiết kế message tự chứa đủ dữ liệu + có `Version`; consumer bỏ qua
   message cũ hơn version đã thấy. ✅ Đơn giản và bền nhất.
2. **Thứ tự theo aggregate** — publish nối tiếp trong phạm vi một `AggregateId` (partition key =
   AggregateId, xem MQ-6).
3. **Một worker duy nhất** — đơn giản nhưng mất khả năng scale (CC-18).

**⚖️ Dọn dẹp**: bảng outbox phải có job xoá row đã xử lý (giữ 3–7 ngày để điều tra sự cố).
Bảng outbox 50 triệu dòng không được dọn là một sự cố production kinh điển.

---

## TX-9. 2PC / XA — nó hoạt động ra sao và vì sao gần như không ai dùng nữa

**⚙️ Two-phase commit:**
```
Coordinator                     Participant A        Participant B
    │── PREPARE ──────────────────►│                     │
    │◄─ YES (đã ghi log, ĐÃ KHOÁ) ─│                     │
    │── PREPARE ─────────────────────────────────────────►│
    │◄─ YES ─────────────────────────────────────────────│
    │── COMMIT ───────────────────►│────────────────────►│
```

**⚖️ Vì sao bị loại bỏ trong hệ thống chịu tải cao:**
1. **Blocking**: nếu coordinator chết sau PREPARE, participant giữ lock **vô thời hạn**
   (in-doubt transaction) — phải can thiệp tay.
2. **Latency**: 2 vòng mạng + 2 lần fsync ⇒ transaction dài ra ⇒ TX-6.
3. **Khả dụng nhân lên**: cần **tất cả** participant sống ⇒ `0.99 × 0.99 × 0.99` giảm dần.
4. Broker hiện đại (Kafka, RabbitMQ) **không hỗ trợ XA** một cách thực dụng.

**⚖️ Khi nào vẫn hợp lý**: 2 database quan hệ trong cùng một trung tâm dữ liệu, tần suất thấp,
nghiệp vụ tài chính bắt buộc. Còn lại: dùng Outbox + Saga.
Biến thể nhẹ hơn: **TCC (Try–Confirm–Cancel)** — "Try" đặt chỗ (giữ tồn kho tạm), "Confirm" chốt,
"Cancel" nhả; về bản chất là saga có semantic lock rõ ràng.

---

## TX-10. Saga — thay rollback bằng compensation

**❓ Vấn đề gốc**: đặt hàng = trừ kho (service A) + trừ tiền (service B) + tạo vận đơn (service C).
Không có transaction chung. Bước 3 lỗi thì bước 1, 2 phải "hoàn tác" bằng **nghiệp vụ**, không phải
bằng `ROLLBACK`.

**⚙️ Hai kiểu:**

| | **Orchestration** (project HW dùng) | **Choreography** |
|---|---|---|
| Điều phối | một saga giữ state, ra lệnh từng bước | mỗi service nghe event và tự phản ứng |
| Ưu | thấy rõ toàn bộ luồng, dễ debug, dễ compensate | ít khớp nối, không có điểm trung tâm |
| Nhược | orchestrator là điểm phụ thuộc | luồng "vô hình", 5 service là không ai hiểu nổi |
| Nên dùng | ≥ 3 bước, có compensation | 2 bước đơn giản, kiểu thông báo |

**⚙️ Compensation không phải rollback:**
- `ROLLBACK` xoá sạch dấu vết. **Compensation là một giao dịch nghiệp vụ mới**: đã trừ tiền thì
  hoàn tiền (có dòng "REFUND" trong sổ), đã gửi email thì gửi email xin lỗi — không xoá được.
- Compensation phải **idempotent** và **không được phép thất bại vĩnh viễn** (retry mãi, cuối cùng
  đưa vào hàng đợi can thiệp tay).
- Có bước **không compensate được** (đã giao hàng) ⇒ đặt các bước đó **cuối cùng** trong saga
  (nguyên tắc: *pivot transaction* — sau điểm này chỉ tiến, không lùi).

**💻 Khung `EventSourcedSaga` theo rule của project HW:**
```csharp
public sealed class OrderSaga : EventSourcedSaga
{
    public OrderStep Step { get; private set; }

    // Apply() là fold THUẦN: không đồng hồ, không sinh id, không Send
    protected override void Apply(ISagaEvent e) => Step = e switch
    {
        OrderSagaStarted     => OrderStep.StockReserving,
        StockReserved        => OrderStep.Paying,
        PaymentSucceeded     => OrderStep.Shipping,
        PaymentFailed        => OrderStep.CompensatingStock,
        StockReleased        => OrderStep.Failed,
        _                    => Step
    };

    // Decision method: nơi DUY NHẤT được Raise / Send, luôn guard theo step hiện tại
    public void OnPaymentFailed(string reason)
    {
        if (Step != OrderStep.Paying) return;          // ⬅️ chống message trùng / tới muộn
        Raise(new PaymentFailed(reason));
        Send(new ReleaseStockCommand(OrderId));        // bước bù trừ
    }
}
```

**⚖️ Dị thường của saga (bắt buộc nêu khi phỏng vấn)**: saga **không có isolation**.
Giữa các bước, dữ liệu ở trạng thái trung gian mà người khác **nhìn thấy được** ⇒ sinh ra
lost update và "dirty read" ở mức nghiệp vụ. Biện pháp:
- **Semantic lock**: cột trạng thái `PENDING` để nơi khác biết bản ghi đang trong saga.
- **Commutative updates**: thiết kế thao tác hoán vị được (cộng/trừ thay vì gán).
- **Reread value**: đọc lại và kiểm tra trước khi ghi (giống optimistic).

---

## TX-11. Exactly-once là ảo tưởng — "at-least-once + idempotent" mới là sự thật

**⚙️ Chứng minh ngắn**: consumer xử lý xong nhưng chết **trước khi** ack ⇒ broker giao lại.
Consumer ack **trước khi** xử lý rồi chết ⇒ mất message. Không có thứ tự nào giữa "xử lý" và "ack"
cho ra exactly-once, vì hai việc đó ở hai hệ thống khác nhau (lại là dual write, TX-7).

**⚙️ Điều đạt được**: **effectively-once** = at-least-once + **idempotent consumer**.

**💻 Inbox pattern — dedup trong cùng transaction với tác động nghiệp vụ:**
```csharp
public async Task HandleAsync(OrderCreatedMessage msg, CancellationToken ct)
{
    await _uow.ExecuteAsync(async () =>
    {
        // 1) Chốt chặn dedup: PK trùng ⇒ đã xử lý rồi ⇒ bỏ qua
        _context.InboxMessages.Add(new InboxMessage(msg.MessageId, msg.Type, DateTimeOffset.UtcNow));
        try { await _context.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.IsUniqueViolation()) { return; }   // duplicate

        // 2) Tác động nghiệp vụ — CÙNG transaction với bản ghi dedup
        await ProcessAsync(msg, ct);
    });
}
```
Điểm mấu chốt: bản ghi dedup và tác động nghiệp vụ **phải nằm trong cùng một transaction**.
Nếu dedup ghi vào Redis còn nghiệp vụ ghi vào DB thì bạn lại tạo ra một dual write mới.

**⚙️ Ba mức idempotent, từ tốt xuống xấu:**
1. **Tự nhiên idempotent** — `SET status = 'PAID'` (gán, không cộng dồn). Tốt nhất, không cần bảng gì.
2. **Idempotent theo khoá nghiệp vụ** — `INSERT ... ON DUPLICATE KEY UPDATE` với unique key.
3. **Bảng inbox/dedup** — dùng khi tác động không thể tự idempotent (cộng tiền, gửi email).

**⚖️ Về "Kafka exactly-once (EOS)"**: nó chỉ đúng **trong phạm vi Kafka** (đọc topic → ghi topic +
commit offset trong transaction của Kafka). Khi sink là MySQL hay một API bên ngoài,
bạn vẫn cần idempotent ở phía sink. Trả lời được ý này là điểm cộng lớn.

---

## TX-12. Domain event vs Integration event — hai thứ khác nhau, hay bị nhầm

| | **Domain event** | **Integration event** |
|---|---|---|
| Phạm vi | trong một service/bounded context | giữa các service |
| Đi qua | MediatR (in-process) | broker (RabbitMQ/Kafka) |
| Thời điểm | trong hoặc ngay sau transaction | sau khi commit (qua outbox) |
| Nội dung | giàu, dùng chính domain object | **hợp đồng ổn định**, chỉ dữ liệu nguyên thuỷ |
| Đổi tên/xoá | tự do trong service | ❌ **cấm** — là API công khai |

Trong project HW, `OutboxMessage` route theo kiểu payload: `IDomainEvent` → MediatR,
`[Message("topic")]` → broker. Đây chính là ranh giới trên.

**⚖️ Sai lầm kinh điển**: publish thẳng entity EF ra broker. Consumer bên ngoài sẽ phụ thuộc vào
**cấu trúc bảng của bạn** ⇒ bạn không đổi được schema nữa. Integration event phải là một DTO
được thiết kế riêng, có version (MQ-10).

---

## TX-13. Event sourcing — trạng thái là kết quả của một chuỗi sự kiện

**❓ Vấn đề gốc**: bảng quan hệ chỉ lưu **trạng thái hiện tại**; bạn mất lịch sử "vì sao đến đây",
và với saga phân tán thì "trạng thái hiện tại" rất khó khôi phục sau sự cố.

**⚙️ Cơ chế**: chỉ ghi thêm (append-only) các event; state = `fold(Apply, events)`.
- **Concurrency**: unique `(StreamId, Version)` ⇒ 2 writer cùng ghi version 5 thì một người thua
  ⇒ đọc lại + thử lại. Đây là optimistic concurrency ở dạng thuần khiết nhất.
- **Snapshot**: stream dài thì lưu ảnh chụp mỗi N event để không phải replay từ đầu.
- **Projection**: dựng bảng đọc (read model) từ stream ⇒ đây chính là CQRS thực thụ.

```sql
CREATE TABLE SagaEvents (
  StreamId  CHAR(36)     NOT NULL,
  Version   INT          NOT NULL,
  Type      VARCHAR(200) NOT NULL,
  Payload   JSON         NOT NULL,
  CreatedAt DATETIME(6)  NOT NULL,
  PRIMARY KEY (StreamId, Version)     -- ⬅️ khoá chống ghi đồng thời
);
```

**⚖️ Ba luật bất di bất dịch** (khớp với CLAUDE.md):
1. **Không bao giờ xoá hoặc đổi tên một `ISagaEvent`** — stream được replay mãi mãi.
   Cần đổi thì thêm `V2` và giữ upcaster từ V1.
2. **`Apply()` phải thuần** — không `DateTime.Now`, không `Guid.NewGuid()`, không `Send`.
   Nếu không, replay hôm nay sẽ ra kết quả khác hôm qua.
3. **Không đặt logic quyết định trong `Apply()`** — quyết định (`Raise`/`Send`) chỉ ở decision method.

**⚖️ Trade-off**: event sourcing đắt (đọc phức tạp, cần projection, khó truy vấn ad-hoc).
Chỉ dùng cho phần **có giá trị lịch sử/kiểm toán** (saga, ví tiền, workflow), không dùng cho toàn bộ CRUD.

---

## TX-14. CAP và PACELC — nói cho đúng chứ đừng nói thuộc lòng

**⚙️ CAP**: khi có **network partition (P)**, phải chọn giữa **Consistency** và **Availability**.
Không phải "chọn 2 trong 3" — P là điều kiện bắt buộc trong hệ phân tán, nên thực chất chỉ là
**CP hay AP khi có sự cố mạng**.

**⚙️ PACELC** đầy đủ hơn: *if **P**artition then **A** or **C**, **E**lse then **L**atency or **C**onsistency*.
Nghĩa là: ngay cả **khi không có sự cố**, bạn vẫn phải chọn giữa độ trễ và tính nhất quán.

| Hệ | Khi partition | Khi bình thường |
|---|---|---|
| MySQL/Postgres single primary | CP (mất khả năng ghi) | C hơn L |
| MySQL + async replica đọc | vẫn đọc được (AP cho đọc) | **L hơn C** ⇒ đọc dữ liệu cũ |
| Redis (async replication) | AP | L |
| etcd / ZooKeeper (Raft) | CP | C |
| Kafka `acks=all` + `min.insync.replicas=2` | CP cho ghi | C |

**⚖️ Câu trả lời ghi điểm**: *"Chọn CP hay AP là quyết định **theo từng use case**, không phải theo
từng hệ thống. Trong cùng một app: số dư ví = CP (thà báo lỗi còn hơn sai), còn danh sách sản phẩm
= AP (trả dữ liệu cũ vài giây vẫn tốt hơn trả lỗi)."*

---

## TX-15. Read replica và bài toán read-after-write

**❓ Vấn đề gốc**: tách đọc sang replica để giảm tải primary. Nhưng replication là **bất đồng bộ**
⇒ user vừa `POST` xong, `GET` lại **không thấy dữ liệu của chính mình** — bug bị báo nhiều nhất
khi mới tách replica.

**⚙️ Bốn cách xử lý, theo thứ tự nên dùng:**
1. **Sticky-to-primary sau khi ghi** — sau mỗi write, ghi cookie/flag ("dùng primary trong 5s")
   cho chính user đó. Đơn giản, hiệu quả nhất.
2. **Đọc theo LSN/GTID** — client giữ vị trí replication của lần ghi cuối; nếu replica chưa tới đó
   thì chờ hoặc rơi về primary (`WAIT_FOR_EXECUTED_GTID_SET` của MySQL).
3. **Trả về dữ liệu đã ghi** — `POST` trả luôn resource vừa tạo, UI không cần `GET` lại. Rẻ nhất.
4. **Chấp nhận** — cho các màn hình không quan trọng (thống kê, danh sách chung).

**💻 Định tuyến trong project HW (query dùng replica, command dùng primary):**
```csharp
// Query handler: đánh dấu đọc từ replica
public sealed class GetBlogsQueryHandler(IReadDbContext read) : IQueryHandler<GetBlogsQuery, PagedResult<BlogDto>>
{ /* read context trỏ tới connection string của replica */ }

// Command handler: LUÔN dùng primary (mọi write + mọi đọc phục vụ quyết định ghi)
```
Cảnh báo cần bật: `Seconds_Behind_Master` / `pg_last_xact_replay_timestamp`. Lag > 5s thì
**tự động rơi về primary** thay vì phục vụ dữ liệu quá cũ.

**⚖️ Ba mức consistency nên biết tên gọi**: *read-your-writes* (thấy được cái mình vừa ghi),
*monotonic reads* (không bao giờ thấy dữ liệu lùi về quá khứ — vi phạm khi 2 request rơi vào 2
replica lệch nhau ⇒ sửa bằng sticky theo user), *consistent prefix* (không thấy hệ quả trước nguyên nhân).

---

## TX-16. `SaveChanges` bên trong: thứ tự lệnh, batch, và vì sao có `DetectChanges`

**⚙️ Trình tự của `SaveChangesAsync`:**
```
1. DetectChanges          — quét ChangeTracker, so snapshot ⇒ O(entity × property)
2. Sắp xếp theo phụ thuộc — INSERT cha trước con, DELETE con trước cha
3. Chạy interceptor       — SaveChangesInterceptor: điền CreatedAt/UpdatedBy, gom domain event
4. Gom batch              — MySQL/SQL Server gộp nhiều lệnh trong 1 round-trip
5. Kiểm tra concurrency   — so rows-affected với kỳ vọng ⇒ DbUpdateConcurrencyException
6. Chấp nhận thay đổi     — AcceptAllChanges: Added/Modified → Unchanged
```

**⚖️ Bốn hệ quả về hiệu năng:**
- `DetectChanges` là O(số entity đang track). Track 50.000 entity ⇒ mỗi `SaveChanges` quét cả 50.000
  ⇒ vòng lặp "sửa 1 dòng, save 1 lần" là O(n²). **Sửa**: gom rồi save một lần, hoặc `AsNoTracking`
  cho phần chỉ đọc.
- `SaveChanges` **tự mở transaction ngầm** nếu chưa có ⇒ nhiều `SaveChanges` = nhiều transaction
  = mất tính nguyên tử (lý do project HW gom về `ExecuteAsync`).
- **Không** dùng `ExecuteUpdate/ExecuteDelete` (bỏ qua ChangeTracker) chung với entity đang track
  mà không `Reload()` — bộ nhớ và DB sẽ lệch nhau.
- Bulk thật sự (> vài nghìn dòng): dùng `LOAD DATA INFILE` / `SqlBulkCopy` / thư viện bulk,
  không dùng `AddRange` + `SaveChanges`.

---

## TX-17. Xử lý tiền: sổ kép, trạng thái, và vì sao không được dùng `double`

**❓ Vấn đề gốc**: nghiệp vụ tiền là nơi mọi sai sót về transaction đều thành sự cố có thật.

**⚙️ Bốn nguyên tắc bắt buộc:**
1. **Kiểu dữ liệu**: `decimal(19,4)` trong DB, `decimal` trong C#. **Không bao giờ** `double`/`float`
   (0.1 + 0.2 ≠ 0.3). Tiền tệ nhiều loại ⇒ lưu kèm mã tiền tệ, không cộng khác loại.
2. **Sổ kép (double-entry)**: không `UPDATE balance`. Ghi **hai bút toán** nợ/có
   (append-only `LedgerEntries`); số dư = `SUM` hoặc cột tổng được cập nhật nguyên tử.
   Lịch sử không thể sửa ⇒ kiểm toán được và tự phát hiện lệch.
3. **Bất biến trong DB**: tổng nợ = tổng có (kiểm tra định kỳ bằng job reconcile);
   `CHECK (balance >= 0)` cho tài khoản không được âm — **ràng buộc DB là tuyến phòng thủ cuối**.
4. **Máy trạng thái tường minh**: `Pending → Authorized → Captured → Settled | Failed | Refunded`.
   Mọi chuyển trạng thái là `UPDATE ... WHERE status = @expected` ⇒ số row bị ảnh hưởng cho biết
   chuyển hợp lệ hay không (CC-3).

**💻 Chuyển khoản an toàn giữa 2 tài khoản trong cùng DB:**
```csharp
await strategy.ExecuteAsync(async () =>
{
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

    // Luôn khoá theo THỨ TỰ CỐ ĐỊNH (vd: theo Id tăng dần) để không deadlock
    var ids = new[] { fromId, toId }.OrderBy(x => x, StringComparer.Ordinal).ToArray();

    // Trừ tiền có điều kiện: không đủ số dư thì affected = 0, không cần đọc trước
    var ok = await db.Accounts.Where(a => a.Id == fromId && a.Balance >= amount)
        .ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, a => a.Balance - amount), ct);
    if (ok == 0) throw new BadRequestException("Số dư không đủ");

    await db.Accounts.Where(a => a.Id == toId)
        .ExecuteUpdateAsync(s => s.SetProperty(a => a.Balance, a => a.Balance + amount), ct);

    db.LedgerEntries.AddRange(LedgerEntry.Debit(fromId, amount, refId),
                              LedgerEntry.Credit(toId,  amount, refId));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
});
```
Kèm **idempotency key** ở biên API (CC-7) — nếu không, retry của client = chuyển tiền 2 lần.

---

## TX-18. Multi-tenant và sharding: tránh transaction xuyên shard

**⚙️ Ba mô hình multi-tenant:**

| Mô hình | Cô lập | Transaction | Chi phí vận hành |
|---|---|---|---|
| Shared DB, cột `TenantId` | thấp (dựa vào query filter) | ✅ dễ | thấp |
| Schema riêng mỗi tenant | trung bình | ✅ trong 1 DB | migration × N schema |
| DB riêng mỗi tenant | cao | ✅ trong tenant, ❌ xuyên tenant | cao nhất |

**💻 Global query filter — bắt buộc nếu dùng cột `TenantId`:**
```csharp
modelBuilder.Entity<Blog>().HasQueryFilter(b =>
    b.TenantId == _tenantContext.TenantId && b.IsDelete != true);   // kết hợp với soft delete
```
Bẫy: `HasQueryFilter` **không áp dụng** cho `FromSql` thô, cho `ExecuteUpdate/ExecuteDelete`, và
bị bỏ qua khi dùng `IgnoreQueryFilters()`. Kiểm thử phải có ca "tenant A không thấy dữ liệu tenant B".

**⚖️ Sharding**: chọn shard key sao cho **99% transaction nằm trong 1 shard**
(thường là `TenantId` hoặc `UserId`). Nếu nghiệp vụ thường xuyên cần transaction xuyên shard thì
shard key đã chọn sai. Xuyên shard bắt buộc ⇒ saga (TX-10), không phải 2PC.
Rebalance: dùng **consistent hashing** hoặc bảng ánh xạ `tenant → shard` (dễ di chuyển hơn nhiều
so với `hash % N` — đổi N là phải di chuyển gần hết dữ liệu).

---

## TX-19. Bảng chẩn đoán: triệu chứng → nguyên nhân → cách sửa

| Triệu chứng | Nguyên nhân thường gặp | Cách sửa |
|---|---|---|
| `Lock wait timeout exceeded` | transaction dài giữ row lock | rút ngắn transaction, bỏ I/O ngoài, giảm `innodb_lock_wait_timeout` |
| `Deadlock found when trying to get lock` | thứ tự khoá khác nhau / gap lock | sắp thứ tự cố định, thêm index, retry cả transaction |
| `DbUpdateConcurrencyException` liên tục | hot row + optimistic | đổi sang atomic update có điều kiện hoặc hàng đợi theo key |
| Pool timeout khi lấy connection | transaction dài / `.Result` chặn thread | async all the way, transaction ngắn (CC-10) |
| Replica lag tăng vọt | transaction/batch quá lớn | chia nhỏ batch, giảm ghi |
| Undo/tempdb/bloat phình | transaction đọc mở lâu | tìm `innodb_trx`/`pg_stat_activity`, giới hạn thời gian sống |
| Message trùng ở consumer | at-least-once (đúng bản chất) | inbox dedup (TX-11) |
| Message "ma" (không có dữ liệu) | publish trước commit | chuyển sang outbox (TX-7) |
| Dữ liệu vừa ghi không thấy | đọc từ replica | sticky-to-primary sau ghi (TX-15) |

---

## TX-20. Ba câu hỏi ngược kinh điển (chuẩn bị sẵn)

**1. *"Đơn hàng đã trừ kho nhưng thanh toán lỗi, bạn xử lý sao?"***
> Saga có compensation: `ReserveStock → Charge → Ship`. `Charge` lỗi ⇒ saga chuyển sang
> `CompensatingStock`, gửi `ReleaseStockCommand`. Việc giữ kho là **semantic lock** có TTL —
> nếu saga chết giữa chừng, job quét saga quá hạn sẽ tự bù trừ. Bước không hoàn tác được
> (giao hàng) luôn nằm sau điểm pivot.

**2. *"Vì sao không dùng distributed transaction cho chắc?"***
> 2PC blocking khi coordinator chết, nhân độ trễ và giảm khả dụng theo tích các thành phần,
> và broker hiện đại không hỗ trợ XA thực dụng. Đổi lại, outbox + idempotent consumer cho tôi
> at-least-once với chi phí bằng một `INSERT` — và tôi kiểm soát được trạng thái trung gian
> ở mức nghiệp vụ.

**3. *"Làm sao đảm bảo message không bị xử lý 2 lần?"***
> Không đảm bảo được, và không cần. Tôi đảm bảo **xử lý 2 lần cho ra cùng một kết quả**:
> bản ghi dedup theo `MessageId` nằm **cùng transaction** với tác động nghiệp vụ, cộng với việc
> thiết kế thao tác gán thay vì cộng dồn ở nơi nào có thể.

---

## ✅ Checklist tự kiểm tra — Phần 16

- [ ] Giải thích undo log / redo log tương ứng chữ nào của ACID, và WAL giúp gì.
- [ ] Vẽ bảng anomaly × isolation level; giải thích write skew và 4 cách chống.
- [ ] Nói được MVCC lưu version ở đâu với InnoDB / Postgres / SQL Server và cái giá của transaction dài.
- [ ] Giải thích gap lock và một kịch bản deadlock giữa 2 INSERT.
- [ ] Nêu bẫy `EnableRetryOnFailure` + `BeginTransaction` và cách sửa bằng execution strategy.
- [ ] Chứng minh dual write problem và mô tả outbox (bảng, processor, at-least-once, độ trễ).
- [ ] So sánh outbox polling vs CDC, và 3 cách xử lý thứ tự message.
- [ ] Giải thích vì sao 2PC bị loại bỏ, và saga thay thế nó bằng gì.
- [ ] Nêu 3 luật của event sourcing (không xoá event, `Apply` thuần, quyết định ở decision method).
- [ ] Giải thích PACELC và cho ví dụ chọn CP/AP theo từng use case trong cùng một app.
- [ ] Nêu 4 cách xử lý read-after-write khi có read replica.

⬅️ Quay lại [Mục lục](interview.NET.md) | Tiếp: [Phần 17 — Caching ➡️](interview.NET.17-Caching-Performance.md)
