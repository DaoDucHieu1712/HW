# Hooks — tầng **tất định** của loop

> **Luận điểm gốc (AI-05, HR-1):** model là phần **xác suất**; harness là phần **tất định**.
> Mọi thứ bạn muốn xảy ra **chắc chắn 100%** — format sau khi sửa, chặn `rm -rf`, không cho
> kết thúc khi build đỏ — **không được** đặt vào prompt. Nó phải đặt vào **hook / permission**.
>
> Câu hỏi đầu tiên khi thêm bất cứ thứ gì: **"cái này được phép không xảy ra không?"**
> Không được phép bỏ sót → hook. Chỉ là *nên làm* → skill hoặc CLAUDE.md.

---

## 1. Bảng hook trong template này

| Script | Sự kiện | Matcher | Chặn? | Vai trò trong loop |
|---|---|---|---|---|
| `session-start-context.ps1` | `SessionStart` | `startup\|resume\|clear` | ❌ | **GATHER** — nạp branch/diff/task vào context bằng dữ liệu thật |
| `prompt-guard.ps1` | `UserPromptSubmit` | — | ❌ | dán nhãn dữ liệu không đáng tin; reset bộ đếm thrashing |
| `block-dangerous.ps1` | `PreToolUse` | `Bash` | ✅ **deny/ask** | ranh giới an toàn không bẻ được bằng prompt |
| `protect-secrets.ps1` | `PreToolUse` | `Read\|Grep\|Glob` | ✅ **deny** | chặn đọc secret |
| `format-after-edit.ps1` | `PostToolUse` | `Edit\|Write` | ❌ (async) | **ACT** — ép chuẩn code 100% lần |
| `loop-progress.ps1` | `PostToolUseFailure` | `*` | ⚠️ exit 2 | **STOP** — phát hiện thrashing (AG-6 §4) |
| `stop-quality-gate.ps1` | `Stop` | — | ✅ **exit 2** | **VERIFY** — chống "stopping short" (AG-8) |
| `subagent-stop-log.ps1` | `SubagentStop` | — | ❌ (async) | số liệu kinh tế multi-agent (MA-2) |
| `pre-compact-snapshot.ps1` | `PreCompact` | `auto\|manual` | ❌ (async) | giữ dấu vết trước khi context bị nén |
| `audit-log.ps1` | `PostToolUse` | `*` | ❌ (async) | kiểm toán → `.claude/state/audit.jsonl` |

Bản POSIX của 4 hook quan trọng nhất nằm ở `sh/` (macOS/Linux, cần `jq`).

---

## 2. Hợp đồng I/O — thuộc bảng này là đủ (HR-5)

**Vào:** JSON qua **stdin**. Trường chung: `session_id`, `transcript_path`, `cwd`,
`permission_mode`, `hook_event_name`, `effort`; sự kiện tool có thêm `tool_name`, `tool_input`,
`tool_use_id`.

**Ra — ba đường:**

| Exit code | Ý nghĩa |
|---|---|
| **0** | Thành công. stdout chỉ vào debug log — **trừ** `UserPromptSubmit`, `UserPromptExpansion`, `SessionStart`, `PostModelSwitch`: stdout **được đưa vào context cho model** |
| **2** | **Lỗi chặn.** `PreToolUse` chặn tool · `UserPromptSubmit` từ chối prompt · `Stop`/`SubagentStop` **không cho dừng** · `TaskCompleted` không cho đánh dấu xong. Thông điệp lấy từ **stderr** |
| khác | Lỗi không chặn — ghi log, agent chạy tiếp |

**Hoặc in JSON ra stdout để điều khiển có cấu trúc:**

```json
{
  "systemMessage": "hiện trong transcript",
  "additionalContext": "được thêm vào context của Claude",
  "hookSpecificOutput": {
    "hookEventName": "PreToolUse",
    "permissionDecision": "allow | deny | request",
    "permissionDecisionReason": "lý do — Claude đọc được",
    "updatedInput": { "…": "SỬA tham số tool trước khi chạy" }
  }
}
```

⚠️ **Hai chi tiết hay bị hỏi:**
- `PostToolUse` **không thể ngăn** tool (nó đã chạy rồi). exit 2 ở đó chỉ **đưa stderr cho Claude
  đọc** để nó tự sửa — đó chính là cách `loop-progress.ps1` bẻ hướng agent đang kẹt.
- `updatedInput` cho phép hook **viết lại tham số tool**. Rất mạnh, nhưng model **không thấy**
  mình bị sửa — dùng dè dặt.

---

## 3. "Function hooks" — năm **loại** hook, không chỉ `command`

Trường `type` trong `hooks[]` quyết định *ai* thực thi việc kiểm tra. Đây là trục thiết kế bị
bỏ quên nhiều nhất:

