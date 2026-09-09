# Phần 18 — Messaging: RabbitMQ, Kafka và kiến trúc hướng sự kiện chịu tải

[⬅️ Phần 17 — Caching](interview.NET.17-Caching-Performance.md) | [⬅️ Về mục lục](interview.NET.md) | Tiếp theo: [Phần 19 — Case study hệ thống lớn ➡️](interview.NET.19-System-Design-Cases.md)

> Khung mỗi câu: **❓ Vấn đề gốc → ⚙️ Cơ chế bên dưới → 💻 Code/đo đạc → ⚖️ Hệ quả thực chiến**.
>
> Project HW đã có sẵn `IMessageBus` / `IMessageHandler<T>` với provider chọn theo cấu hình
> (`None` / `RabbitMq` / `Kafka` / `MassTransit`), outbox bật mặc định và saga event-sourced.
> File này giải thích **vì sao** các quyết định đó đúng, và điều gì xảy ra ở quy mô lớn.

---

## 🗺️ Bản đồ: message đi từ đâu tới đâu

```
Command handler
   │  PublishAsync()  ── Messaging:UseOutbox = true ⇒ CHỈ ghi 1 dòng vào OutboxMessages
   ▼
[ DB transaction COMMIT ]                       ← ranh giới nguyên tử duy nhất (TX-7)
   │
   ▼  OutboxProcessor (BackgroundService, ≤ ~10s)
Broker
   ├─ RabbitMQ:  exchange → binding → queue → (prefetch) → consumer → ack
   └─ Kafka:     topic → partition (theo key) → segment → consumer group → commit offset
   │
   ▼
IMessageHandler<T>  ── inbox dedup ── nghiệp vụ ── (retry → delay queue → DLQ)
```

**Câu chốt phỏng vấn:** *"Messaging không làm hệ thống nhanh hơn — nó **dời việc chậm ra khỏi
đường phản hồi của người dùng** và **hấp thụ đỉnh tải**. Cái giá phải trả là eventual consistency,
trùng lặp, và mất thứ tự — cả ba đều phải được thiết kế, không phải được hy vọng."*

---

## MQ-1. Vì sao dùng message queue? Bốn lý do, và ba cái giá

**⚙️ Bốn lý do (nêu đúng tên gọi sẽ ghi điểm):**
1. **Load leveling** — đỉnh 10.000 req/s đổ vào queue, consumer xử lý đều 1.000/s.
   Không có queue thì phải chuẩn bị hạ tầng cho **đỉnh**, có queue thì chuẩn bị cho **trung bình**.
2. **Decoupling thời gian** — producer không cần consumer đang sống.
3. **Decoupling không gian** — thêm consumer mới (analytics, search index) không cần sửa producer.
4. **Cô lập lỗi** — service gửi email chết thì đơn hàng vẫn tạo được.

**⚖️ Ba cái giá luôn phải nói kèm:**
1. **Eventual consistency** — user tạo đơn xong không thấy ngay ở màn hình khác.
2. **At-least-once** ⇒ trùng lặp ⇒ **bắt buộc idempotent** (TX-11).
3. **Vận hành phức tạp gấp đôi** — thêm broker, thêm DLQ, thêm lag monitoring, thêm bài toán
   thứ tự và schema evolution.

**⚖️ Khi nào **không** nên dùng**: nghiệp vụ cần kết quả ngay (đăng nhập, kiểm tra số dư),
hoặc chỉ có 2 service và gọi HTTP đồng bộ là đủ. *"Message queue không phải huy hiệu kiến trúc."*

---

## MQ-2. Queue vs Topic vs Log — ba mô hình khác nhau về bản chất

| | **Queue** (RabbitMQ) | **Pub/Sub** (fanout) | **Log** (Kafka) |
|---|---|---|---|
| Message sau khi đọc | **bị xoá** | bị xoá theo từng subscriber | **giữ lại** theo retention |
| Nhiều consumer | chia nhau (competing consumers) | mỗi người một bản | chia theo partition, mỗi group đọc độc lập |
| Đọc lại quá khứ | ❌ không | ❌ không | ✅ tua offset về |
| Thêm consumer mới | không thấy message cũ | không thấy message cũ | ✅ **replay từ đầu** |
| Đơn vị song song | queue / consumer | | **partition** (giới hạn cứng) |

