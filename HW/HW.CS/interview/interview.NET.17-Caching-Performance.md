# Phần 17 — Caching: từ CPU cache tới CDN, và cách không tự bắn vào chân

[⬅️ Phần 16 — Transaction & Consistency](interview.NET.16-Transaction-Consistency.md) | [⬅️ Về mục lục](interview.NET.md) | Tiếp theo: [Phần 18 — Messaging ➡️](interview.NET.18-Messaging-Scaling.md)

> Khung mỗi câu: **❓ Vấn đề gốc → ⚙️ Cơ chế bên dưới → 💻 Code/đo đạc → ⚖️ Hệ quả thực chiến**.
>
> Cache là kỹ thuật **dễ thêm nhất và dễ sai nhất**. Mọi câu hỏi phỏng vấn về cache thực chất chỉ
> xoay quanh hai thứ: **invalidation** (làm sao biết dữ liệu đã cũ) và **stampede** (điều gì xảy ra
> khi cache miss đồng loạt).

---

## 🗺️ Bản đồ: 7 tầng cache của một request

```
Browser cache        (Cache-Control, ETag)              ─ giảm 100% request
   ▼
CDN / edge           (surrogate key, stale-while-revalidate)
   ▼
API gateway          (response cache, request coalescing)
   ▼
App L1 — in-process  (IMemoryCache)   ns–µs, KHÔNG đồng nhất giữa instance
   ▼
App L2 — phân tán    (Redis)          ~0.5–2ms, dùng chung, có thể chết
   ▼
Database buffer pool (InnoDB pool, shared_buffers)
   ▼
Đĩa
```

**Câu chốt phỏng vấn:** *"Cache không làm hệ thống nhanh lên — nó **đổi tính nhất quán lấy độ trễ**.
Nên câu hỏi đầu tiên luôn là: dữ liệu này được phép cũ bao lâu? Nếu câu trả lời là 'không được cũ',
thì thứ bạn cần không phải cache mà là index tốt hơn hoặc mô hình đọc khác."*

---

## CA-1. Toán học của cache — hit ratio quan trọng hơn bạn tưởng

**❓ Vấn đề gốc**: "cache hit 90% là tốt chưa?" — nghe thì cao, tính ra thì không.

**⚙️ Công thức**:
```
T_trung bình = h × T_hit + (1 − h) × T_miss

Ví dụ: T_hit = 1ms (Redis), T_miss = 50ms (DB)
h = 0.90 → 0.9(1) + 0.1(50) = 5.9ms
h = 0.95 → 0.95(1) + 0.05(50) = 3.45ms   (tăng 5% hit ⇒ nhanh hơn 42%)
h = 0.99 → 0.99(1) + 0.01(50) = 1.49ms   (tăng thêm 4% ⇒ nhanh hơn 57% nữa)
```
**Phần miss chi phối tất cả.** Đi từ 90% → 99% có giá trị hơn nhiều so với việc tối ưu đường hit.

**⚙️ Quan trọng hơn: tải xuống DB.**
```
10.000 req/s, hit 90% ⇒ DB nhận 1.000 req/s
10.000 req/s, hit 99% ⇒ DB nhận   100 req/s   (giảm 10 lần)
Cache chết hoàn toàn  ⇒ DB nhận 10.000 req/s  ⇒ SẬP (xem CA-6, cache avalanche)
```

**⚖️ Hai câu hỏi phải trả lời được trước khi thêm cache:**
1. *"Nếu cache chết, DB có sống nổi không?"* — nếu không, bạn không có cache, bạn có **một phụ
   thuộc bắt buộc** và phải thiết kế cho nó (rate limit, circuit breaker, degrade).
2. *"Dữ liệu được phép cũ bao lâu?"* — quyết định TTL, chiến lược invalidation, và cả việc có nên
   cache hay không.

---

## CA-2. Bốn mẫu cache — và mẫu nào project .NET thực tế dùng

**⚙️ So sánh:**

| Mẫu | Đọc | Ghi | Ưu / Nhược |
|---|---|---|---|
| **Cache-aside** (lazy loading) | app kiểm tra cache → miss thì đọc DB → nạp cache | app ghi DB → **xoá** cache | ✅ phổ biến nhất, chịu được cache chết<br>❌ mỗi miss là 1 lần chạm DB, có race |
| **Read-through** | cache tự đi lấy từ DB | như trên | ✅ code app sạch<br>❌ cần cache hỗ trợ (NCache, HybridCache) |
| **Write-through** | | ghi cache **và** DB đồng bộ | ✅ cache luôn mới<br>❌ ghi chậm hơn, cache đầy dữ liệu không ai đọc |
| **Write-behind** | | ghi cache, flush DB sau | ✅ ghi cực nhanh<br>❌ **mất dữ liệu** nếu cache chết ⇒ chỉ dùng cho counter/metric |
| **Refresh-ahead** | làm mới trước khi hết hạn | | ✅ không có miss cho key nóng<br>❌ làm mới cả key không ai dùng |

