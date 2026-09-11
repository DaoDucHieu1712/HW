# `workflows/` — pipeline **tất định**, và biết khi nào không dùng chúng

## Năm mẫu workflow chuẩn (gọi đúng tên) — AG-1

| Mẫu | Hình dạng | Dùng cho | Trong template này |
|---|---|---|---|
| **Prompt chaining** | A → B → C | task chia được thành bước tuyến tính rõ | `dev-loop` |
| **Routing** | classifier → nhánh chuyên biệt | intent khác nhau cần xử lý khác nhau | `analysis-loop` |
| **Parallelization** | fan-out → fan-in | nhiều góc nhìn độc lập | `/team` |
| **Orchestrator–worker** | 1 điều phối chia việc động cho n worker | số subtask **không biết trước** | `dev-loop` (bước act) |
| **Evaluator–optimizer** | sinh → chấm → sửa, lặp | có tiêu chí chấm rõ ràng | `fixbug-loop` (test đỏ = evaluator) |

## Workflow hay agent?

Khác biệt nằm ở **ai quyết định bước tiếp theo**:

| | **Workflow** | **Agent** |
|---|---|---|
| Ai quyết định luồng | **code/định nghĩa của bạn** (cố định) | **model** (quyết định tại runtime) |
| Dự đoán được | ✅ | ❌ |
| Chi phí | biết trước | biến thiên — **cần trần** |
| Debug | như code thường | phải **đọc trajectory** |
| Hợp với | task đã biết cách làm | task **không thể đặc tả trước** |

> **Bắt đầu từ tầng đơn giản nhất đáp ứng được: một lời gọi → workflow → agent.**
> Dùng agent cho việc workflow làm được là tự nguyện trả thêm tiền, thêm latency, và thêm phi
> tất định.

Ba file JSON ở đây **không phải là engine** — chúng là **hợp đồng đọc được** mô tả bước, đầu ra,
verifier và điều kiện thoát. Model đọc chúng; `functions/Task.ps1` theo dõi vị trí và ngân sách.
Không có engine nào ép thứ tự, vì thứ đáng ép (build đỏ không được kết thúc) đã nằm ở **hook**.

## Cấu trúc một step

```jsonc
{
  "id": "act",
  "goal": "…",                    // một câu, kiểm chứng được
  "agent": "implementer",         // uỷ thác cho ai (bỏ trống = luồng chính tự làm)
  "outputs": ["…"],               // sản phẩm cụ thể, không phải "hoàn thành bước"
  "verifier": "lệnh chạy được",   // KHÔNG phải "kiểm tra lại xem có đúng không"
  "exitCriteria": "…"             // khi nào được đi tiếp
}
```

Trường **`verifier`** là trường quan trọng nhất. Một step không có verifier là một step không
biết khi nào mình xong — và đó chính là cách agent "stopping short" (AG-8).

## Điều kiện dừng ở đâu

| Loại | Ai ép | Ở đâu |
|---|---|---|
| Trần số lượt | `functions/Task.ps1` (exit 2) | `LOOP_MAX_TURNS` |
| Không tiến triển | `hooks/loop-progress.ps1` (exit 2) | `LOOP_MAX_SAME_ERROR` |
| Đạt mục tiêu | `functions/Verify.ps1` (exit 0) | — |
| Không cho kết thúc khi đỏ | `hooks/stop-quality-gate.ps1` (exit 2) | `LOOP_QUALITY_GATE=1` |
| Cần con người | `permissions.ask` + `hooks/block-dangerous.ps1` | `settings.json` |

Chú ý: **không cái nào trong số này là một câu trong prompt.** Đó là chủ ý — điều kiện dừng
thuộc về tầng tất định (HR-1).

## `templates/`

| File | Dùng bởi |
|---|---|
| `SRS.md` | skill `spec-srs`, `/srs` |
| `SRD.md` | skill `spec-srd`, `/srd` |
| `TASK.md` | mẫu để điền `.claude/state/CURRENT_TASK.md` |
| `REPORT.md` | skill `report`, `/report` |