**⚖️ Hệ quả kiến trúc quan trọng nhất**: Kafka là **log có thể replay** ⇒ dùng được cho
event sourcing, backfill, dựng lại read model, thêm consumer sau này.
RabbitMQ là **hàng đợi công việc** ⇒ dùng cho task, routing linh hoạt, ưu tiên, delay.

*"Chọn Kafka khi message là **sự thật lịch sử** cần giữ lại; chọn RabbitMQ khi message là
**mệnh lệnh công việc** cần được ai đó thực hiện rồi thôi."*

---

## MQ-3. RabbitMQ internals — exchange, binding, prefetch, ack

**⚙️ Luồng định tuyến**: producer **không** gửi vào queue, mà gửi vào **exchange**;
exchange dùng **binding** để quyết định queue nào nhận.

| Exchange | Định tuyến theo | Dùng cho |
|---|---|---|
| `direct` | routing key khớp chính xác | gửi tới một loại consumer |
| `topic` | mẫu `order.*.created`, `#` | phổ biến nhất cho event |
| `fanout` | bỏ qua key, gửi tất cả | broadcast |
| `headers` | theo header | hiếm dùng |

**⚙️ `prefetch` (QoS) — tham số quan trọng nhất và hay bị đặt sai nhất:**
```csharp
await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 20, global: false);
```
- `prefetch = 1` — công bằng tuyệt đối, nhưng mỗi message tốn 1 round-trip ⇒ throughput thấp.
- `prefetch = 0` (không giới hạn) — broker đẩy hết vào bộ nhớ consumer ⇒ **OOM**, và message bị
  "giam" ở một consumer chậm trong khi consumer khác rảnh.
- **Quy tắc**: `prefetch ≈ số worker song song × 2`, cộng thêm nếu xử lý rất nhanh.
  Đây chính là **backpressure** ở tầng broker (CC-9).

**⚙️ Ack — ba chế độ:**
```csharp
// ❌ autoAck: true  → message coi như xong NGAY khi gửi tới consumer ⇒ crash = MẤT message
await channel.BasicConsumeAsync(queue, autoAck: false, consumer);

// Xử lý xong mới ack
await channel.BasicAckAsync(ea.DeliveryTag, multiple: false);

// Lỗi tạm thời → requeue để thử lại
await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
// Lỗi vĩnh viễn (poison) → KHÔNG requeue ⇒ rơi vào DLX
await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
```
**Bẫy chết người**: `requeue: true` cho message hỏng ⇒ vòng lặp vô hạn tiêu 100% CPU
("poison message loop"). Luôn đếm số lần thử (header `x-death` hoặc counter riêng) và
chuyển sang DLQ sau N lần.

**⚙️ Độ bền — phải đủ **cả ba** thì message mới sống sót khi broker restart:**
```
1. Queue durable: true
2. Message persistent (delivery mode = 2)
3. Publisher confirms (đợi broker xác nhận đã ghi)
```
Thiếu bất kỳ cái nào ⇒ mất message âm thầm. Và **publisher confirm** là thứ hay bị quên nhất:
không có nó, `BasicPublish` chỉ ghi vào socket rồi trả về ngay.

**⚙️ Quorum queue** (thay cho mirrored queue đã lỗi thời): nhân bản theo Raft trên ≥3 node,
chịu được mất 1 node. Đánh đổi: chậm hơn classic queue, tốn RAM/đĩa hơn.
Với queue rất dài, dùng **lazy queue** (ghi thẳng đĩa) để không bị OOM khi consumer tụt lại xa.

---

## MQ-4. Kafka internals — partition, offset, ISR, consumer group

**⚙️ Mô hình lưu trữ:**
```
Topic "orders"
 ├─ Partition 0: [0][1][2][3][4]...   ← file segment, CHỈ ghi thêm (append-only)
 ├─ Partition 1: [0][1][2]...
 └─ Partition 2: [0][1][2][3]...
        ▲ mỗi partition là một log CÓ THỨ TỰ; giữa các partition KHÔNG có thứ tự
```

**⚙️ Vì sao Kafka nhanh** (3 lý do, nên nói đủ):
1. **Ghi tuần tự vào đĩa** — nhanh hơn ghi ngẫu nhiên hàng chục lần, tận dụng page cache của OS.
2. **Zero-copy** (`sendfile`) — dữ liệu đi thẳng từ page cache ra socket, không qua user space.
3. **Batch + nén** — producer gom nhiều record thành một batch, nén cả batch (lz4/zstd).

