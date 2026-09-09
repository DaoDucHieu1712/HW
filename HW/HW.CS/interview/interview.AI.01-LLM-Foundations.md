# Phần AI-01 — Nền tảng LLM: token, context, sampling, RAG, cache, cost

[⬅️ Về mục lục AI](interview.AI.md) | Tiếp theo: [AI-02 — Prompt & Context Engineering ➡️](interview.AI.02-Prompt-Context.md)

> Khung mỗi câu: **❓ Vấn đề gốc → ⚙️ Cơ chế bên dưới → 💻 Code/đo đạc → ⚖️ Hệ quả thực chiến**.
>
> Phần này là "CLR của thế giới LLM": nếu không nắm token / KV cache / prefill-decode thì mọi câu
> về agent, cost, latency ở các phần sau đều chỉ trả lời được ở mức bề mặt.

---

## 🗺️ Bản đồ: một request đi qua đâu

```
Prompt (text)
   │  tokenizer (BPE)      → 1 request = [tools][system][messages] nối lại thành 1 chuỗi token
   ▼
PREFILL  ── xử lý SONG SONG toàn bộ input, sinh KV cache ── compute-bound  → quyết định TTFT
   ▼
DECODE   ── sinh TỪNG token một, mỗi token đọc lại toàn bộ KV cache ── memory-bandwidth-bound → TPOT
   ▼
stop_reason: end_turn | max_tokens | tool_use | refusal | pause_turn
```

**Câu chốt phỏng vấn:** *"LLM không hiểu câu hỏi rồi trả lời. Nó là hàm
`P(token tiếp theo | toàn bộ token phía trước)` chạy lặp. Mọi kỹ thuật — prompt, RAG, tool use,
agent — đều chỉ là cách **sắp xếp lại chuỗi token đầu vào** để phân phối xác suất đầu ra rơi đúng
chỗ mình muốn."*

---

## LLM-1. Token là gì, và vì sao lập trình viên hay ước lượng sai?

**❓ Vấn đề gốc:** Model không đọc ký tự, cũng không đọc từ. Nó đọc **token** — đơn vị do
thuật toán **BPE (Byte-Pair Encoding)** cắt ra, học từ tần suất trên corpus huấn luyện.

**⚙️ Cơ chế:** BPE bắt đầu từ byte đơn, lặp lại việc gộp cặp byte/ký tự xuất hiện nhiều nhất
thành một token mới, cho tới khi đủ kích thước từ điển (thường 100k–200k token).

Hệ quả trực tiếp:

| Loại nội dung | Tỉ lệ xấp xỉ |
|---|---|
| Tiếng Anh thường | ~1 token ≈ 4 ký tự ≈ 0.75 từ |
| **Tiếng Việt có dấu** | **tệ hơn nhiều** — dấu tiếng Việt là ký tự multi-byte UTF-8, một từ dễ thành 2–4 token |
| Code | tệ hơn văn xuôi (thụt lề, dấu ngoặc, tên biến camelCase bị cắt vụn) |
| JSON | rất tệ — dấu nháy, dấu hai chấm, dấu phẩy, khoảng trắng đều là token |
| GUID / hash / base64 | thảm hoạ — gần như mỗi 2–3 ký tự là 1 token |

**💻 Đo, đừng đoán** — dùng endpoint đếm token, **không** dùng `tiktoken` (đó là tokenizer của
hãng khác, sai số lớn) và không dùng `text.Length / 4`:

```csharp
// POST /v1/messages/count_tokens — đếm chính xác, KHÔNG tính tiền, không sinh output
var count = await client.Messages.CountTokensAsync(new {
    model    = "claude-opus-5",
    system   = systemPrompt,
    messages = history,
    tools    = toolDefs        // ⚠️ tool definitions CŨNG tốn token, rất hay bị quên
});
```

**⚖️ Hệ quả thực chiến:**
- Prompt tiếng Việt đắt hơn prompt tiếng Anh cùng nội dung ~1.5–2×. Với hệ thống lớn, viết
  **system prompt bằng tiếng Anh, dữ liệu bằng tiếng Việt** là một lựa chọn cost hợp lệ.
- Nhồi `JsonSerializer.Serialize(entity)` nguyên vẹn vào prompt là cách đốt token nhanh nhất.
  Chuyển sang **CSV/TSV hoặc markdown table** thường giảm 30–50% token cho cùng dữ liệu.
- Tokenizer **khác nhau giữa các thế hệ model**. Đổi model ⇒ phải đo lại baseline token, đừng
  giả định chi phí giữ nguyên.

---

## LLM-2. Context window là gì? Vì sao attention là O(n²) và điều đó ảnh hưởng gì tới ví tiền?

**❓ Vấn đề gốc:** "Context window 1M token" không có nghĩa là "cứ nhét 1M vào là ổn".

