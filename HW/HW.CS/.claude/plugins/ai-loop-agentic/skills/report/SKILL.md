---
name: report
description: Viết báo cáo kết quả cho người đọc — đã làm gì, chứng minh bằng gì, còn lại gì, rủi ro nào. Dùng khi người dùng nói "báo cáo", "report", "tổng hợp lại", "tóm tắt kết quả", "viết lại cho sếp", hoặc ở cuối một pipeline nhiều bước.
argument-hint: "[phạm vi báo cáo]"
---

# Viết báo cáo

## Quy tắc trung thực — quan trọng hơn mọi định dạng

1. **Test đỏ thì nói test đỏ**, kèm output. Không viết "về cơ bản đã hoàn thành".
2. **Bước bị bỏ qua thì nói là bị bỏ qua**, và vì sao bỏ.
3. **Không nói "đã kiểm tra kỹ"** trừ khi dẫn ra được lệnh verifier cụ thể.
4. **Việc đã xong và đã kiểm chứng thì nói thẳng, không rào đón.** Dè dặt vô cớ làm mất thông
   tin đúng như thổi phồng.

Báo cáo tô hồng là **nợ**: người đọc ra quyết định dựa trên nó, và hoá đơn tới muộn.

## Đọc bằng chứng, không đọc lời tường thuật

| Nguồn | Cho biết |
|---|---|
| `git diff --stat` | thay đổi **thật** trên đĩa |
| `Verify.ps1 -Level full` | trạng thái xanh/đỏ **thật** |
| `.claude/state/loop-run.json` | bước, số lượt, ngân sách |
| `.claude/state/audit.jsonl` | hành động đã làm, thứ bị chặn |

Lời tường thuật của agent (kể cả của chính bạn ở lượt trước) **không phải bằng chứng**.

## Ba trạng thái, không phải hai

**HOÀN THÀNH** · **HOÀN THÀNH MỘT PHẦN** · **BỊ CHẶN**

Trạng thái thứ tư ngầm — *"đã làm nhưng không kiểm chứng được"* — phải được nói ra như vậy, không
được nhập vào HOÀN THÀNH.

## Cấu trúc

```
# <Tiêu đề>
**Trạng thái:** …
**Bằng chứng:** <lệnh> → PASS/FAIL

## Tóm tắt (3 dòng, cho người chỉ đọc 3 dòng)

## Đã làm
| # | Thay đổi | File | Kiểm chứng bằng |

## Chưa làm / cố ý bỏ qua
| Việc | Vì sao |

## Rủi ro còn lại
- <cái người đọc cần biết TRƯỚC khi merge>

## Việc tiếp theo đề xuất
- <cụ thể — không phải "tiếp tục cải thiện">
```

Ghi ra `docs/reports/<YYYY-MM-DD>-<slug>.md`, trả về đường dẫn + trạng thái + 3 dòng tóm tắt.

## Viết cho ai

Mặc định: **đồng nghiệp kỹ thuật chưa theo dõi phiên này**. Nghĩa là nêu bối cảnh vừa đủ, dùng
đường dẫn file thật, và không dùng đại từ trỏ về những thứ chỉ có trong hội thoại ("cái lỗi lúc
nãy", "như đã bàn").
