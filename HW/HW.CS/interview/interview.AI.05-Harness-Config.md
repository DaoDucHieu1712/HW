# Phần AI-05 — Harness: Hooks, Settings, Commands, Skills, Plugins

[⬅️ AI-04 — MCP](interview.AI.04-MCP.md) | [⬅️ Mục lục AI](interview.AI.md) | Tiếp theo: [AI-06 — Multi-agent & Agent Team ➡️](interview.AI.06-MultiAgent-Teams.md)

> Khung mỗi câu: **❓ Vấn đề gốc → ⚙️ Cơ chế bên dưới → 💻 Cấu hình → ⚖️ Hệ quả thực chiến**.
>
> **Luận điểm trung tâm:** model là phần **xác suất**; harness là phần **tất định**. Mọi thứ bạn
> muốn xảy ra **chắc chắn 100%** — format code sau khi sửa, chặn `rm -rf`, cấm commit secret —
> **không được** đặt vào prompt. Nó phải đặt vào **hook / permission / settings**.

---

## 🗺️ Bản đồ: sáu cơ chế mở rộng và trục quyết định

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

**Câu chốt phỏng vấn:** *"Câu hỏi đầu tiên khi mở rộng agent là: **thứ này được phép không xảy ra
không?** Nếu **không được phép bỏ sót** → hook hoặc permission. Nếu chỉ là *nên làm* → skill hoặc
CLAUDE.md. Nhầm hai loại này là nguồn gốc của mọi 'sao Claude không làm theo hướng dẫn của tôi'."*

---

## HR-1. Sáu cơ chế mở rộng — bảng quyết định (câu hỏi trung tâm của phần này)

| Cơ chế | Ai kích hoạt | Tất định? | Vào context khi nào | Dùng cho |
|---|---|---|---|---|
| **CLAUDE.md / rules** | tự động | ❌ (chỉ dẫn) | **luôn luôn** — trả tiền mỗi lượt | quy ước ngắn, ranh giới, lệnh build |
| **Skill** | model (theo `description`) **hoặc** người (`/tên`) | ❌ | **description luôn; thân nạp khi kích hoạt** | quy trình/kiến thức dài, dùng lại được |
| **Slash command** | **người** gõ | ❌ (nội dung là prompt) | khi gọi | thao tác nhanh người chủ động |
| **Subagent** | model uỷ thác, hoặc `context: fork` | ❌ | context **riêng** | việc đọc-nhiều, cần cô lập ngữ cảnh |
| **MCP tool** | model gọi | ❌ | schema luôn nạp (trừ khi defer) | **năng lực** (đọc/ghi hệ thống ngoài) |
| **Hook** | **sự kiện của harness** | ✅ **CÓ** | không vào context (trừ khi hook chủ động chèn) | bắt buộc, chặn, kiểm toán, tự động hoá |

**⚖️ Ba lỗi ánh xạ hay gặp:**
- Viết *"luôn chạy `dotnet format` sau khi sửa file"* vào CLAUDE.md → nó **sẽ bị bỏ sót**. Đây là
  việc của `PostToolUse` hook.
- Nhét 3.000 dòng tài liệu nghiệp vụ vào CLAUDE.md → trả tiền mỗi lượt. Đây là việc của **skill**.
- Dùng MCP server chỉ để cung cấp *kiến thức* (không có hành động) → tốn schema tool vô ích. Đây
  cũng là việc của **skill**.

---

## HR-2. Settings: năm nguồn và thứ tự ưu tiên

**⚙️ Thứ tự, cao nhất trước:**

```
1. Managed settings        (tổ chức triển khai: managed-settings.json / MDM / console)
2. Command line            (claude --settings ...)   — chỉ cho phiên hiện tại
3. Project local           (.claude/settings.local.json)  — cá nhân, cho project này
4. Shared project          (.claude/settings.json)        — commit vào git, cả team
5. User                    (~/.claude/settings.json)      — cá nhân, mọi project
```

**⚠️ Bốn chi tiết dễ mất điểm:**

1. **List thì MERGE, không override.** `permissions.allow` ở nhiều file được **gộp lại**, không
   phải file cao hơn đè file thấp hơn. (Một số key dạng danh sách model như `fallbackModel`,
   `availableModels` có luật riêng — lấy nguyên khối từ nguồn cao nhất.)