**⚙️ Cơ chế:** Ở mỗi lớp Transformer, mỗi token phải tính **attention** với mọi token trước đó.
Với chuỗi dài `n`:
- Chi phí tính toán prefill: **O(n²)** — gấp đôi input ⇒ gấp ~4 lần công tính.
- Bộ nhớ **KV cache**: **O(n)**, tuyến tính theo số lớp × số head × chiều ẩn. Đây mới là thứ
  giới hạn thực tế trên GPU, không phải "độ thông minh".

**⚙️ KV cache là gì:** Khi sinh token thứ `k`, model cần Key/Value của cả `k-1` token trước.
Nếu tính lại từ đầu mỗi token thì decode sẽ lại là O(n²). Nên nó **lưu K,V lại** — đổi bộ nhớ
lấy tốc độ. Đó cũng là lý do **prompt caching** (LLM-13) khả thi về mặt kỹ thuật: cache chính là
KV đã tính sẵn cho một tiền tố.

**⚖️ Hệ quả thực chiến — ba điều luôn phải nói:**
1. **Context window là giới hạn cứng, không phải mục tiêu.** Chất lượng suy giảm dần khi context
   phình ra rất lâu trước khi chạm trần (xem LLM-6).
2. **Giá tính theo token, không theo "lượt hỏi".** Trong hội thoại nhiều lượt, bạn **gửi lại
   toàn bộ lịch sử mỗi lượt** ⇒ chi phí tích luỹ theo **O(số lượt²)** nếu không cache/compact.
3. Model là **stateless**. "Nó nhớ cuộc trò chuyện" chỉ vì client gửi lại lịch sử. Mọi cơ chế
   "memory" đều là kỹ thuật ở tầng ứng dụng.

---

## LLM-3. Prefill vs Decode — vì sao input dài rẻ mà output dài thì đắt và chậm?

**⚙️ Hai pha có đặc tính phần cứng khác hẳn nhau:**

| | **Prefill** (đọc input) | **Decode** (sinh output) |
|---|---|---|
| Song song hoá | ✅ toàn bộ input xử lý 1 lượt | ❌ tuần tự, từng token một |
| Nút thắt | **compute** (FLOPs) | **băng thông bộ nhớ** (đọc lại weights + KV mỗi token) |
| Quyết định chỉ số | **TTFT** (time to first token) | **TPOT** (time per output token) |
| Giá | rẻ hơn | thường **~5×** giá input |

**⚖️ Hệ quả thực chiến — đây là chỗ tối ưu latency thật sự nằm:**
- **Muốn nhanh: cắt output, đừng cắt input.** Bắt model trả lời 200 token thay vì 2000 token
  giảm latency gần như tuyến tính. Cắt 50% input chỉ giúp TTFT, không giúp TPOT.
- Ép model "giải thích chi tiết từng bước" trong output mà người dùng không đọc = trả tiền giá
  output cho rác. Nếu cần suy luận, dùng **thinking** (LLM-7) và không hiển thị.
- **Streaming là bắt buộc** khi `max_tokens` lớn: không stream thì TTFT ≈ toàn bộ thời gian sinh,
  và request dễ chết vì HTTP timeout. Với `max_tokens` cỡ 64k–128k, SDK **yêu cầu** stream.
- Batch API (chạy bất đồng bộ, không cần realtime) thường **giảm 50% giá** — dùng cho backfill,
  gán nhãn, tổng hợp báo cáo đêm.

---

## LLM-4. Vì sao cùng một prompt lại ra hai kết quả khác nhau? temperature/top-p thực chất làm gì?

**⚙️ Cơ chế:** Mỗi bước, model cho ra một vector **logits** trên toàn bộ từ điển. `softmax` biến
nó thành phân phối xác suất. Sau đó **sampling**:

| Tham số | Làm gì | Đặt sai thì sao |
|---|---|---|
| `temperature` | chia logits cho T trước softmax. T→0: gần argmax (greedy). T cao: phân phối phẳng | T cao ⇒ "sáng tạo" = bịa; T=0 ⇒ lặp, kẹt vòng |
| `top_p` (nucleus) | chỉ lấy tập token nhỏ nhất có tổng xác suất ≥ p | p thấp ⇒ văn phong nghèo |
| `top_k` | chỉ lấy k token xác suất cao nhất | thô hơn top_p |

**⚠️ Điểm rất hay bị hỏi bẫy:** *"Đặt `temperature = 0` thì có deterministic không?"*
**Không.** Vẫn còn ba nguồn phi tất định:
1. Phép cộng dấu chấm động trên GPU **không kết hợp** (`(a+b)+c ≠ a+(b+c)`), thứ tự reduce đổi
   theo batch size ⇒ logits lệch ở chữ số cuối ⇒ đôi khi đổi argmax.
