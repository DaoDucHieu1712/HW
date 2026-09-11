---
description: Xem loop đang ở đâu và loop đang hỏng ở chỗ nào
---

## Trạng thái loop

!`powershell -NoProfile -ExecutionPolicy Bypass -File .claude/functions/Task.ps1 -Action status`

## Chỉ số vận hành

!`powershell -NoProfile -ExecutionPolicy Bypass -File .claude/functions/Trace.ps1`

## Nhiệm vụ

Đọc số liệu ở trên và trả lời **ba câu hỏi vận hành** (AG-12) — không phải "độ chính xác":

| Chỉ số | Đọc ra điều gì |
|---|---|
| **Turns per completed task** (p50/p95) | tăng đột biến = model đang **mò** ⇒ tool hoặc prompt hỏng, không phải model kém |
| **Cost per completed task** | chỉ số kinh tế **duy nhất** đáng theo dõi — không phải cost/request |
| **Tool error rate theo từng tool** | tool nào hay lỗi là tool có **mô tả/schema tệ** |

Thêm: tỉ lệ **chạm trần ngân sách** (chạm nhiều = trần sai **hoặc** agent kẹt), và số lần
**thrashing**.

Nếu thấy vấn đề, đề xuất sửa theo **thứ tự sức nặng** — và **chỉ một biến mỗi vòng**:

1. verifier (thêm/làm chặt) ★★★★★
2. mô tả tool ★★★★
3. ngân sách / điều kiện dừng ★★★
4. chiến lược compaction ★★★
5. effort / model ★★
6. câu chữ trong prompt ★

Đổi hai biến cùng lúc thì kết quả không quy được cho biến nào — và bạn đã đốt một vòng đo.
