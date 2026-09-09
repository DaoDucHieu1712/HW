# Phần 19 — Case study: ghép Concurrency + Transaction + Caching + Messaging

[⬅️ Phần 18 — Messaging](interview.NET.18-Messaging-Scaling.md) | [⬅️ Về mục lục](interview.NET.md)

> File 15–18 dạy từng công cụ. File này dạy **cách chọn công cụ** khi interviewer đưa một bài toán
> mở: *"Thiết kế hệ thống flash sale 100.000 người cùng lúc"*.
>
> Mỗi case theo khung: **📋 Yêu cầu → 🔢 Ước lượng → 🏗️ Thiết kế → ⚠️ Chỗ hỏng → 🎯 Câu hỏi khoan sâu**.

---

## 🧭 Khung trả lời system design (dùng cho MỌI câu hỏi mở)

```
1. LÀM RÕ YÊU CẦU   (2–3 phút — đừng bỏ qua, đây là phần bị chấm điểm nhiều nhất)
   • Chức năng: ai làm gì? Đọc nhiều hay ghi nhiều?
   • Phi chức năng: QPS đỉnh? p99 mục tiêu? Được phép cũ bao lâu? Mất dữ liệu có chấp nhận được?
   • Ràng buộc: team mấy người? Hạ tầng sẵn có? Thời gian?

2. ƯỚC LƯỢNG        (2 phút — cho thấy bạn nghĩ bằng con số)
   • QPS trung bình → QPS đỉnh (thường ×3–10)
   • Dung lượng/ngày, băng thông, số connection cần (Little's Law — CC-1)

3. API + MÔ HÌNH DỮ LIỆU  (5 phút)
   • Vài endpoint chính; chỉ ra ĐÂU là ràng buộc nhất quán mạnh

4. THIẾT KẾ CAO       (10 phút)
   • Vẽ luồng; nêu rõ chỗ nào đồng bộ, chỗ nào bất đồng bộ
   • Nêu rõ ranh giới transaction và ranh giới eventual consistency

5. KHOAN SÂU 1–2 ĐIỂM (10 phút — nơi thể hiện file 15–18)
   • Concurrency: race ở đâu, chống bằng gì
   • Transaction: atomic tới đâu, ngoài đó là gì
   • Cache: cache gì, invalidate ra sao, cache chết thì sao
   • Messaging: cái gì bất đồng bộ, trùng lặp xử lý ra sao

6. VẬN HÀNH & THẤT BẠI (5 phút — hầu hết ứng viên quên phần này)
   • Metric/cảnh báo nào? Quá tải thì hành xử ra sao? Từng thành phần chết thì sao?
```

**Nguyên tắc số 1**: nói ra **trade-off**, đừng nói "giải pháp tốt nhất".
**Nguyên tắc số 2**: bắt đầu đơn giản (1 DB + cache), chỉ thêm phức tạp khi con số ước lượng bắt buộc.

---

## 📐 Số liệu nền cần thuộc (để ước lượng nhanh)

| Thao tác | Độ trễ | Ghi nhớ |
|---|---|---|
| L1 cache CPU | ~1 ns | |
| Truy cập RAM | ~100 ns | |
| `IMemoryCache` hit | ~0.1–1 µs | nhanh gấp ~1.000 lần Redis |
| Redis (cùng DC) | ~0.5–2 ms | chủ yếu là round-trip mạng |
| SSD random read | ~100 µs | |
| DB query có index | ~1–10 ms | |
| DB query thiếu index (full scan 1M row) | ~100–1.000 ms | |
| HTTP nội bộ cùng DC | ~1–5 ms | |
| HTTP xuyên vùng | ~50–150 ms | |

| Quy đổi nhanh | Giá trị |
|---|---|
| 1 ngày | ~86.400 s ≈ 10⁵ s |
| 1 triệu req/ngày | ≈ **12 req/s** trung bình |
| 100 triệu req/ngày | ≈ **1.200 req/s** trung bình (đỉnh ~5.000–10.000) |
| 1 dòng DB điển hình | ~200 byte – 1 KB |
| 1 tỷ dòng × 500B | ~500 GB |

---

# 🔥 Case 1 — Flash sale: 100.000 người tranh 1.000 sản phẩm

