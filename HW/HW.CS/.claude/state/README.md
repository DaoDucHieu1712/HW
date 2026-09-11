# `state/` — trạng thái loop, nằm **ngoài** context

## Vì sao trạng thái phải nằm trên đĩa

1. **Context bị nén.** Sau compaction, chi tiết biến mất — file thì không.
2. **Subagent khởi động lạnh.** Nó cần một nơi để đọc "đang ở đâu" mà không phải hỏi lại.
3. **Điều kiện dừng phải kiểm chứng được từ bên ngoài.** Một con số trong context không phải là
   ràng buộc; một con số trong file mà hook đọc được thì là.

## File ở đây

| File | Ai ghi | Nội dung |
|---|---|---|
| `CURRENT_TASK.md` | **bạn** (chép từ `workflows/templates/TASK.md`) | task đang mở — hook `SessionStart` tự nạp vào context |
| `loop-run.json` | `functions/Task.ps1` | bước hiện tại, số lượt, ngân sách |
| `loop-progress.json` | `hooks/loop-progress.ps1` | băm (tool+tham số) → số lần lỗi, để phát hiện thrashing |
| `audit.jsonl` | `hooks/audit-log.ps1` | mọi lời gọi tool + mọi thứ bị chặn |
| `subagents.jsonl` | `hooks/subagent-stop-log.ps1` | mỗi lần uỷ thác — số liệu kinh tế multi-agent |
| `compaction.jsonl` | `hooks/pre-compact-snapshot.ps1` | ảnh chụp trước mỗi lần nén context |
| `runs/*.json` | `functions/Task.ps1 -Action close` | lịch sử các lần chạy đã đóng |

## Có nên commit không?

**Không.** Xem `.gitignore` bên cạnh — `CURRENT_TASK.md` là của **bạn**, log là của **máy bạn**.
Thứ đáng commit là `workflows/templates/TASK.md` (cái mẫu), không phải bản đã điền.

## Đọc số liệu

```powershell
powershell -NoProfile -File .claude/functions/Trace.ps1
```

Hoặc `/loop-status` trong phiên.
