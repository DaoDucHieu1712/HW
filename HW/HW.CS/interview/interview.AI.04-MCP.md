# Phần AI-04 — MCP (Model Context Protocol) chuyên sâu

[⬅️ AI-03 — Agentic Loop](interview.AI.03-Agentic-Loop.md) | [⬅️ Mục lục AI](interview.AI.md) | Tiếp theo: [AI-05 — Harness: Hooks, Settings, Commands, Skills, Plugins ➡️](interview.AI.05-Harness-Config.md)

> Khung mỗi câu: **❓ Vấn đề gốc → ⚙️ Cơ chế bên dưới → 💻 Cấu hình/code → ⚖️ Hệ quả thực chiến**.

---

## 🗺️ Bản đồ: ai nói chuyện với ai

```
┌───────────────── HOST (Claude Code / IDE / app của bạn) ─────────────────┐
│  vòng lặp agent + LLM                                                    │
│                                                                          │
│   ┌── MCP Client A ──┐   ┌── MCP Client B ──┐   ┌── MCP Client C ──┐     │
│   │ 1 client ↔ 1 srv │   │                  │   │                  │     │
└───┴────────┬─────────┴───┴────────┬─────────┴───┴────────┬─────────┴─────┘
             │ JSON-RPC 2.0         │                      │
      stdio (process con)    streamable HTTP         streamable HTTP
             │                      │                      │
      ┌──────▼──────┐        ┌──────▼──────┐        ┌──────▼──────┐
      │ MCP Server  │        │ MCP Server  │        │ MCP Server  │
      │ filesystem  │        │   GitHub    │        │   Postgres  │
      └─────────────┘        └─────────────┘        └─────────────┘
         tools / resources / prompts
```

**Câu chốt phỏng vấn:** *"MCP không làm model thông minh hơn. Nó biến bài toán tích hợp từ
**M host × N tool** thành **M + N** — mỗi host viết client một lần, mỗi tool viết server một
lần. Đó là toàn bộ giá trị: **chuẩn hoá bề mặt tích hợp**, giống như LSP đã làm cho editor ×
ngôn ngữ."*

---

## MCP-1. Vấn đề gốc: vì sao cần một giao thức, mà không chỉ "viết tool"?

**❓ Trước MCP:** mỗi ứng dụng AI tự định nghĩa cách khai báo tool. Muốn dùng GitHub trong 5 host
khác nhau ⇒ viết 5 adapter. Muốn thêm host thứ 6 ⇒ viết lại N adapter. **M × N**.

**⚙️ MCP chuẩn hoá bốn thứ:**
1. **Cách khám phá năng lực** (`tools/list`, `resources/list`, `prompts/list`).
2. **Cách gọi** (`tools/call` với JSON Schema đã công bố).
3. **Cách đóng gói kết quả** (content block: text / image / resource).
4. **Vòng đời & thương lượng năng lực** (`initialize`).

**⚖️ Hệ quả:** MCP server là **tài sản dùng lại được** giữa các host và giữa các team. Nhưng phải
nói kèm điểm yếu: *"MCP chuẩn hoá **kênh truyền**, nó không chuẩn hoá **chất lượng thiết kế
tool**. Một MCP server tồi vẫn là một bề mặt tool tồi — chỉ là giờ nó tồi ở nhiều host cùng lúc."*

---

## MCP-2. Ba vai: Host, Client, Server — phân biệt cho chuẩn

| Vai | Là gì | Ví dụ |
|---|---|---|
| **Host** | Ứng dụng chạy LLM và vòng lặp agent; sở hữu quyền và UI duyệt | Claude Code, IDE, app của bạn |
| **Client** | Thành phần **bên trong host**, giữ kết nối **1–1** với một server | mỗi server một client |
| **Server** | Tiến trình/dịch vụ **cung cấp năng lực** | filesystem, GitHub, Postgres |

**⚠️ Ba hiểu nhầm hay gặp:**
- *"Server gọi model"* — **không**. Server chỉ cung cấp năng lực; host mới chạy model. (Ngoại lệ
  duy nhất là **sampling**: server *xin* host chạy một lời gọi LLM hộ mình — xem MCP-6.)
- *"Client là app người dùng"* — không, client là **lớp kết nối** bên trong host.
- *"MCP thay thế tool use"* — không. MCP là **cách cung cấp** tool; model vẫn dùng đúng cơ chế
  `tool_use` / `tool_result` như LLM-12.

---

## MCP-3. Giao thức: JSON-RPC 2.0 và vòng đời phiên

**⚙️ MCP = JSON-RPC 2.0** (request có `id`, notification không có `id`) chạy trên một transport.

