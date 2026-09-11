---
description: Rà soát thay đổi trên nhánh — đúng đắn và bảo mật, chỉ đọc
argument-hint: "[phạm vi, mặc định: diff của nhánh]"
---

## Diff cần review

!`git diff --stat`
!`git diff`

## Phạm vi bổ sung

$ARGUMENTS

## Nhiệm vụ

Uỷ thác agent `reviewer` (nó **không có** `Edit`/`Write` — cố ý: reviewer sửa được code là
reviewer đã mất tính độc lập).

Chỉ review **thay đổi trên nhánh**, không review toàn bộ repo. Nợ kỹ thuật có sẵn không phải
phát hiện của lần review này.

Xếp hạng: **BLOCKER** (sai đúng đắn / lỗ hổng có kịch bản khai thác) · **MAJOR** (sẽ hỏng ở
production trong điều kiện dự đoán được) · **MINOR** (đúng nhưng khó bảo trì) · **NIT** (tối đa 3).

Ba quy tắc cứng:
1. Mỗi phát hiện có `file:line` **và** một kịch bản hỏng cụ thể: input gì → trạng thái gì →
   kết quả sai gì. Không suy đoán không bằng chứng.
2. Không phát minh yêu cầu. "Nên thêm caching" không phải phát hiện review trừ khi có bằng chứng
   vấn đề hiệu năng thật.
3. **Không có gì để nói là kết quả hợp lệ.** Đừng nặn ra phát hiện để trông có ích.

Kết thúc bằng mục **"Đã kiểm tra, không có vấn đề"** để người đọc biết review bao phủ tới đâu.
