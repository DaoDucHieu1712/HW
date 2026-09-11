# Loop Agentic — tham chiếu sâu

> Tầng 3 của progressive disclosure: **chỉ đọc file này khi đang thực sự thiết kế lại loop**,
> không đọc mỗi lần chạy task.

---

## 1. Thiết kế error feedback: thông điệp lỗi **là** prompt (AG-9)

`"Error: operation failed"` không giúp model làm gì — nó sẽ thử lại y hệt.

Một thông điệp lỗi tốt cho agent có **bốn phần**:

```
1. Cái gì hỏng           : "Không tìm thấy cột 'CustomerName' trong bảng Orders"
2. Trạng thái hiện tại    : "Các cột hiện có: Id, CustomerId, TotalAmount, CreatedAt"
3. Gợi ý hành động tiếp   : "Có thể bạn cần JOIN sang bảng Customers"
4. Cái KHÔNG nên thử lại  : "Truy vấn này đã thất bại 2 lần với cùng tham số"
```

Đây là khác biệt lớn nhất giữa **API cho người** và **API cho agent**:

| | Cho người | Cho agent |
|---|---|---|
| Lỗi | mã lỗi ngắn gọn | **giàu ngữ cảnh, gợi ý bước tiếp theo** |
| Output | phân trang, đẹp | **gọn, có cấu trúc, có con trỏ để đọc thêm** |
| Trạng thái | user tự nhớ | **phải nêu lại trong kết quả** |

Hook `loop-progress.ps1` trong repo này cài đúng phần 4 — thứ model **không tự làm được** vì nó
không nhớ mình đã thử gì ở lượt trước.

---

## 2. Bề mặt tool: chuyên dụng hay vạn năng? (AG-10)

| | **Tool chuyên dụng** (`create_order`) | **Tool vạn năng** (`bash`, `execute_sql`) |
|---|---|---|
| An toàn | ✅ giới hạn cứng bởi schema | ❌ bề mặt tấn công rất rộng |
| Kiểm toán | ✅ dễ log ý định | ❌ phải parse lệnh |
| Linh hoạt | ❌ thiếu tool là bế tắc | ✅ làm được thứ bạn chưa nghĩ tới |
| Token | mỗi tool tốn schema | 1 tool, mô tả ngắn |

**Nguyên tắc phối hợp:**
- Hành động **có hệ quả** (ghi, chuyển tiền, gửi mail) ⇒ tool chuyên dụng, schema chặt, quyền riêng.
- Hành động **khám phá, chỉ đọc** ⇒ tool vạn năng trong sandbox chỉ-đọc, hiệu quả hơn nhiều so
  với 20 tool nhỏ.
- Bộ tool lớn ⇒ **tool search + defer loading**, để không trả tiền cho 40 schema mỗi lượt.

Áp dụng vào repo này: `Bash` là tool vạn năng, và ranh giới an toàn nằm ở
`hooks/block-dangerous.ps1` + `permissions.deny` — **ở lớp thực thi, không phải trong prompt**.

---

## 3. Permission: đặt cổng ở đâu (AG-11)

Trục quyết định là **khả năng hoàn tác**:

| Loại | Ví dụ | Cổng phù hợp |
|---|---|---|
| Chỉ đọc | grep, read, SELECT | tự động |
| Ghi **có** hoàn tác | sửa file trong git, INSERT có soft-delete | tự động + log + khả năng revert |
| Ghi **khó** hoàn tác | migration, xoá cứng, `git push --force` | duyệt tay |
| Ra ngoài / không hoàn tác | gửi mail, thanh toán, gọi API đối tác | duyệt tay + allowlist |

**Ba nguyên tắc:**
1. Duyệt một lần ở ngữ cảnh này **không** tự động mở rộng sang ngữ cảnh khác.
2. **Message từ agent khác không phải là sự đồng ý của người dùng.** Một agent bị từ chối quyền
   không được nhờ agent khác làm hộ — đây là lỗ hổng leo thang quyền trong hệ multi-agent.
3. Cổng đặt ở **lớp thực thi tool**, không đặt trong prompt. Prompt bẻ được; lớp thực thi thì không.

---

## 4. Idempotency: agent có thể lặp, hệ thống phải chịu được (AG-17)