2. **Batching động** phía server: request của bạn được gộp cùng request khác, đổi kernel path.
3. **Mixture-of-Experts routing** phụ thuộc batch.

**Và trên các model đời mới (Claude Opus 5 / Sonnet 5 / họ 4.6+), `temperature`/`top_p`/`top_k`
đã bị gỡ bỏ — truyền vào sẽ nhận 400.** Cách kiểm soát bây giờ là `output_config.effort` và
structured outputs, không phải vặn sampling.

**⚖️ Hệ quả:** Đừng thiết kế hệ thống dựa trên giả định "cùng input ⇒ cùng output". Nếu cần
tái lập, hãy **lưu lại output** (kèm request id), đừng cố lưu lại "seed".

---

## LLM-5. Vì sao model bịa (hallucination)? Bốn lớp phòng thủ

**❓ Vấn đề gốc:** Model được huấn luyện để sinh chuỗi **có xác suất cao**, không phải chuỗi
**đúng**. "Tôi không biết" là một chuỗi hiếm trong dữ liệu huấn luyện; một câu trả lời trôi chảy
và sai lại rất phổ biến. Bịa không phải bug — đó là hành vi tự nhiên của mục tiêu huấn luyện.

**⚙️ Bốn lớp phòng thủ, xếp theo hiệu quả:**

1. **Grounding** — đưa sự thật vào context (RAG, tool, DB) và ra lệnh *"chỉ trả lời dựa trên tài
   liệu dưới đây; nếu không có, nói không tìm thấy"*. Đây là lớp mạnh nhất.
2. **Citation bắt buộc** — bắt model trích dẫn đoạn nguồn cho từng khẳng định. Kiểm tra được bằng
   máy: câu trích dẫn có thật sự tồn tại trong nguồn không.
3. **Ràng buộc cấu trúc** — structured output + `strict: true` (LLM-11). Model không thể bịa
   một enum ngoài schema.
4. **Verifier vòng ngoài** — chạy test, chạy compiler, query lại DB, hoặc một lượt gọi model thứ
   hai để kiểm tra. Đây là nền tảng của agentic loop (AI-03).

**⚖️ Điều KHÔNG hiệu quả (bẫy phỏng vấn):** thêm câu *"đừng bịa"*, *"hãy chính xác 100%"* vào
prompt. Nó gần như không thay đổi phân phối. Cái thay đổi phân phối là **dữ liệu trong context**
và **ràng buộc trên đầu ra**.

---

## LLM-6. "Context rot": vì sao nhồi nhiều context lại làm model kém đi?

**❓ Vấn đề gốc:** Trực giác sai phổ biến nhất: *"context 1M ⇒ cứ đổ hết repo vào là model hiểu
hết dự án"*.

**⚙️ Ba cơ chế làm chất lượng tụt:**
1. **Lost in the middle** — độ chú ý phân bổ không đều: thông tin ở đầu và cuối context được
   dùng tốt hơn hẳn thông tin ở giữa.
2. **Nhiễu cạnh tranh** — thêm 50 đoạn không liên quan làm loãng tín hiệu của 2 đoạn liên quan.
   Ở đây **precision của retrieval quan trọng hơn recall**.
3. **Context poisoning** — một sai lầm/hiểu nhầm đã nằm trong lịch sử sẽ được model tham chiếu
   lại ở các lượt sau và tự củng cố. Trong agent loop, đây là nguyên nhân số 1 của "càng chạy
   càng lú".

**💻 Cách đo:** dựng bộ eval "needle in a haystack" **có biến thể**: chèn 1 sự thật vào vị trí
10%/50%/90% của context ở các độ dài 10k/100k/500k, rồi hỏi lại. Không chỉ đo "tìm thấy không"
mà đo **"suy luận kết hợp 2 needle cách xa nhau"** — đây mới là thứ tụt trước.

**⚖️ Hệ quả thực chiến:** context là **ngân sách phải quản lý**, không phải thùng chứa.
Ba công cụ chuẩn (chi tiết ở AI-02):
- **Compaction** — tóm tắt lịch sử cũ, giữ lại kết luận. Với server-side compaction: bắt buộc
  append nguyên `response.content` — mất block compaction là mất trạng thái.
- **Context editing** — **xoá** tool result cũ / thinking block. Khác hẳn compaction (tóm tắt).
- **Memory ngoài** — ghi ra file/DB, đọc lại khi cần.

---

## LLM-7. Thinking / reasoning model: khác gì "chain-of-thought trong prompt"?

**❓ Vấn đề gốc:** Trước đây ta viết *"hãy suy nghĩ từng bước"* để ép model sinh reasoning trong
**output**. Reasoning model làm việc đó ở một kênh riêng, được huấn luyện chuyên biệt.