**Vòng đời:**
```
client → initialize        { protocolVersion, capabilities, clientInfo }
server → result            { protocolVersion, capabilities, serverInfo }
client → notifications/initialized
        ── từ đây mới được gọi các method khác ──
client → tools/list        → server trả danh sách + JSON Schema
client → tools/call        → server trả content[]
server → notifications/tools/list_changed   (nếu công bố năng lực đó)
```

**⚙️ Thương lượng năng lực (capability negotiation)** là điểm thiết kế quan trọng: hai bên nói
trước mình hỗ trợ gì (`tools`, `resources`, `prompts`, `sampling`, `roots`, `elicitation`,
`logging`, ...). Nhờ đó **client cũ vẫn nói chuyện được với server mới** — không bên nào giả định
tính năng chưa được công bố.

**⚖️ Hệ quả:** khi viết server, **luôn khai báo đúng capability**. Gửi `list_changed` mà không
công bố năng lực đó ⇒ client hợp lệ sẽ bỏ qua, và bạn tưởng "MCP bị lỗi".

---

## MCP-4. Transport: chọn stdio hay HTTP?

| Transport | Cách chạy | Ưu | Nhược | Dùng khi |
|---|---|---|---|---|
| **stdio** | host spawn **tiến trình con**, nói qua stdin/stdout | đơn giản nhất, không cần auth mạng, nhanh, chạy được lệnh local | chỉ local; mỗi client một tiến trình; **log phải ghi ra stderr** | truy cập filesystem, git, DB local, script nội bộ |
| **Streamable HTTP** | HTTP POST + có thể nâng cấp lên stream sự kiện | remote, nhiều client dùng chung, hợp với OAuth | cần hạ tầng, cần bảo mật | SaaS, server dùng chung cho cả team |
| **SSE** (cũ) | HTTP + Server-Sent Events | — | **đã deprecated** | chỉ để tương thích ngược |
| **WebSocket** | kênh hai chiều | server đẩy sự kiện chủ động | phức tạp hơn | server cần push event không theo yêu cầu |

**⚠️ Bẫy kinh điển của stdio:** **in log ra stdout sẽ phá giao thức.** stdout là kênh JSON-RPC.
Mọi log phải đi **stderr**. Triệu chứng: server "kết nối rồi rớt" hoặc "parse error" không rõ lý do.

**💻 Cấu hình trong `.mcp.json`:**
```jsonc
{
  "mcpServers": {
    "docs": {                                   // remote
      "type": "http",
      "url": "https://mcp.example.com/mcp",
      "headers": { "Authorization": "Bearer ${DOCS_TOKEN}" }
    },
    "db": {                                     // local
      "type": "stdio",
      "command": "npx",
      "args": ["-y", "@bytebase/dbhub", "--dsn", "${DB_URL}"],
      "env": { "DB_URL": "${DB_URL}" }
    }
  }
}
```
Hỗ trợ mở rộng biến môi trường: `${VAR}` và `${VAR:-mặc_định}` — **đừng commit secret vào file
này**, hãy để nó đọc biến môi trường.

---

## MCP-5. Ba primitive phía server — và ai "điều khiển" từng cái

Đây là câu hỏi phân loại rất hay được dùng để lọc người chỉ đọc lướt tài liệu:

| Primitive | Ai điều khiển | Bản chất | Ví dụ |
|---|---|---|---|
| **Tools** | **Model** quyết định gọi | hành động, **có thể có side effect** | `create_issue`, `run_query` |
| **Resources** | **Ứng dụng/host** quyết định đưa vào context | dữ liệu **chỉ đọc**, định danh bằng URI | `file:///repo/README.md`, `db://schema` |
| **Prompts** | **Người dùng** kích hoạt | mẫu prompt/workflow đóng gói sẵn | `/github:review-pr` |

**⚖️ Vì sao sự phân biệt này quan trọng:** nếu bạn phơi mọi thứ dưới dạng **tool**, model phải
tự quyết định gọi 30 thứ và bạn trả tiền cho 30 schema mỗi lượt. Dữ liệu tham chiếu nên là
**resource** (host chèn khi cần), quy trình cố định nên là **prompt** (người gọi). Tool để dành
cho **hành động thật sự cần model quyết định**.

Trong Claude Code:
- Tool xuất hiện dưới tên `mcp__<server>__<tool>`.
- Resource tham chiếu bằng `@<server>:<đường/dẫn>`.
- Prompt trở thành slash command `/<server>:<prompt>`.

---

## MCP-6. Ba primitive phía client: sampling, roots, elicitation

