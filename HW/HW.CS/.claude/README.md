# AI Loop Agentic — boilerplate `.claude/`

Khung làm việc cho dev: **coding · fixbug · analysis (SRS/SRD/mockup) · report**, dựng theo
[`interview/interview.AI.md`](../interview/interview.AI.md).

---

## 🧭 Luận điểm trung tâm

> **Model là phần xác suất. Harness là phần tất định.**
>
> Mọi thứ bạn muốn xảy ra **chắc chắn 100%** — format sau khi sửa, chặn `rm -rf`, không cho kết
> thúc khi build đỏ — **không được** đặt vào prompt. Nó phải đặt vào **hook / permission**.

Câu hỏi đầu tiên khi thêm bất cứ thứ gì vào đây:

```
                        AI QUYẾT ĐỊNH?
                 model ◄──────────────────► người / hệ thống
   ┌──────────────────────────────┬──────────────────────────────┐
   │ SKILL      (model tự kích     │ SLASH COMMAND (người gõ)     │
   │            hoạt theo mô tả)   │ SETTINGS      (cấu hình)     │
   │ SUBAGENT   (model uỷ thác)    │ HOOK          (sự kiện, TẤT  │
   │ MCP TOOL   (model gọi)        │                ĐỊNH, chặn được)│
   └──────────────────────────────┴──────────────────────────────┘
         ▲ xác suất — "có thể xảy ra"      ▲ tất định — "chắc chắn xảy ra"
```

**Không được phép bỏ sót → hook.** Chỉ là *nên làm* → skill hoặc CLAUDE.md.
Nhầm hai loại này là nguồn gốc của mọi *"sao Claude không làm theo hướng dẫn của tôi"*.

---

## 🔁 Vòng lặp

```
  ┌──────────────┐                                     │
  │ 1. GATHER    │  hook SessionStart nạp branch/diff   │
  │              │  agent `explorer` nếu phải đọc nhiều │
  └──────┬───────┘                                     │
         ▼                                             │
  ┌──────────────┐                              ┌──────┴───────┐
  │ 2. ACT       │─────────────────────────────▶│ 3. VERIFY    │
  │  hook format │                              │ Verify.ps1   │
  │  sau mỗi sửa │                              │ hook Stop    │
  └──────────────┘                              └──────┬───────┘
                                          đạt? ────────┴──── chưa ─┘
                                            │              (hook loop-progress
                                            ▼               chặn thrashing)
                                       4. REPORT
```

**Agent = LLM + tool + vòng lặp + điều kiện dừng.** Bỏ **verify** thì nó chỉ là cỗ máy sinh
hành động tự tin.

---

## 🚀 Bắt đầu

```bash
# 1. Đặt CLAUDE.md ở gốc project (giữ NGẮN — nó vào context mỗi lượt)
cp .claude/CLAUDE.template.md ./CLAUDE.md

# 2. Cấu hình cá nhân (đã gitignore)
cp .claude/settings.local.json.example .claude/settings.local.json

# 3. Kiểm tra verifier chạy được — đây là điều kiện tiên quyết của cả hệ thống
powershell -NoProfile -File .claude/functions/Verify.ps1 -Level build

# 4. (tuỳ chọn) MCP dùng chung
cp .claude/mcp/mcp.example.json ./.mcp.json
```

Trong phiên: `/harness-doctor` để xác nhận mọi thứ đã nạp đúng.

---

## 📁 Cấu trúc

| Thư mục | Là gì | Ai kích hoạt | Tất định? |
|---|---|---|---|
| [`settings.json`](settings.json) | permission + đăng ký hook | harness | ✅ |
| [`hooks/`](hooks/README.md) | 10 hook: chặn, format, cổng chất lượng, thrashing, kiểm toán | **sự kiện** | ✅ |
| [`functions/`](functions/README.md) | `Verify` · `Task` · `Trace` — thư viện tất định dùng chung | hook, command, người | ✅ |
| [`agents/`](agents/README.md) | 8 subagent, mỗi cái **giới hạn tool cứng** | model uỷ thác | ❌ |
| [`skills/`](skills/README.md) | 8 quy trình dài, progressive disclosure | **model** tự quyết | ❌ |
| [`commands/`](commands/README.md) | 12 lệnh `/`, mỗi lệnh mở đầu bằng dữ liệu thật | **người** gõ | ❌ |
| [`workflows/`](workflows/README.md) | 3 pipeline + 4 mẫu tài liệu | — | — |
| [`llms/`](llms/README.md) | model registry, routing, prompt có version | code của bạn | — |
| [`mcp/`](mcp/README.md) | mẫu `.mcp.json` + chi phí context + 6 rủi ro | model gọi | ❌ |
| [`plugins/`](plugins/ai-loop-agentic/README.md) | bản đóng gói để dùng lại giữa nhiều repo | — | — |
| [`state/`](state/README.md) | trạng thái loop **ngoài context** (gitignored) | hook, functions | — |

