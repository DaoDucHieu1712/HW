---
description: Dựng mockup/wireframe màn hình để chốt yêu cầu UI trước khi code
argument-hint: "[tên màn hình]"
---

## Màn hình

$ARGUMENTS

## Nhiệm vụ

Theo skill `mockup`.

**Chọn độ trung thực trước khi vẽ** — bắt đầu ở mức thấp nhất trả lời được câu hỏi đang tranh cãi:

| Mức | Dạng | Khi nào |
|---|---|---|
| Thấp | ASCII/khối trong Markdown | đang chốt **bố cục và luồng** |
| Trung bình | HTML tĩnh một file | cần thấy **thứ bậc thông tin** |
| Cao | HTML + tương tác | cần **bấm thử** để tìm lỗ hổng luồng |

Nhảy thẳng lên độ trung thực cao khi bố cục chưa chốt là lãng phí — mọi góp ý sẽ nhắm vào màu
sắc thay vì vào luồng.

**Bốn trạng thái bắt buộc phải vẽ** (chỗ hay bị bỏ nhất): rỗng · đang tải · lỗi · nhiều dữ liệu.
Mockup chỉ vẽ trạng thái "có dữ liệu, mọi thứ ổn" là mockup giấu đi phần khó nhất.

Nếu dựng HTML: một file, không build step, mở được bằng double-click; **dữ liệu giả trông thật**
(tên tiếng Việt, ngày thật, số tiền thật) — `foo`/`bar` che mất vấn đề tràn chữ.
Ghi ra `docs/mockups/<slug>.html` và ghi rõ trong file rằng **đây là mockup**.

Chốt xong → chuyển quyết định về luồng và trạng thái thành acceptance criteria bằng `/srs`.