**📋 Yêu cầu**: bán đúng 1.000 sản phẩm, **tuyệt đối không oversell**; đỉnh 50.000 req/s trong 10 giây;
người thua phải biết ngay; người thắng có 10 phút để thanh toán.

**🔢 Ước lượng**: 50.000 req/s × 10s = 500.000 request cho 1.000 suất
⇒ **99,8% request là để bị từ chối**. Đây là insight quan trọng nhất của bài này:
**tối ưu cho đường từ chối, không phải đường thành công.**

**🏗️ Thiết kế — lọc theo tầng, mỗi tầng loại bỏ bớt:**
```
Tầng 0 — CDN/UI:  countdown, nút disable, phân tán thời điểm bấm      → giảm ~30%
Tầng 1 — Gateway: rate limit theo user + IP (CC-8), chặn bot          → giảm ~40%
Tầng 2 — Redis:   DECR nguyên tử trên tồn kho                          → chỉ 1.000 người qua
Tầng 3 — Queue:   1.000 message đặt chỗ → consumer ghi DB tuần tự
Tầng 4 — DB:      transaction thật, ràng buộc CHECK là chốt chặn cuối
```

**💻 Tầng 2 — trái tim của thiết kế (Lua để nguyên tử tuyệt đối):**
```lua
-- KEYS[1]=stock:{sku}  KEYS[2]=bought:{sku}(SET)  ARGV[1]=userId
if redis.call('SISMEMBER', KEYS[2], ARGV[1]) == 1 then return -2 end   -- mỗi người 1 suất
local n = tonumber(redis.call('GET', KEYS[1]) or '0')
if n <= 0 then return -1 end                                            -- hết hàng
redis.call('DECR', KEYS[1])
redis.call('SADD', KEYS[2], ARGV[1])
return n - 1                                                            -- còn lại bao nhiêu
```
Vì sao Lua chứ không phải `GET` rồi `DECR`? Vì Redis đơn luồng ⇒ **cả script chạy nguyên tử**,
không có khe hở giữa kiểm tra và trừ (CC-3). Đây là "một câu lệnh nguyên tử" ở tầng cache.

**💻 Tầng 3 — không ghi thẳng DB từ 1.000 request song song:**
```csharp
// Người thắng ở Redis → publish message, trả 202 Accepted ngay (không chờ DB)
await _bus.PublishAsync(new ReserveStockCommand(sku, userId, requestId), ct);
return Accepted(new { status = "processing", pollUrl = $"/api/order/status/{requestId}" });

// Consumer: partition key = sku ⇒ mọi lệnh của 1 SKU vào 1 partition ⇒ xử lý TUẦN TỰ
// ⇒ không lock, không deadlock, không tranh chấp row (CC-11, MQ-6)
```

**💻 Tầng 4 — ràng buộc DB là tuyến phòng thủ cuối, không phải nguồn chính:**
```sql
ALTER TABLE FlashSaleStock ADD CONSTRAINT chk_stock CHECK (Remaining >= 0);
-- Trừ kho có điều kiện: nếu Redis có sai lệch, DB vẫn từ chối
UPDATE FlashSaleStock SET Remaining = Remaining - 1
 WHERE Sku = @sku AND Remaining > 0;      -- affected = 0 ⇒ trả suất về, hoàn tiền/huỷ giữ chỗ
```

**⚠️ Chỗ hỏng và cách vá:**

| Chỗ hỏng | Hậu quả | Cách vá |
|---|---|---|
| Redis chết giữa flash sale | mất trạng thái tồn kho | AOF `everysec` + replica; khi mất, dựng lại từ DB và **tạm dừng** bán vài giây thay vì bán bừa |
| Người thắng không thanh toán | hàng bị giữ vô hạn | `HeldUntil` + job giải phóng (semantic lock có TTL — TX-10) |
| Redis trừ rồi consumer chết | mất suất | outbox/message at-least-once + reconcile định kỳ giữa Redis và DB |
| Bot mua hết | mất uy tín | rate limit theo user, captcha, giới hạn theo tài khoản đã xác thực |
| DB nhận đủ 50.000 req/s | sập | tầng 2 chặn 99,8% — **DB không bao giờ thấy request thua** |

