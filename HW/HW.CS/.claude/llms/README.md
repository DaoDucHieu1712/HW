# `llms/` — model, routing, và prompt như tài sản có phiên bản

> ⚠️ **Về độ tươi của thông tin.** API và giá thay đổi nhanh. Mọi thứ có **ngày tháng hoặc phiên
> bản cụ thể** trong thư mục này (model id, giá, tên tham số) cần **kiểm chứng lại** trước khi
> dùng làm khẳng định chắc chắn — kiểm bằng `GET /v1/models` và trang pricing chính thức.
>
> *"Biết một API đã thay đổi, và biết vì sao nó thay đổi, giá trị hơn thuộc lòng API hiện tại."*

## Ba file

| File | Trả lời | Ai đọc |
|---|---|---|
| `models.json` | model nào có gì, giá bao nhiêu, API nào đã đổi | người + code của bạn |
| `routing.json` | loại việc nào đi model/effort nào | **code của bạn** (không phải Claude Code tự đọc) |
| `prompts/` | prompt sản xuất, có version và lý do đổi | code của bạn |

## Vì sao quản lý prompt như code (PE-13)

Prompt là **hành vi của hệ thống**. Đổi một câu trong system prompt có thể đổi kết quả trên toàn
bộ traffic — đó là một lần deploy, không phải một lần sửa chữ.

Mỗi file trong `prompts/` có frontmatter: `id`, `version`, `model`, `changedAt`, `changeReason`.
Trường **`changeReason`** là trường có giá trị nhất và hay bị bỏ trống: sáu tháng sau, không ai
nhớ vì sao câu đó được thêm vào, nên không ai dám xoá — và prompt cứ phình ra.

### Quy trình đổi prompt

```
1. Có eval trước (skill eval-harness). Không có eval → bạn đang đoán.
2. Đổi ĐÚNG MỘT biến. Tăng version.
3. Đo lại trên tập val.
4. Tốt hơn → giữ + ghi changeReason. Không → revert (và ghi lại đã thử, để người sau khỏi thử lại).
```

## LLM Gateway — vì sao không gọi SDK trực tiếp từ khắp nơi (ARCH-1)

Nếu mỗi service tự `new AnthropicClient()`, bạn mất **cùng lúc** sáu thứ, và mỗi thứ chỉ phát
hiện ra khi đã muộn:

| Mất gì | Triệu chứng khi thiếu |
|---|---|
| Điểm đo chi phí tập trung | "hoá đơn tăng gấp đôi" mà không biết service nào |
| Retry / circuit breaker nhất quán | mỗi nơi retry một kiểu, cùng lúc, làm rate limit tệ hơn |
| Chỗ đổi model một lần | migrate model = sửa 14 repo |
| Kiểm toán prompt/response | không trả lời được "hôm đó model đã nói gì" |
| Áp hạn mức theo tenant | một tenant đốt hết quota của cả hệ thống |
| Chuyển đổi provider | khoá cứng vào một nhà cung cấp |

**Gateway tối thiểu cần có:**
```
- Chọn model + effort theo route  →  đọc routing.json
- Quản lý prompt theo version     →  đọc prompts/
- Ghi usage span mỗi request      →  trace_id, model, prompt_version, effort,
                                      input/cache_read/cache_write/output tokens,
                                      stop_reason, latency
- Retry có phân loại lỗi          →  429/5xx/mạng: retry; 400/404: KHÔNG retry
- Hạn mức + cảnh báo theo tenant
- Chế độ suy giảm                 →  model chính hỏng thì làm gì (KHÔNG phải sập)
```

## Trường usage phải log (ARCH-7)

```
trace_id, session_id, turn_index
model, prompt_version, effort
usage: input / cache_read / cache_write / output tokens     → tiền
stop_reason
tool_calls: [{ name, input_hash, duration_ms, is_error, output_size }]
verifier_result
```

Thiếu `cache_read` là không phát hiện được silent invalidator. Thiếu `prompt_version` là không
quy được thay đổi chất lượng cho lần đổi prompt nào.

## Vị trí trong Clean Architecture (ARCH-2)

```
Domain          →  KHÔNG biết gì về LLM. Không có using Anthropic ở đây.
Application     →  interface (IAgentGateway, IPromptStore) + use case
Infrastructure  →  cài đặt gateway, SDK, retry, cache, tracing
Api             →  endpoint; agent chạy dài KHÔNG nằm trên đường HTTP request (ARCH-3)
```

Agent chạy dài phải là **background job** (hosted service / queue), không phải HTTP request:
nó chạy nhiều phút, cần retry, cần huỷ được, và cần sống sót qua một lần restart. Repo này đã có
sẵn Outbox — cùng một lý do, cùng một mẫu.