**⚙️ Độ bền — `acks` quyết định tất cả:**

| `acks` | Ý nghĩa | Rủi ro |
|---|---|---|
| `0` | không chờ | mất message thoải mái |
| `1` | leader đã ghi | leader chết trước khi replica kịp sao ⇒ **mất** |
| `all` + `min.insync.replicas=2` | leader + ≥1 follower trong ISR đã ghi | an toàn; ghi chậm hơn |

Kèm `enable.idempotence=true` (mặc định từ 3.0) để producer retry không tạo bản trùng trong Kafka
(mỗi producer có PID + sequence number).

**⚙️ Consumer group & rebalance:**
- Một partition tại một thời điểm chỉ được **một** consumer trong group đọc
  ⇒ **số consumer hữu ích ≤ số partition**. Thêm consumer thứ 13 vào topic 12 partition = ngồi chơi.
- **Rebalance** xảy ra khi consumer vào/ra hoặc quá `max.poll.interval.ms`:
  - `eager` (cũ): **stop-the-world**, toàn group ngừng.
  - `cooperative-sticky` (nên dùng): chỉ chuyển phần partition cần chuyển.
- **Nguyên nhân rebalance số 1**: xử lý một batch quá lâu, vượt `max.poll.interval.ms` (mặc định 5 phút)
  ⇒ broker tưởng consumer chết ⇒ rebalance ⇒ message được giao lại ⇒ **xử lý trùng**.
  Sửa: giảm `max.poll.records`, hoặc tách việc nặng sang worker khác và poll đều đặn.

**⚙️ Offset commit:**
```csharp
// ❌ enable.auto.commit = true: commit theo chu kỳ ⇒ có thể commit TRƯỚC khi xử lý xong ⇒ MẤT message
// ✅ Commit thủ công SAU khi xử lý xong
var cr = consumer.Consume(ct);
await HandleAsync(cr.Message.Value, ct);
consumer.StoreOffset(cr);          // commit theo lô, do auto.commit.interval flush
```
Offset commit sau xử lý ⇒ **at-least-once**. Muốn at-most-once thì commit trước (hiếm khi muốn).

**⚙️ Retention & compaction:**
- `retention.ms` — giữ theo thời gian (7 ngày mặc định) hoặc theo dung lượng.
- **Log compaction** (`cleanup.policy=compact`) — chỉ giữ **bản ghi mới nhất cho mỗi key**
  ⇒ topic trở thành "snapshot trạng thái hiện tại", dùng để dựng lại cache/read model từ đầu.

---

## MQ-5. Delivery semantics — và vì sao "exactly-once" luôn có dấu sao

**⚙️ Ba mức:**

| Mức | Cách đạt | Rủi ro |
|---|---|---|
| **At-most-once** | ack/commit **trước** khi xử lý | mất message |
| **At-least-once** | ack/commit **sau** khi xử lý | trùng lặp — **mặc định nên chọn** |
| **Exactly-once** | không tồn tại xuyên hệ thống | chỉ có trong phạm vi một hệ thống |

**⚙️ Kafka EOS (Exactly-Once Semantics)** hoạt động khi luồng là **Kafka → xử lý → Kafka**:
producer transaction ghi cả output record lẫn offset commit trong **một** transaction của Kafka.
Nhưng khi sink là MySQL hoặc một API bên ngoài, transaction của Kafka **không** bao trùm được nó
⇒ lại là dual write (TX-7) ⇒ vẫn phải idempotent ở sink.

**💻 Idempotent consumer — mẫu chuẩn trong project HW:**
```csharp
public sealed class OrderCreatedHandler(IUnitOfWork uow, AppDbContext db) : IMessageHandler<OrderCreated>
{
    public async Task HandleAsync(OrderCreated msg, CancellationToken ct) =>
        await uow.ExecuteAsync(async () =>
        {
            // Chốt dedup nằm CÙNG transaction với tác động nghiệp vụ (TX-11)
            db.InboxMessages.Add(new InboxMessage(msg.MessageId));
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException e) when (e.IsUniqueViolation()) { return; }   // đã xử lý rồi

            await ApplyAsync(msg, ct);
        });
}
```

**⚖️ Câu trả lời chuẩn khi bị hỏi "làm sao exactly-once?"**:
> *"Tôi không cố đạt exactly-once. Tôi chọn at-least-once và làm consumer idempotent, vì đó là
> thứ duy nhất đứng vững qua crash, timeout và retry. Bản ghi dedup phải nằm cùng transaction với
> tác động nghiệp vụ — nếu tách ra Redis thì tôi chỉ đang tạo thêm một dual write mới."*