**⚙️ Cơ chế & API hiện tại (hay bị hỏi vì kiến thức cũ đã lỗi thời):**

| Khái niệm | Trạng thái hiện tại |
|---|---|
| `thinking: {type: "enabled", budget_tokens: N}` | **Lỗi thời**. Trên Opus 5 / Sonnet 5 / 4.7 / 4.8 → **400** |
| `thinking: {type: "adaptive"}` | Cách đúng — model tự quyết định nghĩ bao nhiêu |
| `output_config.effort` | `low` / `medium` / `high` / `xhigh` / `max` — cần gạt điều chỉnh độ sâu & chi phí |
| `thinking.display` | `omitted` (mặc định đời mới) / `summarized`. **Chỉ đổi hiển thị — vẫn nghĩ, vẫn tính tiền** |

**💻 Quy tắc replay quan trọng:** khi tiếp tục hội thoại trên **cùng model**, phải **gửi lại
nguyên vẹn** các thinking block. Sửa hoặc bỏ chúng đi sẽ làm hỏng trạng thái suy luận.

**⚖️ Hệ quả thực chiến:**
- `effort` là **cần gạt tiết kiệm chi phí đầu tiên có đánh đổi chất lượng** (sau các cần gạt
  miễn phí: cache, cắt token). `low` cho subagent/việc đơn giản, `high`–`xhigh` cho coding và
  agent chạy dài, `max` chỉ khi độ đúng quan trọng hơn tiền.
- Một câu trả lời sắc: *"Model mới ở effort thấp thường vượt model đời trước ở effort cao. Nên
  trước khi dựng cascade nhiều model để tiết kiệm, hãy đo phương án 'một model mạnh + effort
  thấp' — nó còn giữ được **một namespace cache duy nhất**, trong khi cascade làm mất cache reuse
  vì cache gắn theo model."*
- Nếu tắt thinking để tiết kiệm: cẩn thận, model có thể viết lời gọi tool vào **text hiển thị**
  thay vì sinh `tool_use` block ⇒ lệnh không bao giờ chạy mà **không có lỗi nào được ném ra**.
  Hạ `effort` an toàn hơn tắt thinking.

---

## LLM-8. Embedding & vector search: cosine similarity thực sự đo cái gì?

**⚙️ Cơ chế:** Embedding model ánh xạ đoạn text → vector `d` chiều (768/1024/1536...) sao cho
**gần nhau về ngữ nghĩa ⇒ gần nhau về hình học**. Đo bằng **cosine** (góc), không phải khoảng
cách Euclid — vì độ dài vector phản ánh độ dài văn bản chứ không phản ánh nghĩa.

**⚙️ Chuỗi mắt xích và chỗ hỏng của từng mắt:**

| Bước | Hỏng ở đâu |
|---|---|
| **Chunking** | Chunk quá to ⇒ 1 vector gánh nhiều chủ đề, nhiễu. Quá nhỏ ⇒ mất ngữ cảnh ("nó" trỏ vào đâu?). Cắt giữa câu/giữa hàm ⇒ vô nghĩa |
| **Embedding** | Query và document phải **cùng model, cùng version**. Đổi model ⇒ **phải reindex toàn bộ** |
| **Index (ANN)** | HNSW/IVF là **xấp xỉ** — đánh đổi recall lấy tốc độ. Recall 95% nghĩa là 5% lần bạn mất kết quả đúng |
| **Ranking** | Cosine cao ≠ hữu ích. "Chính sách hoàn tiền" và "Chính sách KHÔNG hoàn tiền" rất gần nhau về vector |

**⚖️ Hệ quả — ba cải tiến gần như luôn đáng làm:**
1. **Hybrid search**: BM25 (khớp từ khoá, bắt được mã lỗi `E4012`, tên hàm, mã SKU — thứ vector
   luôn thua) + vector, hợp nhất bằng **RRF (Reciprocal Rank Fusion)**.
2. **Re-ranker** (cross-encoder): lấy top-50 từ ANN rồi chấm lại kỹ, giữ top-5. Đây thường là
   cải tiến chất lượng lớn nhất trên mỗi đơn vị công sức trong RAG.
3. **Chunk theo cấu trúc** (heading, hàm, điều khoản) + **overlap** + nhúng metadata (tiêu đề,
   đường dẫn, ngày) vào **chính nội dung chunk** để nó có ngữ cảnh khi đứng một mình.

---

## LLM-9. Kiến trúc RAG đầy đủ và 6 chỗ hỏng thường gặp