**🎯 Câu hỏi khoan sâu:**
- *"Vì sao dùng Redis chứ không phải `SELECT FOR UPDATE`?"* → 50.000 req/s trên một row = xếp hàng
  trên một row lock, throughput trần vài nghìn/s và deadlock. Redis `DECR` là nguyên tử ở
  ~100k ops/s. DB vẫn là nguồn sự thật cuối, nhưng **không nằm trên đường nóng**.
- *"Oversell xảy ra khi nào?"* → chỉ khi Redis và DB lệch (Redis mất dữ liệu khi failover).
  Vì thế phải có `CHECK (Remaining >= 0)` + job đối soát; và với hàng giá trị cao thì đảo lại:
  DB là nguồn duy nhất, chấp nhận throughput thấp hơn.

---

# 🎫 Case 2 — Đặt vé/ghế: không được bán trùng, giữ chỗ có hạn

**📋 Yêu cầu**: mỗi ghế chỉ một người; giữ chỗ 10 phút; hiển thị sơ đồ ghế thời gian thực;
5.000 người xem một sự kiện, ~200 thao tác chọn ghế/giây.

**🏗️ Khác biệt cốt lõi so với Case 1**: ở đây **mỗi ghế là một tài nguyên riêng biệt**
⇒ tranh chấp phân tán tự nhiên, không có hot row duy nhất ⇒ **dùng thẳng DB là đủ**.

**💻 Giữ chỗ bằng một câu UPDATE có điều kiện (không cần lock, không cần Redis):**
```csharp
var held = await db.Seats
    .Where(s => s.Id == seatId
             && (s.Status == SeatStatus.Free
                 || (s.Status == SeatStatus.Held && s.HeldUntil < DateTimeOffset.UtcNow)))  // hết hạn thì cướp được
    .ExecuteUpdateAsync(s => s
        .SetProperty(x => x.Status,    SeatStatus.Held)
        .SetProperty(x => x.HeldBy,    userId)
        .SetProperty(x => x.HeldUntil, DateTimeOffset.UtcNow.AddMinutes(10)), ct);

if (held == 0) throw new ConflictException("Ghế vừa có người chọn");   // 409, UI tô đỏ ghế đó
```
Không cần job dọn: điều kiện `HeldUntil < now` khiến ghế **tự** trở thành khả dụng.
(Vẫn nên có job đặt lại `Status = Free` để hiển thị đúng, nhưng tính đúng đắn không phụ thuộc job đó.)

**💻 Sơ đồ ghế thời gian thực**: SignalR + Redis backplane (CC-19).
```csharp
// Sau khi giữ ghế thành công (trong domain event handler, chạy SAU commit)
await _hub.Clients.Group($"event:{eventId}").SendAsync("SeatChanged", seatId, SeatStatus.Held, ct);
```
Cache sơ đồ ghế: **không** cache trạng thái từng ghế (đổi liên tục), mà cache **bố cục tĩnh**
(vị trí, hạng ghế, giá) — phần thay đổi thì đẩy qua WebSocket.

**⚠️ Chỗ hỏng:**
- **Chọn nhiều ghế liền nhau** (4 ghế cạnh nhau): cập nhật nhiều row ⇒ nguy cơ deadlock.
  Sửa: `ORDER BY SeatId` cố định trước khi khoá (TX-4), hoặc một câu `UPDATE ... WHERE Id IN (...)`
  rồi so `affected == 4`, không đủ thì rollback cả nhóm.
- **Người dùng đóng tab**: TTL tự giải quyết.
- **Thanh toán quá 10 phút**: ghế bị người khác cướp trong lúc đang thanh toán ⇒ trước khi capture
  tiền phải kiểm tra lại `HeldBy == userId && HeldUntil > now` **trong cùng transaction** với việc
  chuyển sang `Sold`.

**🎯 Câu hỏi khoan sâu**: *"Vì sao ở đây không cần Redis mà Case 1 lại cần?"*
→ Phân phối tranh chấp. Flash sale: 500.000 request tranh **một** counter (α ≈ 1 trong USL — CC-2).
Đặt vé: 200 request/s trải trên hàng nghìn ghế ⇒ tranh chấp trên mỗi row rất thấp ⇒ DB thừa sức.
*"Chọn công cụ theo hình dạng tranh chấp, không theo QPS tổng."*

