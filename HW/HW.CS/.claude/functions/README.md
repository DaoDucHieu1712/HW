# `functions/` — thư viện tất định dùng chung

Ba script này là **phần không xác suất** của loop. Chúng được gọi từ ba nơi:

1. **Hook** — `stop-quality-gate.ps1` gọi cùng logic với `Verify.ps1`;
2. **Slash command** — qua cú pháp `` !`lệnh` ``, kết quả **có sẵn trong prompt** thay vì bắt
   model đi gọi tool (HR-9);
3. **Người** — chạy tay khi muốn kiểm chứng.

| Script | Trả lời câu hỏi | Exit code |
|---|---|---|
| `Verify.ps1` | *"Đã xong thật chưa?"* | `0` = pass, `1` = fail |
| `Task.ps1` | *"Loop đang ở bước nào, còn bao nhiêu ngân sách?"* | `2` = hết ngân sách |
| `Trace.ps1` | *"Loop đang hỏng ở đâu?"* | `0` |

---

## Vì sao `!`cmd`` quan trọng hơn nó trông

```markdown
## Kết quả verifier
!`powershell -NoProfile -File .claude/functions/Verify.ps1 -Level build`
```

Không có nó, model phải **tự quyết định** gọi tool để lấy kết quả build — thêm một lượt, và
**có thể quên**. Có nó, dữ liệu đã nằm sẵn trong prompt trước khi model nghĩ câu đầu tiên.

Đây chính là ranh giới **workflow (tất định)** vs **agent (xác suất)** áp dụng ở cấp một lệnh.

---

## Mở rộng cho repo của bạn

`Verify.ps1` mới cài verifier **hạng A (build)** và **hạng B (test)**. Hạng C — *assert trên
trạng thái thật* — là thứ chỉ repo của bạn viết được, và nó là hạng đáng đầu tư nhất cho agent
nghiệp vụ:

```powershell
# Thêm vào Verify.ps1, sau stage 'test':
if ($Level -eq 'full' -and $env:LOOP_ASSERT_CMD) {
    $results.Add((Run-Stage -Name 'assert' -Grade 'C' -Command $env:LOOP_ASSERT_CMD))
}
```

Ví dụ hạng C: gọi lại `GET /blogs/{id}` sau khi agent bảo đã tạo blog; query lại bảng
`OutboxMessages` để xác nhận domain event đã được ghi. **Agent nói "đã tạo" không phải bằng
chứng; đọc lại được mới là bằng chứng.**

Đổi lệnh cho ngôn ngữ khác bằng biến môi trường, không cần sửa script:

```jsonc
// .claude/settings.json
"env": {
  "LOOP_BUILD_CMD": "npm run typecheck",
  "LOOP_TEST_CMD":  "npm test -- --run",
  "LOOP_ASSERT_CMD": "npm run smoke"
}
```