```
[Ingest]  nguồn → parse → chunk → embed → vector store (+ BM25 index)
[Query]   câu hỏi → (rewrite/expand) → hybrid retrieve → re-rank top-k
                                              │
                                              ▼
          prompt = system + [tài liệu + nguồn] + câu hỏi → LLM → trả lời + citation
[Verify]  citation có tồn tại trong tài liệu không? → chặn / gắn cờ nếu không
```

**⚖️ Sáu chỗ hỏng, xếp theo tần suất thực tế:**

| # | Triệu chứng | Nguyên nhân thật |
|---|---|---|
| 1 | Trả lời sai dù tài liệu có | **Retrieval trượt** — luôn log top-k để kiểm chứng trước khi đổ lỗi cho model |
| 2 | Trả lời dựa trên bản cũ | Index **stale** — không có pipeline cập nhật/xoá theo sự kiện |
| 3 | Lộ dữ liệu giữa các khách hàng | Không lọc **quyền hạn ở tầng truy vấn** — filter phải nằm **trong** ANN query, không phải lọc sau |
| 4 | Câu hỏi tổng hợp ("tổng doanh thu quý 3") trả lời bậy | RAG là **tìm kiếm**, không phải **tính toán**. Việc này phải đi bằng tool/SQL |
| 5 | Câu hỏi mơ hồ ("cái đó thì sao?") trượt hoàn toàn | Thiếu **query rewriting** dựa trên lịch sử hội thoại |
| 6 | Chi phí phình | Nhồi top-20 chunk vào mọi request thay vì top-3 sau re-rank |

**Câu chốt:** *"RAG hỏng ở tầng retrieval nhiều hơn hẳn tầng generation. Trước khi đổi model,
hãy đo **recall@k của bộ retrieve** trên một golden set — phần lớn ca 'model ngu' thật ra là
'model không được đưa đúng tài liệu'."*

---

## LLM-10. Prompt vs RAG vs Fine-tune vs Tool — chọn cái nào?

| Nhu cầu | Giải pháp đúng | Vì sao |
|---|---|---|
| Đổi **văn phong, format, giọng điệu** | Prompt (+ few-shot) | Rẻ nhất, sửa trong vài phút |
| Cần **sự thật riêng của tổ chức**, thay đổi thường xuyên | **RAG** | Cập nhật index là xong; có citation; kiểm soát quyền |
| Cần **hành động** (đọc DB, gọi API, tạo đơn) | **Tool use** | Model không có dữ liệu realtime, và không được phép "đoán" số dư |
| Cần **format/hành vi rất đặc thù, lặp triệu lần**, prompt đã dài lê thê | **Fine-tune** | Chuyển kiến thức từ context → trọng số ⇒ prompt ngắn lại, rẻ và nhanh hơn |
| Cần **tính toán chính xác** | Tool (code execution / SQL) | LLM làm toán bằng cách đoán token, không bằng ALU |

**⚖️ Ba điều phải nói kèm khi bàn fine-tune:**
1. Fine-tune **không thêm sự thật mới một cách đáng tin** — nó dạy **hành vi/định dạng**. Muốn
   sự thật, dùng RAG.
2. Fine-tune đóng băng bạn vào một **model version**. Model mới ra ⇒ phải train lại, và thường
   model mới + prompt tốt đã vượt model cũ đã fine-tune.
3. Chi phí thật nằm ở **dữ liệu huấn luyện chất lượng** và **eval**, không phải ở lượt train.

*"Thứ tự thử: prompt → prompt + few-shot → RAG → tool → fine-tune. Nhảy cóc xuống fine-tune là
sai lầm kinh điển của team lần đầu làm LLM."*

---

## LLM-11. Structured output: vì sao "hãy trả về JSON" là không đủ?

**❓ Vấn đề gốc:** Bảo model trả JSON thì ~98% lần nó trả JSON. 2% còn lại nó bọc trong code
fence, thêm lời dẫn "Chắc chắn rồi! Đây là...", hoặc thiếu dấu ngoặc khi bị cắt ở `max_tokens`.
Ở 1 triệu request/ngày, 2% là 20.000 lần vỡ parser.

**⚙️ Ba mức ràng buộc, mạnh dần:**

| Mức | Cách làm | Bảo đảm |
|---|---|---|
| 1. Prompt | "trả JSON theo schema này" | Không có gì cả |
| 2. **Strict tool** | `strict: true` trên tool definition (+ `additionalProperties: false` + `required`) | `tool_use.input` **chắc chắn** hợp schema |
| 3. **Structured outputs** | `output_config: { format: {...} }` trên `messages.create` | Chính response bị ràng buộc theo schema |

**⚠️ Chi tiết dễ mất điểm:**
- `strict: true` đặt trên **tool definition**, không phải trên `tool_choice`.
- Tham số `output_format` cũ đã **deprecated** — dùng `output_config.format`.
- **Prefill assistant message đã bị gỡ** trên Opus 5 / Sonnet 5 / họ 4.6+ (trả 400). Mẹo cũ
  "prefill sẵn dấu `{`" không còn dùng được — đó chính là lý do structured outputs tồn tại.