**💻 Cache-aside chuẩn — 90% trường hợp:**
```csharp
public async Task<BlogDto?> GetAsync(string id, CancellationToken ct)
{
    var key = $"blog:v1:{id}";                                  // v1 = version của SCHEMA dto
    if (await _cache.GetAsync<BlogDto>(key, ct) is { } cached) return cached;

    var dto = (await _repo.FindByIdAsync(id, ct))?.Adapt<BlogDto>();
    if (dto is null)
    {
        await _cache.SetAsync(key, NullSentinel, TimeSpan.FromSeconds(30), ct);  // cache negative (CA-6)
        return null;
    }
    await _cache.SetAsync(key, dto, Jitter(TimeSpan.FromMinutes(10)), ct);       // TTL có jitter
    return dto;
}
```

**⚖️ Chi tiết ăn điểm**: `v1` trong key. Khi bạn đổi cấu trúc `BlogDto` và deploy, các bản ghi cũ
trong Redis sẽ deserialize lỗi hoặc thiếu field. Đổi `v1 → v2` là cách invalidate **toàn bộ** cache
của loại đó chỉ bằng một dòng code, không cần `FLUSHDB`.

---

## CA-3. Invalidation — xoá hay ghi đè, và thứ tự nào đúng

**❓ Vấn đề gốc**: cập nhật DB rồi làm gì với cache? Đây là chỗ sinh ra bug "dữ liệu cũ mãi không đổi".

**⚙️ Bốn thứ tự, chỉ một cái đúng:**

| Thứ tự | Vấn đề |
|---|---|
| ❌ Xoá cache → ghi DB | giữa 2 bước có reader nạp lại **giá trị cũ** ⇒ cache sai **vĩnh viễn** |
| ❌ Ghi DB → ghi đè cache bằng giá trị mới | 2 writer đồng thời ⇒ cache giữ giá trị của writer **cũ hơn** |
| ⚠️ Xoá cache → ghi DB → xoá lại sau 500ms (*delayed double delete*) | vá được đa số nhưng vẫn xác suất |
| ✅ **Ghi DB (commit) → xoá cache** | khe hở chỉ là khoảng thời gian rất ngắn giữa commit và delete |

**Vì sao xoá tốt hơn ghi đè?** Xoá là **idempotent** và không mang giá trị — hai lần xoá vẫn đúng.
Ghi đè mang theo giá trị ⇒ có thể ghi đè bằng dữ liệu cũ hơn.

**💻 Xoá SAU khi commit — trong project HW, đúng chỗ là domain event handler / outbox:**
```csharp
// ❌ SAI: xoá cache TRONG transaction. Nếu transaction rollback, cache đã bị xoá oan (chấp nhận được),
//    nhưng tệ hơn: reader nạp lại giá trị CŨ trước khi commit ⇒ cache sai cho tới hết TTL.
internal sealed class BlogUpdatedHandler(ICacheService cache) : INotificationHandler<BlogUpdatedDomainEvent>
{
    // Handler này chạy khi outbox được xử lý ⇒ CHẮC CHẮN sau commit
    public Task Handle(BlogUpdatedDomainEvent e, CancellationToken ct) =>
        cache.RemoveAsync($"blog:v1:{e.BlogId}", ct);
}
```

**⚙️ Ba chiến lược invalidation, chọn theo độ phức tạp:**
1. **TTL thuần** — không xoá gì, chấp nhận cũ tối đa TTL. Đơn giản nhất, đúng cho 70% trường hợp.
2. **Xoá theo key** — như trên. Vấn đề: một lần ghi ảnh hưởng nhiều key (chi tiết + danh sách +
   trang tìm kiếm) ⇒ phải biết hết các key liên quan.
3. **Versioned key / tag** — thay vì xoá N key, tăng một số version:
```csharp
// Key thật: "blogs:list:{tenantId}:v{version}:{page}"
// Khi có bài mới: INCR "ver:blogs:{tenantId}" ⇒ TOÀN BỘ key cũ trở nên không ai truy cập nữa,
// Redis tự dọn theo TTL. Không cần SCAN, không cần biết trước danh sách key.
var ver = await _redis.StringGetAsync($"ver:blogs:{tenantId}");
var key = $"blogs:list:{tenantId}:v{ver}:{page}";
```
**Tuyệt đối không dùng `KEYS blog:*` để xoá** — `KEYS` là O(N) và **chặn toàn bộ Redis** (CA-9).

---

## CA-4. Cache stampede (dogpile) — 1.000 request cùng miss một key

**❓ Vấn đề gốc**: key nóng hết hạn lúc 10:00:00. Ngay giây đó có 1.000 request ⇒ cả 1.000 cùng
thấy miss ⇒ cả 1.000 cùng chạy query nặng ⇒ DB sập. Đây là **nguyên nhân sự cố cache số 1**.

**⚙️ Bốn cách chống, có thể kết hợp:**

