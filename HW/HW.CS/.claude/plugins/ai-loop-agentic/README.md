# Plugin `ai-loop-agentic`

Bản đóng gói của template này để **dùng lại giữa nhiều repo**.

## Cấu trúc (điểm hay sai nhất)

> **Chỉ `plugin.json` nằm trong `.claude-plugin/`.** Mọi thư mục khác nằm ở **gốc plugin**.

```
ai-loop-agentic/
├── .claude-plugin/plugin.json   # name, description, version, author — CHỈ file này ở đây
├── skills/<tên>/SKILL.md
├── agents/*.md
├── commands/*.md
├── hooks/hooks.json             # ⚠️ hook của plugin ở ĐÂY, không ở settings.json
├── hooks/*.ps1
└── .mcp.json.example            # đổi tên thành .mcp.json nếu plugin kèm MCP server
```

## Hai biến bắt buộc dùng

| Biến | Vì sao |
|---|---|
| `${CLAUDE_PLUGIN_ROOT}` | đường dẫn cài đặt **khác nhau trên từng máy** — hard-code là hỏng |
| `${CLAUDE_PLUGIN_DATA}` | nơi ghi dữ liệu riêng của plugin |

Đây là lỗi "chạy trên máy tôi" phổ biến nhất khi đóng gói plugin.

## Vòng đời phát triển

```bash
claude --plugin-dir ./.claude/plugins/ai-loop-agentic   # thử tại chỗ, lặp nhiều lần được
/reload-plugins                                          # nạp lại, không cần restart
claude plugin validate ./.claude/plugins/ai-loop-agentic # kiểm tra trước khi phát hành
```

Phân phối qua **marketplace** — một repo có `marketplace.json` (xem `marketplace.example.json`
bên cạnh). Dùng repo private cho plugin nội bộ. `version` trong `plugin.json` quyết định khi nào
người dùng nhận bản cập nhật.

## Standalone `.claude/` hay plugin? (HR-15)

| | `.claude/` standalone | Plugin |
|---|---|---|
| Tên skill | `/loop-agentic` | `/ai-loop-agentic:loop-agentic` |
| Phân phối | copy tay / commit repo | marketplace, có version |
| Hợp với | thử nghiệm, đặc thù một repo | chia sẻ giữa nhiều repo/team, phát hành có kiểm soát |

⚠️ **Luật ưu tiên khi trùng tên:**
- **Agent**: định nghĩa ở project/user `.claude/agents/` **đè** agent cùng tên của plugin.
- **Skill**: **không đè nhau** — plugin skill có namespace, nên cả `/loop-agentic` và
  `/ai-loop-agentic:loop-agentic` cùng tồn tại. **Sau khi chuyển hẳn sang plugin, phải xoá bản
  trong `.claude/skills/`** nếu không muốn có hai bản song song.
- Plugin nạp bằng `--plugin-dir` **thắng** bản cùng tên đã cài.

## Trạng thái hiện tại của thư mục này

Các file ở đây là **bản sao** của `.claude/skills`, `.claude/agents`, `.claude/commands`,
`.claude/hooks`. Đó là chủ ý: template chạy được ngay ở dạng standalone, và thư mục này là điểm
xuất phát khi bạn muốn phát hành.

**Khi đã quyết định dùng plugin:** xoá bản standalone tương ứng để không bảo trì hai nơi.

---

## ⚠️ Một ràng buộc đã biết khi phát hành dạng plugin

Các file trong `commands/` gọi verifier bằng đường dẫn **tương đối của layout standalone**:

```markdown
!`powershell -NoProfile -File .claude/functions/Verify.ps1 -Level build`
```

Khi plugin được cài vào một repo **không có** `.claude/functions/`, khối `` !`…` `` đó sẽ lỗi
(lệnh chạy, không tìm thấy file — command vẫn hoạt động nhưng mất phần dữ liệu thật ở đầu).

Chọn **một** trong hai cách xử lý:

| Cách | Làm gì | Hợp với |
|---|---|---|
| **A. Giữ `functions/` ở mỗi repo** | copy `functions/` vào `.claude/` của repo đích | mỗi repo có verifier riêng (lệnh build khác nhau) — **thường là đúng** |
| **B. Trỏ vào plugin** | sửa các command thành `${CLAUDE_PLUGIN_ROOT}/functions/Verify.ps1` | mọi repo dùng chung một verifier |

Bản sao `functions/` trong thư mục này là để phục vụ cách **B**.

Lý do không tự động chọn hộ: `LOOP_BUILD_CMD` / `LOOP_TEST_CMD` là **thuộc tính của repo**, không
phải của plugin. Một plugin ép mọi repo dùng chung một lệnh build là plugin sai.

## Hook nào **không** đóng gói vào plugin — và vì sao

`hooks.json` của plugin cố ý **chỉ** đăng ký 4 hook: chặn lệnh nguy hiểm, chặn đọc secret, format
sau khi sửa, phát hiện thrashing. Ba hook còn lại nằm ngoài:

| Hook | Vì sao không đóng gói |
|---|---|
| `stop-quality-gate` | chạy build+test ở **mỗi** lần kết thúc lượt — quá chậm để bật mặc định cho repo lạ; phải là lựa chọn có ý thức |
| `session-start-context` | nội dung phụ thuộc quy ước từng repo (task file, nhánh) |
| `audit-log` | ghi log là **chính sách của tổ chức**, không phải mặc định của một plugin |

Nguyên tắc: **plugin cài đặt ranh giới an toàn (ai cũng cần), không cài đặt chính sách vận hành
(mỗi nơi một khác).**