| `type` | Ai chạy | Tất định? | Dùng cho | Ví dụ trong template |
|---|---|---|---|---|
| `command` | shell của bạn | ✅ | mặc định — nhanh, rẻ, kiểm chứng được | tất cả hook ở trên |
| `http` | endpoint từ xa | ✅ | kiểm toán/tuân thủ tập trung cho nhiều repo | xem §3.1 |
| `mcp_tool` | một tool MCP | ✅ | tra cứu chính sách trong hệ thống nội bộ | xem §3.2 |
| `prompt` | **chính model** | ❌ | phán xét ngữ nghĩa mà regex không làm được | xem §3.3 |
| `agent` | một subagent | ❌ | thẩm định nhiều bước (review bảo mật trước khi push) | xem §3.4 |

> ⚖️ **Đánh đổi phải nói ra:** `prompt` và `agent` đánh mất **chính lý do hooks tồn tại** —
> tính tất định. Chúng là verifier **hạng D** theo thang AG-5. Dùng khi và chỉ khi không có
> verifier hạng A–C (compiler / test / assert trạng thái). Và chúng tốn token **mỗi lần sự kiện
> nổ ra**, nên đừng gắn vào `PreToolUse` của một tool hay được gọi.

### 3.1 `http` — kiểm toán tập trung

```json
{ "PostToolUse": [{ "matcher": "Edit|Write|Bash",
  "hooks": [{ "type": "http",
              "url": "https://compliance.internal/api/claude-audit",
              "headers": { "Authorization": "Bearer ${COMPLIANCE_TOKEN}" },
              "timeout": 10, "async": true }] }] }
```
Secret đi qua `${ENV}`, **không bao giờ** hard-code vào file commit.

### 3.2 `mcp_tool` — hỏi hệ thống chính sách nội bộ

```json
{ "PreToolUse": [{ "matcher": "Bash",
  "hooks": [{ "type": "mcp_tool",
              "server": "policy",
              "tool": "check_command",
              "timeout": 15 }] }] }
```

### 3.3 `prompt` — phán xét ngữ nghĩa

```json
{ "Stop": [{ "hooks": [{ "type": "prompt", "model": "claude-haiku-4-5-20251001",
  "prompt": "Đọc transcript. Agent có tuyên bố hoàn thành mà KHÔNG chạy verifier nào không? Nếu có, trả về JSON {\"block\":true,\"reason\":\"...\"}. Nếu không, {\"block\":false}." }] }] }
```
Dùng model rẻ nhất đủ dùng cho vai trò gác cổng — nó chạy **mỗi lượt**.

### 3.4 `agent` — thẩm định nhiều bước

```json
{ "PreToolUse": [{ "matcher": "Bash", "if": "Bash(git push:*)",
  "hooks": [{ "type": "agent", "agent": "reviewer", "timeout": 300 }] }] }
```

---

## 4. Bảo mật của chính hooks (HR-8)

Hook chạy **với quyền của người dùng, không sandbox**. Ba hệ quả:

1. Một `.claude/settings.json` độc hại trong repo clone về **là code thực thi**. Review file
   settings của repo lạ như review script `postinstall`.
2. Hook nhận `tool_input` do **model** sinh ra ⇒ nội dung có thể do kẻ tấn công điều khiển qua
   prompt injection. **Luôn parse bằng `jq`/`ConvertFrom-Json`, luôn đóng ngoặc kép biến,
   không bao giờ `eval` / `Invoke-Expression` chuỗi lệnh.**
3. Tổ chức muốn hook không bị vô hiệu hoá ⇒ đặt ở **managed settings**, và cân nhắc kiểm soát
   cờ `disableAllHooks`.

---

## 5. Vận hành

- Hook phải **nhanh và im lặng khi không có việc**. Hook chậm nằm trên `PreToolUse` sẽ nhân lên
  theo **mỗi** lời gọi tool và giết trải nghiệm. Việc dài ⇒ `async: true`.
- Đường dẫn **luôn** dùng `${CLAUDE_PROJECT_DIR}` (hoặc `${CLAUDE_PLUGIN_ROOT}` trong plugin).
  Hard-code đường dẫn là lỗi hỏng-trên-máy-người-khác phổ biến nhất.
- Debug: `claude --debug` in ra hook nào khớp, exit code, output.
- *"Hook không chạy"* thường là: matcher sai (khớp theo **tên tool**, không phải nội dung lệnh),
  file không có quyền thực thi, hoặc đường dẫn không dùng `${CLAUDE_PROJECT_DIR}`.

### Công tắc nhanh

| Biến | Mặc định | Tác dụng |
|---|---|---|
| `LOOP_QUALITY_GATE` | `0` | `1` = bật cổng build+test ở `Stop` |
| `LOOP_MAX_SAME_ERROR` | `3` | số lần lỗi giống hệt trước khi ép đổi hướng |
| `LOOP_AUDIT` | `1` | `0` = tắt ghi `audit.jsonl` |
| `LOOP_BUILD_CMD` / `LOOP_TEST_CMD` | `dotnet build` / `dotnet test` | lệnh verifier của repo |