---

## MQ-6. Thứ tự message — đảm bảo tới đâu, và cái giá

**⚙️ Sự thật phải nắm:**
- **Kafka**: đảm bảo thứ tự **trong một partition**. Không có thứ tự toàn cục.
- **RabbitMQ**: đảm bảo thứ tự trong một queue **chỉ khi có 1 consumer và không requeue**.
  Có 2 consumer (hoặc có retry) là thứ tự vỡ ngay.

**⚙️ Muốn có thứ tự theo thực thể ⇒ dùng partition key = id thực thể:**
```csharp
// Mọi message của cùng một OrderId đi vào cùng một partition ⇒ tuần tự với nhau,
// nhưng vẫn song song với các Order khác. Đây là mẫu đúng cho 95% trường hợp.
await producer.ProduceAsync("orders",
    new Message<string, string> { Key = orderId, Value = json }, ct);
```

**⚙️ Ba xung đột kinh điển giữa thứ tự và các yêu cầu khác:**
1. **Thứ tự vs throughput** — 1 partition = tuần tự = trần throughput bằng 1 consumer.
2. **Thứ tự vs retry** — message #5 lỗi, bạn retry sau 30s trong khi #6 đã xử lý xong ⇒ **vỡ thứ tự**.
   Chọn một trong hai: hoặc **chặn cả partition** cho tới khi #5 xong (an toàn, chậm),
   hoặc chấp nhận vỡ thứ tự và thiết kế message **giao hoán được**.
3. **Thứ tự vs rebalance** — rebalance khiến message được giao lại; kết hợp với dedup thì
   thứ tự có thể khác lần đầu.

**⚙️ Cách né bài toán thứ tự hoàn toàn (nên ưu tiên):**
```csharp
// Message mang theo VERSION; consumer bỏ qua message cũ hơn trạng thái hiện có.
if (msg.Version <= entity.Version) return;                 // đã áp dụng phiên bản mới hơn
entity.ApplyFrom(msg);
// Hoặc: message mang TOÀN BỘ trạng thái mới (state-carried event) thay vì delta
//       ⇒ áp dụng nhiều lần, sai thứ tự, vẫn hội tụ đúng.
```
*"Thiết kế message giao hoán/idempotent rẻ hơn nhiều so với việc bắt hạ tầng đảm bảo thứ tự."*

---

## MQ-7. Retry, backoff và Dead Letter Queue — thiết kế đầy đủ

**❓ Vấn đề gốc**: message lỗi. Retry ngay lập tức ⇒ lỗi tiếp ⇒ vòng lặp CPU 100%.
Không retry ⇒ mất việc. Cần **phân loại lỗi**.

**⚙️ Ba loại lỗi và cách xử lý khác nhau hoàn toàn:**

| Loại | Ví dụ | Xử lý |
|---|---|---|
| **Tạm thời** (transient) | mất mạng, DB deadlock, 503 | retry có backoff + jitter |
| **Vĩnh viễn** (poison) | JSON hỏng, thiếu field bắt buộc, vi phạm nghiệp vụ | **không retry** ⇒ DLQ ngay |
| **Phụ thuộc chết** | service đích down 10 phút | circuit breaker + retry cửa sổ dài, hoặc dừng consumer |

**💻 Delay queue bằng TTL + DLX của RabbitMQ (không có delay plugin thì đây là cách chuẩn):**
```
retry.5s   (x-message-ttl=5000,   x-dead-letter-exchange=work)   ─┐
retry.30s  (x-message-ttl=30000,  x-dead-letter-exchange=work)    ├─ hết TTL thì tự quay lại work
retry.5m   (x-message-ttl=300000, x-dead-letter-exchange=work)   ─┘
work → (lỗi lần n) → retry.{bậc n} → work → ... → sau N lần → dlq.orders (không TTL)
```

**💻 Kafka không có DLQ sẵn ⇒ tự làm bằng topic:**
```csharp
catch (Exception ex) when (IsTransient(ex) && attempt < MaxAttempts)
{
    await producer.ProduceAsync($"orders.retry.{attempt + 1}", WithHeaders(msg, attempt + 1), ct);
}
catch (Exception ex)
{
    await producer.ProduceAsync("orders.dlq", WithHeaders(msg, error: ex.ToString()), ct);
    // ⬅️ Vẫn commit offset để KHÔNG chặn partition (đây là quyết định có chủ đích, phải nói rõ)
}
```