---

## ⚡ Dùng hằng ngày

| Việc | Gõ |
|---|---|
| Làm một feature | `/dev <mô tả>` |
| Sửa lỗi | `/fix <triệu chứng>` |
| Hiểu code | `/analyze <câu hỏi>` |
| Đặc tả nghiệp vụ | `/srs <feature>` |
| Tài liệu thiết kế | `/srd <feature>` |
| Phác thảo UI | `/mockup <màn hình>` |
| Rà soát trước khi push | `/review` |
| Xong chưa? | `/verify` |
| Báo cáo | `/report <phạm vi>` |
| Loop đang hỏng ở đâu? | `/loop-status` |
| Cấu hình sai ở đâu? | `/harness-doctor` |
| Cần nhiều góc nhìn | `/team <nhiệm vụ>` |

---

## 🛡️ Ranh giới an toàn (tất định — không bẻ được bằng prompt)

| Chặn | Ở đâu |
|---|---|
| `rm -rf /`, force push, `git reset --hard`, `DROP TABLE`, `DELETE` không `WHERE` | `hooks/block-dangerous.ps1` |
| Đọc `.env`, `secrets/`, `*.pfx`, `id_rsa` | `hooks/protect-secrets.ps1` + `permissions.deny` |
| Migration, `kubectl apply`, `docker push` | `permissions.ask` |
| Kết thúc lượt khi build/test đỏ | `hooks/stop-quality-gate.ps1` (`LOOP_QUALITY_GATE=1`) |
| Lặp cùng lỗi 3 lần | `hooks/loop-progress.ps1` |

⚠️ **Hook chạy với quyền của bạn, không sandbox.** Một `.claude/settings.json` trong repo lạ
**là code thực thi** — review nó như review script `postinstall`.

---

## ⚙️ Công tắc

| Biến (trong `settings.json` → `env`) | Mặc định | Tác dụng |
|---|---|---|
| `LOOP_QUALITY_GATE` | `0` | `1` = không cho kết thúc lượt khi build/test đỏ |
| `LOOP_MAX_TURNS` | `25` | trần số lượt của một loop |
| `LOOP_MAX_SAME_ERROR` | `3` | số lần lỗi giống hệt trước khi ép đổi hướng |
| `LOOP_BUILD_CMD` / `LOOP_TEST_CMD` | `dotnet build` / `dotnet test` | verifier của repo |
| `LOOP_AUDIT` | `1` | `0` = tắt ghi `audit.jsonl` |

Dùng ngôn ngữ khác? Đổi `LOOP_BUILD_CMD` / `LOOP_TEST_CMD` — không cần sửa script nào.

---

## 🖥️ Nền tảng

Hook viết bằng **PowerShell** (nền tảng đích là Windows). Bản **POSIX** của 4 hook quan trọng
nhất nằm ở [`hooks/sh/`](hooks/sh) (cần `jq`) — đổi `command` trong `settings.json` sang
`bash "${CLAUDE_PROJECT_DIR}/.claude/hooks/sh/<tên>.sh"` khi dùng trên macOS/Linux.

---

## 📚 Nguồn

Mọi quyết định thiết kế ở đây đều truy được về bộ đề trong `interview/`:

| Chủ đề | File | Mã tham chiếu dùng trong template |
|---|---|---|
| Prompt & context | [AI-02](../interview/interview.AI.02-Prompt-Context.md) | `PE-*` |
| Agentic loop | [AI-03](../interview/interview.AI.03-Agentic-Loop.md) | `AG-*` |
| MCP | [AI-04](../interview/interview.AI.04-MCP.md) | `MCP-*` |
| Harness | [AI-05](../interview/interview.AI.05-Harness-Config.md) | `HR-*` |
| Multi-agent | [AI-06](../interview/interview.AI.06-MultiAgent-Teams.md) | `MA-*` |
| Kiến trúc | [AI-07](../interview/interview.AI.07-Impl-Architecture.md) | `ARCH-*` |

---

## ⚠️ Độ tươi của thông tin

API và harness thay đổi nhanh. Những chỗ có **phiên bản hoặc con số cụ thể** (model id, giá,
tên hook event, cấu trúc plugin) cần kiểm chứng lại trước khi dùng làm khẳng định chắc chắn —
xem `llms/models.json` → `apiDrift` cho danh sách những thứ **đã lỗi thời**.

*"Biết một API đã thay đổi, và biết vì sao nó thay đổi, giá trị hơn thuộc lòng API hiện tại."*
