# Phần AI-07 — Kiến trúc triển khai hệ thống AI trong sản phẩm thật

[⬅️ AI-06 — Multi-agent](interview.AI.06-MultiAgent-Teams.md) | [⬅️ Mục lục AI](interview.AI.md)

> Khung mỗi câu: **❓ Vấn đề gốc → ⚙️ Cơ chế bên dưới → 💻 Code/cấu hình → ⚖️ Hệ quả thực chiến**.
>
> Phần này là **vòng phỏng vấn senior / system design**: không hỏi "LLM là gì" mà hỏi
> *"đưa nó vào hệ thống 8 instance, đa tenant, có SLA và có kiểm toán thì hỏng ở đâu?"*

---

## 🗺️ Kiến trúc tham chiếu

```
        Client (web / mobile)
             │ HTTP + SSE (stream)
             ▼
 ┌───────────────────────────────────────────────────────────────┐
 │ API layer      auth · tenancy · rate limit · idempotency key   │
 └───────────────┬───────────────────────────────────────────────┘
                 │ enqueue (việc dài) hoặc gọi trực tiếp (việc ngắn)
                 ▼
 ┌───────────────────────────────────────────────────────────────┐
 │ AGENT RUNTIME (BackgroundService / worker)                     │
 │   loop: gather → act → verify   |  budget · trần lượt · dừng   │
 │   tool registry (đọc / ghi tách biệt) · permission gate         │
 └───────┬───────────────────────────────┬───────────────────────┘
         │                               │
         ▼                               ▼
 ┌────────────────┐            ┌────────────────────────────────┐
 │ LLM GATEWAY    │            │ TOOLS                          │
 │ · chọn model   │            │ · read: DB, search, RAG        │
 │ · key vault    │            │ · write: qua OUTBOX + duyệt    │
 │ · rate limit   │            │ · sandbox cho code execution   │
 │ · retry/fallbk │            └────────────────────────────────┘
 │ · cache        │
 │ · cost meter   │            ┌────────────────────────────────┐
 │ · audit log    │──────────▶ │ OBSERVABILITY: trace/span,     │
 └────────────────┘            │ token, cost, eval results      │
                               └────────────────────────────────┘
```

**Câu chốt phỏng vấn:** *"LLM trong kiến trúc là **một dependency ngoài, chậm, đắt, có giới hạn
công suất và phi tất định**. Mọi mẫu bạn đã dùng cho một API bên thứ ba — timeout, retry có
jitter, circuit breaker, bulkhead, idempotency, outbox, quan sát — đều áp dụng nguyên vẹn. Cái
**mới** chỉ có ba thứ: quản trị **context**, quản trị **chi phí theo token**, và **eval** thay cho
test tất định."*

---

## ARCH-1. LLM Gateway: vì sao không gọi SDK trực tiếp từ khắp nơi?

**❓ Vấn đề gốc:** Ba service cùng `new AnthropicClient(...)` ⇒ không ai biết tổng chi phí, không
đổi model tập trung được, key rải rác, mỗi nơi retry một kiểu.

**⚙️ Gateway (một thư viện nội bộ hoặc một service) tập trung tám việc:**

| # | Trách nhiệm | Vì sao phải tập trung |
|---|---|---|
| 1 | **Quản lý credential** | key không nằm trong code từng service |
| 2 | **Chọn model & effort theo route** | đổi chính sách một chỗ, không phải deploy 6 service |
| 3 | **Rate limit phía client** (token bucket) | tự chặn trước khi bị 429; ưu tiên traffic người dùng hơn job nền |
| 4 | **Retry / backoff / fallback** | một chính sách đúng, không phải 6 chính sách sai |
| 5 | **Prompt cache & cache breakpoint** | đảm bảo bố cục prompt ổn định (LLM-13) |
| 6 | **Đo chi phí & phân bổ** | token → tiền → gán cho tenant/feature/user |
| 7 | **Audit log** | lưu request id, prompt version, quyết định — bắt buộc cho tuân thủ |
| 8 | **Redaction PII** | lọc trước khi gửi ra ngoài |