| Cách | Cơ chế | Đánh đổi |
|---|---|---|
| **Locking / singleflight** | chỉ 1 request đi tải, số còn lại chờ kết quả | những request kia phải chờ |
| **TTL jitter** | TTL = base ± random ⇒ không hết hạn đồng loạt | vẫn có stampede cho 1 key cực nóng |
| **Stale-while-revalidate** | trả bản cũ ngay, làm mới nền | phục vụ dữ liệu cũ trong chốc lát |
| **Probabilistic early expiration (XFetch)** | càng gần hết hạn, xác suất làm mới sớm càng cao | phức tạp hơn, cần lưu thời gian tính toán |

**💻 Singleflight trong process (bắt buộc phải biết viết):**
```csharp
private readonly ConcurrentDictionary<string, Lazy<Task<BlogDto?>>> _inflight = new();

public Task<BlogDto?> GetAsync(string id, CancellationToken ct)
{
    var key = $"blog:v1:{id}";
    // Lazy đảm bảo factory chỉ CHẠY một lần, dù GetOrAdd được gọi đồng thời nhiều lần (CC-14)
    var lazy = _inflight.GetOrAdd(key, k => new Lazy<Task<BlogDto?>>(
        () => LoadAndCacheAsync(k, id, ct), LazyThreadSafetyMode.ExecutionAndPublication));
    try { return lazy.Value; }
    finally { _ = lazy.Value.ContinueWith(_ => _inflight.TryRemove(key, out _)); }
}
```
Nó gộp N request **trong một instance**. Với N instance thì vẫn có N lần chạm DB — chấp nhận được
(N = 8, không phải 1.000). Muốn gộp toàn cục thì cần Redis lock (CC-6) — thường **không đáng**
vì đổi lấy thêm 1 RTT cho mọi request.

**💻 TTL jitter — một dòng, hiệu quả cực cao:**
```csharp
static TimeSpan Jitter(TimeSpan baseTtl) =>
    baseTtl * (0.8 + Random.Shared.NextDouble() * 0.4);   // ±20% ⇒ trải đều thời điểm hết hạn
```

**💻 Stale-while-revalidate — mẫu tốt nhất cho trang chủ / dữ liệu tổng hợp:**
```csharp
// Lưu kèm thời điểm "mềm" hết hạn; TTL cứng dài hơn nhiều
record Envelope<T>(T Value, DateTimeOffset SoftExpiry);

var env = await _cache.GetAsync<Envelope<T>>(key, ct);
if (env is not null)
{
    if (env.SoftExpiry < DateTimeOffset.UtcNow)
        _ = Task.Run(() => RefreshAsync(key, ct));   // làm mới nền, KHÔNG chặn người dùng
    return env.Value;                                 // trả ngay, kể cả hơi cũ
}
```

**⚖️ Nguyên tắc**: với dữ liệu đọc-nhiều, **phục vụ dữ liệu hơi cũ luôn tốt hơn phục vụ lỗi 500**.
Đây cũng là fallback lý tưởng cho circuit breaker (CC-12).

---

## CA-5. Penetration / Breakdown / Avalanche — ba "cơn bão cache" phải phân biệt

**⚙️ Ba vấn đề khác nhau, hay bị gộp làm một:**

| Tên | Hiện tượng | Nguyên nhân | Cách chống |
|---|---|---|---|
| **Penetration** (xuyên thủng) | truy vấn key **không tồn tại** ⇒ luôn miss ⇒ luôn chạm DB | crawler/tấn công dò id ngẫu nhiên | **cache giá trị null** (TTL ngắn 30–60s) + **Bloom filter** + validate id trước |
| **Breakdown** (sập điểm) | **một key cực nóng** hết hạn | TTL của key nóng | singleflight + không đặt TTL cho key nóng (chỉ chủ động làm mới) + stale-while-revalidate |
| **Avalanche** (tuyết lở) | **rất nhiều key** hết hạn cùng lúc, hoặc **Redis chết** | nạp cache hàng loạt cùng TTL / restart | **TTL jitter** + cache nhiều tầng (L1) + circuit breaker tới DB + warm-up sau deploy |

**💻 Bloom filter chống penetration (khi tập khoá hợp lệ rất lớn):**
```csharp
// Bloom filter cho false positive (nói "có thể có" khi không có) nhưng KHÔNG cho false negative.
// ⇒ trả lời "chắc chắn KHÔNG có" là đáng tin ⇒ chặn được ngay, không cần chạm DB.
if (!_bloom.MightContain(id)) return null;    // ~1% lọt qua vẫn bị cache-null chặn tiếp
```

**💻 Bảo vệ DB khi Redis chết (bắt buộc có, không phải tuỳ chọn):**
```csharp
try { return await _redis.GetAsync<T>(key, ct); }
catch (RedisConnectionException)
{
    // 1) Không ném lỗi ra ngoài — cache chết không được làm chết app
    // 2) NHƯNG phải giới hạn lượng request rơi xuống DB, nếu không chính bạn tự DDoS DB
    if (!await _dbFallbackLimiter.TryAcquireAsync()) throw new ServiceUnavailableException();
    return await LoadFromDbAsync(ct);
}
```

