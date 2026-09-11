---
name: spec-srd
description: Viết SRD (Software Requirements/Design Document) — đặc tả THIẾT KẾ: kiến trúc, data model, API contract, luồng xử lý, đánh đổi kỹ thuật, kế hoạch migration. Dùng khi người dùng nói "viết SRD", "tài liệu thiết kế", "design doc", "thiết kế API", "data model", hoặc sau khi SRS đã chốt.
argument-hint: "[tên feature]"
---

# Viết SRD

SRD trả lời **"làm thế nào"**. Nó bắt đầu từ SRS đã chốt và kết thúc ở chỗ lập trình viên (hoặc
agent `implementer`) có thể bắt tay viết code mà không phải đoán.

## Điều kiện tiên quyết

Phải có SRS hoặc yêu cầu rõ ràng. **Không viết SRD trên yêu cầu mơ hồ** — bạn sẽ thiết kế cho
một bài toán không tồn tại. Nếu chưa có, gọi skill `spec-srs` trước.

## Bắt buộc có

| Mục | Vì sao |
|---|---|
| **Bảng quyết định + đánh đổi** | Quyết định không ghi lý do sẽ bị lật lại sau 3 tháng bởi chính người viết |
| **Data model** (bảng, cột, index, ràng buộc) | Chỗ sai đắt nhất và khó sửa nhất về sau |
| **API contract** (path, request, response, mã lỗi) | Hợp đồng với bên gọi; cũng là input để sinh test |
| **Luồng xử lý** cho ca chính + ca lỗi | Ca lỗi hay bị bỏ, và đó là chỗ production hỏng |
| **Verifier cho từng phần** | Không có nó, SRD không dùng làm đầu vào cho loop được |
| **Kế hoạch migration & rollback** | Thay đổi schema không có đường lùi là rủi ro không được phép |

## Bảng đánh đổi — định dạng bắt buộc

```
| Quyết định | Đã chọn | Phương án loại bỏ | Vì sao | Đánh đổi chấp nhận |
|---|---|---|---|---|
```

Cột **"Đánh đổi chấp nhận"** là cột quan trọng nhất và hay bị bỏ trống. Một quyết định không có
đánh đổi nghĩa là bạn chưa hiểu nó.

## Quy trình

1. Đọc SRS + code hiện có. Bám **quy ước sẵn có của repo**, không mang kiến trúc từ nơi khác vào.
2. Điền theo `.claude/workflows/templates/SRD.md`.
3. Với mỗi bước triển khai, viết **lệnh verifier chạy được**. "Kiểm tra lại" không phải verifier;
   `dotnet test --filter BlogTests` mới là.
4. Ghi ra `docs/specs/SRD-<slug>.md`.

## Ranh giới

- **Không thiết kế cho vấn đề chưa có.** Thêm abstraction "cho sau này" là over-engineering (MA-3).
- **Không chọn tầng phức tạp hơn mức cần.** Nếu một hàm giải quyết được, đừng vẽ pipeline.