**⚖️ Ba yêu cầu bắt buộc của một DLQ dùng được (không có = DLQ chỉ là thùng rác):**
1. **Cảnh báo khi DLQ có message** — DLQ im lặng nghĩa là bạn đang mất dữ liệu mà không biết.
2. **Giữ đủ ngữ cảnh** — payload gốc, exception, số lần thử, thời điểm, correlation id, topic gốc.
3. **Công cụ replay** — nút bấm/CLI để đẩy message trở lại sau khi đã sửa bug. Không có nó thì
   sửa bug xong vẫn không cứu được dữ liệu.

---

## MQ-8. Consumer lag — chỉ số sức khoẻ quan trọng nhất của hệ thống messaging

**⚙️ Lag = offset mới nhất − offset đã commit**. Ý nghĩa: *"consumer đang tụt lại bao nhiêu message"*.

**Đọc lag đúng cách:**
- Lag **ổn định** (dù lớn) = consumer theo kịp, chỉ là có tồn đọng.
- Lag **tăng đều** = tốc độ tới > tốc độ xử lý ⇒ sẽ vỡ ⇒ phải hành động.
- **Lag theo thời gian** (`lag / throughput`) mới là con số dùng được: lag 1 triệu message với
  tốc độ 100k/s = **10 giây**, không phải thảm hoạ.

**💻**
```bash
kafka-consumer-groups.sh --bootstrap-server broker:9092 --describe --group order-service
# TOPIC  PARTITION  CURRENT-OFFSET  LOG-END-OFFSET  LAG  CONSUMER-ID
# Lag lệch nhau nhiều giữa các partition ⇒ hot partition (CC-11) hoặc 1 consumer bị treo
```

**⚙️ Bốn cách xử lý lag tăng (theo thứ tự nên thử):**
1. **Xử lý theo lô** — gom 100 message ghi DB một lần thay vì 100 lần `SaveChanges` (TX-16).
2. **Tăng consumer** — chỉ hiệu quả tới **số partition**. Vượt qua thì phải tăng partition trước
   (lưu ý: tăng partition **làm đổi ánh xạ key → partition** ⇒ vỡ thứ tự trong lúc chuyển tiếp).
3. **Song song trong consumer** — poll tuần tự nhưng xử lý song song theo key (giữ thứ tự trong key):
```csharp
// Gom message theo key rồi chạy song song giữa các key, tuần tự trong mỗi key
await Parallel.ForEachAsync(batch.GroupBy(m => m.Key),
    new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
    async (group, token) => { foreach (var m in group) await HandleAsync(m, token); });
// Chỉ commit offset của message ĐÃ hoàn tất ⇒ commit tới "high-water mark liên tục"
```
4. **Autoscale theo lag** — KEDA `kafka` scaler / `rabbitmq` scaler. Đây là tín hiệu scale **đúng**
   cho worker, chính xác hơn CPU rất nhiều (CPU thấp nhưng lag cao là chuyện thường với I/O-bound).

---

## MQ-9. Hợp đồng message & schema evolution — thứ phá vỡ hệ thống sau 6 tháng

**❓ Vấn đề gốc**: message là **API công khai** giữa các service. Đổi nó = làm hỏng service người khác,
và tệ hơn: các message **cũ vẫn nằm trong queue/topic** sẽ được xử lý bởi code mới.

**⚙️ Quy tắc tương thích:**

| Thay đổi | An toàn? | Ghi chú |
|---|---|---|
| Thêm field **có giá trị mặc định** | ✅ | consumer cũ bỏ qua |
| Xoá field | ❌ | consumer cũ vỡ ⇒ phải deprecate trước, xoá sau nhiều tháng |
| Đổi tên field | ❌ | = xoá + thêm |
| Đổi kiểu (`int` → `string`) | ❌ | luôn phá vỡ |
| Thêm giá trị enum mới | ⚠️ | consumer cũ phải có nhánh `default` |
| Đổi ý nghĩa field (đơn vị tiền, múi giờ) | ❌❌ | tệ nhất: không lỗi, chỉ **sai thầm lặng** |

