---
name: spec-srs
description: Viết SRS (Software Requirements Specification) — đặc tả yêu cầu ở góc nhìn NGHIỆP VỤ: actor, user story, acceptance criteria, business rule, ngoài phạm vi. Dùng khi người dùng nói "viết SRS", "đặc tả yêu cầu", "phân tích nghiệp vụ", "user story", "requirement", hoặc khi bắt đầu một feature mà yêu cầu chưa rõ ràng.
argument-hint: "[tên feature]"
---

# Viết SRS

SRS trả lời **"hệ thống phải làm gì và làm cho ai"**. Nó **không** nói *làm thế nào* — đó là
việc của SRD (skill `spec-srd`).

## Quy tắc quyết định chất lượng

> **Mỗi yêu cầu phải kèm cách chứng minh nó đã đạt.**

Một dòng SRS không gắn được với acceptance criterion kiểm chứng được thì đó là **nguyện vọng**,
không phải yêu cầu. Đây cũng chính là điều kiện để agent implement nó có **verifier** (AG-5) —
SRS mơ hồ đẻ ra loop không biết khi nào dừng.

| Viết thế này | Thay bằng |
|---|---|
| "Hệ thống phải nhanh" | "p95 của `GET /blogs` < 300 ms với 10k bản ghi" |
| "Xử lý lỗi tốt" | "Khi `blogId` không tồn tại → 404 + body `{ code: 'BLOG_NOT_FOUND' }`" |
| "Giao diện thân thiện" | "Form hiển thị lỗi validation ngay dưới trường sai, không cần submit lại" |

## Quy trình

1. **Đọc code trước.** `Grep` entity/endpoint liên quan. Đặc tả mâu thuẫn với code hiện có là
   đặc tả vô dụng. Nếu phải quét nhiều, uỷ thác cho agent `explorer`.
2. Điền theo `.claude/workflows/templates/SRS.md`.
3. Mọi chỗ tự suy ra thay vì được nói → đánh dấu `[GIẢ ĐỊNH]` và gom lên mục **Câu hỏi còn mở**
   ở đầu tài liệu.
4. Ghi ra `docs/specs/SRS-<slug>.md`.

## Bắt buộc có

- **Ngoài phạm vi** — mục này ngăn agent implement over-eager (AG-8) tốt hơn mọi câu nhắc trong
  prompt. Viết cụ thể: *"Không làm phân trang trong phiên bản này"*, không phải *"phạm vi hạn chế"*.
- **Yêu cầu phi chức năng có số** — hiệu năng, đồng thời, bảo mật, tương thích. Không có số thì
  không kiểm chứng được.
- **Truy vết** — mỗi user story có id (`US-01`), mỗi acceptance criterion có id (`AC-01.1`) để
  test và báo cáo dẫn chiếu được.

## Trả về

Đường dẫn file + 3–5 gạch đầu dòng quyết định chính + danh sách câu hỏi còn mở.
**Không** lặp lại nội dung tài liệu.
