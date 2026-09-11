---
description: Chạy pipeline phát triển đầy đủ cho một task — phân tích → kế hoạch → code → kiểm chứng → báo cáo
argument-hint: "[mô tả task]"
---

## Trạng thái hiện tại

Nhánh & thay đổi:
!`git status --short --branch`

Loop đang chạy (nếu có):
!`powershell -NoProfile -ExecutionPolicy Bypass -File .claude/functions/Task.ps1 -Action status`

## Task

$ARGUMENTS

## Nhiệm vụ

Chạy pipeline `dev-loop` (định nghĩa: @.claude/workflows/dev-loop.json), theo khung trong
skill `loop-agentic`.

**Trước khi bắt đầu, hãy quyết định tầng — và nói ra quyết định đó:**

- Task này **workflow làm được** (các bước biết trước) hay **cần agent** (không đặc tả trước
  được)? Dùng agent cho việc workflow làm được là tự nguyện trả thêm tiền và phi tất định.
- Nếu task nhỏ và rõ (sửa một hàm, thêm một field): **làm thẳng**, đừng chạy pipeline 5 bước.
  Nói rõ bạn đang rút gọn và vì sao.

**Nếu chạy pipeline đầy đủ:**

1. **Khởi tạo loop**
   `powershell -NoProfile -File .claude/functions/Task.ps1 -Action init -Workflow dev-loop -Title "<tiêu đề>"`

2. **GATHER** — hiểu bài toán. Phải đọc rộng → uỷ thác agent `explorer` (rác ở lại bên kia).
   Yêu cầu mơ hồ → skill `spec-srs` trước khi viết dòng code nào.

3. **PLAN** — uỷ thác agent `architect`. Kế hoạch phải có: các bước, file sẽ đụng,
   **verifier cho từng bước**, và mục **ngoài phạm vi**.

4. **ACT** — uỷ thác agent `implementer`, hoặc tự làm nếu phạm vi nhỏ.
   Chạy verifier sau **mỗi đơn vị nhỏ**, không phải một lần ở cuối.

5. **VERIFY** — uỷ thác agent `verifier`.
   `Verify.ps1 -Level full` xanh mới được coi là xong. "KHÔNG KIỂM CHỨNG ĐƯỢC" **không phải** PASS.

6. **REPORT** — skill `report` hoặc agent `reporter`, rồi
   `Task.ps1 -Action close -Outcome done|blocked`

## Ranh giới

- **Chỉ sửa file trong phạm vi task.** Phát hiện ngoài phạm vi → ghi vào báo cáo, **không sửa**.
- Không commit, không push trừ khi được yêu cầu rõ ràng.
- Cùng một lỗi 3 lần → **dừng**, đổi giả thuyết hoặc escalate. Đừng thử lại lần thứ tư.
