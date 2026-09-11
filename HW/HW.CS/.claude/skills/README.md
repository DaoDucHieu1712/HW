# `skills/` — quy trình dài, chỉ nạp khi liên quan

## Progressive disclosure — ba tầng (HR-10)

| Tầng | Nạp khi nào | Chi phí |
|---|---|---|
| `description` | **luôn luôn** — để model biết skill tồn tại | rất nhỏ; tổng mọi description bị giới hạn trong ~1% cửa sổ context |
| Thân `SKILL.md` | khi skill được kích hoạt (model tự quyết, hoặc người gõ `/tên`) | vừa; **ở lại context cho các lượt sau** |
| File tham chiếu (`reference.md`) | khi được đọc | chỉ khi cần |

Khi tổng description vượt ngân sách, harness **rút ngắn mô tả của skill ít dùng** để giữ skill
hay dùng còn nhìn thấy được — nghĩa là **skill viết mô tả tệ sẽ bị "vô hình" trước tiên**.

> ⚖️ **Hệ quả:** `description` là phần quan trọng nhất của skill, **không phải nội dung**.
> Viết theo dạng *"Dùng khi \<tình huống\>, \<từ khoá người dùng thường gõ\>"*.
> Mô tả kiểu *"Công cụ hỗ trợ triển khai"* = skill chết.

Mọi skill trong template này có **từ khoá cả tiếng Việt lẫn tiếng Anh** trong `description`, vì
người dùng thật gõ lẫn lộn hai thứ tiếng.

## Bảng skill

| Skill | Dùng khi | Có `reference.md`? |
|---|---|---|
| `loop-agentic` | bắt đầu task nhiều bước; loop chạy lạc/lặp/cháy ngân sách | ✅ |
| `spec-srs` | đặc tả **nghiệp vụ** — làm gì, cho ai | ❌ |
| `spec-srd` | đặc tả **thiết kế** — làm thế nào | ❌ |
| `mockup` | cần thứ nhìn được để chốt yêu cầu UI | ❌ |
| `fixbug` | có lỗi/exception/hành vi sai | ❌ |
| `code-analysis` | phân tích kiến trúc, tác động, điểm nóng | ❌ |
| `report` | tổng hợp kết quả cho người đọc | ❌ |
| `eval-harness` | chứng minh một thay đổi prompt/loop là cải thiện | ❌ |

## Frontmatter đáng nhớ (HR-12)

```yaml
---
name: deploy-prod
description: Triển khai lên production. Dùng khi người dùng nói "deploy", "phát hành bản mới".
disable-model-invocation: true                  # CHỈ người được gọi — thao tác có hệ quả
user-invocable: false                           # ẩn khỏi menu /, model vẫn tự kích hoạt
allowed-tools: Bash(docker *) Bash(kubectl *)   # pre-approve TẠM THỜI
disallowed-tools: AskUserQuestion               # gỡ tool khỏi pool khi skill hoạt động
model: claude-opus-5
effort: high
paths: "src/**/*.cs"                            # chỉ kích hoạt trong phạm vi này
context: fork                                   # chạy trong subagent, context riêng
agent: explorer                                 # loại subagent khi fork
argument-hint: "[môi-trường]"
---
```

⚠️ **Ba điểm hay bị hỏi:**
- `allowed-tools` là **pre-approve tạm thời** — quyền được cấp cho lượt hiện tại và **mất khi
  người dùng gửi message tiếp theo**. Nó **không** vượt qua được `deny` trong permissions.
- `paths` là cách rất rẻ để giảm nhiễu: skill về React không nên xuất hiện khi đang sửa file SQL.
- `context: fork` biến skill thành subagent — dùng khi skill phải đọc rất nhiều.

## Skill hay Command hay Hook? (HR-11)

| Bạn muốn… | Dùng | Vì sao |
|---|---|---|
| Kiến thức/quy trình dài, chỉ cần khi liên quan | **Skill** | progressive disclosure — không trả tiền khi không dùng |
| Người chủ động chạy một thao tác (deploy, commit) | **Skill** + `disable-model-invocation: true` | bạn kiểm soát thời điểm |
| Kiến thức nền model nên tự dùng, người không cần thấy | **Skill** + `user-invocable: false` | ẩn khỏi menu `/` |
| Việc đọc-nhiều, cần cô lập context | **Subagent** (hoặc skill `context: fork`) | kết quả về, rác ở lại bên kia |
| **Bắt buộc xảy ra, chặn được** | **Hook** | tất định |
| Truy cập hệ thống ngoài | **MCP tool** | năng lực, có auth, dùng lại giữa các host |
| Quy ước ngắn áp dụng mọi lúc | **CLAUDE.md** | nhưng phải thật ngắn |

Câu hỏi thiết kế đúng là: ***tôi có muốn model tự dùng thứ này không?***
Có → skill với description tốt. Không (thao tác có hệ quả) → `disable-model-invocation: true`.