**💻 Cổng vào tối thiểu:**
```csharp
public interface ILlmGateway
{
    Task<LlmResponse> CompleteAsync(LlmRequest req, CancellationToken ct);
    IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct);
}

public sealed record LlmRequest(
    string  RouteKey,          // "support.classify" — dùng để tra model/effort/budget
    string  PromptId,          // version prompt, để truy vết (PE-13)
    string? System,
    IReadOnlyList<Message> Messages,
    IReadOnlyList<ToolDef>? Tools = null,
    TenantId Tenant = default);   // để phân bổ chi phí và áp quota
```

**⚖️ Cảnh báo cân bằng:** gateway **không được** trở thành lớp trừu tượng "hỗ trợ mọi nhà cung
cấp" theo mẫu số chung nhỏ nhất. Làm thế bạn sẽ mất prompt caching, thinking, structured outputs,
tool search — tức là mất đúng những thứ quyết định chất lượng và chi phí. *Gateway để **quản trị**,
không phải để **che giấu** khả năng của nhà cung cấp.*

---

## ARCH-2. Đặt LLM ở đâu trong Clean Architecture / DDD?

**⚙️ Quy tắc: LLM là **Infrastructure**, không phải Domain.**

```
Domain          : SupportTicket, RefundPolicy — KHÔNG biết LLM tồn tại
Application     : ClassifyTicketCommand + Handler → phụ thuộc INTERFACE
                  ITicketClassifier { Task<Intent> ClassifyAsync(string text) }
Infrastructure  : LlmTicketClassifier : ITicketClassifier   ← prompt, SDK, parsing ở đây
```

**⚖️ Ba lợi ích cụ thể (không phải lý thuyết):**
1. **Test được**: handler test bằng `FakeClassifier`, không cần gọi API, không tốn tiền, không
   flaky.
2. **Thay thế được**: hôm nay LLM, mai có thể là một model phân loại rẻ hơn — Application không đổi.
3. **Ranh giới lỗi rõ**: `LlmUnavailableException` được ánh xạ về một lỗi ứng dụng ở đúng một chỗ.

**⚠️ Anti-pattern:** để `Anthropic.Message` hay JSON schema rò rỉ vào tầng Application. Adapter
phải trả về **kiểu domain** (`Intent`, `RefundDecision`), không trả về `string` thô để tầng trên
tự parse — nếu không, mọi tầng đều phải biết về format của model.

---

## ARCH-3. Agent phải là background job, không phải HTTP request

**❓ Vấn đề gốc:** Một agent chạy 20 lượt có thể mất 5–15 phút. Giữ một HTTP request mở suốt thời
gian đó là sai: timeout của LB, retry của client tạo ra công việc trùng, deploy làm mất tiến độ.

**⚙️ Kiến trúc đúng:**
```
POST /agent-runs  { task, idempotency_key }  → 202 Accepted { runId }
   └─ ghi AgentRun(status=Queued) + OUTBOX trong CÙNG một transaction

Worker (BackgroundService) nhận → chạy loop → cập nhật trạng thái từng lượt

GET  /agent-runs/{id}          → trạng thái + kết quả
GET  /agent-runs/{id}/stream   → SSE: token, tool call, tiến độ
```

**⚖️ Bốn hệ quả bắt buộc:**
1. **Idempotency key ở API** — client retry không tạo hai lần chạy.
2. **Checkpoint từng lượt** — deploy giữa chừng không mất toàn bộ; chạy lại từ lượt cuối.
3. **Graceful shutdown** — nhận SIGTERM thì kết thúc lượt hiện tại rồi lưu trạng thái, không cắt
   giữa một tool ghi.
4. **Streaming tách khỏi thực thi** — SSE chỉ là cửa sổ nhìn vào trạng thái; ngắt kết nối không
   được huỷ công việc.

---

## ARCH-4. Lưu trạng thái hội thoại: schema và những quyết định đi kèm

**💻 Mô hình tối thiểu:**
```
Conversations(Id, TenantId, UserId, CreatedAt, Title, Status)
Messages(Id, ConversationId, Seq, Role, ContentJson, PromptId, ModelId,
         InTok, CachedTok, OutTok, CostCents, CreatedAt)
ToolCalls(Id, MessageId, Name, InputJson, OutputJson, IsError, DurationMs)
AgentRuns(Id, ConversationId, Status, TurnCount, TokenBudget, TokensUsed, StopReason)
```