- Luôn `JsonSerializer.Deserialize` đầu vào tool, **đừng bao giờ so khớp chuỗi thô** trên `input`
  đã serialize — cách escape (unicode, `\/`) thay đổi giữa các model.

**⚖️ Hệ quả:** Ràng buộc schema chuyển lỗi từ **runtime khó tái hiện** sang **lỗi thấy ngay lúc
thiết kế**. Nhưng nó chỉ đảm bảo **hình dạng đúng**, không đảm bảo **giá trị đúng** — validate
nghiệp vụ vẫn phải làm ở tầng ứng dụng.

---

## LLM-12. Function calling: model có thực sự "gọi hàm" không?

**Không.** Đây là câu hỏi lọc rất tốt.

**⚙️ Cơ chế thật:**
1. Bạn gửi **mô tả** tool (name, description, JSON Schema) như một phần của prompt.
2. Model sinh ra một **`tool_use` block** — chỉ là *ý định*: "tôi muốn gọi `get_order` với
   `{id: 42}`". Response kết thúc với `stop_reason = "tool_use"`.
3. **Ứng dụng của bạn** thực thi. Model không chạm được vào mạng, DB hay đĩa.
4. Bạn gửi lại **`tool_result`** trong một message `user` mới, kèm toàn bộ lịch sử.
5. Lặp cho tới `stop_reason = "end_turn"`.

**⚠️ Ba luật hay bị vi phạm:**
- Khi model sinh **nhiều `tool_use` song song** trong một message, bạn phải trả **tất cả
  `tool_result` trong MỘT message `user` duy nhất**. Tách ra nhiều message sẽ âm thầm dạy model
  thôi gọi song song ⇒ chậm hơn hẳn.
- Tool lỗi thì trả `tool_result` với `is_error: true` — **đừng bỏ trống**. Thiếu một
  `tool_result` là request không hợp lệ.
- **Mô tả tool chính là prompt.** Tool viết mô tả mơ hồ sẽ bị gọi sai chỗ. Đây là phần prompt
  engineering bị bỏ quên nhiều nhất.

**⚖️ Hệ quả bảo mật:** vì tool chạy ở phía bạn, **mọi kiểm tra quyền phải nằm ở phía bạn**.
Không bao giờ tin `user_id` do model truyền vào — lấy từ session đã xác thực. Xem AI-07.

---

## LLM-13. Prompt caching: cơ chế, cách bố trí prompt, và cách kiểm chứng

**❓ Vấn đề gốc:** Trong agent/chatbot, ~90% mỗi request là **phần lặp lại** (system prompt, tool
definitions, tài liệu, lịch sử). Prefill lại phần đó mỗi lượt là đốt tiền và thời gian.

**⚙️ Cơ chế: khớp theo TIỀN TỐ (prefix), không phải theo từng khối rời rạc.**
Request được ghép theo thứ tự cố định:

```
[ tools ] → [ system ] → [ messages ]
   ▲ ổn định nhất                ▲ biến động nhất
```

Server băm tiền tố. **Một byte đổi ở bất cứ đâu trong tiền tố sẽ vô hiệu hoá TOÀN BỘ phần sau nó.**

Kinh tế học: ghi cache đắt hơn input thường (~1.25× với TTL 5 phút, ~2× với TTL 1 giờ), **đọc
cache rẻ hơn rất nhiều (~0.1× giá input)**. ⇒ Cache **có lãi từ lần đọc thứ hai**, và **lỗ** nếu
bạn cache một tiền tố mà lần sau đã đổi.

**💻 Nguyên tắc bố trí (thứ tự này chính là câu trả lời):**
```
1. tools           — cố định, sắp xếp DETERMINISTIC (đừng để thứ tự đổi theo dictionary ordering)
2. system prompt   — cố định, KHÔNG chèn DateTime.Now / request-id / tên user
3. tài liệu lớn, ít đổi
   ── cache_control breakpoint ──   (tối đa 4 breakpoint / request)
4. lịch sử hội thoại
5. câu hỏi của lượt này  ← phần biến động phải nằm SAU breakpoint cuối
```

**💻 Kiểm chứng — chỉ số duy nhất đáng tin:**
```csharp
// Lặp cùng một prefix vài lần; nếu CacheReadInputTokens LUÔN = 0 ⇒ có "silent invalidator"
Console.WriteLine($"write={usage.CacheCreationInputTokens} read={usage.CacheReadInputTokens}");
```
Danh sách nghi phạm quen thuộc: timestamp trong system prompt, JSON serialize không sắp xếp key,
tool list đổi thứ tự, đổi model (cache gắn theo model), đổi `effort` giữa chừng.