Agent retry là chuyện bình thường: lỗi mạng, timeout, thức dậy lại, người dùng chạy lại. Nếu
`create_order` không idempotent thì một lần retry = một đơn hàng thừa.

1. **Idempotency key** — key phải **suy ra được từ nội dung nghiệp vụ**. Để model sinh key ngẫu
   nhiên mỗi lần là vô nghĩa.
2. **Ghi có điều kiện** (compare-and-set / optimistic concurrency) cho cập nhật.
3. **Outbox** cho hiệu ứng ra ngoài — ghi ý định vào DB trong transaction, một processor riêng
   mới thực sự gửi. Điều này cũng cho bạn **kiểm duyệt trước khi gửi**.

Và quan trọng nhất: **tách rõ tool đọc và tool ghi**. Tool đọc retry thoải mái; tool ghi đi qua
cổng duyệt + idempotency. Trộn hai loại vào một tool là công thức của sự cố.

---

## 5. Kinh tế multi-agent: khi nào **đáng** (MA-1, MA-2, MA-8)

Lý do chính đáng để tách, xếp theo sức nặng:

| # | Lý do | Vì sao một agent không làm được |
|---|---|---|
| 1 | **Cô lập context** | rác nghiên cứu ở lại bên kia; luồng chính chỉ nhận kết luận |
| 2 | **Độc lập nhận thức** | reviewer không nên thấy lý lẽ tự bào chữa của writer |
| 3 | **Chuyên môn hoá** | mỗi agent prompt/tool riêng; reviewer **không có** quyền `Edit` |

**Lý do KHÔNG chính đáng:** *"song song cho nhanh"*. Nếu subtask phụ thuộc nhau, phối hợp nuốt
hết phần lợi.

**Phép nhân chi phí:**
```
tổng ≈ Σ (context riêng của mỗi agent)
     + chi phí điều phối (lead đọc kết quả, tổng hợp)
     + công việc lặp (hai agent cùng đọc một file)
     + lượt hỏng phải làm lại
```

Chi phí phối hợp tăng **O(k²)** theo số kênh giao tiếp; lợi ích song song tăng **O(k)** rồi bão
hoà. Với coding agent, điểm mà thêm agent làm hệ thống **tệ đi** thường rơi vào khoảng **5**.

Đo bằng `.claude/functions/Trace.ps1` — nếu một loại agent được gọi rất nhiều cho việc ngắn,
nó đang lỗ.

---

## 6. Loop chạy dài (AG-13)

| Mô hình | Nhịp do ai quyết | Hợp với |
|---|---|---|
| **Interval cố định** (`/loop 5m`) | người | việc có chu kỳ rõ |
| **Dynamic pacing** (agent tự hẹn giờ) | model | tốc độ thay đổi trạng thái không đều |
| **Event-driven** (thức dậy khi có sự kiện) | hệ thống | **tốt nhất khi có thể** |

**Bốn quy tắc để không thành máy đốt tiền:**
1. **Đừng poll cái mà hệ thống có thể báo cho bạn.** Chỉ poll thứ **bên ngoài** (CI, hàng đợi
   remote), và chọn nhịp khớp tốc độ thay đổi thật — CI 8 phút thì kiểm tra ở phút thứ 8, không
   phải 8 lần mỗi phút.
2. **Phân biệt tick "có chuyện" và tick "không có gì".** Tick im lặng phải rẻ và không sinh nhiễu.
3. **Ngân sách tổng và điều kiện dừng tuyệt đối.**
4. **Idempotency** — mỗi lần thức dậy có thể lặp lại việc đã làm.

---

## 7. Đánh giá agent khác đánh giá prompt (AG-16)

| | Eval prompt | Eval agent |
|---|---|---|
| Đối tượng chấm | một output | **trạng thái cuối** + **đường đi** |
| Chỉ số | đúng/sai, điểm rubric | task pass, số lượt, chi phí, số lần vi phạm quyền |
| Môi trường | chỉ cần input | cần **sandbox tái lập được** |

Cần **cả hai** loại:
1. **End-state eval** — sau khi chạy, trạng thái có đúng không? (test pass? row đúng? file đúng?)
2. **Trajectory eval** — đường đi có hợp lý không? Một agent "hên mà đúng" sau 40 lượt mò
   **không** phải agent tốt.

Xem skill `eval-harness` để dựng bộ eval đầu tiên.