**⚙️ Ba nguyên tắc thực hành:**
1. **Tolerant reader** — consumer bỏ qua field lạ, không fail khi gặp field chưa biết.
2. **Versioning tường minh** — `[Message("order.created.v1")]`; đổi phá vỡ ⇒ tạo `v2`,
   chạy song song cả hai cho tới khi mọi consumer đã chuyển.
3. **Schema registry** (Avro/Protobuf/JSON Schema) — cưỡng chế kiểm tra tương thích **lúc build**,
   không phải lúc production.

**💻 Trong project HW:**
```csharp
[Message("order.created.v1")]                // topic/routing key là một phần của HỢP ĐỒNG
public sealed record OrderCreatedV1(
    string MessageId,                        // ⬅️ cho dedup (MQ-5)
    string OrderId,
    decimal Amount,
    string Currency,                         // ⬅️ luôn kèm đơn vị, đừng để ngầm hiểu
    DateTimeOffset OccurredAt,               // ⬅️ luôn UTC + offset
    string CorrelationId);                   // ⬅️ cho truy vết (MQ-12)
```
**Không bao giờ** publish entity EF ra broker (TX-12) — consumer sẽ bị khoá vào schema bảng của bạn.

---

## MQ-10. Fan-out: pub/sub, per-consumer queue, và bẫy khuếch đại

**⚙️ Ba topo:**
```
(a) Shared queue — competing consumers:   1 message → CHỈ 1 consumer xử lý   (chia việc)
(b) Fanout — mỗi consumer 1 queue riêng:  1 message → MỌI consumer đều nhận  (thông báo)
(c) Kafka consumer group:                 mỗi group nhận đủ; trong group thì chia partition
```
Sai lầm hay gặp ở RabbitMQ: nhiều service **cùng bind vào một queue** rồi ngạc nhiên vì mỗi service
chỉ nhận được một phần message. Đúng là: mỗi service một queue riêng, cùng bind vào một exchange.

**⚖️ Bẫy khuếch đại (fan-out amplification)**: 1 message vào → 5 consumer → mỗi consumer publish
2 message → 10 message → ... Chuỗi phản ứng này có thể làm nổ broker.
Phòng ngừa: giới hạn độ sâu chuỗi event (`hopCount` trong header, vượt ngưỡng thì DLQ),
và vẽ sơ đồ luồng event — nếu không vẽ nổi thì hệ thống đã quá phức tạp.

---

## MQ-11. Sagas trong project HW — orchestrator event-sourced

**⚙️ Vì sao orchestrator lưu bằng **event stream** chứ không phải một dòng trạng thái?**
- Một dòng trạng thái chỉ cho biết *"đang ở bước 4"*; event stream cho biết **vì sao** đến bước 4,
  điều tối quan trọng khi debug workflow phân tán lúc 2 giờ sáng.
- Ghi bằng `INSERT` (append-only) + unique `(StreamId, Version)` ⇒ **optimistic concurrency
  miễn phí**: hai reply đến cùng lúc thì một cái thua và retry (TX-13).
- Replay được ⇒ sửa bug projection rồi dựng lại.

**⚙️ Ba luật của project (CLAUDE.md) và lý do kỹ thuật:**
1. **`Apply()` là fold thuần** — không clock, không id, không `Send`.
   *Lý do*: replay hôm nay phải cho ra đúng trạng thái như hôm qua. Có `DateTime.Now` trong `Apply()`
   là hỏng ngay.
2. **Chỉ decision method mới `Raise`/`Send`**, và luôn guard theo step hiện tại.
   *Lý do*: reply đến **hai lần** (at-least-once) hoặc đến **muộn** là chuyện bình thường —
   guard chính là idempotency của saga.
3. **Không xoá/đổi tên `ISagaEvent`** — stream được replay mãi mãi. Cần đổi thì thêm `V2` + upcaster.

**💻 Reply handler — trường hợp duy nhất được dùng `IUnitOfWork.ExecuteAsync` ngoài `TransactionBehavior`:**
```csharp
public sealed class PaymentFailedHandler(...) : SagaMessageHandler<OrderSaga, PaymentFailed>
{
    // Message handler không phải MediatR request ⇒ TransactionBehavior không bọc được
    // ⇒ lớp cơ sở tự mở transaction: load stream → decision → append event → outbox → COMMIT
}
```

**⚖️ Hai thứ bắt buộc phải có mà nhiều người quên:**
- **Timeout cho từng bước**: saga treo ở `Paying` 2 ngày vì reply không bao giờ tới.
  Cần job quét saga quá hạn → kích hoạt compensation.