Chiều ngược lại — server yêu cầu host làm gì đó. Ít người biết, hỏi ra rất phân loại.

| Primitive | Ai xin | Làm gì | Rủi ro/kiểm soát |
|---|---|---|---|
| **Sampling** | server → host | "chạy hộ tôi một lời gọi LLM" (server không cần API key riêng) | **phải có người duyệt**; nếu không, server bên thứ ba tiêu tiền model của bạn và có thể tiêm chỉ thị |
| **Roots** | host → server | "phạm vi bạn được phép làm việc là các thư mục này" | ranh giới bảo mật cho server filesystem |
| **Elicitation** | server → host | "hỏi người dùng thêm một thông tin" (chọn nhánh, nhập tham số) | tránh việc server phải đoán; nhưng là một kênh có thể bị lạm dụng để dụ người dùng |

**⚖️ Hệ quả bảo mật:** **sampling** là primitive nguy hiểm nhất trong MCP. Nó cho phép một server
không tin cậy **điều khiển gián tiếp một lời gọi model bằng chi phí của bạn**. Chính sách đúng:
mặc định tắt, bật theo từng server, và luôn hiển thị nội dung cho người duyệt.

---

## MCP-7. Cấu hình MCP trong Claude Code: scope và thứ tự ưu tiên

| Scope | Lưu ở đâu | Ai thấy | Dùng cho |
|---|---|---|---|
| **local** (mặc định) | `~/.claude.json`, theo từng project | chỉ bạn | server thử nghiệm, cấu hình cá nhân |
| **project** | **`.mcp.json`** ở gốc repo | cả team (commit vào git) | server dùng chung của dự án |
| **user** | `~/.claude.json` toàn cục | bạn, ở mọi project | tiện ích cá nhân dùng khắp nơi |

**Thứ tự ưu tiên:** `local` > `project` > `user` > `plugin` > connector.

```bash
claude mcp add --transport http stripe https://mcp.stripe.com          # local
claude mcp add --transport http shared --scope project https://.../mcp # project → .mcp.json
claude mcp list        # trạng thái sức khoẻ từng server
claude mcp get notion  # chi tiết
/mcp                   # trong phiên: xem trạng thái, đăng nhập, reconnect
```

**⚠️ Workspace trust:** server khai báo trong `.mcp.json` (đến từ repo) **cần được người dùng
tin tưởng thư mục** trước khi chạy. Đây là rào chắn chống việc `git clone` một repo lạ rồi bị nó
tự khởi động một tiến trình. Rào này bị bỏ qua trong `claude -p`, Agent SDK, cloud session — nên
ở CI phải cân nhắc `--strict-mcp-config`.

---

## MCP-8. Đặt tên & không gian tên: vì sao `mcp__server__tool`?

**⚙️ Dạng tên:**
```
mcp__<server-name>__<tool-name>                                  # server thường
mcp__plugin_<plugin-name>_<server-name>__<tool-name>             # server đi kèm plugin
```

**⚖️ Vì sao cần tiền tố:** hai server đều có tool tên `search` ⇒ model không biết gọi cái nào, và
tệ hơn — một server độc hại có thể cố ý đặt trùng tên tool của server đáng tin để **chiếm quyền
gọi** (**tool shadowing**). Không gian tên làm cho việc này bất khả thi và làm cho **permission
rule viết được chính xác**:

```jsonc
{ "permissions": {
    "allow": ["mcp__docs__search"],           // chỉ cho phép đúng 1 tool
    "deny" : ["mcp__db__execute_write"]       // chặn cứng tool ghi
}}
```

---

## MCP-9. Chi phí context của MCP — chỗ MCP hay "âm thầm phá" agent

**❓ Vấn đề gốc:** Cắm 6 server, mỗi server 15 tool ⇒ 90 định nghĩa tool + schema nằm trong
**mọi** request. Con số này dễ đạt 20k–40k token *trước khi người dùng gõ gì*.

**⚙️ Bốn cơ chế kiểm soát:**

| Cơ chế | Làm gì |
|---|---|
| **Tool search + `defer_loading: true`** | chỉ nạp schema của tool khi model cần. ⚠️ **Không được defer tất cả** — chính tool search và ít nhất một tool phải luôn nạp, nếu không API trả 400 |
| **Giới hạn output** | `MAX_MCP_OUTPUT_TOKENS` (mặc định 25.000, cảnh báo từ 10.000); server cũng có thể tự khai báo giới hạn qua `_meta` |
| **Discovery cache** | cache danh sách tool để không phải kết nối mọi server lúc khởi động; kết nối theo nhu cầu |
| **Tự backgrounding** | lời gọi tool chạy quá lâu (mặc định > 2 phút) được đẩy xuống chạy nền thay vì chặn phiên |