2. **Biến môi trường KHÔNG phải một tầng trong stack này.** Quan hệ được quyết định **theo từng
   cặp key**: ví dụ `ANTHROPIC_MODEL` export ở shell thắng key `model` từ mọi file, nhưng
   `ANTHROPIC_DEFAULT_MODEL` chỉ có tác dụng khi không file nào đặt `model`.
3. **Một số key không có tác dụng từ file trong repo.** Ví dụ `permissions.defaultMode` với giá
   trị `auto` / `bypassPermissions` **không** ăn từ project/local settings — phải đặt ở user hoặc
   managed. Đây là rào chắn an toàn: một repo lạ không thể tự nâng quyền cho mình.
4. **Ngoại lệ an toàn của managed settings.** Với vài key nhạy cảm, Claude Code **ưu tiên giá trị
   NGHIÊM NGẶT HƠN** dù nó đến từ tầng thấp hơn (ví dụ tắt connector, tắt Artifact). Nguyên tắc:
   *tổ chức không thể bị người dùng nới lỏng, nhưng người dùng luôn được phép siết chặt thêm.*

**💻 Chẩn đoán:** `/status` cho biết **file nào đã nạp** và **managed source nào đang áp dụng** —
đây là bước đầu tiên khi "cấu hình của tôi không có tác dụng".

---

## HR-3. Permission: `allow` / `ask` / `deny` và vì sao "đã bấm đừng hỏi lại" vẫn bị hỏi

**⚙️ Ba loại rule, và **`deny` > `ask` > `allow`** về sức mạnh (không phải theo tầng file).

```jsonc
{
  "permissions": {
    "defaultMode": "default",
    "allow": ["Bash(git status)", "Bash(dotnet build*)", "Read", "mcp__docs__search"],
    "ask":   ["Bash(git push*)"],
    "deny":  ["Bash(rm -rf*)", "Read(./.env)", "Read(./secrets/**)"],
    "additionalDirectories": ["../shared-lib"]
  }
}
```

**⚖️ Giải thích tình huống kinh điển:** *"Tôi đã chọn 'Yes, and don't ask again' nhưng vẫn bị
hỏi."* → Lựa chọn đó ghi một rule **`allow` vào file local của bạn**. Nhưng project hoặc managed
settings có một rule **`ask`** cho cùng thao tác. `ask` mạnh hơn `allow`, **bất kể** file nào ở
tầng cao hơn. Đây là thiết kế cố ý: cá nhân không được tự bỏ qua cổng kiểm soát của team.

**⚠️ Workspace trust:** `permissions.allow`, `additionalDirectories`, phần lớn `env`, và
marketplace từ file repo **chỉ có hiệu lực sau khi người dùng tin tưởng thư mục**. Ngược lại,
`deny` và `ask` **có hiệu lực ngay** — chiều siết chặt không cần chờ tin tưởng.

---

## HR-4. Hooks: vì sao chúng tồn tại, và bản đồ sự kiện

**❓ Vấn đề gốc:** Chỉ dẫn trong prompt là **xác suất**. Với những việc phải đúng 100% lần —
tuân thủ, bảo mật, kiểm toán, format — xác suất là không chấp nhận được.

**⚙️ Hook = chương trình do **harness** chạy tại một sự kiện xác định. Nó chạy dù model "quên",
và nó có thể **chặn** hành động.

**Bản đồ sự kiện theo nhóm (không cần thuộc hết, cần biết có những nhóm nào):**

| Nhóm | Sự kiện tiêu biểu |
|---|---|
| **Vòng đời phiên** | `SessionStart`, `SessionEnd`, `Setup` |
| **Đầu vào người dùng** | `UserPromptSubmit`, `UserPromptExpansion` |
| **Tool** | `PreToolUse`, `PostToolUse`, `PostToolUseFailure`, `PostToolBatch` |
| **Quyền** | `PermissionRequest`, `PermissionDenied` |
| **Kết thúc lượt** | `Stop`, `StopFailure`, `SubagentStart`, `SubagentStop` |
| **Agent team / task** | `TaskCreated`, `TaskCompleted`, `TeammateIdle` |
| **Context** | `PreCompact`, `PostCompact`, `InstructionsLoaded` |
| **Môi trường** | `ConfigChange`, `CwdChanged`, `FileChanged`, `DirectoryAdded`, `WorktreeCreate/Remove` |
| **Model / MCP** | `PreModelSwitch`, `PostModelSwitch`, `Elicitation`, `ElicitationResult` |