---

# 💳 Case 3 — Thanh toán: không được trừ tiền hai lần

**📋 Yêu cầu**: gọi cổng thanh toán bên ngoài (chậm, hay timeout, không idempotent nếu ta không tự làm);
tuyệt đối không trừ tiền hai lần; phải đối soát được với sao kê của đối tác.

**🏗️ Thiết kế — 5 lớp phòng thủ:**
```
1. Idempotency-Key từ client            (CC-7)  → chống double-submit / retry của client
2. Máy trạng thái trong DB              (TX-17) → Pending→Authorized→Captured→Settled|Failed
3. Reference id gửi sang đối tác        → dùng chính OrderId, để retry sang họ cũng idempotent
4. Outbox cho mọi tác động ra ngoài     (TX-7)  → không bao giờ gọi API trong transaction
5. Job đối soát hằng ngày với sao kê    → phát hiện lệch mà 4 lớp trên bỏ sót
```

**💻 Chuyển trạng thái an toàn — không đọc rồi ghi, mà ghi có điều kiện:**
```csharp
// Chỉ chuyển được nếu đang ở đúng trạng thái kỳ vọng ⇒ nguyên tử, không cần lock
var moved = await db.Payments
    .Where(p => p.Id == id && p.Status == PaymentStatus.Pending)
    .ExecuteUpdateAsync(s => s
        .SetProperty(p => p.Status,    PaymentStatus.Authorizing)
        .SetProperty(p => p.UpdatedAt, DateTimeOffset.UtcNow), ct);

if (moved == 0) return;    // ai đó đã xử lý — đây là đường trả về BÌNH THƯỜNG, không phải lỗi
```

**💻 Gọi cổng ngoài — luôn nằm ngoài transaction, luôn có reference id ổn định:**
```csharp
// Consumer của message AuthorizePaymentCommand (đã ở ngoài transaction ghi DB)
var result = await _gateway.AuthorizeAsync(new
{
    reference = payment.Id,                 // ⬅️ retry gửi cùng reference ⇒ đối tác dedup giúp ta
    amount    = payment.Amount,
    currency  = payment.Currency
}, ct);

// Ghi kết quả trong transaction riêng, vẫn có điều kiện trạng thái
await _uow.ExecuteAsync(async () => { /* Authorizing → Authorized | Failed + LedgerEntry */ });
```

**⚠️ Ba tình huống khó phải trả lời được:**
1. **Gọi đối tác timeout — không biết họ đã trừ tiền chưa.**
   → **Không được** coi là thất bại. Chuyển sang `Unknown`, và **query lại trạng thái** ở đối tác
   theo `reference` (backoff luỹ thừa). Đây là lý do bắt buộc phải có reference id ổn định.
2. **Webhook của đối tác tới trước khi ta kịp ghi `Authorizing`.**
   → Webhook handler phải idempotent và **không giả định thứ tự**: nó cũng dùng `UPDATE ... WHERE
   Status IN (...)`; nếu chưa có bản ghi thì lưu vào bảng chờ và xử lý lại sau (out-of-order arrival).
3. **Webhook tới hai lần.**
   → Dedup theo `MessageId` của đối tác, cùng transaction với ghi sổ (TX-11).

**🎯 Câu hỏi khoan sâu**: *"Vì sao không dùng distributed transaction với cổng thanh toán?"*
→ Họ không tham gia 2PC của bạn (TX-9). Cách duy nhất là saga: `Authorize → Capture`, với
compensation là `Void`/`Refund` — và `Refund` là **giao dịch nghiệp vụ mới**, không phải rollback.

---

# 📰 Case 4 — News feed: 10 triệu user, đọc nhiều gấp 100 lần ghi

**📋 Yêu cầu**: user theo dõi n người; feed sắp theo thời gian; p99 < 200ms;
10 triệu DAU, mỗi người mở feed 20 lần/ngày.

**🔢 Ước lượng**:
```
Đọc: 10M × 20 / 86.400 ≈ 2.300 req/s trung bình, đỉnh ~10.000 req/s
Ghi: 10M × 0.5 bài/ngày / 86.400 ≈ 60 bài/s
⇒ Tỉ lệ đọc/ghi ≈ 170:1  ⇒ TỐI ƯU CHO ĐỌC, sẵn sàng trả giá ở đường ghi
```

