---
name: loop-agentic
description: Khung vòng lặp agentic của repo này — gather → act → verify → stop. Dùng khi bắt đầu bất kỳ task nhiều bước nào (feature, fixbug, refactor, phân tích), khi cần quyết định workflow hay agent, khi loop chạy lạc/lặp/cháy ngân sách, hoặc khi người dùng nói "loop", "agentic", "chạy pipeline", "tự làm đi". Kèm điều kiện dừng và bảng chẩn đoán 6 chế độ hỏng.
argument-hint: "[mô tả task]"
---

# Loop Agentic — khung vận hành

> **Agent = LLM + tool + vòng lặp + điều kiện dừng.**
> Bỏ vòng lặp → còn là một lời gọi API. Bỏ **verify** → chỉ là cỗ máy sinh hành động tự tin.

## 0. Trước khi lặp: chọn đúng tầng (AG-1, AG-2)

```
một lời gọi  →  workflow (bước cố định)  →  agent (model tự quyết luồng)
```

Đi từ trái sang phải, **dừng ở tầng đầu tiên đáp ứng được**. Bốn tiêu chí để leo lên tầng agent:

| Tiêu chí | Nếu "không" |
|---|---|
| Task nhiều bước và **không đặc tả trước được**? | dùng workflow |
| Kết quả đáng với chi phí + latency cao hơn? | dùng một lời gọi |
| Model thực sự làm được loại việc này? | đừng xây, sẽ thất bại tốn kém |
| **Lỗi có phát hiện & khôi phục được không?** | phải có người duyệt |

Tiêu chí 4 quan trọng nhất và hay bị bỏ qua. Coding agent thành công **không** vì model giỏi mà
vì môi trường có verifier rẻ và nhanh: compiler, test, linter, `git revert`.

## 1. Vòng lặp

```
        ┌──────────────────────────────────────────────┐
        ▼                                              │
  ┌──────────────┐                                     │
  │ 1. GATHER    │  đọc file, grep, git log            │
  │    CONTEXT   │  → agent `explorer` nếu phải đọc nhiều
  └──────┬───────┘                                     │
         ▼                                             │
  ┌──────────────┐                              ┌──────┴───────┐
  │ 2. ACT       │─────────────────────────────▶│ 3. VERIFY    │
  │    sửa 1 đơn │                              │ Verify.ps1   │
  │    vị nhỏ    │                              │ (hạng A/B/C) │
  └──────────────┘                              └──────┬───────┘
                                          đạt? ────────┴──── chưa ─┘
                                            │
                                            ▼
                                       4. REPORT
```

**Chạy verifier sau mỗi đơn vị nhỏ**, không phải một lần ở cuối.

```powershell
powershell -NoProfile -File .claude/functions/Verify.ps1 -Level build   # nhanh, mỗi lần sửa
powershell -NoProfile -File .claude/functions/Verify.ps1 -Level full    # trước khi tuyên bố xong
```

## 2. Thang verifier — luôn dùng hạng cao nhất có sẵn (AG-5)

| Hạng | Verifier | Đặc tính |
|---|---|---|
| **A** | compiler / type-checker / linter | rẻ, nhanh, tuyệt đối đúng trong phạm vi của nó |
| **B** | unit / integration test | mạnh nhất khi test đáng tin |
| **C** | assert trên trạng thái thật (query lại DB, gọi lại API) | tốt nhất cho agent nghiệp vụ |
| **D** | LLM-as-judge theo rubric | chỉ khi không có A–C; phải hiệu chuẩn |
| **E** | người duyệt | đắt nhất — dành cho hành động không hoàn tác được |

Khi verifier FAIL: đưa **nguyên văn** output lỗi vào bước sau. Đừng tóm tắt — model cần đọc đúng
thứ compiler nói.

## 3. Điều kiện dừng — sáu cái, thiếu là cháy tiền (AG-6)

| # | Dừng khi | Cơ chế trong repo này |
|---|---|---|
| 1 | Chạm trần số lượt | `Task.ps1 -Action step` → exit 2 |
| 2 | Chạm trần ngân sách | `LOOP_MAX_TURNS` trong `settings.json` |
| 3 | Quá thời gian tường | `timeout` của hook |
| 4 | **Không tiến triển** | hook `loop-progress.ps1` (cùng tool + cùng tham số + cùng lỗi ×3) |
| 5 | Đạt mục tiêu | `Verify.ps1` exit 0 |
| 6 | **Cần con người** | escalate — xem §5 |

Điều kiện 4 hay thiếu nhất. Lặp lại y hệt 3 lần nghĩa là agent đang **kẹt**, không phải đang
**cố gắng**.

## 4. Sáu chế độ hỏng — chẩn đoán qua triệu chứng (AG-8)

| Triệu chứng | Chế độ hỏng | Cách chữa |
|---|---|---|
| Lặp cùng tool, cùng tham số, cùng lỗi | **Thrashing** | đổi giả thuyết, hoặc dừng và báo |
| Càng chạy càng lú, bám giả định sai từ lượt 5 | **Context poisoning** | restart sạch với tóm tắt **đã kiểm chứng** |
| Refactor cả module khi chỉ được nhờ sửa 1 hàm | **Over-eager** | nêu rõ phạm vi + điều cấm |
| Báo xong khi mới làm nửa việc | **Stopping short** | định nghĩa "xong" = verifier chạy được |
| Không dùng tool có sẵn, tự bịa dữ liệu | **Tool blindness** | viết lại mô tả tool; giảm/gộp tool |
| Tool ném lỗi nhưng model tưởng thành công | **Silent failure** | luôn trả lỗi thật, không nuốt exception |

**Chẩn đoán bằng cách đọc trajectory** (chuỗi tool call + kết quả), **không** đọc câu trả lời
cuối. Câu trả lời cuối luôn nghe hợp lý — đó chính là vấn đề.

## 5. Escalate là hành động hạng nhất

Dừng và hỏi người **không phải là thất bại**. Escalate khi:

- Hành động không hoàn tác được mà chưa có phê duyệt rõ ràng;
- Ba giả thuyết đều bị bác bỏ và không còn giả thuyết thứ tư;
- Yêu cầu mơ hồ tới mức hai cách hiểu dẫn tới hai công việc khác hẳn nhau.

Khi escalate, đưa đủ: **đã thử gì → thất bại thế nào → cần quyết định gì**. Đừng chỉ nói "tôi bị
kẹt". Cho agent một lối thoát danh dự làm **giảm hẳn** hành vi bịa và thrashing.

## 6. Trạng thái loop

```powershell
powershell -NoProfile -File .claude/functions/Task.ps1 -Action init -Workflow dev-loop -Title "..."
powershell -NoProfile -File .claude/functions/Task.ps1 -Action step -Step implement
powershell -NoProfile -File .claude/functions/Task.ps1 -Action close -Outcome done
```

Trạng thái nằm **ngoài context** vì context bị nén, còn file thì không.

---

Chi tiết sâu hơn — thiết kế tool surface, error feedback cho agent, idempotency, kinh tế
multi-agent — xem `reference.md` trong thư mục skill này (chỉ đọc khi thật sự cần).