**⚖️ Câu hỏi ngược hay gặp**: *"Redis chết lúc 10h sáng thì sao?"*
→ Trả lời phải có đủ 3 ý: (a) app không sập vì lỗi cache; (b) DB được bảo vệ bằng
concurrency limiter / circuit breaker; (c) có L1 in-process hấp thụ phần lớn key nóng.

---

## CA-6. `IMemoryCache` (L1) — nhanh nhất, và nguy hiểm nhất

**⚙️ Cơ chế**: `ConcurrentDictionary` + hàng đợi hết hạn, dọn **lười** (khi truy cập hoặc theo
`ExpirationScanFrequency` mặc định 1 phút) — không có timer riêng cho từng entry.

**💻 Cấu hình bắt buộc — thiếu là OOM:**
```csharp
builder.Services.AddMemoryCache(o =>
{
    o.SizeLimit = 10_000;                                  // KHÔNG có đơn vị — do bạn tự định nghĩa
    o.CompactionPercentage = 0.25;                         // đầy thì dọn 25%
});

_memory.Set(key, dto, new MemoryCacheEntryOptions
{
    Size = 1,                                              // ❗ bắt buộc khi đã đặt SizeLimit
    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),   // luôn có giới hạn TUYỆT ĐỐI
    SlidingExpiration = TimeSpan.FromMinutes(1),           // sliding MỘT MÌNH = có thể sống mãi
    Priority = CacheItemPriority.Normal
});
```

**⚖️ Bốn bẫy chết người:**
1. **Không đặt `SizeLimit`** ⇒ cache lớn vô hạn ⇒ OOM (và cache lớn còn đẩy object lên Gen 2,
   làm GC gen 2 lâu hơn — xem file 12).
2. **Chỉ dùng `SlidingExpiration`** ⇒ key nóng không bao giờ hết hạn ⇒ dữ liệu cũ vĩnh viễn.
   Luôn kèm `AbsoluteExpiration`.
3. **Cache entity EF đang được track** ⇒ giữ luôn `DbContext` ⇒ leak nghiêm trọng.
   **Chỉ cache DTO/record bất biến.**
4. **Không đồng nhất giữa instance** ⇒ 8 pod = 8 bản cache khác nhau; user F5 thấy 2 kết quả.
   Chấp nhận được **chỉ khi** TTL đủ ngắn để nghiệp vụ không quan tâm.

---

## CA-7. `HybridCache` (.NET 9) — L1 + L2 + stampede protection đóng gói sẵn

**❓ Vấn đề gốc**: mọi team đều tự viết lại "L1 + L2 + singleflight + serialize + tag" và đều viết sai
ở đâu đó.

**⚙️ `HybridCache` (`Microsoft.Extensions.Caching.Hybrid`) làm sẵn:**
- L1 in-process + L2 `IDistributedCache`, tự chọn tầng.
- **Stampede protection** sẵn có (gộp các lời gọi trùng key trong process).
- **Tag-based invalidation** — `RemoveByTagAsync`, thứ `IDistributedCache` không có.
- Serializer cắm được, hỗ trợ `IBufferWriter` (ít cấp phát hơn).

**💻**
```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = cs);
builder.Services.AddHybridCache(o =>
{
    o.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration      = TimeSpan.FromMinutes(10),   // L2 (Redis)
        LocalCacheExpiration = TimeSpan.FromSeconds(30)  // L1 — NGẮN để giảm lệch giữa instance
    };
});

// Dùng: factory chỉ chạy khi cả L1 và L2 đều miss, và chỉ chạy MỘT lần cho các request trùng key
var dto = await _hybrid.GetOrCreateAsync(
    $"blog:v1:{id}",
    async token => (await _repo.FindByIdAsync(id, token))?.Adapt<BlogDto>(),
    tags: [$"blog", $"tenant:{tenantId}"],
    cancellationToken: ct);

await _hybrid.RemoveByTagAsync("tenant:" + tenantId, ct);   // invalidate cả nhóm
```

**⚖️ Điểm phải nêu**: `LocalCacheExpiration` chính là "cửa sổ không nhất quán" giữa các instance.
Đặt 30s nghĩa là bạn chấp nhận 8 pod có thể lệch nhau tối đa 30 giây. Với danh mục sản phẩm: ổn.
Với giá tiền đang khuyến mãi: không ổn — phải dùng L2-only hoặc thêm pub/sub để đẩy lệnh xoá L1.

---

## CA-8. Redis internals — vì sao một lệnh sai làm chậm cả cụm

**⚙️ Kiến trúc**: Redis xử lý lệnh trên **một luồng duy nhất** (event loop; từ 6.0 chỉ có
I/O threading cho việc đọc/ghi socket, còn *thực thi lệnh vẫn đơn luồng*).
Hệ quả trực tiếp: **mọi lệnh đều nguyên tử**, nhưng **một lệnh chậm chặn tất cả**.