**⚠️ Bốn quyết định phải nêu ra ở phỏng vấn:**
1. **Lưu nguyên `content` block, không chỉ lưu text.** Thinking block và compaction block phải
   được gửi lại nguyên vẹn (LLM-7, PE-7). Lưu mỗi `text` là mất trạng thái.
2. **Append-only.** Đừng sửa message cũ. Ngoài lý do audit, việc chỉnh sửa lịch sử còn có thể
   làm **vô hiệu hoá thinking block** trên một số model.
3. **Retention & PII.** Hội thoại chứa dữ liệu người dùng. Phải có chính sách xoá theo thời hạn,
   và ẩn danh trước khi đưa vào eval dataset.
4. **Chi phí lưu ngay tại message.** Đây là cách duy nhất để trả lời "tenant nào tốn bao nhiêu"
   mà không phải tính lại từ log.

---

## ARCH-5. Độ bền: retry, circuit breaker, fallback, chế độ suy giảm

**⚙️ Bảng ánh xạ lỗi → hành động (viết sai bảng này là mất điểm ngay):**

| Tình huống | Hành động |
|---|---|
| `429` | tôn trọng `retry-after`; **không** backoff mù |
| `529 overloaded` / `5xx` / timeout / lỗi mạng | exponential backoff + **full jitter**, tối đa N lần |
| `400` (schema sai, tham số bị gỡ) | **không retry** — đây là lỗi code, phải fail nhanh và ồn ào |
| `stop_reason = "refusal"` | **không phải lỗi HTTP** — trả 200. Phải kiểm tra `stop_reason` **trước** khi đọc `content`; xử lý bằng fallback |
| `stop_reason = "max_tokens"` | output cụt — tăng `max_tokens` hoặc chia nhỏ việc; đừng parse phần cụt |
| Circuit mở (lỗi liên tục) | chuyển sang **chế độ suy giảm**, không xếp hàng vô hạn |

**⚙️ Chế độ suy giảm (degraded) — thiết kế trước, không ứng biến:**
```
Bậc 1: model chính, effort cao
Bậc 2: model chính, effort thấp            (rẻ hơn, vẫn tốt)
Bậc 3: đường không-LLM (rule-based, tìm kiếm từ khoá, template)
Bậc 4: xếp hàng và báo người dùng "sẽ trả lời sau"
```

**⚠️ Lưu ý về cache khi fallback:** cache **gắn theo model** ⇒ mỗi lần rơi bậc là mất cache hit,
chi phí tăng đúng lúc hệ thống đang căng. Đây là lý do nên **hạ effort trước khi đổi model**.

---

## ARCH-6. Quản trị chi phí: phân bổ, hạn mức, cảnh báo

**⚙️ Ba tầng kiểm soát:**

| Tầng | Cơ chế |
|---|---|
| **Trước khi gọi** | ước lượng token bằng `count_tokens`; từ chối nếu vượt hạn mức tenant/route |
| **Trong khi chạy** | task budget (model tự pace) + trần lượt + trần token cứng |
| **Sau khi chạy** | ghi `usage` vào DB, cộng dồn theo tenant/feature/user, cảnh báo theo ngưỡng |

**💻 Phân bổ chi phí — điểm mà nhiều team làm sai:**
```csharp
var cost = (u.InputTokens        - u.CacheReadInputTokens) * p.In
         +  u.CacheReadInputTokens                          * p.CacheRead
         +  u.CacheCreationInputTokens                      * p.CacheWrite
         +  u.OutputTokens                                  * p.Out;
// Ghi kèm: tenant, routeKey, promptId, model, effort  → mới truy vấn được "ai tốn, vì sao"
```

**⚖️ Ba chỉ số quản trị:**
- **Cost per completed task** theo route (không phải cost per request).
- **Cache hit ratio** theo route — tụt = có silent invalidator, sửa là tiết kiệm ngay.
- **Tỉ lệ chi phí của các run bị huỷ/thất bại** — thường là khoản lãng phí lớn nhất và vô hình nhất.

---

## ARCH-7. Quan sát: trace, span và những trường bắt buộc

**⚙️ Mô hình span (theo hướng OpenTelemetry cho GenAI):**
```
span: agent.run          { run_id, tenant, route, status, total_cost }
 └ span: agent.turn      { turn_index, stop_reason }
    ├ span: llm.call     { model, prompt_id, effort,
    │                      in_tok, cached_tok, out_tok, ttft_ms, request_id }
    └ span: tool.call    { name, is_error, duration_ms, input_hash, output_bytes }
```