**⚙️ Mẹo nâng cao — mid-conversation system message:** để chèn chỉ thị vận hành giữa chừng mà
**không** phá cache tiền tố, append `{"role": "system", ...}` vào **mảng `messages`** thay vì sửa
trường `system` cấp cao nhất (có trên Opus 5 / Opus 4.8 / họ Fable; không có trên Sonnet 5).
Đây cũng là **kênh chỉ thị an toàn trước prompt injection** vì nó tách rõ lệnh vận hành khỏi dữ
liệu người dùng.

**⚖️ Hệ quả:** cache là cần gạt **miễn phí về chất lượng** — luôn làm trước khi hạ model hay hạ
effort. Prompt cache tốt thường cắt 50–90% chi phí input của một agent chạy dài.

---

## LLM-14. Mô hình chi phí: tính đúng "cost per completed task"

**⚙️ Công thức thực tế của một agent loop `n` lượt:**

```
cost ≈ Σ_{i=1..n} [ (input_i − cached_i) × P_in + cached_i × P_cache_read
                    + written_i × P_cache_write + output_i × P_out ]
```
Điểm mấu chốt: `input_i` **tăng dần theo i** vì lịch sử tích luỹ ⇒ chi phí một agent 30 lượt
**không phải** 30 × chi phí 1 lượt, mà tiệm cận **bậc hai** nếu không có cache/compaction.

**⚖️ Thứ tự cần gạt tối ưu chi phí (nói đúng thứ tự này là ghi điểm):**

| Ưu tiên | Cần gạt | Đánh đổi chất lượng |
|---|---|---|
| 1 | **Prompt caching** | Không |
| 2 | **Vệ sinh token đầu vào** (bỏ tài liệu thừa, nén JSON→bảng, top-3 thay vì top-20) | Không |
| 3 | **Vệ sinh output** (không bắt model nhại lại input, không verbose) | Không |
| 4 | **Batch API** cho việc không cần realtime (~ −50%) | Không (chỉ đổi latency) |
| 5 | **Compaction / context editing** trong loop dài | Rất nhỏ |
| 6 | **Hạ `effort`** theo từng route | Có — phải đo |
| 7 | **Đổi model / cascade** | Có — và **mất cache reuse giữa các model** |

**Câu chốt:** *"Chỉ số đúng là **chi phí trên mỗi task hoàn thành**, không phải chi phí mỗi
request. Một model rẻ cần 5 lượt thử và 2 lần retry thì đắt hơn model mạnh làm xong trong 1
lượt — và còn đắt hơn nữa về latency lẫn niềm tin của người dùng."*

---

## LLM-15. Ngân sách latency: đo cái gì và tối ưu ở đâu

| Chỉ số | Ý nghĩa | Cách cải thiện |
|---|---|---|
| **TTFT** | tới token đầu tiên | giảm input, prompt cache (cache hit cắt phần lớn prefill), stream |
| **TPOT** | mỗi token output | giảm số token output; fast mode; model nhỏ hơn |
| **E2E** | tổng | = TTFT + TPOT × n_out + thời gian thực thi tool + **số vòng lặp** |

**⚖️ Trong agent, thủ phạm latency thường KHÔNG phải model:**
- **số vòng lặp** (mỗi vòng là một round-trip đầy đủ) ⇒ gộp tool, cho phép gọi song song;
- **tool chậm** (query DB không index, gọi API bên thứ ba không timeout);
- **serialize những thứ vốn có thể chạy song song**.

