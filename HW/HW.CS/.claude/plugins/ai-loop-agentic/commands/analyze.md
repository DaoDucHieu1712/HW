---
description: Phân tích codebase theo một câu hỏi cụ thể — định vị, tác động, kiến trúc, điểm nóng
argument-hint: "[câu hỏi hoặc phạm vi]"
---

## Hình dạng repo

!`git ls-files | head -60`

## Điểm nóng (file sửa nhiều nhất 6 tháng qua)

!`git log --since="6 months ago" --name-only --pretty=format: | sort | uniq -c | sort -rn | head -20`

## Câu hỏi

$ARGUMENTS

## Nhiệm vụ

Theo skill `code-analysis`.

**Bước 0 — chốt câu hỏi.** "Phân tích codebase" là yêu cầu vô hạn. Xác định đây là loại nào và
nói ra điều kiện dừng:

| Loại | Dừng khi |
|---|---|
| Định vị | tìm được `file:line` |
| Tác động | liệt kê hết caller + test bao phủ |
| Kiến trúc | vẽ được luồng dữ liệu đầu-cuối |
| Chất lượng | 3–5 phát hiện **có bằng chứng** |
| Onboarding | liệt kê được quy ước + cạm bẫy |

Không chốt được câu hỏi → **hỏi người dùng**, đừng đọc bừa rồi tóm tắt chung chung.

**Phải quét rộng → uỷ thác agent `explorer`.** Đó là lý do nó tồn tại: file bạn quét sẽ ở lại
trong context và đầu độc mọi lượt sau. Nhận kết luận, không nhận file.

## Quy tắc

- Mọi khẳng định có `file:line`. "Kiến trúc hơi rối" không phải phát hiện.
- Tách **quan sát** khỏi **ý kiến**. "Có 3 nơi cùng parse ngày" là quan sát; "nên gom lại" là ý kiến.
- Nói cả những gì đã kiểm tra mà **thấy ổn** — để người đọc biết phạm vi bao phủ.
- **Không phát minh vấn đề.** Codebase khoẻ mạnh là kết quả hợp lệ.
