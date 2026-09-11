# SRS — <Tên feature>

| | |
|---|---|
| **Mã** | SRS-\<slug\> |
| **Phiên bản** | 0.1 |
| **Ngày** | YYYY-MM-DD |
| **Trạng thái** | Nháp / Đang review / Đã chốt |
| **Liên quan** | SRD-\<slug\>, issue #… |

---

## 0. Câu hỏi còn mở

> ⚠️ Gom **mọi** `[GIẢ ĐỊNH]` trong tài liệu lên đây. Người đọc trả lời một lượt thay vì phải
> dò cả tài liệu. Mục này rỗng nghĩa là yêu cầu đã rõ hoàn toàn — hiếm khi đúng ở bản nháp đầu.

| # | Câu hỏi | Giả định đang dùng | Ảnh hưởng nếu sai |
|---|---|---|---|
| 1 | | | |

---

## 1. Bối cảnh & vấn đề

**Vấn đề hiện tại:** <ai đang khổ vì cái gì — mô tả bằng hiện tượng quan sát được, không bằng
giải pháp mong muốn>

**Vì sao làm bây giờ:** <điều gì thay đổi khiến việc này đáng làm lúc này>

**Hiện trạng trong code:** <file/module liên quan đã có gì — `file:line`>

---

## 2. Phạm vi

### 2.1 Trong phạm vi
- …

### 2.2 **Ngoài phạm vi** (bắt buộc điền)
- …

> Mục 2.2 ngăn agent implement over-eager hiệu quả hơn mọi câu nhắc trong prompt. Viết cụ thể:
> *"Không làm phân trang trong phiên bản này"* — không phải *"phạm vi hạn chế"*.

---

## 3. Actor & quyền

| Actor | Mô tả | Được làm gì | **Không** được làm gì |
|---|---|---|---|

---

## 4. User story & acceptance criteria

### US-01 — <tiêu đề>

> Là **\<actor\>**, tôi muốn **\<việc\>**, để **\<giá trị\>**.

| # | Acceptance criteria | Kiểm chứng bằng |
|---|---|---|
| AC-01.1 | Given \<trạng thái\>, When \<hành động\>, Then \<kết quả quan sát được\> | `dotnet test --filter …` |
| AC-01.2 | | |

> **Quy tắc:** cột "Kiểm chứng bằng" **không được để trống**. Yêu cầu không kiểm chứng được là
> nguyện vọng, và nó sẽ đẻ ra một loop không biết khi nào dừng.

### US-02 — …

---

## 5. Business rule

| # | Quy tắc | Nguồn | Xử lý khi vi phạm |
|---|---|---|---|
| BR-01 | | | |

---

## 6. Ca lỗi & ca biên

> Phần production hỏng nằm ở đây, không nằm ở luồng chính.

| # | Tình huống | Hành vi mong đợi | Thông điệp cho người dùng |
|---|---|---|---|
| EX-01 | Dữ liệu không tồn tại | | |
| EX-02 | Người dùng không đủ quyền | | |
| EX-03 | Hệ thống ngoài không phản hồi | | |
| EX-04 | Gửi trùng / bấm hai lần | | |

---

## 7. Yêu cầu phi chức năng (**phải có số**)

| Loại | Yêu cầu | Đo bằng |
|---|---|---|
| Hiệu năng | p95 < … ms với … bản ghi | |
| Đồng thời | … request/s | |
| Bảo mật | | |
| Tương thích | | |
| Nhật ký/kiểm toán | | |

---

## 8. Phụ thuộc & rủi ro

| Phụ thuộc | Loại | Rủi ro nếu không có |
|---|---|---|

---

## 9. Truy vết

| US / AC | SRD mục | Code | Test |
|---|---|---|---|