**🏗️ Ba mô hình:**

| Mô hình | Ghi | Đọc | Vấn đề |
|---|---|---|---|
| **Fan-out on read** (pull) | rẻ (1 insert) | đắt: `WHERE authorId IN (500 người) ORDER BY time` | đọc chậm, không cache được |
| **Fan-out on write** (push) | đắt: 1 bài → N feed | rẻ: đọc sẵn một danh sách | celebrity 10 triệu follower = 10 triệu lượt ghi |
| **Hybrid** ✅ | push cho user thường, pull cho celebrity | trộn 2 nguồn lúc đọc | phức tạp hơn, nhưng là cách thực tế |

**💻 Hybrid — cách các mạng xã hội thật sự làm:**
```csharp
// GHI: A đăng bài
if (followerCount < 10_000)                       // user thường → push
    await _bus.PublishAsync(new FanOutPost(postId, authorId), ct);   // consumer ghi vào feed từng follower
// else: celebrity → KHÔNG fan-out, chỉ lưu vào "posts của A"

// ĐỌC: feed của B
var pushed  = await _redis.SortedSetRangeByRankAsync($"feed:{userId}", 0, 49, Order.Descending);
var celebs  = await GetCelebritiesFollowedAsync(userId);            // thường < 50 người
var pulled  = await GetRecentPostsAsync(celebs, limit: 50);         // truy vấn nhỏ, cache được
return Merge(pushed, pulled).OrderByDescending(p => p.CreatedAt).Take(50);
```

**💻 Cấu trúc lưu feed — Redis ZSET, cắt bớt để không phình:**
```csharp
await _db.SortedSetAddAsync($"feed:{followerId}", postId, timestampScore);
await _db.SortedSetRemoveRangeByRankAsync($"feed:{followerId}", 0, -1001);  // chỉ giữ 1.000 mới nhất
```
Không ai cuộn quá 1.000 bài; muốn xem cũ hơn thì rơi về truy vấn DB (chấp nhận chậm hơn).

**⚠️ Chỗ hỏng:**
- **Fan-out là công việc lớn** ⇒ phải bất đồng bộ qua queue, chia lô (mỗi message 1.000 follower),
  giới hạn concurrency (CC-13). Fan-out đồng bộ trong request tạo bài = timeout chắc chắn.
- **User không hoạt động 6 tháng** vẫn được ghi feed ⇒ lãng phí lớn.
  Sửa: chỉ push cho user hoạt động trong 30 ngày; user quay lại thì dựng feed theo pull.
- **Feed trong Redis mất khi failover** ⇒ phải dựng lại được từ DB (feed là **cache**, không phải
  nguồn sự thật — CA-10).

**🎯 Câu hỏi khoan sâu**: *"Vì sao không dùng một câu SQL `JOIN` cho đơn giản?"*
→ 2.300 req/s × truy vấn `IN (500 authorId) ORDER BY created_at DESC LIMIT 50` là truy vấn tốn kém
mà **không cache được** (mỗi user một kết quả — CA-14). Fan-out chuyển chi phí từ đường đọc
(2.300/s) sang đường ghi (60/s) — đúng chiều tỉ lệ 170:1.

---

# 🔍 Case 5 — Đồng bộ DB sang Elasticsearch (search index)

**📋 Yêu cầu**: mọi thay đổi sản phẩm phải lên search trong ≤ 5s; không được mất cập nhật;
đôi khi cần dựng lại toàn bộ index.

**🏗️ Bốn cách, và vì sao chỉ hai cách đúng:**

| Cách | Đánh giá |
|---|---|
| ❌ Ghi DB rồi ghi ES ngay trong handler | dual write (TX-7): ES lỗi ⇒ lệch vĩnh viễn; ES chậm ⇒ transaction dài |
| ❌ Job quét `UpdatedAt > lastRun` mỗi phút | bỏ sót do lệch đồng hồ; không bắt được DELETE; tải DB |
| ✅ **Outbox + consumer** | atomic với nghiệp vụ, retry được, có DLQ |
| ✅ **CDC (Debezium đọc binlog)** | độ trễ ms, không đụng vào code ứng dụng, nhưng thêm hạ tầng |