**⚖️ Bốn sự kiện đáng nhớ nhất cho phỏng vấn:** `PreToolUse` (chặn trước khi chạy),
`PostToolUse` (làm sạch/format sau khi chạy), `UserPromptSubmit` (chèn ngữ cảnh hoặc từ chối
prompt), `Stop` (không cho dừng khi chưa xong việc).

---

## HR-5. Hợp đồng I/O của hook: stdin, exit code, JSON output

**⚙️ Vào:** JSON qua **stdin**. Trường chung: `session_id`, `transcript_path`, `cwd`,
`permission_mode`, `hook_event_name`, `effort`, và với sự kiện tool có thêm `tool_name`,
`tool_input`, `tool_use_id`.

**⚙️ Ra — ba đường:**

| Exit code | Ý nghĩa |
|---|---|
| **0** | Thành công. Với hầu hết sự kiện, stdout chỉ vào debug log. **Ngoại lệ**: `UserPromptSubmit`, `UserPromptExpansion`, `SessionStart`, `PostModelSwitch` — stdout **được đưa vào context cho model** |
| **2** | **Lỗi chặn.** `PreToolUse` chặn tool; `UserPromptSubmit` từ chối prompt; `Stop`/`SubagentStop` **không cho dừng**; `TaskCompleted` không cho đánh dấu xong. Thông điệp lấy từ stderr hoặc từ `permissionDecisionReason` |
| khác | Lỗi không chặn (ghi log, agent chạy tiếp) |

**⚙️ Hoặc in JSON ra stdout để điều khiển có cấu trúc:**
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

**⚠️ Chi tiết rất hay bị hỏi:** `PostToolUse` **không thể ngăn** tool (nó đã chạy rồi) — exit 2 ở
đó chỉ **đưa stderr cho Claude đọc** để nó tự sửa. Và `updatedInput` cho phép hook **viết lại tham
số** — mạnh, nhưng phải dùng cẩn thận vì model không thấy mình bị sửa.

**💻 Hook chặn lệnh nguy hiểm:**
```bash
#!/usr/bin/env bash
# .claude/hooks/block-danger.sh   (PreToolUse, matcher "Bash")
cmd=$(jq -r '.tool_input.command')
if echo "$cmd" | grep -Eq 'rm -rf /|git push .*--force|DROP TABLE'; then
  jq -n '{hookSpecificOutput:{hookEventName:"PreToolUse",
          permissionDecision:"deny",
          permissionDecisionReason:"Lệnh phá huỷ bị chặn bởi chính sách repo."}}'
  exit 0
fi
exit 0   # không quyết định → luồng permission bình thường tiếp tục
```

---

## HR-6. Cấu hình hook: matcher, loại hook, và các cờ vận hành

```jsonc
{
  "hooks": {
    "PostToolUse": [{
      "matcher": "Edit|Write",                       // khớp theo TÊN TOOL
      "hooks": [{
        "type": "command",                           // command | http | mcp_tool | prompt | agent
        "command": "${CLAUDE_PROJECT_DIR}/.claude/hooks/format.sh",
        "timeout": 60,
        "async": true,                               // không chặn vòng lặp
        "statusMessage": "Đang format…"
      }]
    }],
    "PreToolUse": [{
      "matcher": "Bash",
      "hooks": [{ "type": "command",
                  "if": "Bash(rm *)",                // lọc thêm theo permission rule
                  "command": ".claude/hooks/block-danger.sh" }]
    }],
    "SessionStart": [{
      "matcher": "startup",                          // startup|resume|clear|compact|fork
      "hooks": [{ "type": "command", "command": "git status --short", "once": true }]
    }]
  }
}
```

**⚙️ Matcher mang ý nghĩa khác nhau theo sự kiện:** tên tool cho sự kiện tool; kiểu khởi động cho
`SessionStart`; loại agent cho `SubagentStart/Stop`; `manual|auto` cho `PreCompact`; tên model cho
`PreModelSwitch`; nguồn config cho `ConfigChange`.

