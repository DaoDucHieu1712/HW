# MCP — cấu hình, chi phí, và khi nào **không** dùng

> ⚠️ File `.mcp.json` thật phải nằm ở **gốc repo**, không nằm trong `.claude/`.
> Chép `mcp.example.json` ra `<gốc repo>/.mcp.json` và sửa.

## Scope và thứ tự ưu tiên (MCP-7)

| Scope | Lưu ở đâu | Ai thấy | Dùng cho |
|---|---|---|---|
| **local** (mặc định) | `~/.claude.json`, theo từng project | chỉ bạn | server thử nghiệm, cấu hình cá nhân |
| **project** | **`.mcp.json`** ở gốc repo | cả team (commit vào git) | server dùng chung của dự án |
| **user** | `~/.claude.json` toàn cục | bạn, ở mọi project | tiện ích cá nhân dùng khắp nơi |

**Ưu tiên:** `local` > `project` > `user` > `plugin` > connector.

```bash
claude mcp add --transport http shared --scope project https://.../mcp   # → .mcp.json
claude mcp list          # trạng thái sức khoẻ từng server
/mcp                     # trong phiên: xem trạng thái, đăng nhập, reconnect
```

⚠️ **Workspace trust:** server khai báo trong `.mcp.json` (đến từ repo) **cần người dùng tin
tưởng thư mục** trước khi chạy. Đây là rào chắn chống việc `git clone` một repo lạ rồi bị nó tự
khởi động một tiến trình. Rào này **bị bỏ qua** trong `claude -p`, Agent SDK và cloud session —
nên ở CI phải cân nhắc `--strict-mcp-config`.

## Transport

| | **stdio** | **streamable HTTP** |
|---|---|---|
| Chạy ở đâu | tiến trình con trên máy bạn | dịch vụ từ xa |
| Hợp với | file cục bộ, CLI, dữ liệu nhạy cảm không rời máy | server dùng chung, có OAuth, nhiều người dùng |
| Xác thực | kế thừa môi trường cục bộ | OAuth / header |

⚠️ **SSE đã lỗi thời** — dùng streamable HTTP. Biết điều này (và vì sao) là một câu trả lời tốt.

## Namespace: `mcp__<server>__<tool>` (MCP-8)

Tiền tố tồn tại vì hai lý do, lý do thứ hai quan trọng hơn:

1. Hai server đều có tool tên `search` ⇒ model không biết gọi cái nào.
2. **Tool shadowing** — một server độc hại cố ý đặt trùng tên tool của server đáng tin để **chiếm
   quyền gọi**. Namespace làm việc này bất khả thi.

Và nó làm **permission rule viết được chính xác**:

```jsonc
{ "permissions": {
    "allow": ["mcp__docs__search"],        // chỉ cho phép đúng 1 tool
    "deny":  ["mcp__db__execute_write"]    // chặn cứng tool ghi
}}
```

## ⚠️ Chi phí context — chỗ MCP hay "âm thầm phá" agent (MCP-9)

**Schema của mọi tool MCP được nạp vào context ở mỗi lượt.** Ba server, mỗi cái 15 tool, là 45
schema bạn **trả tiền cho mỗi lời gọi** — kể cả những lượt không dùng tool nào.

Triệu chứng: `/context` cho thấy phần MCP chiếm chỗ lớn; chất lượng giảm dần vì nhiễu; model bắt
đầu gọi nhầm tool.

Cách chữa, theo thứ tự:
1. **Gỡ server không dùng.** Rẻ nhất, hiệu quả nhất.
2. **`defer_loading` + tool search** cho bộ tool lớn.
3. **Gộp tool** — 5 tool rõ ràng tốt hơn 20 tool chồng lấn.

## Khi nào **không** nên dùng MCP (MCP-12)

| Tình huống | Dùng gì thay thế |
|---|---|
| Chỉ cần cung cấp **kiến thức** (không có hành động) | **Skill** — MCP tốn schema tool vô ích |
| Chỉ dùng trong một repo, một lần | script + `Bash`, hoặc slash command với `` !`cmd` `` |
| Việc phải **chắc chắn** xảy ra | **Hook** — MCP tool là xác suất, model có thể không gọi |
| Cần chặn/kiểm duyệt trước khi chạy | Hook `PreToolUse` bọc quanh nó |

> Đây là lỗi ánh xạ phổ biến nhất: **dựng MCP server chỉ để cấp kiến thức.** Kiến thức là việc
> của skill; MCP là **năng lực** (đọc/ghi hệ thống ngoài, có auth, dùng lại được giữa các host).

## Sáu rủi ro bảo mật phải kể được tên (MCP-11)

1. **Prompt injection qua nội dung tool trả về** — server trả về text chứa lệnh; model đọc nó như
   chỉ thị. Ranh giới thật nằm ở **quyền của tool**, không ở prompt.
2. **Tool shadowing** — server độc đặt trùng tên tool đáng tin.
3. **Token passthrough** — chuyển thẳng token người dùng xuống dịch vụ dưới, mất mọi kiểm soát
   phạm vi. **Anti-pattern.**
4. **Quyền quá rộng** — server có quyền ghi trong khi chỉ cần đọc.
5. **Server độc hại / chuỗi cung ứng** — `npx @ai-nao-do/mcp` là **code thực thi**.
6. **Rò rỉ dữ liệu qua tham số tool** — dữ liệu nhạy cảm đi ra ngoài trong tham số của lời gọi.

**Giảm thiểu trong template này:** `permissions.deny` cho mọi tool ghi; `permissions.allow` liệt
kê **đích danh** từng tool chỉ-đọc, không dùng wildcard cho cả server.

## Debug (MCP-16) — đi từ dưới lên

```
1. Server chạy tay được không?   → chạy đúng lệnh trong .mcp.json ở terminal
2. /mcp                          → đã kết nối chưa? cần đăng nhập không?
3. /context                      → tool của nó có trong context không? tốn bao nhiêu?
4. claude --debug                → log JSON-RPC: initialize có thành công không?
5. Thư mục đã được trust chưa?   → server từ .mcp.json cần workspace trust
```