**⚙️ Các lệnh cấm dùng trên production:**

| Lệnh | Độ phức tạp | Thay bằng |
|---|---|---|
| `KEYS pattern` | O(N) toàn bộ keyspace, **chặn** | `SCAN` (có cursor, không chặn) |
| `FLUSHALL` / `FLUSHDB` | O(N) | versioned key (CA-3) |
| `SMEMBERS` / `HGETALL` trên tập lớn | O(N) | `SSCAN`/`HSCAN`, hoặc chia nhỏ key |
| `DEL` một key khổng lồ | O(N) | `UNLINK` (giải phóng ở luồng nền) |
| `SORT`, `ZUNIONSTORE` tập lớn | O(N log N) | tính trước, lưu kết quả |

**💻 Chẩn đoán:**
```bash
redis-cli --latency-history            # theo dõi độ trễ theo thời gian
redis-cli slowlog get 10               # các lệnh chậm gần nhất
redis-cli --bigkeys                    # tìm key khổng lồ (thủ phạm số 1)
redis-cli --hotkeys                    # cần maxmemory-policy = allkeys-lfu
redis-cli info commandstats            # lệnh nào tốn tổng thời gian nhiều nhất
```

**⚙️ Pipeline & batching** — giảm số round-trip, đây mới là tối ưu lớn nhất trong thực tế:
```csharp
// ❌ 100 round-trip: 100 × 0.5ms = 50ms
foreach (var id in ids) result.Add(await _db.StringGetAsync($"blog:{id}"));

// ✅ 1 round-trip: ~0.6ms
var tasks = ids.Select(id => _db.StringGetAsync($"blog:{id}")).ToArray();  // StackExchange.Redis
await Task.WhenAll(tasks);                                                // tự gộp pipeline
// hoặc dùng thẳng: await _db.StringGetAsync(keys.Select(k => (RedisKey)k).ToArray()) — MGET
```

**⚙️ Redis Cluster**: 16.384 hash slot chia cho các node. Lệnh nhiều key phải **cùng slot**
(nếu không sẽ lỗi `CROSSSLOT`) ⇒ dùng **hash tag** để ép cùng slot:
```
user:{12345}:profile   và   user:{12345}:settings   → cùng slot vì phần trong {} giống nhau
```
Nhưng cẩn thận: hash tag quá "to" (ví dụ `{tenant1}`) sẽ dồn cả tenant vào một node ⇒ hot node (CC-11).

**⚖️ Persistence & failover — ảnh hưởng trực tiếp tới đúng/sai của app:**
- **RDB** (snapshot định kỳ): mất dữ liệu từ snapshot cuối; `fork()` gây tăng RAM đột biến.
- **AOF** (ghi lệnh, `appendfsync everysec`): mất ≤ 1s.
- Replication **bất đồng bộ** ⇒ khi failover, **các lệnh ghi cuối cùng có thể mất**.
  ⇒ Nếu bạn dùng Redis làm distributed lock hoặc idempotency store, failover = mất tính đúng đắn
  (đúng như phân tích ở CC-6). Redis là **cache**, không phải nguồn sự thật.

---

## CA-9. Thiết kế key và payload — nơi tiết kiệm được nhiều nhất

**⚙️ Quy ước key tốt:**
```
{app}:{entity}:{schemaVersion}:{id}[:{variant}]
hw:blog:v1:0193f2a1-...           hw:blog:list:v1:tenant42:page3:size20:sortName
```
- Có **tiền tố app** (nhiều app dùng chung Redis).
- Có **schema version** ⇒ deploy đổi DTO chỉ cần tăng version (CA-2).
- **Không** nhét dữ liệu nhạy cảm vào key (key hiện trong `MONITOR`, log, metric).
- Key dài tốn RAM thật: 1 triệu key × 50 byte thừa = 50MB.

**⚙️ Payload:**

| Cách | Kích thước | CPU | Ghi chú |
|---|---|---|---|
| JSON (STJ) | 1× | thấp | mặc định, dễ debug |
| JSON + Gzip/Brotli | 0.2–0.4× | trung bình | đáng làm khi payload > 4–8KB |
| MessagePack / protobuf | 0.3–0.5× | thấp nhất | nhanh nhất, khó debug |

**⚖️ Ba nguyên tắc:**
1. **Không cache object khổng lồ** (danh sách 10.000 phần tử). Redis là single-thread ⇒ serialize
   và truyền một value 5MB chặn mọi client khác. Chia trang trước rồi mới cache.
2. **Cache DTO đã chiếu (projected)**, không cache entity đầy đủ — vừa nhỏ hơn, vừa không kéo theo
   navigation property và `DbContext`.
3. **Cache kết quả đắt, không cache truy vấn rẻ.** Cache một `SELECT` theo primary key có index
   thường **không đáng** — bạn đổi 0.3ms lấy 0.5ms + rủi ro dữ liệu cũ.

---

## CA-10. Eviction: LRU, LFU, TTL — và "cache" khác "kho lưu trữ"