**⚙️ Năm loại hook:** `command` (chạy lệnh), `http` (gọi endpoint — hợp với hệ thống tuân thủ tập
trung), `mcp_tool` (gọi một tool MCP), `prompt` và `agent` (dùng chính model để phán xét — mạnh
nhưng **mất tính tất định**, hãy cân nhắc kỹ).

**⚙️ Nơi khai báo hook:** user settings, project settings, local settings, managed policy,
`hooks/hooks.json` của plugin, frontmatter của skill (áp dụng phần còn lại của phiên), frontmatter
của subagent (chỉ khi nó chạy).

---

## HR-7. Năm hook đáng làm nhất trong một repo thật

| Hook | Sự kiện | Giá trị |
|---|---|---|
| **Format/lint sau khi sửa file** | `PostToolUse` matcher `Edit\|Write` | ép chuẩn code 100% lần, không tốn token nhắc trong prompt |
| **Chặn lệnh phá huỷ & chặn đọc secret** | `PreToolUse` matcher `Bash`/`Read` | ranh giới an toàn thật sự, không bẻ được bằng prompt injection |
| **Chèn ngữ cảnh phiên** | `SessionStart` | nhánh git, task đang mở, trạng thái CI — đưa vào context ngay đầu phiên |
| **Cổng chất lượng trước khi kết thúc** | `Stop` (exit 2 nếu test đỏ) | chống "stopping short" (AG-8) một cách tất định |
| **Kiểm toán** | `PostToolUse` + `type: "http"` | gửi log mọi hành động ghi về hệ thống tuân thủ |

**⚖️ Nguyên tắc:** hook nên **nhanh và im lặng khi không có việc**. Hook chậm nằm trên đường
`PreToolUse` sẽ nhân lên theo mỗi lời gọi tool và giết trải nghiệm. Việc dài ⇒ `async: true`.

---

## HR-8. Bảo mật của chính hooks

**⚠️ Hook chạy **với quyền của người dùng**, không sandbox. Ba hệ quả:**
1. Một `.claude/settings.json` độc hại trong repo clone về **là code thực thi**. Đây là lý do
   phải review file settings của repo lạ như review script `postinstall`.
2. Hook nhận `tool_input` do **model** sinh ra ⇒ nội dung đó có thể do kẻ tấn công điều khiển
   (prompt injection). **Luôn trích trường bằng `jq`, đóng ngoặc kép biến, không `eval`.**
3. Tổ chức muốn đảm bảo hook không bị vô hiệu hoá ⇒ đặt ở **managed settings**; có cả cờ
   `disableAllHooks` mà chính sách cần cân nhắc kiểm soát.

---

## HR-9. Slash command / skill invocation: cách truyền tham số và chèn dữ liệu sống

**⚙️ Ba cơ chế thay thế trong nội dung:**

| Cú pháp | Làm gì |
|---|---|
| `$ARGUMENTS` | toàn bộ phần người dùng gõ sau tên lệnh |
| `$1 $2 …` / `$tên` | tham số theo vị trí hoặc theo tên (khai báo `arguments: [issue, branch]`) |
| `` !`lệnh` `` | **chạy lệnh trước**, thay bằng output — model nhận **dữ liệu thật**, không phải hướng dẫn đi lấy |
| `@đường/dẫn` | tham chiếu file |
| `${CLAUDE_PROJECT_DIR}`, `${CLAUDE_SKILL_DIR}`, `${CLAUDE_SESSION_ID}`, `${CLAUDE_EFFORT}` | biến môi trường của harness |

**💻 Vì sao `` !`cmd` `` là kỹ thuật mạnh:**
```markdown
---
description: Tóm tắt thay đổi của PR hiện tại
---
## Diff hiện tại
!`gh pr diff`

## Nhiệm vụ
Tóm tắt các thay đổi trên theo nhóm chức năng, nêu rủi ro.
```
Không có nó, model phải **tự quyết định** gọi tool để lấy diff — thêm một lượt, và có thể quên.
Có nó, dữ liệu đã nằm sẵn trong prompt. Đây là ranh giới giữa **workflow** (tất định) và **agent**
(xác suất) áp dụng ở cấp một lệnh.