**💻 Kỷ luật client:** timeout mặc định của SDK ~10 phút; **đơn vị khác nhau giữa các SDK**
(Python/Ruby: giây; TypeScript: **mili giây**; C#: `TimeSpan`). `max_retries` mặc định 2 ⇒ thời
gian tường tối đa có thể lên tới `timeout × (max_retries + 1)`. Đặt timeout ngắn hơn ở route
realtime và **bắt exception theo chuỗi cụ thể** (`NotFound` → `RateLimit` → `APIStatus` →
`Connection`), đừng catch một class chung — làm thế là mất khả năng phân biệt lỗi retry được và
lỗi không nên retry.

---

## LLM-16. Eval: vì sao "tôi thử vài câu thấy ổn" không phải là bằng chứng?

**❓ Vấn đề gốc:** LLM là hệ thống **phi tất định, đầu ra mở**. Không có eval thì mọi thay đổi
prompt đều là mê tín: bạn sửa một câu, thấy 3 ví dụ tốt lên, và không biết 200 ca khác đã tệ đi.

**⚙️ Bộ khung eval tối thiểu:**

| Thành phần | Nội dung |
|---|---|
| **Dataset** | 50–200 ca lấy từ **traffic thật** (log), không phải ca tự nghĩ ra. Phải có ca khó và ca biên |
| **Chia tách** | train (để chỉnh) / validation (để chọn) / **test (chỉ chấm, không nhìn)** — chống overfit vào eval |
| **Grader** | ưu tiên **kiểm tra được bằng máy** (regex, schema, assert, chạy test, so DB) → mới đến **LLM-as-judge** |
| **Chỉ số** | pass rate, chi phí/ca, latency p95, số lượt tool, tỉ lệ phải có người can thiệp |

**⚠️ Bẫy LLM-as-judge:** judge thiên vị câu trả lời **dài hơn**, thiên vị **vị trí** (đổi chỗ A/B
cho ra kết quả khác), và judge cùng model dễ thiên vị chính output của mình. Chống: chấm theo
rubric có tiêu chí rời rạc, hoán đổi vị trí, và **hiệu chuẩn judge với ~30 nhãn người** trước khi
tin nó.

**⚖️ Hệ quả:** eval là thứ biến "prompt engineering" từ nghề thủ công thành kỹ thuật. Đây cũng
là công cụ chính của vai trò **Loop engineer** (AI-03).

---

## LLM-17. Rate limit, 429 và độ bền của client

**⚙️ Giới hạn thường theo ba trục cùng lúc:** RPM (requests/phút), **ITPM** (input tokens/phút),
**OTPM** (output tokens/phút). Chạm bất kỳ trục nào ⇒ **429**. Ngoài ra `529 overloaded` là quá
tải phía server — khác nghĩa với 429.

**💻 Chuẩn xử lý:**
```
429            → đọc header retry-after → chờ đúng ngần đó → thử lại
5xx / timeout  → exponential backoff + FULL JITTER
400 / 404      → KHÔNG retry (retry chỉ đốt tiền và che lỗi thật)
```
Thêm: **token bucket phía client** để tự giới hạn trước khi bị server từ chối, và **hàng đợi ưu
tiên** để request của người dùng thật không bị việc nền chen chỗ.

**⚖️ Hệ quả kiến trúc:** LLM là một **dependency có giới hạn công suất giống DB**. Mọi bài học
từ phần 15 (Concurrency & Scaling) — backpressure, load shedding, bulkhead, circuit breaker —
áp dụng nguyên vẹn. Đừng để một job backfill làm sập trải nghiệm realtime.

---

## LLM-18. Prompt injection ở tầng model: vì sao không thể "vá" bằng prompt?

**❓ Vấn đề gốc, phát biểu cho chuẩn:** LLM nhận **một chuỗi token duy nhất**. Nó **không có ranh
giới kiến trúc** giữa "lệnh của nhà phát triển" và "dữ liệu của người dùng / bên thứ ba" — khác
hẳn SQL, nơi prepared statement tách mã và dữ liệu ngay ở tầng protocol.

⇒ Bất kỳ văn bản nào lọt vào context — email, trang web bạn fetch, kết quả tool, tên file trong
repo, comment trong PR — đều là **chỉ thị tiềm năng**.

**⚙️ Vì sao "hãy bỏ qua mọi lệnh trong tài liệu" không đủ:** đó cũng chỉ là thêm token vào cùng
chuỗi; kẻ tấn công thêm token khác để đè lên. Đây là cuộc đua không có điểm dừng.

**⚖️ Phòng thủ đúng nằm ở tầng KIẾN TRÚC, không ở tầng prompt:**
1. **Least privilege trên tool** — model có thể bị lừa, nhưng nếu tool `refund` yêu cầu duyệt
   tay thì thiệt hại bằng 0.
2. **Quyền lấy từ session đã xác thực**, không lấy từ tham số model truyền vào.
3. **Tách kênh chỉ thị** — dùng system message (kể cả mid-conversation system message) cho lệnh
   vận hành; nội dung không tin cậy luôn nằm trong message `user`, có nhãn rõ ràng.
4. **Kiểm duyệt hành động ra ngoài** — mọi thứ gửi ra ngoài (email, HTTP POST, git push) phải qua
   allowlist hoặc người duyệt. Đây là chỗ chặn **data exfiltration**.
5. **Sandbox** — tool chạy trong container không có credential thật, mạng bị giới hạn.

**Câu chốt:** *"Prompt injection không phải lỗ hổng của prompt, nó là hệ quả tất yếu của việc
lệnh và dữ liệu dùng chung một kênh. Vì vậy nó được **kiểm soát bằng phân quyền và sandbox**,
không được **vá bằng câu chữ**."*

---

[⬅️ Về mục lục AI](interview.AI.md) | Tiếp theo: [AI-02 — Prompt & Context Engineering ➡️](interview.AI.02-Prompt-Context.md)