**⚙️ `maxmemory-policy` của Redis:**

| Policy | Hành vi | Dùng khi |
|---|---|---|
| `noeviction` | đầy thì lỗi khi ghi | Redis dùng làm **hàng đợi/nguồn sự thật** (phải cảnh báo dung lượng!) |
| `allkeys-lru` | bỏ key ít dùng gần đây nhất | cache thuần, mặc định hợp lý |
| `allkeys-lfu` | bỏ key **ít dùng thường xuyên** | tốt hơn LRU khi có "quét một lần" (scan pollution) |
| `volatile-*` | chỉ bỏ key **có TTL** | khi trộn cache và dữ liệu bền trong cùng instance ⚠️ |

**⚖️ Quy tắc quan trọng nhất của phần cache:**
> **Mọi thứ trong cache đều có thể biến mất bất cứ lúc nào** (eviction, failover, restart, OOM).
> Nếu mất đi mà nghiệp vụ sai ⇒ nó không phải cache, và Redis là chỗ sai để đặt nó.

Ví dụ đặt sai chỗ: giỏ hàng chỉ nằm trong Redis không TTL; session duy nhất trong Redis
`allkeys-lru` (bị evict = đăng xuất ngẫu nhiên); trạng thái saga trong Redis.
Trộn cache và dữ liệu bền trong **cùng một instance** là sai lầm vận hành phổ biến — hãy tách
instance/DB index riêng, và policy riêng.

---

## CA-11. HTTP caching & CDN — tầng cache rẻ nhất mà hay bị bỏ quên

**⚙️ Cache tốt nhất là cache **không bao giờ chạm tới server của bạn**.

```http
Cache-Control: public, max-age=60, stale-while-revalidate=300, stale-if-error=86400
ETag: "a1b2c3"
Vary: Accept-Encoding, Accept-Language
```
- `max-age=60` — client dùng lại 60s, **không gửi request nào**.
- `stale-while-revalidate=300` — CDN trả bản cũ ngay và làm mới nền (giống CA-4).
- `stale-if-error=86400` — **backend chết thì CDN vẫn phục vụ bản cũ 24h**. Cực kỳ giá trị.
- `ETag` + `If-None-Match` ⇒ **304 Not Modified**: vẫn tốn 1 round-trip nhưng không tốn băng thông
  và không phải render lại.
- `Vary` sai là bug bảo mật: thiếu `Vary: Authorization` ⇒ CDN phục vụ dữ liệu của user A cho user B.

**💻 Output caching (.NET 8) — thay cho `ResponseCaching` cũ:**
```csharp
builder.Services.AddOutputCache(o =>
{
    o.AddPolicy("blogs", b => b
        .Expire(TimeSpan.FromSeconds(30))
        .SetVaryByQuery("page", "size")
        .Tag("blogs"));                       // ⬅️ tag để xoá theo nhóm
});
app.UseOutputCache();

app.MapGet("/api/blog", handler).CacheOutput("blogs");

// Sau khi có bài mới (trong domain event handler chạy sau commit):
await outputCacheStore.EvictByTagAsync("blogs", ct);
```
Khác biệt phải nêu: **`ResponseCaching`** tuân theo header HTTP và không cache response có
`Authorization`; **`OutputCaching`** do server chủ động quyết định, cache được cả response
đã xác thực (nên phải tự `VaryByValue` theo user, nếu không sẽ **lộ dữ liệu chéo user**).

**⚖️ Quy tắc phân loại:**
- Ảnh, JS, CSS có hash trong tên → `max-age=31536000, immutable`.
- API công khai (danh mục, bài viết) → `max-age` ngắn + `stale-while-revalidate` + CDN.
- API cá nhân hoá → `Cache-Control: private, no-store` (hoặc `private, max-age` rất ngắn).
- Bất kỳ thứ gì có `Set-Cookie` hoặc dữ liệu riêng → **không bao giờ** `public`.

---

## CA-12. Cache đa tầng và bài toán đồng bộ L1

**❓ Vấn đề gốc**: L1 (in-process) nhanh nhất nhưng khi dữ liệu đổi, **7 instance kia không biết**.

**⚙️ Ba mức giải quyết, tăng dần chi phí:**
1. **TTL L1 rất ngắn (5–30s)** — chấp nhận lệch tối đa TTL. Đơn giản, đủ cho 90% trường hợp.
2. **Pub/Sub đẩy lệnh xoá** — instance ghi phát `INVALIDATE key` qua Redis pub/sub, các instance
   khác xoá L1 của mình. Độ trễ ~ms nhưng pub/sub là **at-most-once** (instance đang restart sẽ
   bỏ lỡ) ⇒ **vẫn phải có TTL** làm lưới an toàn.
3. **Redis client-side caching (RESP3 tracking)** — Redis chủ động báo invalidate cho client đã
   đọc key đó. Chuẩn xác nhất, nhưng cần phiên bản/thư viện hỗ trợ.