---

## HR-10. Skills: cấu trúc, progressive disclosure và ngân sách

**⚙️ Một skill = một thư mục có `SKILL.md`:**
```
.claude/skills/deploy-prod/
├── SKILL.md          # frontmatter + hướng dẫn (giữ dưới ~500 dòng)
├── reference.md      # chi tiết — chỉ đọc khi cần
└── scripts/check.sh
```

**⚙️ Progressive disclosure — ba tầng (đây là câu trả lời cốt lõi):**

| Tầng | Nạp khi nào | Chi phí |
|---|---|---|
| `description` | **luôn luôn** — để model biết skill tồn tại | rất nhỏ; tổng mọi description bị giới hạn trong ~1% cửa sổ context |
| Thân `SKILL.md` | khi skill được kích hoạt (bởi model hoặc người) | vừa; **ở lại context cho các lượt sau** |
| File tham chiếu | khi được đọc | chỉ khi cần |

Khi tổng description vượt ngân sách, harness **rút ngắn mô tả của các skill ít dùng** để giữ
skill hay dùng còn nhìn thấy được — nghĩa là **skill viết mô tả tệ sẽ bị "vô hình" trước tiên**.

**⚖️ Hệ quả thiết kế:** `description` là phần quan trọng nhất của skill, không phải nội dung.
Viết theo dạng *"Dùng khi <tình huống>, <từ khoá người dùng thường nói>"*. Mô tả kiểu *"Công cụ
hỗ trợ triển khai"* = skill chết.

---

## HR-11. Skill vs Command vs Subagent vs Hook vs MCP — bảng quyết định

| Bạn muốn… | Dùng | Vì sao |
|---|---|---|
| Kiến thức/quy trình dài, chỉ cần khi liên quan | **Skill** | progressive disclosure — không trả tiền khi không dùng |
| Người chủ động chạy một thao tác (deploy, commit) | **Skill với `disable-model-invocation: true`** | bạn kiểm soát thời điểm; model không tự bấm |
| Kiến thức nền model nên tự dùng, người không cần thấy | **Skill với `user-invocable: false`** | ẩn khỏi menu `/`, model vẫn tự kích hoạt |
| Việc đọc-nhiều, cần cô lập context | **Subagent** (hoặc skill với `context: fork`) | kết quả về, rác ở lại bên kia |
| Bắt buộc xảy ra, chặn được | **Hook** | tất định |
| Truy cập hệ thống ngoài (đọc/ghi) | **MCP tool** | năng lực, có auth, dùng lại giữa các host |
| Quy ước ngắn áp dụng mọi lúc | **CLAUDE.md** | nhưng phải thật ngắn |

**⚖️ Câu trả lời hay nhất cho câu hỏi "skill khác command chỗ nào?":**
*"Về hình thức chúng đã hội tụ — cùng là file markdown có frontmatter, cùng gọi bằng `/tên`.
Khác biệt thật nằm ở **ai kích hoạt và khi nào nội dung được nạp**: skill có `description` luôn
thường trú để **model tự quyết định dùng**, và thân chỉ nạp khi cần; command là thứ **người** gõ.
Nên câu hỏi thiết kế đúng là: *tôi có muốn model tự dùng thứ này không?* Nếu có — làm skill với
description tốt. Nếu không (thao tác có hệ quả) — `disable-model-invocation: true`."*

---

## HR-12. Điều khiển skill: các trường frontmatter đáng nhớ

```yaml
---
name: deploy-prod
description: Triển khai lên production. Dùng khi người dùng nói "deploy", "phát hành bản mới".
disable-model-invocation: true          # chỉ người được gọi — thao tác có hệ quả
allowed-tools: Bash(docker *) Bash(kubectl *)   # pre-approve, hết hiệu lực ở message kế tiếp
disallowed-tools: AskUserQuestion       # gỡ tool khỏi pool khi skill đang hoạt động
model: claude-opus-5
effort: high
paths: "src/**/*.cs"                    # chỉ kích hoạt khi làm việc trong phạm vi này
context: fork                           # chạy trong subagent, context riêng
agent: Explore                          # loại subagent khi fork
background: false                       # chờ kết quả fork thay vì chạy nền
argument-hint: "[môi-trường]"
---
```