**💻 Consumer index — 4 chi tiết bắt buộc:**
```csharp
public async Task HandleAsync(ProductChanged msg, CancellationToken ct)
{
    // (1) Idempotent bằng version: ES từ chối ghi đè bằng bản cũ hơn
    await _es.IndexAsync(doc, i => i
        .Id(msg.ProductId)
        .Version(msg.Version).VersionType(VersionType.External), ct);

    // (2) Ghi theo lô: gom 500 doc/lần bằng _bulk — nhanh hơn hàng chục lần so với ghi lẻ
    // (3) Xoá: dùng soft delete ⇒ index cờ IsDelete và lọc lúc truy vấn, hoặc xoá hẳn khỏi index
    // (4) Refresh: KHÔNG dùng refresh=true cho mỗi lần ghi (giết throughput của ES)
}
```
`VersionType.External` chính là mẹo né bài toán thứ tự (MQ-6): message tới sai thứ tự vẫn hội tụ đúng.

**💻 Dựng lại toàn bộ index — mẫu blue-green, không downtime:**
```
1. Tạo index mới:  products_v2
2. Backfill từ DB theo lô (kèm cả stream event mới để không bỏ sót thay đổi trong lúc backfill)
3. Kiểm tra số lượng + lấy mẫu so sánh
4. Chuyển alias:   products → products_v2   (nguyên tử ở phía ES)
5. Xoá products_v1 sau vài ngày
```

**🎯 Câu hỏi khoan sâu**: *"Trong lúc backfill 4 tiếng, có cập nhật mới thì sao?"*
→ Vẫn cho consumer ghi vào **cả** v1 và v2 trong giai đoạn chuyển tiếp; nhờ `external version`
nên thứ tự giữa backfill và stream không quan trọng — bản version cao hơn luôn thắng.

---

# 📊 Case 6 — Đếm & thống kê thời gian thực (view, like, dashboard)

**📋 Yêu cầu**: đếm lượt xem cho 10 triệu bài viết; hiển thị gần thời gian thực; sai số nhỏ chấp nhận được;
đỉnh 50.000 lượt xem/giây.

**🏗️ Kiến trúc phễu — không bao giờ ghi thẳng DB:**
```
View event (50k/s)
  → Redis INCR có key splitting (CC-11, CC-17)   ← chịu được toàn bộ tải ghi
  → Job mỗi 10s: gộp bucket → ghi DB một lần/bài  ← DB chỉ nhận vài trăm write/s
  → Đọc: Redis (mới nhất) + DB (lịch sử)
```

**💻**
```csharp
// GHI — tản ra 16 bucket để không có hot key
await _redis.StringIncrementAsync($"views:{postId}:{Random.Shared.Next(16)}");

// FLUSH — mỗi 10s, gộp và ghi DB bằng UPSERT cộng dồn (idempotent theo delta)
var total = (await Task.WhenAll(Enumerable.Range(0, 16)
    .Select(b => _redis.StringGetSetAsync($"views:{postId}:{b}", 0)))).Sum(v => (long)v);
await db.Database.ExecuteSqlAsync(
    $"INSERT INTO PostStats (PostId, Views) VALUES ({postId}, {total}) " +
    $"ON DUPLICATE KEY UPDATE Views = Views + {total}");
```
`GETSET` (lấy rồi đặt 0) là nguyên tử ⇒ không mất lượt đếm xảy ra giữa lúc đọc và reset.
Nếu job chết sau `GETSET` mà trước khi ghi DB thì **mất** phần đó — chấp nhận được cho view count;
không chấp nhận được cho tiền (khi đó dùng append-only + rollup — CC-17 cách 3).

**💻 Đếm unique — HyperLogLog thay vì Set (CA-13):**
```csharp
await _redis.HyperLogLogAddAsync($"uv:{postId}:{date:yyyyMMdd}", userId);   // 12KB cố định
var uniqueViewers = await _redis.HyperLogLogLengthAsync($"uv:{postId}:{date:yyyyMMdd}");
```

**🎯 Câu hỏi khoan sâu**: *"Cần chính xác tuyệt đối thì sao?"*
→ Đổi sang append-only: mỗi lượt xem là một message vào Kafka (partition theo postId), consumer gộp
theo cửa sổ thời gian và ghi DB idempotent theo `(postId, windowStart)`. Chính xác tuyệt đối,
đổi lại độ trễ cao hơn và hạ tầng nặng hơn.