**⚠️ Ba trường quan trọng nhất và hay bị quên:**
1. **`request_id` của nhà cung cấp** — không có nó thì không thể mở ticket hỗ trợ khi lỗi lạ.
2. **`prompt_id`** — không có nó thì không biết output tệ đến từ phiên bản prompt nào.
3. **`cached_tok`** — không có nó thì không phát hiện được cache đã chết.

**⚖️ Nguyên tắc log:** log **metadata và số liệu** mặc định; log **nội dung prompt/response** phải
là quyết định có ý thức (sampling, redaction, retention riêng) vì đó là dữ liệu người dùng.

---

## ARCH-8. Bảo mật và đa tenant

**⚙️ Bảy kiểm soát, xếp theo mức độ bắt buộc:**

| # | Kiểm soát | Chi tiết |
|---|---|---|
| 1 | **Phân quyền ở lớp thực thi tool** | quyền lấy từ **session đã xác thực**, không lấy từ tham số model truyền vào |
| 2 | **Cô lập tenant trong retrieval** | filter tenant nằm **trong** truy vấn vector/SQL, không lọc sau khi lấy về |
| 3 | **Sandbox cho code execution** | container, không credential thật, không mạng ra ngoài trừ allowlist |
| 4 | **Kiểm soát egress** | mọi hành động ra ngoài (mail, webhook, push) qua allowlist hoặc người duyệt — đây là chỗ chặn exfiltration |
| 5 | **Redaction PII** | lọc trước khi gửi ra ngoài; và cân nhắc chính sách lưu trữ dữ liệu của nhà cung cấp |
| 6 | **Secret không bao giờ vào prompt** | model có thể lặp lại chúng trong output, và output có thể được log |
| 7 | **Ràng buộc địa lý suy luận** | khi có yêu cầu chủ quyền dữ liệu, dùng tham số chỉ định vùng suy luận và ghi lại nơi đã chạy |

**⚠️ Bẫy đa tenant kinh điển của LLM:** **prompt cache dùng chung**. Nếu bạn nhét dữ liệu của
tenant A vào phần tiền tố được cache rồi tenant B dùng chung tiền tố đó, bạn có một sự cố rò rỉ.
Nguyên tắc: **chỉ cache phần thật sự dùng chung** (system prompt, tool, tài liệu công khai); dữ
liệu riêng của tenant luôn nằm **sau** breakpoint cuối cùng.

---

## ARCH-9. Kiểm thử hệ thống LLM: bốn tầng

| Tầng | Kiểm cái gì | Công cụ |
|---|---|---|
| **Unit** | logic quanh LLM (parse, retry, tính tiền, cắt context) | fake `ILlmGateway`, không gọi mạng |
| **Contract** | tool có làm đúng schema đã công bố không | test schema + test thực thi từng tool |
| **Golden/replay** | ghi lại response thật, phát lại | bắt hồi quy trong parsing và luồng, **không** bắt hồi quy chất lượng |
| **Eval** | chất lượng đầu ra & hành vi agent | dataset thật + grader; chạy trong CI, có ngưỡng |

**⚖️ Ba nguyên tắc:**
1. **Không gọi API thật trong unit test.** Chậm, tốn tiền, flaky.
2. **Eval là quality gate, không phải test pass/fail cứng.** Đặt **ngưỡng** (ví dụ ≥ 92% trên
   tập test) và cảnh báo khi tụt, thay vì đòi 100%.
3. **Test cả đường hỏng**: model trả `refusal`, `max_tokens`, JSON hỏng, tool timeout, 429. Đây
   là nơi hệ thống thật sự vỡ, không phải ở đường thành công.

---

## ARCH-10. Triển khai và rollout: prompt/model là thay đổi hành vi

**⚙️ Đối xử với "đổi prompt" và "đổi model" như đổi thuật toán lõi:**

```
1. Chạy eval offline trên tập test         → so với baseline
2. Canary 5% traffic, cùng lúc log song song (shadow) nếu có thể
3. Theo dõi: pass rate, cost/task, latency p95, tỉ lệ escalate, feedback người dùng
4. Rollout dần 25% → 50% → 100%
5. Nút rollback: đổi cấu hình, KHÔNG cần deploy
```

