# `agents/` — subagent định nghĩa sẵn

## Vì sao định nghĩa sẵn thay vì mô tả trong prompt (HR-13)

1. **Giới hạn tool là ràng buộc cứng.** `reviewer` không có `Edit`/`Write` thì **không thể** sửa
   code, dù bị dụ bằng bất cứ prompt nào. Một câu "đừng sửa code" trong prompt thì không.
2. **Tái sử dụng** — cùng một định nghĩa dùng được cả khi uỷ thác lẫn khi làm teammate.
3. **Context sạch** — subagent không mang theo lịch sử hội thoại của luồng chính, và cũng
   **không trả về** đống rác nó đã đọc.

## Bảng agent

| Agent | Ghi được? | Vai trong loop | Tách ra vì |
|---|---|---|---|
| `explorer` | ❌ | GATHER | đọc 200 file, trả về 10 dòng — rác ở lại bên kia |
| `analyst` | ✅ (chỉ `docs/`) | GATHER | đặc tả là sản phẩm riêng, cần đọc nhiều |
| `architect` | ❌ | PLAN | quyết định trước khi ai đó viết code |
| `implementer` | ✅ | ACT | thực thi trong phạm vi đã chốt |
| `bug-hunter` | ❌ | GATHER | **độc lập nhận thức** — chẩn đoán tách khỏi chữa |
| `verifier` | ❌ | VERIFY | không có lợi ích trong việc tuyên bố "đã xong" |
| `reviewer` | ❌ | VERIFY | không thấy lý lẽ tự bào chữa của người viết |
| `reporter` | ✅ (chỉ `docs/`) | REPORT | đọc bằng chứng, không tin lời tường thuật |

## Khi nào **không** tách subagent (AG-15, MA-15)

Lý do thật để tách là **cô lập context**, không phải "chạy song song cho nhanh". Đừng tách khi:

- Việc cần **toàn bộ lịch sử hội thoại** — subagent khởi động lạnh, mô tả lại nhiệm vụ thường
  tốn hơn là tự làm.
- Việc **ngắn và tuần tự** — chi phí nạp lại project context vượt lợi ích.
- Việc **sửa cùng một file** — hai agent ghi cùng file là xung đột, không phải song song.

> *"Chi phí bàn giao là chi phí thật. Nếu mô tả nhiệm vụ cho subagent tốn nhiều công hơn tự làm,
> thì đừng tách."* (MA-6)

## Giao thức bàn giao — sáu phần (MA-6)

Subagent **không** thấy 30 lượt trước của bạn. Spawn prompt nghèo nàn = kết quả lạc đề. Luôn đưa
đủ sáu phần:

```
1. VAI TRÒ + PHẠM VI    : "Rà soát bảo mật src/auth/. CHỈ ĐỌC."
2. BỐI CẢNH ĐÃ BIẾT     : "JWT lưu trong cookie httpOnly; refresh flow vừa sửa hôm qua."
3. ĐỊNH NGHĨA XONG      : "Mỗi phát hiện: file:line, kịch bản khai thác, mức độ."
4. RÀNG BUỘC / ĐIỀU CẤM : "Không sửa file. Không chạy migration. Không suy đoán."
5. ĐẦU RA MONG ĐỢI      : định dạng cụ thể để lead tổng hợp được
6. NGÕ CỤT ĐÃ BIẾT      : "Đã kiểm tra CORS, không phải nguyên nhân."
```

Phần 6 hay bị bỏ và tốn kém nhất — thiếu nó, subagent đi lại đúng con đường bạn vừa đi.

## Quy mô

3–5 agent cho một việc là trần thực dụng. Chi phí phối hợp tăng **O(k²)** theo số kênh giao
tiếp, trong khi lợi ích song song tăng **O(k)** rồi bão hoà — luôn có điểm mà thêm agent làm hệ
thống **tệ đi** (MA-8). **Ba người tập trung hơn năm người tản mát.**