---

# 🏢 Case 7 — Áp dụng vào chính project HW

**📋 Bối cảnh**: Clean Architecture + CQRS + MediatR + EF Core (MariaDB) + Outbox + saga event-sourced,
`IMessageBus` chọn provider theo cấu hình.

**🏗️ Lộ trình nâng cấp chịu tải, theo thứ tự nên làm:**

| Bước | Việc | Thuộc file | Vì sao ở thứ tự này |
|---|---|---|---|
| 1 | Đo trước: p99 theo endpoint, slow query log, ThreadPool queue | CC-21 | không đo thì tối ưu mù |
| 2 | Sửa N+1, thêm index, chiếu DTO thay vì load entity | file 08 | rẻ nhất, hiệu quả cao nhất |
| 3 | `AsNoTracking` cho mọi query handler | TX-16 | giảm `DetectChanges` và bộ nhớ |
| 4 | Rút ngắn transaction: bỏ mọi I/O ngoài khỏi `ExecuteAsync` | TX-6 | gốc rễ của cạn pool |
| 5 | Thêm `HybridCache` cho các query đọc nhiều | CA-7 | có L1+L2+chống stampede sẵn |
| 6 | Xoá cache trong domain event handler (sau commit) | CA-3 | đúng thứ tự invalidation |
| 7 | Rate limiter theo user cho endpoint đắt | CC-8 | bảo vệ khi bị lạm dụng |
| 8 | Outbox: claim bằng `SKIP LOCKED`, nhiều instance | CC-18, TX-8 | bỏ nút thắt 1 worker |
| 9 | Inbox dedup cho mọi `IMessageHandler<T>` | TX-11 | at-least-once là mặc định |
| 10 | Concurrency token cho entity hay bị sửa đồng thời | CC-5 | chống lost update |
| 11 | Read replica cho query, sticky-to-primary sau ghi | TX-15 | giảm tải primary an toàn |
| 12 | Graceful shutdown + readiness/liveness tách bạch | CC-20 | không mất request khi deploy |

**⚠️ Ba điểm yếu cụ thể của kiến trúc hiện tại và cách nói khi phỏng vấn:**
1. **Outbox polling có độ trễ ≤ ~10s.** → Chấp nhận được cho thông báo/index; **không** dùng cho
   nghiệp vụ cần phản hồi tức thì. Cần nhanh hơn thì giảm chu kỳ + đánh thức bằng signal sau commit,
   hoặc chuyển sang CDC (TX-8).
2. **`PublishAsync` từ đường không `SaveChanges` bị bỏ im lặng** (đúng như CLAUDE.md).
   → Đây là bẫy phải nêu rõ trong onboarding; có thể thêm assertion ở môi trường dev để phát hiện sớm.
3. **Lazy-loading proxies + navigation `virtual`** rất tiện nhưng dễ sinh N+1 trong vòng lặp.
   → Query handler phải chiếu thẳng sang DTO bằng `Select`/Mapster `ProjectToType`, không duyệt
   navigation trong vòng lặp.

**🎯 Câu hỏi khoan sâu điển hình**: *"Kiến trúc của bạn chịu được bao nhiêu tải?"*
> *"Cổ chai đầu tiên là DB connection pool, không phải CPU. Với p99 mục tiêu 200ms và transaction
> trung bình giữ connection ~20ms, theo Little's Law 100 connection cho phép khoảng 5.000 command/s —
> nhưng thực tế sẽ chạm giới hạn ở row lock của các bảng nóng trước. Query đã tách sang cache và
> read replica nên gần như không tốn connection của primary. Con số thật phải đo bằng load test
> open-loop tới knee point; tôi sẽ đo trước khi hứa."*

---

## 🧩 Bảng tra nhanh: gặp vấn đề gì thì dùng công cụ nào