**💻 Vì thế prompt và model phải là **cấu hình**, không phải hằng số biên dịch:**
```jsonc
{ "routes": {
    "support.classify": { "model": "claude-haiku-4-5", "effort": "low",  "promptId": "cls@v7" },
    "support.resolve" : { "model": "claude-opus-5",    "effort": "high", "promptId": "res@v12" }
}}
```

**⚠️ Và nhớ: migration model **luôn** kèm audit prompt (PE-6).** Prompt viết cho model cũ chứa các
mẹo giờ đã phản tác dụng. Bỏ qua bước này là lý do phổ biến nhất khiến "model mới mà lại tệ hơn".

---

## ARCH-11. Caching nhiều tầng — và cạm bẫy của semantic cache

| Tầng | Khớp theo | Tiết kiệm | Rủi ro |
|---|---|---|---|
| **Exact cache** | hash của (prompt + tham số) | 100% chi phí khi trúng | tỉ lệ trúng thấp với ngôn ngữ tự nhiên |
| **Prompt cache** (nhà cung cấp) | **tiền tố** | phần lớn chi phí input | phá cache âm thầm (LLM-13) |
| **Semantic cache** | embedding gần nhau | cao | **trả lời sai câu hỏi khác** |

**⚠️ Vì sao semantic cache nguy hiểm:** *"Chính sách hoàn tiền cho khách VIP"* và *"Chính sách
hoàn tiền cho khách thường"* rất gần nhau về vector. Trúng cache sai ⇒ trả lời sai **một cách tự
tin**, và không có dấu vết lỗi nào.

**⚖️ Nếu vẫn dùng semantic cache, ba điều kiện bắt buộc:** ngưỡng tương đồng cao và được hiệu
chuẩn bằng dữ liệu thật; **phân vùng cache theo tenant/quyền** (nếu không là rò rỉ dữ liệu); và
**không bao giờ** dùng cho câu trả lời phụ thuộc trạng thái thay đổi (số dư, trạng thái đơn).

---

## ARCH-12. Human-in-the-loop ở tầng hệ thống

**⚙️ Đừng cài "chờ người duyệt" bằng cách giữ vòng lặp treo.** Mô hình đúng là **hàng đợi duyệt**:

```
Agent muốn hành động rủi ro
   └─ ghi ApprovalRequest(status=Pending, action, payload, expiresAt) + tạm dừng run
Người duyệt (UI/Slack) → Approve/Reject
   └─ event → worker tiếp tục run từ checkpoint, đưa kết quả duyệt vào tool_result
```

**⚖️ Bốn thuộc tính bắt buộc:** có **hạn** (hết hạn thì tự từ chối, không treo vĩnh viễn); có
**ngữ cảnh đủ để người quyết định** (không chỉ "Approve?"); có **audit** (ai duyệt, lúc nào, dựa
trên gì); và **idempotent** (bấm duyệt hai lần không thực hiện hai lần).

---

## ARCH-13. Áp dụng vào project HW (CQRS + MediatR + Outbox)

**⚙️ Ánh xạ trực tiếp — đây là câu hỏi "bạn có thật sự hiểu không":**

```
HW.Domain          : không đổi. KHÔNG có gì liên quan LLM.
HW.Application     : + IAiAssistant / ITicketClassifier (interface)
                     + Command/Query mới: ClassifyTicketCommand, SummarizeOrderQuery
                     + pipeline behavior sẵn có (validation, logging) áp dụng luôn cho AI
HW.Infrastructure  : + LlmGateway (SDK, retry, cache, cost meter)
                     + Adapter: LlmTicketClassifier : ITicketClassifier
                     + AgentRunRepository, ApprovalRequestRepository
HW.Api             : + POST /agent-runs (202) + GET /agent-runs/{id}/stream (SSE)
```

**⚖️ Ba thứ trong HW dùng lại được nguyên vẹn — nói được là ghi điểm lớn:**
1. **Outbox** — mọi hiệu ứng ra ngoài của agent (gửi mail, publish event) đi qua outbox. Nhờ đó
   agent có **exactly-once về hiệu ứng**, và bạn có một điểm chặn để kiểm duyệt trước khi gửi.
