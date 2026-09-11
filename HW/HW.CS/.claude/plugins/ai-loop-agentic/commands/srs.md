---
description: Viết SRS — đặc tả yêu cầu nghiệp vụ với acceptance criteria kiểm chứng được
argument-hint: "[tên feature]"
---

## Đặc tả đã có

!`dir /b docs\specs 2>nul || echo (chua co thu muc docs/specs)`

## Feature

$ARGUMENTS

## Nhiệm vụ

Theo skill `spec-srs`, dùng mẫu @.claude/workflows/templates/SRS.md

**Đọc code trước khi viết.** Grep entity/endpoint liên quan — đặc tả mâu thuẫn với code hiện có
là đặc tả vô dụng. Phải quét rộng → uỷ thác agent `explorer` hoặc `analyst`.

**Kiểm tra bắt buộc trước khi ghi file:** mỗi yêu cầu có gắn được với một acceptance criterion
**kiểm chứng được** không? Nếu không, viết lại cho tới khi có. Yêu cầu không kiểm chứng được
sẽ đẻ ra một loop không biết khi nào dừng.

Mọi chỗ tự suy ra thay vì được nói → `[GIẢ ĐỊNH]`, gom lên mục **Câu hỏi còn mở** ở đầu tài liệu.

Ghi ra `docs/specs/SRS-<slug>.md`. Trả về: đường dẫn + 3–5 quyết định chính + câu hỏi còn mở.
**Không** lặp lại nội dung tài liệu.
