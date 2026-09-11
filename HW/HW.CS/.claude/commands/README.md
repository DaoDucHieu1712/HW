# `commands/` — cửa vào do **người** gõ

| Lệnh | Làm gì | Gọi tới |
|---|---|---|
| `/dev <task>` | pipeline phát triển đầy đủ | skill `loop-agentic`, agents `architect`/`implementer`/`verifier` |
| `/fix <triệu chứng>` | quy trình sửa lỗi có kỷ luật | skill `fixbug`, agent `bug-hunter` |
| `/analyze <câu hỏi>` | phân tích codebase | skill `code-analysis`, agent `explorer` |
| `/srs <feature>` | đặc tả nghiệp vụ | skill `spec-srs`, agent `analyst` |
| `/srd <feature>` | tài liệu thiết kế | skill `spec-srd` |
| `/mockup <màn hình>` | wireframe / prototype | skill `mockup` |
| `/review` | rà soát diff của nhánh | agent `reviewer` |
| `/verify` | chạy verifier đầy đủ | `functions/Verify.ps1` |
| `/report <phạm vi>` | báo cáo dựa trên bằng chứng | skill `report`, agent `reporter` |
| `/loop-status` | loop đang ở đâu, hỏng ở đâu | `functions/Task.ps1`, `Trace.ps1` |
| `/team <nhiệm vụ>` | lập tổ agent nhiều góc nhìn | agents |
| `/harness-doctor` | chẩn đoán cấu hình harness | — |

## Ba cơ chế thay thế trong nội dung lệnh (HR-9)

| Cú pháp | Làm gì |
|---|---|
| `$ARGUMENTS` | toàn bộ phần người dùng gõ sau tên lệnh |
| `$1 $2 …` | tham số theo vị trí |
| `` !`lệnh` `` | **chạy lệnh trước**, thay bằng output — model nhận **dữ liệu thật** |
| `@đường/dẫn` | tham chiếu file |
| `${CLAUDE_PROJECT_DIR}` | thư mục gốc project |

## Vì sao `` !`cmd` `` là kỹ thuật mạnh nhất ở đây

```markdown
## Kết quả verifier
!`powershell -NoProfile -File .claude/functions/Verify.ps1 -Level build`
```

Không có nó, model phải **tự quyết định** gọi tool để lấy kết quả build — thêm một lượt, và
**có thể quên**. Có nó, dữ liệu đã nằm sẵn trong prompt trước khi model nghĩ câu đầu tiên.

Đây chính là ranh giới **workflow (tất định)** vs **agent (xác suất)**, áp dụng ở cấp một lệnh.
Mỗi lệnh trong thư mục này bắt đầu bằng một khối `` !`…` `` — đó không phải trang trí, đó là
cách biến một lệnh thành nửa-workflow.

## Command hay Skill?

Về hình thức chúng đã hội tụ — cùng là file markdown có frontmatter, cùng gọi bằng `/tên`.
Khác biệt thật nằm ở **ai kích hoạt và khi nào nội dung được nạp**:

- **Skill** có `description` **luôn thường trú** để **model tự quyết định dùng**; thân chỉ nạp
  khi cần.
- **Command** là thứ **người** gõ.

Câu hỏi thiết kế đúng: ***tôi có muốn model tự dùng thứ này không?***
Ở template này, mỗi command là một **cửa vào có chủ đích của người**, và nó uỷ quyền phần kiến
thức dài cho skill tương ứng — nên kiến thức không bị nhân đôi ở hai chỗ.
