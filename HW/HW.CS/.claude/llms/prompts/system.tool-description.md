---
id: system.tool-description
version: 1
changedAt: 2026-09-09
---

# Viết mô tả tool — phần prompt engineering bị bỏ quên nhiều nhất

Mô tả tool **là prompt**, và nó là prompt được đọc ở đúng thời điểm model quyết định hành động.
Một tool bị gọi sai thường không phải lỗi model — là lỗi mô tả.

## Khuôn mẫu

```jsonc
{
  "name": "search_orders",
  "description": "Tìm đơn hàng theo tên khách, email, hoặc khoảng thời gian. Trả về tối đa 20 đơn, mới nhất trước.\n\nDÙNG KHI: chưa biết mã đơn.\nKHÔNG DÙNG KHI: đã có mã đơn — dùng `get_order`, nhanh hơn và đầy đủ hơn.\n\nTrả về: mảng { orderId, customerName, total, status, createdAt }.\nKhông tìm thấy: mảng rỗng (KHÔNG phải lỗi).",
  "input_schema": {
    "type": "object",
    "properties": {
      "query":    { "type": "string", "description": "Tên khách hoặc email. Không nhận mã đơn." },
      "fromDate": { "type": "string", "format": "date", "description": "Bao gồm ngày này. Bỏ trống = không giới hạn." }
    },
    "required": ["query"],
    "additionalProperties": false
  },
  "strict": true
}
```

## Bốn phần bắt buộc

| Phần | Vì sao |
|---|---|
| **Làm gì** | một câu, cụ thể |
| **DÙNG KHI / KHÔNG DÙNG KHI** | phân biệt với tool anh em — đây là phần chống gọi nhầm hiệu quả nhất |
| **Trả về gì** | model lập kế hoạch bước sau dựa vào đây |
| **Ca rỗng / ca lỗi** | "không tìm thấy" phải nói rõ là **không phải lỗi**, nếu không model sẽ retry vô ích |

## Chẩn đoán

| Triệu chứng | Nguyên nhân | Sửa |
|---|---|---|
| Tool không bao giờ được gọi | mô tả không nói rõ **khi nào** dùng | thêm "DÙNG KHI" |
| Gọi nhầm tool anh em | hai mô tả chồng lấn | thêm "KHÔNG DÙNG KHI: … — dùng `X`" vào cả hai |
| Tham số sai kiểu | schema lỏng | `strict: true` + `additionalProperties: false` |
| Retry vô hạn khi rỗng | model tưởng rỗng là lỗi | nói rõ ca rỗng trong mô tả |
| Bịa dữ liệu thay vì gọi tool | quá nhiều tool, hoặc mô tả mờ | giảm/gộp tool; cân nhắc tool search + `defer_loading` |

**Tool error rate theo từng tool** là chỉ số chỉ thẳng vào tool nào có mô tả tệ — đo nó, đừng đoán.
