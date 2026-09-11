---
name: fixbug
description: Quy trình sửa lỗi có kỷ luật — tái hiện, dựng nhiều giả thuyết cạnh tranh, tìm bằng chứng bác bỏ, viết test đỏ trước, sửa tối thiểu, kiểm chứng. Dùng khi người dùng báo lỗi, exception, hành vi sai, test đỏ, hoặc nói "fix", "sửa lỗi", "bug", "vì sao lỗi", "không chạy được".
argument-hint: "[mô tả triệu chứng]"
---

# Quy trình sửa lỗi

> **Sửa mà không có test đỏ-trước là sửa không chứng minh được.**
> Bạn không biết mình đã sửa bug, hay chỉ làm nó ẩn đi.

## Sáu bước — không bỏ bước nào

### 1. Tái hiện
Chạy được lỗi trước đã. Không tái hiện được → **nói rõ**, và mọi kết luận sau đó phải được dán
nhãn là suy đoán. Đừng sửa mò một lỗi bạn chưa từng thấy.

### 2. Đọc thông điệp lỗi **gốc**, đầy đủ
Cả stack trace, không phải dòng đầu. Dòng đầu nói *chỗ nổ*; stack nói *đường đi tới đó*.

### 3. Dựng **ít nhất 3 giả thuyết cạnh tranh** trước khi đào sâu cái nào
Một giả thuyết = không có gì để so sánh = bạn sẽ tìm thấy đúng thứ mình đã tin (anchoring, MA-9).

```
| # | Giả thuyết | Nếu đúng thì cái gì KHÁC phải đúng theo? | Kiểm tra thế nào |
```

Cột 3 là cột làm việc. Với mỗi giả thuyết, đi tìm **bằng chứng bác bỏ**, không đi tìm bằng chứng
ủng hộ.

Bug khó, nhiều tầng, hoặc cần quét rộng → uỷ thác agent `bug-hunter` (chỉ đọc, buộc phải chứng
minh trước khi ai đó động vào code).

### 4. Viết test **đỏ** tái hiện bug — trước khi sửa
Test này phải **thất bại** bây giờ vì đúng lý do bug gây ra. Nếu nó xanh ngay, bạn chưa hiểu bug.

### 5. Sửa **tối thiểu**
- Sửa **nguyên nhân**, không sửa triệu chứng. Thêm `try/catch` quanh chỗ nổ là giấu bug.
- **Không** refactor kèm theo. Không dọn code xung quanh. Một lần sửa = một thay đổi hiểu được.
- Thấy vấn đề khác → ghi lại, không sửa.

### 6. Kiểm chứng
```powershell
powershell -NoProfile -File .claude/functions/Verify.ps1 -Level full
```
Test mới **xanh** *và* test cũ **không đỏ thêm**. Cả hai, không chỉ cái đầu.

## Khi kẹt

Cùng một cách sửa thất bại **3 lần** ⇒ bạn đang thrashing, không phải đang cố gắng (AG-8).
Hook `loop-progress.ps1` sẽ chặn bạn lại. Khi đó, chọn **một**:

1. **Đổi giả thuyết** — quay lại bước 3, giả thuyết nào chưa bị bác bỏ?
2. **Thu hẹp** — tái hiện bằng test nhỏ nhất có thể; loại bỏ biến số.
3. **Dừng và báo** — đã thử gì, thất bại thế nào, cần quyết định gì.

## Báo cáo

```
## Nguyên nhân gốc
`file.cs:line` — <cơ chế, giải thích được vì sao triệu chứng đúng như đã thấy>

## Đã sửa
<thay đổi tối thiểu>

## Bằng chứng
- Test `<tên>`: ĐỎ trước khi sửa → XANH sau khi sửa
- `Verify.ps1 -Level full` → PASS

## Chỗ khác có cùng lỗi này (không sửa)
- …
```

Mục cuối quan trọng: một bug hiếm khi chỉ có một chỗ.