**💻 Mẫu 2:**
```csharp
// Publisher — sau khi commit
await _sub.PublishAsync(RedisChannel.Literal("cache:inval"), key);

// Subscriber — mỗi instance khi khởi động
await _sub.SubscribeAsync(RedisChannel.Literal("cache:inval"),
    (_, msg) => _memory.Remove((string)msg!));
```

**⚖️ Nói thẳng khi phỏng vấn**: *"L1 luôn kèm một cửa sổ không nhất quán. Tôi chọn độ dài cửa sổ đó
theo nghiệp vụ, và tôi **ghi rõ nó ra** thay vì giả vờ là cache luôn đúng."*

---

## CA-13. Redis không chỉ là key-value — dùng đúng cấu trúc dữ liệu

**⚙️ Các cấu trúc và bài toán tương ứng:**

| Cấu trúc | Bài toán | Lệnh chính |
|---|---|---|
| String | cache đối tượng, counter | `GET/SET/INCR/SETNX` |
| Hash | object nhiều field, cập nhật lẻ | `HSET/HGET/HINCRBY` |
| **ZSET** | bảng xếp hạng, hàng đợi ưu tiên, sliding window rate limit | `ZADD/ZRANGE/ZREMRANGEBYSCORE` |
| Set | tag, dedup, "đã xem" | `SADD/SISMEMBER/SINTER` |
| List | hàng đợi đơn giản | `LPUSH/BRPOP` |
| **Stream** | hàng đợi có consumer group, có ack | `XADD/XREADGROUP/XACK` |
| HyperLogLog | đếm unique xấp xỉ (sai số ~0.81%), 12KB cố định | `PFADD/PFCOUNT` |
| Bitmap | trạng thái nhị phân theo id (điểm danh, feature flag) | `SETBIT/BITCOUNT` |
| GEO | tìm quanh đây | `GEOADD/GEOSEARCH` |

**💻 Bảng xếp hạng thời gian thực — 2 lệnh, không cần DB:**
```csharp
await _db.SortedSetIncrementAsync("lb:2026-09", userId, score);              // O(log N)
var top = await _db.SortedSetRangeByRankWithScoresAsync("lb:2026-09", 0, 9, Order.Descending);
var myRank = await _db.SortedSetRankAsync("lb:2026-09", userId, Order.Descending);
```

**💻 Sliding-window rate limit chính xác bằng ZSET (bổ sung cho CC-8):**
```lua
redis.call('ZREMRANGEBYSCORE', KEYS[1], 0, ARGV[1] - ARGV[2])   -- xoá ngoài cửa sổ
local n = redis.call('ZCARD', KEYS[1])
if n >= tonumber(ARGV[3]) then return 0 end
redis.call('ZADD', KEYS[1], ARGV[1], ARGV[4])                   -- thêm timestamp request
redis.call('PEXPIRE', KEYS[1], ARGV[2])
return 1
```

**💻 Đếm unique lớn — HyperLogLog thay vì Set:**
```
Set 10 triệu user id  ≈ 400MB+
HLL 10 triệu user id  = 12KB, sai số ~0.81%   ⇒ đúng cho "số lượt truy cập duy nhất"
```

**⚖️ Điểm ăn tiền**: nêu được *"Redis đơn luồng nên mọi thao tác trên các cấu trúc này đều nguyên tử
— đó là lý do nó thay thế được rất nhiều lock ứng dụng"* (nối lại với CC-6, CC-17).

---

## CA-14. Precomputation vs caching — khi cache không phải câu trả lời

**❓ Vấn đề gốc**: có những truy vấn dù cache vẫn tệ — vì mỗi user một kết quả (cache hit ≈ 0),
hoặc vì lần miss quá đắt (30 giây).

**⚙️ Bốn lựa chọn thay cache:**
1. **Materialized view / bảng tổng hợp** — job tính trước theo lịch. Đọc luôn là O(1), không có miss.
2. **Read model của CQRS** — cập nhật khi có domain event, không tính lại từ đầu.
   (Đây chính là lý do project HW dùng CQRS: mô hình đọc có thể phi chuẩn hoá tuỳ ý.)
3. **Fan-out on write** — feed mạng xã hội: khi A đăng bài thì **ghi sẵn** vào feed của follower.
   Đọc rất nhanh; ghi rất đắt với tài khoản triệu follower ⇒ mô hình lai (fan-out cho tài khoản
   thường, fan-out-on-read cho celebrity).
4. **Index đúng** — rất nhiều "cần cache" thực chất là "thiếu index" hoặc "N+1 query" (file 08).

**⚖️ Thứ tự kiểm tra trước khi thêm cache** (nói được thứ tự này rất ghi điểm):
```
1. Có N+1 query không?              → sửa Include/projection
2. Có index phù hợp chưa?           → EXPLAIN, xem type/rows/Extra
3. Có SELECT * và over-fetch không? → chiếu (projection) đúng cột cần
4. Có thể tính trước không?         → materialized view / read model
5. ─── ĐẾN ĐÂY MỚI THÊM CACHE ───
```
*"Cache một truy vấn tệ chỉ giấu vấn đề đi và làm nó khó gỡ hơn khi cache miss."*

