---
description: Chạy quy trình sửa lỗi có kỷ luật — tái hiện, nhiều giả thuyết, test đỏ trước, sửa tối thiểu
argument-hint: "[mô tả triệu chứng hoặc thông điệp lỗi]"
---

## Trạng thái build hiện tại

!`powershell -NoProfile -ExecutionPolicy Bypass -File .claude/functions/Verify.ps1 -Level build`

## Thay đổi gần đây (nghi phạm đầu tiên)

!`git log --oneline -8`
!`git diff --stat HEAD~1 2>nul`

## Triệu chứng

$ARGUMENTS

## Nhiệm vụ

Theo skill `fixbug`. **Không bỏ bước.**

1. **Tái hiện** trước. Không tái hiện được → nói rõ; mọi kết luận sau đó là suy đoán và phải
   được dán nhãn như vậy.
2. **Đọc thông điệp lỗi gốc, đầy đủ** — cả stack trace, không chỉ dòng đầu.
3. **Dựng ít nhất 3 giả thuyết cạnh tranh** trước khi đào sâu cái nào. Với mỗi giả thuyết, đi
   tìm **bằng chứng bác bỏ**, không tìm bằng chứng ủng hộ.
   Bug khó / cần quét rộng → uỷ thác agent `bug-hunter` (chỉ đọc, phải chứng minh trước).
4. **Viết test đỏ tái hiện bug — trước khi sửa.** Test phải thất bại bây giờ, đúng vì lý do bug
   gây ra. Nếu nó xanh ngay, bạn chưa hiểu bug.
5. **Sửa tối thiểu** — sửa nguyên nhân, không sửa triệu chứng. Không refactor kèm theo.
6. **Kiểm chứng**: `Verify.ps1 -Level full`. Test mới xanh **và** test cũ không đỏ thêm.

## Dừng lại nếu

Cùng một cách sửa thất bại **3 lần** — đó là thrashing, không phải cố gắng. Chọn một:
đổi giả thuyết · thu hẹp phạm vi tái hiện · dừng và báo (đã thử gì, hỏng thế nào, cần gì).

## Báo cáo cuối

Nguyên nhân gốc ở mức `file:line` · thay đổi tối thiểu · bằng chứng (test đỏ→xanh) ·
**chỗ khác trong repo có cùng lỗi này** (ghi ra, không sửa).