**⚠️ Ba điểm hay bị hỏi:**
- `allowed-tools` là **pre-approve tạm thời** — quyền được cấp cho lượt hiện tại và **mất khi
  người dùng gửi message tiếp theo**. Nó **không** vượt qua được `deny` trong permissions.
- `paths` là cách rất rẻ để giảm nhiễu: skill về React không nên xuất hiện khi đang sửa file SQL.
- `context: fork` biến skill thành một subagent — dùng khi skill phải đọc rất nhiều.

**💻 Quản lý hiển thị mà không sửa file** (hữu ích khi skill đến từ plugin):
```jsonc
{ "skillOverrides": {
    "deploy": "off",                    // ẩn hoàn toàn
    "legacy-context": "name-only",      // model chỉ thấy tên
    "internal-check": "user-invocable-only"
}}
```

---

## HR-13. Subagents định nghĩa sẵn (`.claude/agents/*.md`)

```yaml
---
name: security-reviewer
description: Rà soát bảo mật thay đổi trên nhánh. Dùng khi được yêu cầu review bảo mật.
tools: Read, Grep, Glob, Bash            # giới hạn cứng bộ tool
model: claude-opus-5                     # hoặc "inherit"
---
Bạn là chuyên gia bảo mật. Chỉ đọc, không sửa. Với mỗi phát hiện: file:line, kịch bản khai thác,
mức độ. Không báo cáo suy đoán không có bằng chứng trong code.
```

**⚖️ Ba lý do dùng subagent định nghĩa sẵn thay vì mô tả trong prompt:**
1. **Giới hạn tool** — reviewer không có `Edit`/`Write` thì **không thể** sửa code, dù bị dụ.
2. **Tái sử dụng** — cùng một định nghĩa dùng được cả khi uỷ thác lẫn khi làm teammate (AI-06).
3. **Context sạch** — subagent không mang theo lịch sử hội thoại của luồng chính; nó cũng **không
   trả về** đống rác nó đã đọc.

---

## HR-14. Plugins: đóng gói và phân phối

**⚙️ Cấu trúc (điểm hay sai nhất: **chỉ `plugin.json` nằm trong `.claude-plugin/`**, mọi thư mục
khác nằm ở **gốc plugin**):**
```
my-plugin/
├── .claude-plugin/plugin.json   # name, description, version, author
├── skills/<tên>/SKILL.md
├── agents/*.md
├── hooks/hooks.json             # ⚠️ hook của plugin ở ĐÂY, không ở settings.json
├── .mcp.json                    # MCP server đi kèm
├── .lsp.json                    # language server
├── monitors/monitors.json       # theo dõi nền (tail log…) → gửi thông báo cho Claude
├── bin/                         # thêm vào PATH của Bash khi plugin bật
└── settings.json                # mặc định (hiện hỗ trợ `agent`, `subagentStatusLine`)
```

**⚙️ Namespace:** mọi skill của plugin được gọi là `/<plugin>:<skill>` ⇒ **không đụng tên** giữa
các plugin. MCP tool của plugin cũng có tiền tố riêng
(`mcp__plugin_<plugin>_<server>__<tool>`).

**💻 Vòng đời phát triển:**
```bash
claude --plugin-dir ./my-plugin      # thử tại chỗ (nhận cả .zip); lặp lại nhiều lần được
/reload-plugins                      # nạp lại không cần restart
claude plugin validate ./my-plugin   # kiểm tra trước khi phát hành
claude plugin init my-tool           # scaffold một plugin trong thư mục skills
```
Phân phối qua **marketplace** (một repo có `marketplace.json`) — dùng repo private cho plugin nội
bộ của công ty. `version` trong `plugin.json` quyết định khi nào người dùng nhận bản cập nhật.

**⚖️ `${CLAUDE_PLUGIN_ROOT}` và `${CLAUDE_PLUGIN_DATA}`** là hai biến bắt buộc phải dùng trong
hook/script của plugin — đường dẫn cài đặt khác nhau trên từng máy, hard-code là hỏng.

---

## HR-15. Standalone `.claude/` hay plugin? Và độ ưu tiên khi trùng

