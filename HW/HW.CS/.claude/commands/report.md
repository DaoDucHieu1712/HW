---
description: Tổng hợp công việc đã làm thành báo cáo dựa trên bằng chứng
argument-hint: "[phạm vi báo cáo]"
---

## Bằng chứng thu thập tự động

Thay đổi thật trên đĩa:
!`git diff --stat`
!`git log --oneline -10`

Trạng thái loop:
!`powershell -NoProfile -ExecutionPolicy Bypass -File .claude/functions/Task.ps1 -Action status`

Verifier:
!`powershell -NoProfile -ExecutionPolicy Bypass -File .claude/functions/Verify.ps1 -Level build`

## Phạm vi

$ARGUMENTS

## Nhiệm vụ

Theo skill `report`.

**Viết dựa trên bằng chứng ở trên, không dựa trên lời tường thuật** — kể cả lời tường thuật của
chính bạn ở các lượt trước.

Bốn quy tắc trung thực:
1. Test đỏ thì **nói test đỏ**, kèm output. Không viết "về cơ bản đã hoàn thành".
2. Bước bị bỏ qua thì nói là bị bỏ qua, và vì sao.
3. Không nói "đã kiểm tra kỹ" trừ khi dẫn ra được lệnh verifier cụ thể.
4. Việc đã xong và đã kiểm chứng thì **nói thẳng, không rào đón**.

Trạng thái: **HOÀN THÀNH** / **HOÀN THÀNH MỘT PHẦN** / **BỊ CHẶN**. Trạng thái ngầm thứ tư —
*"đã làm nhưng không kiểm chứng được"* — phải nói ra như vậy, không được nhập vào HOÀN THÀNH.

Ghi ra `docs/reports/<YYYY-MM-DD>-<slug>.md`, trả về đường dẫn + trạng thái + 3 dòng tóm tắt.
