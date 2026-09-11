# SRD — <Tên feature>

| | |
|---|---|
| **Mã** | SRD-\<slug\> |
| **SRS nguồn** | SRS-\<slug\> |
| **Phiên bản** | 0.1 |
| **Ngày** | YYYY-MM-DD |
| **Trạng thái** | Nháp / Đang review / Đã chốt |

---

## 1. Tóm tắt thiết kế

<3–5 câu: cách tiếp cận là gì, và vì sao là nó chứ không phải cái khác>

---

## 2. Quyết định & đánh đổi

| # | Quyết định | Đã chọn | Phương án loại bỏ | Vì sao | **Đánh đổi chấp nhận** |
|---|---|---|---|---|---|
| D-01 | | | | | |

> Cột cuối là cột quan trọng nhất và hay bị bỏ trống. **Một quyết định không có đánh đổi nghĩa
> là bạn chưa hiểu nó.**

---

## 3. Kiến trúc

### 3.1 Vị trí trong hệ thống
```
<sơ đồ khối: request đi vào đâu, qua tầng nào, chạm gì>
```

### 3.2 Thành phần mới / sửa

| Thành phần | Tầng | Mới/Sửa | Trách nhiệm |
|---|---|---|---|

---

## 4. Data model

### 4.1 Bảng / entity

| Cột | Kiểu | Null? | Mặc định | Ghi chú |
|---|---|---|---|---|

### 4.2 Index & ràng buộc

| Tên | Loại | Cột | Vì sao cần |
|---|---|---|---|

> Index không nêu được truy vấn nó phục vụ là index thừa. Ràng buộc không nêu được business rule
> nó bảo vệ cũng vậy.

---

## 5. API contract

### `<METHOD> /path`

**Request**
```json
{ }
```

**Response 200**
```json
{ }
```

**Mã lỗi**

| HTTP | code | Khi nào | Body |
|---|---|---|---|
| 400 | | | |
| 404 | | | |
| 409 | | | |

---

## 6. Luồng xử lý

### 6.1 Ca chính
```
1. …
2. …
```

### 6.2 Ca lỗi (bắt buộc — đây là chỗ production hỏng)

| Ca | Xảy ra ở bước | Xử lý | Trạng thái để lại |
|---|---|---|---|

### 6.3 Idempotency & side effect

> Agent và người dùng đều có thể retry. Nếu thao tác này không idempotent thì một lần retry =
> một lần thừa (AG-17).

| Thao tác | Idempotent? | Cơ chế | Khoá theo gì |
|---|---|---|---|

---

## 7. Kế hoạch triển khai

| # | Bước | File | **Verifier (lệnh chạy được)** |
|---|---|---|---|
| 1 | | | `dotnet test --filter …` |

> "Kiểm tra lại xem có đúng không" **không phải** verifier.

---

## 8. Migration & rollback

| | |
|---|---|
| **Thay đổi schema** | |
| **Tương thích ngược** | có / không — nếu không, vì sao chấp nhận được |
| **Thứ tự triển khai** | |
| **Rollback** | <các bước cụ thể quay lại trạng thái trước> |

> Thay đổi schema không có đường lùi là rủi ro không được phép.

---

## 9. Quan sát

| Cần biết gì ở production | Log/metric/trace | Ngưỡng cảnh báo |
|---|---|---|

---

## 10. Rủi ro còn lại

| Rủi ro | Khả năng | Ảnh hưởng | Giảm thiểu |
|---|---|---|---|
