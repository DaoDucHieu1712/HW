---
description: Chẩn đoán harness — settings/hook/skill/MCP nào đang nạp và cái gì đang hỏng
---

## File cấu hình

!`dir /b .claude 2>nul`
!`dir /b .claude\hooks 2>nul`
!`dir /b .claude\skills 2>nul`
!`dir /b .claude\agents 2>nul`

## settings.json có hợp lệ không

!`powershell -NoProfile -Command "try { Get-Content .claude\settings.json -Raw | ConvertFrom-Json | Out-Null; 'settings.json: JSON HOP LE' } catch { 'settings.json: JSON HONG -> Claude Code se BO QUA CA FILE. ' + $_.Exception.Message }"`

## Hook gần đây có chạy không

!`powershell -NoProfile -Command "if (Test-Path .claude\state\audit.jsonl) { Get-Content .claude\state\audit.jsonl -Tail 10 } else { '(chua co audit.jsonl - hook PostToolUse chua tung chay)' }"`

## Nhiệm vụ

Chẩn đoán theo **đúng thứ tự này** (HR-17) — đừng đoán:

```
1. /status          → file settings nào đã nạp? managed source nào đang áp?
2. /context         → cái gì đang chiếm context: CLAUDE.md, skill, MCP tool, lịch sử
3. /skills          → skill có được thấy không? bị "name-only" hay "off" không?
4. /mcp             → server kết nối chưa? cần đăng nhập không?
5. /plugin          → tab Errors: plugin/LSP/MCP nào nạp hỏng
6. claude --debug   → log hook: hook nào khớp, exit code, output
7. /reload-plugins  → sau khi sửa plugin/skill/hook mà không muốn restart
```

**Ba triệu chứng → nguyên nhân:**

| Triệu chứng | Nguyên nhân thường gặp |
|---|---|
| *"Skill của tôi không bao giờ được dùng"* | `description` yếu · bị `paths` giới hạn · bị `skillOverrides` tắt · ngân sách description tràn |
| *"Hook không chạy"* | matcher sai (khớp theo **tên tool**, không phải nội dung lệnh) · file không có quyền thực thi · đường dẫn không dùng `${CLAUDE_PROJECT_DIR}` |
| *"Setting không có tác dụng"* | tầng cao hơn đè · key đó **không được phép** đặt từ file repo (vd `defaultMode: bypassPermissions`) · **file JSON hỏng — Claude Code bỏ qua cả file** |

Nhắc lại thứ tự ưu tiên settings, cao nhất trước:
`managed` → `--settings` (CLI) → `.claude/settings.local.json` → `.claude/settings.json` → `~/.claude/settings.json`

⚠️ **List thì MERGE, không override** — `permissions.allow` ở nhiều file được **gộp lại**.
Và về sức mạnh rule: **`deny` > `ask` > `allow`**, bất kể đến từ tầng file nào. Đó là lý do
"đã bấm đừng hỏi lại" mà vẫn bị hỏi: project có một rule `ask` cho cùng thao tác.
