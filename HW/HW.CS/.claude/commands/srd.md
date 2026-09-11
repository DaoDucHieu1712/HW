---
description: Viết SRD — tài liệu thiết kế: kiến trúc, data model, API contract, đánh đổi, migration
argument-hint: "[tên feature]"
---

## Đặc tả đã có

!`dir /b docs\specs 2>nul || echo (chua co thu muc docs/specs)`

## Feature

$ARGUMENTS

## Nhiệm vụ

Theo skill `spec-srd`, dùng mẫu @.claude/workflows/templates/SRD.md

**Điều kiện tiên quyết:** phải có SRS hoặc yêu cầu rõ ràng. Chưa có → chạy `/srs` trước.
Viết SRD trên yêu cầu mơ hồ là thiết kế cho một bài toán không tồn tại.

**Ba mục không được để trống:**

1. **Bảng đánh đổi** — đặc biệt cột *"Đánh đổi chấp nhận"*. Một quyết định không có đánh đổi
   nghĩa là bạn chưa hiểu nó.
2. **Verifier cho từng bước triển khai** — lệnh chạy được. "Kiểm tra lại" không phải verifier.
3. **Kế hoạch migration & rollback** — thay đổi schema không có đường lùi là rủi ro không được phép.

Bám **quy ước sẵn có của repo**; đừng mang kiến trúc từ nơi khác vào. Không thêm abstraction
"cho sau này".

Ghi ra `docs/specs/SRD-<slug>.md`.