| Vấn đề | Công cụ đầu tiên | Nếu chưa đủ | Tham chiếu |
|---|---|---|---|
| Hai request cùng sửa một dữ liệu | `UPDATE ... WHERE <điều kiện>` nguyên tử | `FOR UPDATE` → concurrency token → hàng đợi theo key | CC-3..CC-6 |
| Client retry gây tác động kép | Idempotency key | dedup ở consumer (inbox) | CC-7, TX-11 |
| Bị lạm dụng / burst | Token bucket local | Redis Lua limiter phân tán | CC-8 |
| Quá tải, latency tăng vô hạn | Bounded queue + 503 | shed theo deadline, autoscale | CC-9 |
| Cạn DB pool | rút ngắn transaction | tách read replica, pooler | CC-10, TX-6 |
| Một key/partition nóng | key splitting | L1 cache, hàng đợi theo key | CC-11, CA-12 |
| Dependency chậm/chết | timeout + circuit breaker | bulkhead, fallback dữ liệu cũ | CC-12 |
| Cần atomic ngoài phạm vi DB | Outbox | saga + compensation | TX-7, TX-10 |
| Message trùng | thao tác tự idempotent | inbox dedup cùng transaction | TX-11 |
| Đọc chậm, đọc nhiều | index + projection | cache-aside → HybridCache → read model | CA-14 |
| Cache miss đồng loạt | TTL jitter | singleflight, stale-while-revalidate | CA-4 |
| Dữ liệu cache cũ | commit → xoá cache | versioned key / tag | CA-3 |
| Consumer không theo kịp | xử lý theo lô | tăng partition + consumer, autoscale theo lag | MQ-8 |
| Message lỗi lặp vô hạn | phân loại lỗi | retry bậc thang + DLQ + replay | MQ-7 |

---

## 🎤 Mười câu hỏi phỏng vấn hay gặp nhất (và ý chính phải nói)

1. **"Chống oversell thế nào?"** → atomic update có điều kiện; Redis Lua nếu hot; `CHECK` ở DB là chốt cuối.
2. **"Exactly-once làm sao?"** → không có; at-least-once + idempotent, dedup cùng transaction.
3. **"Vì sao cần Outbox?"** → dual write không atomic; ghi message như một dòng dữ liệu trong cùng transaction.
4. **"Cache cũ thì sao?"** → TTL là giới hạn trên của sai lệch; commit → xoá cache; versioned key khi ảnh hưởng nhiều key.
5. **"Redis chết thì sao?"** → app không sập; limiter bảo vệ DB; L1 hấp thụ; nhưng lock/idempotency thì không được neo ở Redis.
6. **"Deadlock xử lý thế nào?"** → thứ tự khoá cố định, thêm index, transaction ngắn, retry **cả** transaction qua execution strategy.
7. **"Scale ngang có tăng tuyến tính không?"** → không; USL: contention + coherency; phải phá điểm tuần tự chung.
8. **"Đo hiệu năng thế nào?"** → open-loop, p99 chứ không phải trung bình, tìm knee point, coordinated omission.
9. **"Bao nhiêu partition/consumer?"** → consumer hữu ích ≤ partition; đặt partition dư cho tương lai vì tăng sau sẽ vỡ ánh xạ key.
10. **"Quá tải thì hệ thống hành xử ra sao?"** → 429 + `Retry-After`, bounded queue, shed theo mức ưu tiên — **không** để timeout 30s.

---

## ✅ Checklist tự kiểm tra — Phần 19

- [ ] Trình bày được khung 6 bước trả lời system design.
- [ ] Ước lượng QPS/dung lượng từ số DAU mà không cần máy tính.
- [ ] Giải thích vì sao flash sale cần Redis nhưng đặt vé thì không (hình dạng tranh chấp).
- [ ] Nêu 5 lớp phòng thủ của luồng thanh toán và xử lý ca timeout không rõ kết quả.
- [ ] So sánh fan-out on read / on write / hybrid và chọn theo tỉ lệ đọc-ghi.
- [ ] Thiết kế đồng bộ DB → ES bằng outbox + external version + blue-green reindex.
- [ ] Thiết kế phễu đếm: Redis bucket → flush → DB, và biết khi nào cách đó không đủ.
- [ ] Nêu 3 điểm yếu cụ thể của kiến trúc project HW và cách khắc phục.
- [ ] Trả lời "hệ thống chịu bao nhiêu tải" bằng phương pháp, không bằng con số bịa.

⬅️ Quay lại [Mục lục](interview.NET.md)