---

## CA-15. Đo lường & vận hành cache

**⚙️ Bốn chỉ số bắt buộc có dashboard:**
1. **Hit ratio theo từng nhóm key** (không phải tổng thể — tổng thể che mất chỗ hỏng).
2. **Độ trễ p99 của chính lời gọi cache** — Redis chậm bất thường thường là dấu hiệu big key/hot key.
3. **Tỷ lệ eviction & memory usage** — eviction cao = cache quá nhỏ hoặc TTL quá dài.
4. **Tải xuống DB khi cache miss** — chính là "chuyện gì xảy ra nếu cache biến mất".

**💻 Đo bằng `Meter` (OpenTelemetry-friendly):**
```csharp
private static readonly Meter Meter = new("HW.Cache");
private static readonly Counter<long> Hits   = Meter.CreateCounter<long>("cache.hits");
private static readonly Counter<long> Misses = Meter.CreateCounter<long>("cache.misses");

if (cached is not null) Hits.Add(1,   new KeyValuePair<string, object?>("region", "blog"));
else                    Misses.Add(1, new KeyValuePair<string, object?>("region", "blog"));
```

**⚖️ Ba quy trình vận hành phải chuẩn bị trước:**
- **Warm-up sau deploy**: pod mới có L1 rỗng ⇒ nếu rolling deploy 8 pod cùng lúc thì DB ăn trọn
  cú miss. Cách chống: nạp trước các key nóng trong `IHostedService` khởi động **trước khi**
  readiness probe trả OK.
- **Kill switch**: cờ cấu hình tắt cache ngay lập tức khi nghi ngờ cache phục vụ dữ liệu sai.
- **Kịch bản "Redis chết"**: phải có bài test thật (chaos) — không chỉ là niềm tin.

---

## CA-16. Ba câu hỏi ngược kinh điển về cache (chuẩn bị sẵn)

**1. *"Cache và DB lệch nhau, bạn phát hiện bằng cách nào?"***
> Ba lớp: (a) TTL luôn có giới hạn trên ⇒ mọi sai lệch **tự hết** sau tối đa TTL — đây là lý do
> tôi không bao giờ cache vĩnh viễn; (b) job đối soát định kỳ lấy mẫu so cache với DB, báo tỉ lệ lệch;
> (c) log invalidation kèm correlation id để truy ngược khi có báo lỗi.

**2. *"Vì sao xoá cache chứ không cập nhật cache?"***
> Vì xoá là idempotent và không mang giá trị. Hai writer đồng thời mà cùng *ghi* cache thì cache có
> thể giữ giá trị của writer cũ hơn — vĩnh viễn tới hết TTL. Xoá thì lần đọc kế tiếp luôn lấy
> giá trị mới nhất từ DB. Chi phí là một lần cache miss — quá rẻ so với dữ liệu sai.

**3. *"Hệ thống của bạn phụ thuộc Redis tới mức nào?"***
> Cache là tuỳ chọn: mọi đường đi đều có nhánh fallback tới DB, có concurrency limiter để không
> tự DDoS DB, và có L1 hấp thụ key nóng. Nhưng nếu tôi dùng Redis cho **rate limit hoặc
> idempotency**, thì đó không còn là cache — Redis failover có thể mất vài lệnh ghi cuối,
> nên phần đúng/sai vẫn phải neo ở DB (unique constraint).

---

## ✅ Checklist tự kiểm tra — Phần 17

- [ ] Tính được `T_tb = h·T_hit + (1−h)·T_miss` và giải thích vì sao 90% → 99% quan trọng.
- [ ] So sánh cache-aside / read-through / write-through / write-behind và chọn đúng cho từng ca.
- [ ] Nêu đúng thứ tự "commit DB → xoá cache" và giải thích vì sao 3 thứ tự kia sai.
- [ ] Viết được singleflight bằng `ConcurrentDictionary<string, Lazy<Task<T>>>`.
- [ ] Phân biệt penetration / breakdown / avalanche và cách chống từng cái.
- [ ] Nêu 4 bẫy của `IMemoryCache` (SizeLimit, sliding-only, cache entity tracked, lệch instance).
- [ ] Giải thích `HybridCache` giải quyết gì và `LocalCacheExpiration` là đánh đổi gì.
- [ ] Giải thích vì sao Redis đơn luồng ⇒ `KEYS`/big key là thảm hoạ; nêu cách chẩn đoán.
- [ ] Dùng đúng ZSET/HLL/Stream cho leaderboard/đếm unique/hàng đợi.
- [ ] Nêu `stale-while-revalidate`, `stale-if-error`, ETag/304 và bẫy `Vary`.
- [ ] Nói được thứ tự kiểm tra 5 bước **trước khi** quyết định thêm cache.

⬅️ Quay lại [Mục lục](interview.NET.md) | Tiếp: [Phần 18 — Messaging ➡️](interview.NET.18-Messaging-Scaling.md)
