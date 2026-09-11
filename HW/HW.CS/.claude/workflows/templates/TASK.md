# Task hiện tại

> Chép file này thành `.claude/state/CURRENT_TASK.md` và điền.
> Hook `SessionStart` sẽ tự nạp nó vào context ở mỗi phiên — nghĩa là bạn **không phải mô tả lại
> nhiệm vụ** mỗi lần mở Claude Code, và một subagent khởi động lạnh cũng đọc được nó.

---

## Mục tiêu

<Một câu. Nếu không viết được thành một câu, task đang quá to — hãy chẻ nhỏ.>

## Định nghĩa "xong" (bắt buộc — phải kiểm chứng được)

- [ ] <điều kiện 1> — kiểm chứng bằng: `<lệnh>`
- [ ] <điều kiện 2> — kiểm chứng bằng: `<lệnh>`

> Không có lệnh kiểm chứng → agent sẽ tự quyết định khi nào nó "xong", và nó sẽ quyết định sớm
> (AG-8, stopping short).

## Phạm vi

**Được sửa:**
- `path/…`

**KHÔNG được sửa:**
- `path/…`

## Bối cảnh đã biết

- <thứ đã thử / đã biết, để agent không đi lại đường cũ>

## Ngõ cụt đã biết

- <cái đã kiểm tra và loại trừ — phần hay bị bỏ nhất và tốn kém nhất, MA-6 phần 6>

## Ràng buộc

- <không đổi API công khai / không thêm dependency / phải giữ tương thích ngược / …>

## Ngân sách

| | |
|---|---|
| Trần số lượt | 25 |
| Cần người duyệt trước khi | <migration / push / gọi API ngoài> |