**⚖️ Nguyên tắc vận hành:** *"Bật MCP server như bật dependency — mỗi cái phải trả tiền context
mỗi lượt. Sáu server đang bật mà bạn chỉ dùng một là đang trả thuế cho năm cái còn lại."*
Kiểm tra bằng `/context` và tắt bớt.

---

## MCP-10. Xác thực: OAuth, header động, và anti-pattern "token passthrough"

**⚙️ Ba cách:**
1. **OAuth 2.1** cho server HTTP — luồng chuẩn, có refresh, thu hồi được. Trong Claude Code:
   `/mcp` → Authenticate, hoặc `claude mcp login <server>` / `claude mcp logout <server>`.
2. **Header tĩnh** — `Authorization: Bearer ${TOKEN}` từ biến môi trường. Đơn giản; hợp với
   server nội bộ.
3. **`headersHelper`** — trỏ tới một script sinh header động (Kerberos, SSO nội bộ, token ngắn
   hạn). Script in ra JSON header. Đây là cách đúng cho token có hạn dùng ngắn.

**⚠️ Anti-pattern phải gọi tên được: "token passthrough".** Server MCP nhận token của người dùng
rồi **chuyển thẳng** sang API hạ nguồn mà không tự xác thực và tự phân quyền. Hậu quả: hạ nguồn
mất khả năng phân biệt "ai thực sự gọi", vượt qua kiểm soát tần suất và audit của nó. Đúng: server
xác thực người gọi, rồi dùng **credential của chính nó** (hoặc token đã hạ quyền) để gọi hạ nguồn.

---

## MCP-11. Bề mặt tấn công của MCP: sáu rủi ro phải kể được tên

| # | Rủi ro | Cơ chế | Giảm thiểu |
|---|---|---|---|
| 1 | **Prompt injection qua tool result** | server trả về nội dung chứa chỉ thị; model đọc và làm theo | coi mọi output MCP là **dữ liệu không tin cậy**; cổng duyệt cho hành động ra ngoài |
| 2 | **Rug pull** | tool description thay đổi **sau khi** người dùng đã duyệt server | ghim phiên bản/hash; cảnh báo khi định nghĩa tool đổi |
| 3 | **Tool shadowing / trùng tên** | server độc chiếm tên tool của server tin cậy | không gian tên `mcp__server__tool`; allowlist theo tên đầy đủ |
| 4 | **Confused deputy** | server có quyền cao thực hiện hành động thay cho bên không đủ quyền | server tự kiểm tra quyền; không passthrough token |
| 5 | **Exfiltration** | server "đọc file" rồi gửi nội dung ra ngoài | roots giới hạn phạm vi; kiểm soát egress; sandbox |
| 6 | **Cung ứng phần mềm** | `npx -y <gói-lạ>` chạy code tuỳ ý trên máy bạn với quyền của bạn | ghim phiên bản, review nguồn, chạy trong container |

**⚖️ Câu chốt:** *"Cài một MCP server = **cài phần mềm chạy với quyền của bạn** và **đưa văn bản
do bên thứ ba kiểm soát vào ngay trung tâm vòng lặp agent**. Vì thế MCP phải được quản trị như
một dependency sản xuất: nguồn gốc, phiên bản, phạm vi quyền, và audit — chứ không phải như một
plugin tiện tay."*

---

## MCP-12. Khi nào **không** nên dùng MCP?

**⚖️ Bốn tình huống MCP là lựa chọn sai:**

1. **Chỉ có một host và một tool.** Viết tool trực tiếp trong ứng dụng đơn giản hơn nhiều —
   không cần tiến trình con, không cần giao thức.
2. **Đã có CLI tốt.** Nếu agent có `bash` và công việc là `gh pr list`, một MCP server bọc lại
   GitHub thường **kém hơn**: tốn schema, thêm lớp, mà `gh` đã có sẵn help, phân trang, auth.
   *"Đừng bọc một CLI tốt bằng MCP chỉ để cho nó 'chuẩn'."*
3. **Nội dung là kiến thức, không phải năng lực.** Quy trình, quy ước, tài liệu ⇒ dùng **skill**
   (nạp theo nhu cầu, không tốn schema tool) — xem AI-05.
4. **Việc chạy một lần trong CI.** Một script thẳng đơn giản và dễ audit hơn.

