---
description: Lập một tổ agent cho việc cần nhiều góc nhìn độc lập (review lớn, điều tra nhiều giả thuyết)
argument-hint: "[nhiệm vụ của tổ]"
---

## Nhiệm vụ

$ARGUMENTS

## Trước khi spawn bất cứ ai — trả lời câu này

> **Việc này cần *thảo luận*, hay chỉ cần *kết quả*?**

Chỉ cần kết quả → **uỷ thác subagent tuần tự**, rẻ hơn nhiều. Đừng lập tổ.

Lý do chính đáng để tách nhiều agent, xếp theo sức nặng (MA-1):

| # | Lý do | Vì sao một agent không làm được |
|---|---|---|
| 1 | **Cô lập context** | rác nghiên cứu ở lại bên kia; luồng chính chỉ nhận kết luận |
| 2 | **Độc lập nhận thức** | reviewer không nên thấy lý lẽ tự bào chữa của writer |
| 3 | **Chuyên môn hoá** | mỗi agent tool riêng; reviewer **không có** `Edit` |

⚠️ **Lý do KHÔNG chính đáng: "song song cho nhanh".** Nếu subtask phụ thuộc nhau, chi phí phối
hợp nuốt hết phần lợi — và multi-agent thường **chậm hơn** về wall-clock cho việc phụ thuộc.

## Kinh tế (nói ra trước khi tiêu tiền của người dùng)

```
tổng ≈ Σ (context riêng của mỗi agent)      ← mỗi agent nạp lại CLAUDE.md, MCP, skill
     + chi phí điều phối (đọc kết quả, tổng hợp)
     + công việc lặp (hai agent cùng đọc một file)
     + lượt hỏng phải làm lại
```

Chi phí phối hợp tăng **O(k²)** theo số kênh; lợi ích song song tăng **O(k)** rồi bão hoà.
Với coding agent, điểm mà thêm agent làm hệ thống **tệ đi** thường rơi vào khoảng **5**.

**Trần thực dụng: 3–5 agent. Ba người tập trung hơn năm người tản mát.**

## Spawn prompt — sáu phần, không thiếu phần nào (MA-6)

Agent khởi động **lạnh**: nó không thấy 30 lượt trước của bạn. Spawn prompt nghèo = kết quả lạc đề.

```
1. VAI TRÒ + PHẠM VI    : "Rà soát bảo mật src/auth/. CHỈ ĐỌC."
2. BỐI CẢNH ĐÃ BIẾT     : "JWT trong cookie httpOnly; refresh flow vừa sửa hôm qua."
3. ĐỊNH NGHĨA XONG      : "Mỗi phát hiện: file:line, kịch bản khai thác, mức độ."
4. RÀNG BUỘC / ĐIỀU CẤM : "Không sửa file. Không chạy migration. Không suy đoán."
5. ĐẦU RA MONG ĐỢI      : định dạng cụ thể để tổng hợp được
6. NGÕ CỤT ĐÃ BIẾT      : "Đã kiểm tra CORS, không phải nguyên nhân."
```

Phần 6 hay bị bỏ và tốn kém nhất — thiếu nó, agent đi lại đúng con đường bạn vừa đi.

## Chia việc

- **Chia sở hữu theo FILE**, ghi thẳng trong spawn prompt. Hai agent sửa cùng file là xung đột,
  không phải song song (MA-8).
- Mỗi agent một **lăng kính khác nhau** (bảo mật / hiệu năng / đúng đắn), không phải cùng lăng
  kính trên phần khác nhau — nếu không bạn chỉ trả tiền ba lần cho một góc nhìn.
- Cỡ task tốt = **một đơn vị tự chứa có sản phẩm rõ ràng** (một file test, một bản review).

## Thiết kế đối kháng khi có nhiều giả thuyết (MA-9)

Với điều tra/quyết định quan trọng, giao cho các agent **giả thuyết khác nhau** và yêu cầu mỗi
agent tìm bằng chứng **bác bỏ** giả thuyết của mình. Một agent điều tra tuần tự sẽ neo vào giả
thuyết đầu tiên và diễn giải mọi bằng chứng sau đó theo hướng củng cố nó.

## Khi tổng hợp

Bạn **đọc bằng chứng** của từng agent, không tin kết luận của chúng. Hai agent cùng đồng ý
**không phải** là xác nhận — có thể chúng cùng hùa theo giả thuyết đầu tiên (đồng thuận sai).