| | `.claude/` standalone | Plugin |
|---|---|---|
| Tên skill | `/hello` | `/plugin-name:hello` |
| Phân phối | copy tay / commit repo | marketplace, có version |
| Hợp với | thử nghiệm, đặc thù một repo | chia sẻ giữa nhiều repo/team, phát hành có kiểm soát |

**⚠️ Luật ưu tiên khi trùng tên (hay bị hỏi):**
- **Agent**: định nghĩa ở project/user `.claude/agents/` **đè** agent cùng tên của plugin.
- **Skill**: **không đè nhau** — vì plugin skill có namespace, cả `/hello` và
  `/plugin-name:hello` cùng tồn tại. Sau khi chuyển sang plugin, **phải xoá bản cũ** nếu không
  muốn có hai bản song song.
- Plugin nạp bằng `--plugin-dir` **thắng** bản cùng tên đã cài — trừ khi managed settings ép
  bật/tắt.

---

## HR-16. Thiết kế "harness của team": checklist triển khai cho 30 dev

```
.claude/settings.json          (COMMIT — cả team)
  ├─ permissions.deny          : rm -rf, force push, đọc .env / secrets/**
  ├─ permissions.ask           : git push, migration, deploy
  ├─ permissions.allow         : build, test, format, các MCP tool chỉ-đọc
  ├─ hooks.PostToolUse         : format + lint sau Edit|Write
  ├─ hooks.PreToolUse          : chặn lệnh phá huỷ (script trong repo)
  ├─ hooks.Stop                : test đỏ → exit 2, không cho kết thúc
  └─ env                       : biến dự án (không chứa secret)

.claude/settings.local.json    (GITIGNORE — cá nhân)
.claude/skills/                (COMMIT — quy trình của team: release, review, migration…)
.claude/agents/                (COMMIT — reviewer chỉ-đọc, explorer…)
.mcp.json                      (COMMIT — server dùng chung; secret qua ${ENV})
CLAUDE.md                      (COMMIT — NGẮN: lệnh build/test, ranh giới, cạm bẫy)
Managed settings               (do tổ chức triển khai — chính sách không thể nới lỏng)
```

**⚖️ Ba nguyên tắc quản trị:**
1. **Chính sách an toàn ở `deny`/`ask`, không ở CLAUDE.md.** Chỉ dẫn có thể bị bỏ qua; rule thì không.
2. **Secret không bao giờ vào file commit** — dùng `${VAR}` và biến môi trường.
3. **Cái gì phải đúng 100% ⇒ hook.** Cái gì chỉ *nên* ⇒ skill/CLAUDE.md. Đây lại là cùng một câu
   hỏi ở HR-1, và đó là lý do nó là câu hỏi trung tâm.

---

## HR-17. Debug harness: đi theo thứ tự này

```
1. /status          → file settings nào đã nạp? managed source nào đang áp?
2. /context         → cái gì đang chiếm context: CLAUDE.md, skill, MCP tool, lịch sử
3. /skills          → skill có được thấy không? bị "name-only" hay "off" không?
4. /mcp             → server kết nối chưa? cần đăng nhập không?
5. /plugin          → tab Errors: plugin/LSP/MCP nào nạp hỏng
6. claude --debug   → log hook: hook nào khớp, exit code, output
7. /reload-plugins  → sau khi sửa plugin/skill/hook mà không muốn restart
```

**⚖️ Ba triệu chứng → nguyên nhân:**
- *"Skill của tôi không bao giờ được dùng"* → `description` yếu, hoặc bị `paths` giới hạn, hoặc bị
  `skillOverrides` tắt, hoặc ngân sách description bị tràn.
- *"Hook không chạy"* → matcher sai (khớp theo **tên tool**, không phải theo nội dung lệnh), hoặc
  file không có quyền thực thi, hoặc đường dẫn không dùng `${CLAUDE_PROJECT_DIR}`.
- *"Setting không có tác dụng"* → tầng cao hơn đè, hoặc key đó **không được phép** đặt từ file repo,
  hoặc file JSON hỏng (Claude Code bỏ qua cả file).

---

[⬅️ AI-04](interview.AI.04-MCP.md) | [⬅️ Mục lục AI](interview.AI.md) | Tiếp theo: [AI-06 — Multi-agent & Agent Team ➡️](interview.AI.06-MultiAgent-Teams.md)
