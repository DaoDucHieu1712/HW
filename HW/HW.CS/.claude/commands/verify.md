---
description: Chạy verifier đầy đủ (build + test) và báo cáo trạng thái khách quan
---

## Kết quả verifier

!`powershell -NoProfile -ExecutionPolicy Bypass -File .claude/functions/Verify.ps1 -Level full`

## Thay đổi đang có trên đĩa

!`git status --short`
!`git diff --stat`

## Nhiệm vụ

Đọc kết quả **ở trên** — nó đã chạy rồi, đừng chạy lại.

Báo cáo theo **ba trạng thái**, không phải hai:

| Kết quả | Nghĩa là |
|---|---|
| **PASS** | verifier đã chạy và xanh |
| **FAIL** | verifier đã chạy và đỏ |
| **KHÔNG KIỂM CHỨNG ĐƯỢC** | verifier không chạy được — trạng thái **chưa biết** |

Nhập nhèm trạng thái thứ ba thành PASS là cách phổ biến nhất để một loop tự lừa chính nó.

Nếu **FAIL**: trích thông điệp lỗi **nguyên văn** kèm `file:line`, rồi nêu giả thuyết nguyên nhân
— nhưng đừng sửa cho tới khi được yêu cầu.

Nếu **PASS**: nói thẳng, không rào đón. Và nêu **phần nào của thay đổi không có test bao phủ** —
xanh không đồng nghĩa với đã được kiểm chứng.