- **Bảng theo dõi**: danh sách saga đang chạy, đang kẹt ở bước nào, bao lâu rồi.
  Không có nó thì không vận hành nổi.

---

## MQ-12. Observability: correlation, trace context, và "message này từ đâu ra?"

**❓ Vấn đề gốc**: request HTTP → command → outbox → broker → consumer → command khác.
Khi có lỗi, bạn cần nối toàn bộ chuỗi đó lại.

**💻 Truyền `traceparent` qua header của message (W3C Trace Context):**
```csharp
// Producer
using var activity = ActivitySource.StartActivity("publish order.created", ActivityKind.Producer);
props.Headers["traceparent"]   = activity?.Id;                 // để consumer nối tiếp span
props.Headers["correlationId"] = correlationId;                // id nghiệp vụ, sống suốt luồng

// Consumer — nối vào trace của producer, KHÔNG tạo trace mới
var parentId = Encoding.UTF8.GetString((byte[])ea.BasicProperties.Headers["traceparent"]);
using var activity = ActivitySource.StartActivity("consume order.created",
    ActivityKind.Consumer, parentId);
```

**⚙️ Ba metric bắt buộc cho mỗi consumer:**
1. `messages_processed_total{result="ok|retry|dlq"}` — tỉ lệ lỗi.
2. `message_processing_duration_seconds` (histogram) — p99 xử lý.
3. `message_age_seconds` = `now − OccurredAt` — **độ trễ end-to-end thực sự**, thứ mà lag của
   Kafka không nói cho bạn (lag thấp vẫn có thể message cũ 1 tiếng nếu producer chậm).

**⚖️ Log tối thiểu cho mỗi message**: `MessageId`, `CorrelationId`, `Type`, `AttemptCount`,
`PartitionKey`. Không có `MessageId` trong log thì không điều tra được trùng lặp.

---

## MQ-13. Chọn công nghệ: RabbitMQ vs Kafka vs Service Bus vs Redis Streams

| Tiêu chí | **RabbitMQ** | **Kafka** | **Azure Service Bus** | **Redis Streams** |
|---|---|---|---|---|
| Mô hình | queue + routing linh hoạt | log phân tán | queue/topic quản lý sẵn | log nhẹ |
| Throughput | trung bình-cao (~10⁴–10⁵/s) | **rất cao** (10⁶/s) | trung bình | cao |
| Giữ lại / replay | ❌ | ✅ | ❌ (có deferred/scheduled) | ✅ có giới hạn |
| Thứ tự | theo queue (yếu) | theo partition (mạnh) | session | theo stream |
| Delay/scheduled | qua TTL+DLX / plugin | ❌ (phải tự làm) | ✅ **có sẵn** | ❌ |
| Ưu tiên (priority) | ✅ | ❌ | ✅ | ❌ |
| Vận hành | dễ | **khó** (ZK/KRaft, partition, rebalance) | không cần (managed) | dễ (nếu đã có Redis) |
| Hợp với | task queue, RPC, routing phức tạp | event streaming, analytics, event sourcing | app .NET trên Azure | dự án nhỏ, đã có Redis |

**⚖️ Câu trả lời trưởng thành khi bị hỏi "chọn cái nào?"**:
> *"Tuỳ vào việc message là mệnh lệnh hay sự kiện lịch sử. Task cần retry/delay/ưu tiên → RabbitMQ.
> Event cần replay, nhiều consumer độc lập, throughput cao → Kafka. Dưới ~5.000 msg/s và team nhỏ,
> tôi sẽ không chọn Kafka: chi phí vận hành (partition, rebalance, lag, schema) lớn hơn lợi ích.
> Project HW trừu tượng hoá qua `IMessageBus` nên đổi provider là đổi cấu hình, không phải viết lại."*

---

## MQ-14. Backpressure ở tầng messaging — khi consumer không theo kịp

**⚙️ Khác với HTTP, queue **hấp thụ** đỉnh tải — nhưng chỉ tới một giới hạn:**
- RabbitMQ: queue dài ⇒ ăn RAM ⇒ chạm ngưỡng ⇒ **flow control**, broker chặn producer lại
  (producer thấy publish chậm dần — đây là tính năng, không phải lỗi). Queue quá dài còn làm
  chậm cả các queue khác trên cùng node ⇒ dùng **lazy queue** cho queue dài.
