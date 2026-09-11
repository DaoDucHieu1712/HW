---
name: eval-harness
description: Dựng bộ eval cho một loop/prompt/agent — dataset từ traffic thật, grader tự động, và quy trình hill-climbing sửa MỘT biến mỗi vòng. Dùng khi người dùng nói "eval", "đo chất lượng", "prompt này tốt hơn không", "so sánh model", "regression prompt", hoặc khi cần chứng minh một thay đổi prompt/loop là cải thiện chứ không phải cảm tính.
---

# Dựng eval cho loop

> **Đây là kỹ năng phân biệt lớn nhất giữa người "dùng AI" và người "vận hành AI".**
> Không có eval thì mọi thay đổi prompt là cảm tính, và bạn không bao giờ biết bản sau có tốt hơn
> bản trước không.

## Vì sao eval agent khác eval prompt (AG-16)

| | Eval prompt | Eval agent |
|---|---|---|
| Chấm cái gì | một output | **trạng thái cuối** + **đường đi** |
| Chỉ số | đúng/sai, điểm rubric | task pass, số lượt, chi phí, số lần vi phạm quyền |
| Môi trường | chỉ cần input | cần **sandbox tái lập được** (repo mẫu, DB seed, API mock) |

Cần **cả hai** loại, không phải một:

1. **End-state eval** — sau khi agent chạy xong, trạng thái có đúng không? (test pass? row trong
   DB đúng? file có nội dung mong đợi?) Đây là chỉ số **chính**.
2. **Trajectory eval** — đường đi có hợp lý không? Có gọi tool nguy hiểm? Có lặp? Có bịa?
   Một agent "hên mà đúng" sau 40 lượt mò **không phải** agent tốt.

## Bốn bước dựng bộ eval đầu tiên

### 1. Dataset — lấy từ traffic thật, không tự nghĩ ra
30 ca là đủ để bắt đầu; 10 ca là quá ít để phân biệt nhiễu với tín hiệu. Lấy từ log, từ issue,
từ ticket thật. Ca tự nghĩ ra có xu hướng dễ hơn thực tế một cách hệ thống.

Chia **train / val / test** ngay từ đầu. Sửa prompt dựa trên `train`, đo trên `val`, và chỉ
chạm `test` khi chốt — nếu không bạn sẽ overfit vào chính bộ đo của mình.

### 2. Grader — tự động trước, LLM-judge sau
Thang tin cậy (AG-5): assert chính xác > so khớp có cấu trúc > LLM-judge theo rubric.
Chỉ dùng LLM-judge khi không thể tự động, và **phải hiệu chuẩn**: chấm tay 20 ca, so với judge,
đo mức đồng thuận. Judge chưa hiệu chuẩn là một cái thước không có vạch.

### 3. Sandbox tái lập được
Không có cái này thì "eval agent" chỉ là chạy thử: repo cố định (commit hash), DB seed,
tool ngoài mock. Cùng input phải cho cùng điểm khởi đầu.

### 4. Hill-climbing — **sửa MỘT biến mỗi vòng**

```
đo baseline → đổi ĐÚNG MỘT biến → đo lại → giữ nếu tốt hơn, bỏ nếu không
```

Các biến, xếp theo sức nặng thực tế:

| # | Biến | Sức nặng |
|---|---|---|
| 1 | **verifier** (thêm/làm chặt) | ★★★★★ |
| 2 | **mô tả tool** | ★★★★ |
| 3 | ngân sách / điều kiện dừng | ★★★ |
| 4 | chiến lược compaction | ★★★ |
| 5 | effort / model | ★★ |
| 6 | câu chữ trong prompt | ★ |

> **Đầu tư một bộ test nhanh cải thiện agent nhiều hơn viết lại system prompt.**
> Cần gạt mạnh nhất không phải câu chữ — mà là **verifier và tool surface**.

Đổi hai biến cùng lúc thì kết quả không quy được cho biến nào, và bạn đã đốt một vòng đo.

## Ba chỉ số vận hành (AG-12)

| Chỉ số | Đọc ra điều gì |
|---|---|
| **Turns per completed task** (p50/p95) | tăng đột biến = model đang mò ⇒ tool/prompt hỏng |
| **Cost per completed task** | chỉ số kinh tế **duy nhất** đáng theo dõi — không phải cost/request |
| **Tool error rate theo từng tool** | tool nào hay lỗi là tool có mô tả/schema tệ |

Thêm: **cache hit ratio** (rơi về 0 = có silent invalidator), **tỉ lệ escalate**, và **tỉ lệ
task bị dừng bởi trần** (chạm trần nhiều = trần sai hoặc agent kẹt).

Repo này: `powershell -NoProfile -File .claude/functions/Trace.ps1`

## Bố cục đề xuất

```
evals/
├── cases/            # 1 file/ca: input + trạng thái đầu + kết quả mong đợi
├── graders/          # grader tự động; LLM-judge kèm rubric ĐÃ hiệu chuẩn
├── runs/<ngày>/      # kết quả mỗi lần chạy — giữ lại để so sánh về sau
└── README.md         # đang đo cái gì, baseline là bao nhiêu
```

`baseline` trong README là thứ hay bị quên và làm cả bộ eval mất giá trị: không có số cũ thì
số mới không nói lên điều gì.
