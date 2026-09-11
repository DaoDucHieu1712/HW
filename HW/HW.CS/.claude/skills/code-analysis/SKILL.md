---
name: code-analysis
description: Phân tích codebase có phương pháp — kiến trúc, luồng dữ liệu, điểm nóng, nợ kỹ thuật, tác động của một thay đổi. Dùng khi người dùng nói "phân tích", "review kiến trúc", "codebase này thế nào", "impact analysis", "chỗ nào ảnh hưởng", "tìm hiểu module", hoặc trước khi sửa một phần code chưa quen.
argument-hint: "[phạm vi hoặc câu hỏi]"
---

# Phân tích codebase

## Quy tắc số một: bắt đầu từ **câu hỏi**, không từ thư mục

"Phân tích codebase" là yêu cầu vô hạn. Trước khi đọc bất cứ file nào, chốt câu hỏi cụ thể:

| Loại phân tích | Câu hỏi | Dừng khi |
|---|---|---|
| **Định vị** | "Logic X nằm ở đâu?" | tìm được `file:line` |
| **Tác động** | "Sửa Y thì cái gì hỏng theo?" | liệt kê hết caller + test bao phủ |
| **Kiến trúc** | "Dữ liệu đi từ đâu tới đâu?" | vẽ được luồng đầu-cuối |
| **Chất lượng** | "Rủi ro lớn nhất ở đâu?" | 3–5 phát hiện có bằng chứng |
| **Onboarding** | "Muốn sửa được module này cần biết gì?" | liệt kê được quy ước + cạm bẫy |

Không chốt được câu hỏi thì hỏi người dùng — đừng đọc bừa 200 file rồi tóm tắt chung chung.

## Chiến lược đọc: hình dạng trước, chi tiết sau

```
1. Glob     → hình dạng thư mục, quy ước đặt tên       (rẻ)
2. Grep     → thu hẹp về các file thật sự liên quan     (rẻ)
3. Read     → chỉ đọc ĐOẠN cần, không đọc cả file       (đắt)
4. git log  → file nào hay đổi = file nóng, nhiều rủi ro
```

**Uỷ thác cho agent `explorer` khi phải quét rộng.** Đó chính là lý do nó tồn tại: 200 file bạn
quét sẽ ở lại trong context và đầu độc mọi lượt sau (MA-1). Trả về kết luận, không trả về file.

## Điểm nóng — tín hiệu rẻ mà ít người dùng

```powershell
# File bị sửa nhiều nhất 6 tháng qua = nơi rủi ro tập trung
git log --since="6 months ago" --name-only --pretty=format: |
  Where-Object { $_ } | Group-Object | Sort-Object Count -Descending |
  Select-Object -First 20
```

File **hay đổi** × **nhiều dòng** × **ít test** = chỗ đáng lo nhất. Ba tín hiệu này rẻ hơn mọi
công cụ phân tích tĩnh và thường chỉ đúng chỗ.

## Quy tắc báo cáo

1. **Mọi khẳng định phải có `file:line`.** "Kiến trúc hơi rối" không phải phát hiện.
2. **Phân biệt quan sát và ý kiến.** "Có 3 nơi cùng parse ngày tháng" là quan sát. "Nên gom lại"
   là ý kiến — ghi riêng.
3. **Nói cả những gì đã kiểm tra mà thấy ổn** — để người đọc biết phạm vi bao phủ.
4. **Không phát minh vấn đề.** Codebase khoẻ mạnh là kết quả hợp lệ.

## Định dạng

```
## Câu hỏi đã trả lời
<một câu>

## Kết luận
<3–5 gạch đầu dòng>

## Bằng chứng
- `path:line` — <vì sao liên quan>

## Điểm nóng
| File | Lần sửa/6th | Dòng | Test bao phủ |

## Rủi ro (có bằng chứng)
| Mức | Chỗ | Kịch bản hỏng |

## Đã kiểm tra, không có vấn đề
- …
```