- Kafka: đĩa lớn nên chứa được lâu, nhưng vượt `retention` là **message bị xoá trước khi kịp đọc**
  — mất dữ liệu âm thầm. Phải cảnh báo khi `lag_time > retention × 0.5`.

**⚙️ Bốn van điều tiết cần chuẩn bị sẵn:**
1. **Giới hạn độ dài queue** (`x-max-length` + `x-overflow=reject-publish`) ⇒ producer nhận lỗi
   và biết đường xử lý, thay vì broker chết.
2. **Ưu tiên**: tách queue cho message quan trọng (thanh toán) và message có thể bỏ (analytics).
3. **Shed có chọn lọc**: message analytics quá 1 giờ thì bỏ; message thanh toán thì không bao giờ bỏ.
4. **Autoscale consumer theo lag** (KEDA) với `maxReplicas` — chú ý: scale consumer sẽ đẩy tải
   xuống DB, nên phải scale **cùng nhịp** với khả năng chịu tải của DB (nếu không, bạn chỉ chuyển
   chỗ nghẽn từ broker sang DB).

---

## MQ-15. Ba câu hỏi ngược kinh điển về messaging (chuẩn bị sẵn)

**1. *"Consumer xử lý xong nhưng chết trước khi ack thì sao?"***
> Broker giao lại (at-least-once). Vì vậy handler của tôi idempotent: bản ghi dedup theo `MessageId`
> nằm cùng transaction với tác động nghiệp vụ. Xử lý lần hai sẽ dừng ở bước dedup và trả về ngay.

**2. *"Message tới sai thứ tự thì sao?"***
> Trước hết tôi giảm khả năng đó bằng partition key theo id thực thể — mọi message của cùng một đơn
> hàng đi cùng một partition. Nhưng tôi **không dựa vào** thứ tự: message mang `Version`, consumer bỏ
> qua bản cũ hơn; và ở nơi nào có thể, tôi thiết kế message mang toàn bộ trạng thái mới thay vì delta,
> để áp dụng nhiều lần và sai thứ tự vẫn hội tụ đúng.

**3. *"Queue tồn 5 triệu message lúc 3 giờ sáng, bạn làm gì?"***
> Theo thứ tự: (1) xem lag **theo thời gian**, không theo số lượng, để biết còn bao lâu thì bắt kịp;
> (2) kiểm tra lag có lệch giữa partition không (hot partition / consumer treo);
> (3) xem DLQ và tỉ lệ lỗi — nếu đang lỗi 100% thì thêm consumer chỉ làm tệ hơn;
> (4) nếu chỉ là đỉnh tải: scale consumer tới số partition, bật xử lý theo lô, tạm ngừng consumer
> của các luồng không quan trọng để nhường tài nguyên DB;
> (5) nếu do bug: dừng consumer, sửa, replay từ DLQ. Và kiểm tra retention để đảm bảo dữ liệu
> không bị xoá trước khi kịp xử lý.

---

## ✅ Checklist tự kiểm tra — Phần 18

- [ ] Nêu 4 lý do dùng message queue và 3 cái giá phải trả.
- [ ] Phân biệt Queue / Pub-Sub / Log và hệ quả kiến trúc (replay được hay không).
- [ ] Giải thích exchange/binding, `prefetch`, và 3 điều kiện để message bền qua restart RabbitMQ.
- [ ] Giải thích partition, offset, ISR, `acks=all` + `min.insync.replicas`.
- [ ] Nêu nguyên nhân rebalance số 1 (`max.poll.interval.ms`) và cách sửa.
- [ ] Chứng minh vì sao exactly-once xuyên hệ thống không tồn tại; mô tả inbox dedup.
- [ ] Nêu 3 xung đột giữa thứ tự và throughput/retry/rebalance; cách né bằng version.
- [ ] Thiết kế retry bậc thang + DLQ với 3 yêu cầu (cảnh báo, ngữ cảnh, replay).
- [ ] Đọc consumer lag đúng cách (theo thời gian) và 4 cách xử lý lag tăng.
- [ ] Nêu quy tắc schema evolution và vì sao không publish entity EF.
- [ ] Giải thích vì sao saga orchestrator dùng event stream và 3 luật của `Apply()`.

⬅️ Quay lại [Mục lục](interview.NET.md) | Tiếp: [Phần 19 — Case study hệ thống lớn ➡️](interview.NET.19-System-Design-Cases.md)