2. **Pipeline behavior của MediatR** — chèn `LlmBudgetBehavior` (chặn khi vượt quota) và
   `LlmAuditBehavior` (ghi prompt id, token, cost) mà không sửa handler nào.
3. **Domain event + saga** — một agent run dài chính là một saga: có trạng thái, có checkpoint,
   có bù trừ khi hỏng. Đừng phát minh lại; dùng đúng cơ chế đã có.

**⚠️ Một điều **không** nên làm:** để `IAiAssistant` xuất hiện trong Domain. Domain phải trả lời
được câu hỏi nghiệp vụ mà không cần biết có AI trên đời.

---

## ARCH-14. Bảng tra nhanh — triệu chứng → nguyên nhân → cách sửa

| Triệu chứng | Nguyên nhân thường gặp | Sửa |
|---|---|---|
| Chi phí tăng gấp đôi không rõ lý do | cache hit về 0 (timestamp trong system prompt, tool đổi thứ tự, đổi model) | kiểm `cache_read_input_tokens`, dọn silent invalidator |
| Agent chạy 40 lượt rồi bỏ cuộc | thiếu verifier + thiếu phát hiện không-tiến-triển | thêm verifier; băm `(tool+input)` phát hiện lặp |
| Model bỏ gọi tool song song, chậm hẳn | `tool_result` bị tách ra nhiều message | gộp tất cả vào **một** message `user` |
| Model "quên" giữa chừng, chi phí không giảm | append thiếu compaction/thinking block | append **nguyên** `response.content` |
| JSON hỏng ~2% | chỉ nhắc trong prompt | `strict: true` / `output_config.format` |
| Lỗi 400 sau khi nâng model | `temperature`, `budget_tokens`, prefill, forced `tool_choice` | gỡ tham số cũ; dùng `effort` + structured outputs |
| Trả lời sai dù tài liệu có | retrieval trượt | log top-k; thêm hybrid + re-rank |
| Sửa prompt xong chỗ khác hỏng | không có eval | dựng golden set + tách tập test |
| Agent làm việc ngoài phạm vi | ranh giới nằm trong prompt | chuyển sang `deny`/`ask` và hook |
| Hai teammate ghi đè file của nhau | không chia sở hữu file | chia sở hữu trong spawn prompt |
| Không debug được sự cố | thiếu `request_id` / `prompt_id` / trace | bổ sung span theo ARCH-7 |

---

## ARCH-15. Mười câu tự kiểm tra trước khi đưa hệ AI lên production

```
1.  Tôi có thể trả lời "output tệ này do prompt version nào, model nào sinh ra" không?
2.  Có eval chạy trong CI với ngưỡng và tập test đóng chưa?
3.  Chi phí có được phân bổ theo tenant/route không? Có hạn mức và cảnh báo chưa?
4.  Cache hit ratio hiện tại là bao nhiêu? Ai theo dõi nó?
5.  Mọi hành động không thể hoàn tác có đi qua cổng duyệt ở LỚP THỰC THI TOOL không?
6.  Quyền của tool lấy từ session đã xác thực hay từ tham số model truyền vào?
7.  Agent có trần lượt, trần token, trần thời gian, và phát hiện không-tiến-triển chưa?
8.  Dữ liệu riêng của tenant có nằm ngoài phần prompt được cache dùng chung không?
9.  Có chế độ suy giảm khi nhà cung cấp lỗi, và nó đã được diễn tập chưa?
10. Nếu ngày mai model bị thay thế, tôi mất bao lâu để migrate — và tôi có kế hoạch audit prompt chưa?
```

**⚖️ Câu kết cho vòng phỏng vấn senior:** *"Phần khó của hệ thống AI không nằm ở chỗ gọi model.
Nó nằm ở **ba ranh giới**: ranh giới **context** (cái gì được vào, ở đâu, lúc nào), ranh giới
**quyền** (agent được làm gì mà không cần hỏi), và ranh giới **bằng chứng** (làm sao biết nó đang
tốt lên hay tệ đi). Ba ranh giới đó là kiến trúc, không phải prompt."*

---

[⬅️ AI-06](interview.AI.06-MultiAgent-Teams.md) | [⬅️ Mục lục AI](interview.AI.md)
