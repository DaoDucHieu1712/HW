---
name: mockup
description: Dựng mockup/wireframe giao diện — màn hình, luồng màn hình, hoặc prototype tương tác để chốt yêu cầu trước khi code. Dùng khi người dùng nói "mockup", "wireframe", "phác thảo giao diện", "UI", "màn hình", "prototype", "demo giao diện", hoặc khi đặc tả UI còn mơ hồ và cần thứ nhìn được để bàn.
argument-hint: "[tên màn hình]"
---

# Mockup / Wireframe

Mục đích của mockup **không phải là đẹp** — mà là **rút ngắn vòng phản hồi về yêu cầu**. Một
màn hình nhìn được giải quyết được nhiều tranh cãi hơn ba trang đặc tả.

## Chọn độ trung thực trước khi vẽ

| Độ trung thực | Dạng | Dùng khi | Chi phí |
|---|---|---|---|
| **Thấp** | ASCII/khối trong Markdown | đang chốt **bố cục và luồng** | phút |
| **Trung bình** | HTML tĩnh một file | cần thấy **thứ bậc thông tin, trạng thái thật** | ~30 phút |
| **Cao** | HTML + tương tác (form, tab, bảng) | cần **bấm thử** để phát hiện lỗ hổng luồng | giờ |

**Bắt đầu ở mức thấp nhất trả lời được câu hỏi đang tranh cãi.** Nhảy thẳng lên độ trung thực
cao khi bố cục còn chưa chốt là lãng phí — mọi góp ý sẽ nhắm vào màu sắc thay vì vào luồng.

## Wireframe độ thấp — làm ngay trong câu trả lời

```
┌─ Danh sách Blog ───────────────────────────── [+ Tạo mới] ─┐
│ [ Tìm kiếm............ ]  Trạng thái ▾   Sắp xếp ▾         │
├────────────────────────────────────────────────────────────┤
│ ▸ Tiêu đề bài viết A          Nháp      12/09   [Sửa][Xoá] │
│ ▸ Tiêu đề bài viết B          Đã đăng   10/09   [Sửa][Xoá] │
├────────────────────────────────────────────────────────────┤
│                                    ◀ 1 2 3 ▶   20/trang    │
└────────────────────────────────────────────────────────────┘
```

## Bốn trạng thái phải vẽ (chỗ hay bị bỏ nhất)

Mockup chỉ vẽ trạng thái "có dữ liệu, mọi thứ ổn" là mockup **giấu đi phần khó nhất**:

1. **Rỗng** — chưa có dữ liệu: nói gì, mời làm gì tiếp?
2. **Đang tải** — skeleton hay spinner? khoá thao tác nào?
3. **Lỗi** — thông điệp gì, người dùng làm gì để thoát?
4. **Nhiều dữ liệu** — 10.000 dòng thì bố cục này còn dùng được không?

## Nếu dựng HTML

- Một file, không build step, không dependency ngoài — mockup phải mở được bằng double-click.
- Dùng **dữ liệu giả trông thật** (tên tiếng Việt, ngày thật, số tiền thật). Dữ liệu `foo`/`bar`
  che mất vấn đề tràn chữ và định dạng.
- Ghi ra `docs/mockups/<slug>.html`.
- ⚠️ Ghi rõ trong file rằng **đây là mockup** — đừng để nó bị nhầm là sản phẩm thật.

## Sau khi có mockup

Mockup là **đầu vào cho SRS**, không phải sản phẩm cuối. Chốt xong, chuyển các quyết định về
luồng và trạng thái thành acceptance criteria bằng skill `spec-srs`.