**MCP xứng đáng khi:** nhiều host/nhiều người cùng dùng, cần auth quản trị tập trung, cần khám phá
năng lực động, hoặc hệ thống đích không có CLI/API dễ dùng.

---

## MCP-13. Thiết kế một MCP server tốt: sáu quy tắc

1. **Ít tool, đúng mức trừu tượng.** Đừng ánh xạ 1–1 mỗi endpoint REST thành một tool. Hãy ánh xạ
   **theo ý định của người dùng** (`find_customer_orders`) thay vì theo bảng dữ liệu.
2. **Description viết như doc API**: DÙNG KHI / KHÔNG DÙNG KHI / giới hạn / định dạng trả về (PE-5).
3. **Output gọn và có cấu trúc.** Trả 500 dòng JSON là đầu độc context. Trả tóm tắt + `next_cursor`
   + hướng dẫn cách lấy thêm.
4. **Lỗi phải dạy được cho model** (AG-9): nói rõ cái gì sai, trạng thái hiện tại, gợi ý bước tiếp.
5. **Tách rõ đọc/ghi** và đánh dấu tool có side effect, để host đặt cổng duyệt đúng chỗ.
6. **Idempotency cho tool ghi** — agent sẽ retry, đó là chuyện chắc chắn xảy ra.

**💻 Test một server bằng công cụ inspector trước khi cắm vào agent** — kiểm tra `tools/list`,
gọi thử từng tool, xem hình dạng output. Debug ở tầng giao thức nhanh hơn debug qua model rất nhiều.

---

## MCP-14. MCP trên Messages API (không qua Claude Code)

**💻 Kết nối MCP server trực tiếp từ API cần **cả hai nửa** — đây là lỗi validation rất phổ biến:

```jsonc
{
  "model": "claude-opus-5",
  "mcp_servers": [ { "type": "url", "url": "https://mcp.example.com/mcp", "name": "docs" } ],
  "tools":       [ { "type": "mcp_toolset", "mcp_server_name": "docs" } ]   // ⚠️ thiếu là 400
}
```
Kèm beta header `mcp-client-2025-11-20`.

**⚠️ Lưu ý tương thích:** tool MCP **không dùng chung** với một số tính năng khác (ví dụ
programmatic tool calling). Và tính khả dụng khác nhau giữa Claude API và các nền tảng đám mây —
phải kiểm tra trước khi thiết kế phụ thuộc vào nó.

---

## MCP-15. Phiên bản & tương thích

**⚙️ Ba mức phiên bản khác nhau, đừng trộn:**
1. **Protocol version** — thương lượng ở `initialize`, dạng ngày tháng.
2. **Server version** — phiên bản triển khai của bạn.
3. **Tool schema** — hợp đồng với model.

**⚖️ Quy tắc thay đổi tool schema (giống versioning API):**
- **Thêm** trường tuỳ chọn: an toàn.
- **Đổi/xoá** trường, đổi enum, đổi ngữ nghĩa: **breaking** — nhưng khác API thường ở chỗ *không
  có compiler nào báo lỗi*. Nó biểu hiện thành **model gọi sai và bạn phát hiện qua eval**.
- ⇒ **Bộ eval là bộ test hồi quy của MCP server.** Đổi description cũng phải chạy eval, vì
  description **là** hợp đồng với model.

---

## MCP-16. Debug MCP: đi từ dưới lên

```
1. Server có chạy độc lập không?      → chạy tay, gửi initialize bằng inspector/stdin
2. stdout có bị nhiễm log không?      → log PHẢI ra stderr (bẫy số 1 của stdio)
3. Host có kết nối được không?        → claude mcp list / /mcp  → trạng thái + lỗi
4. Tool có xuất hiện với model không? → /context xem tool đã nạp; kiểm tra defer_loading
5. Model có gọi không?                → nếu không: description tệ, hoặc bị permission deny
6. Gọi rồi hỏng?                      → xem tool_result: is_error, nội dung lỗi có đủ nghĩa không
7. Chạy chậm/treo?                    → kiểm tra ngưỡng auto-background, timeout, phân trang
```

**⚖️ Nguyên tắc chẩn đoán:** *"Phần lớn 'lỗi MCP' không phải lỗi giao thức. Chúng là **lỗi thiết
kế tool** — mô tả mơ hồ nên model không gọi, hoặc output quá to nên context vỡ. Hãy đọc trajectory
trước khi đọc log giao thức."*

---

[⬅️ AI-03](interview.AI.03-Agentic-Loop.md) | [⬅️ Mục lục AI](interview.AI.md) | Tiếp theo: [AI-05 — Harness ➡️](interview.AI.05-Harness-Config.md)
