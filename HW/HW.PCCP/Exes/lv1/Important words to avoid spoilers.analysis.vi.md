# Phân tích: Important words to avoid spoilers (Từ quan trọng trong tin nhắn chống spoiler)

> File giải: `HW.PCCP/Solutions/Important words to avoid spoilers.cs`
> Chạy thử: `dotnet run --project HW.PCCP/HW.PCCP.csproj`

---

## 1. Đọc hiểu đề bài

### 1.1. Bối cảnh

KakaoTalk có tính năng **chống spoiler**: một đoạn tin nhắn bị che đi, bấm vào thì mới hiện ra.
Muzi gửi cho bạn một tin nhắn có nhiều đoạn bị che. Bạn bấm **lần lượt từng đoạn che, từ trái sang phải**,
và muốn đếm xem có bao nhiêu **từ quan trọng** được lộ ra.

### 1.2. Dữ liệu vào

| Tham số | Kiểu | Ý nghĩa |
|---|---|---|
| `message` | `string` | Tin nhắn, gồm chữ thường + chữ số + dấu cách. Dài 1..20.000 |
| `spoiler_ranges` | `int[,]` | Mảng 2 chiều, mỗi dòng là `[start, end]` — chỉ số ký tự bị che, **bao gồm cả hai đầu**. Có 1..1.000 dòng |

Các ràng buộc quan trọng (chính là chìa khoá để thuật toán đơn giản):

- Dấu cách **không xuất hiện liên tiếp** → không có từ rỗng.
- Các vùng che **không giao nhau** và đã **sắp xếp tăng dần theo `start`**.

### 1.3. Định nghĩa "từ"

Từ = chuỗi liên tiếp các ký tự **không phải dấu cách**. Nói cách khác: tách `message` theo dấu cách.

### 1.4. Định nghĩa "từ bị che"

> Chỉ cần **một** ký tự của từ nằm trong vùng che → **cả từ** bị coi là bị che.

Đây là điểm dễ sai nhất. Ví dụ `"my phone ..."` với vùng che `[5, 5]`:
chỉ ký tự `n` (chỉ số 5) bị che, nhưng cả từ `phone` được coi là từ bị che.

Hệ quả có hai chiều:

- Một từ có thể trải dài qua **nhiều** vùng che.
- Một vùng che có thể chứa **nhiều** từ.

### 1.5. Định nghĩa "từ quan trọng"

Khi bấm mở và từ đó **lộ hoàn toàn** (mọi ký tự của nó đều đã hiện), từ đó là **quan trọng** nếu thoả **cả 3** điều:

1. Nó phải là **từ bị che**.
2. Nó **chưa từng xuất hiện ở vùng không bị che** — tức là toàn bộ phần tin nhắn nằm ngoài mọi vùng che
   (đầu, giữa, cuối đều tính).
3. Nó **không trùng** với một từ che nào đã được lộ **trước đó**.

Nếu nhiều từ lộ cùng lúc → xét lần lượt **từ trái sang phải**.

### 1.6. Cần trả về

Số lượng **từ quan trọng**.

---

## 2. Hai điểm mấu chốt cần suy luận thêm

### 2.1. Khi nào một từ "lộ hoàn toàn"?

Không phải cứ bấm vào vùng che chạm vào từ là từ hiện ra. Từ chỉ hiện **khi vùng che CUỐI CÙNG chạm vào nó
được bấm**.

Vì các vùng che không giao nhau và đã sắp xếp theo `start`, còn một từ là một đoạn liên tục,
nên các vùng che chạm vào một từ luôn là **một dãy chỉ số liên tiếp** `r, r+1, ..., k`.
Ta bấm theo thứ tự `0, 1, 2, ...` nên từ đó lộ đúng ở **bước `k` = chỉ số vùng lớn nhất chạm vào từ**.

> **Kết luận:** với mỗi từ, chỉ cần tính `max` chỉ số vùng che phủ lên các ký tự của nó.
> Giá trị đó vừa cho biết từ có bị che hay không (`-1` = không che), vừa cho biết nó lộ ở bước nào.

### 2.2. "Xuất hiện ở vùng không che" nghĩa là gì?

Một từ bị che **một phần** thì phần còn lại vẫn nhìn thấy được. Vậy phần nhìn thấy đó có tính là
"từ đã xuất hiện ở vùng không che" không?

**Không.** Phần nhìn thấy chỉ là một **mảnh** của từ, không phải một từ (từ được định nghĩa theo dấu cách).
Chỉ những từ **nằm trọn** ngoài mọi vùng che mới được tính là "đã xuất hiện ở vùng không che".

Cách hiểu này chính là thứ tạo ra kết quả đúng ở ví dụ #2 (xem mục 3.2): từ `number` cuối câu bị loại
**không phải** vì mảnh `ber` lộ ra, mà vì có một từ `number` khác nằm hoàn toàn ngoài vùng che ở đầu câu.

---

## 3. Chạy tay hai ví dụ

### 3.1. Ví dụ #1 → kết quả `1`

```
message = "here is muzi here is a secret message"
spoiler_ranges = [[0, 3], [23, 28]]

 chỉ số: 0123 4 56 7 8901 2 3456 7 89 0 1 2 345678 9 012345
         here _ is _ muzi _ here _ is _ a _ secret _ message
         ^^^^                                ^^^^^^
         vùng 0                              vùng 1
```

| Từ | Vị trí | `max` vùng che | Phân loại |
|---|---|---|---|
| `here` | 0–3 | 0 | bị che, lộ ở bước 0 |
| `is` | 5–6 | -1 | **từ sạch** |
| `muzi` | 8–11 | -1 | **từ sạch** |
| `here` | 13–16 | -1 | **từ sạch** |
| `is` | 18–19 | -1 | **từ sạch** |
| `a` | 21 | -1 | **từ sạch** |
| `secret` | 23–28 | 1 | bị che, lộ ở bước 1 |
| `message` | 30–36 | -1 | **từ sạch** |

`clearWords = { is, muzi, here, a, message }`

- **Bước 0** — lộ `here`. Nhưng `here` có trong `clearWords` (từ ở vị trí 13) → **không** quan trọng.
- **Bước 1** — lộ `secret`. Không có trong `clearWords`, chưa lộ lần nào → **quan trọng** ✅

→ Đáp án **1**.

### 3.2. Ví dụ #2 → kết quả `4`

```
message = "my phone number is 01012345678 and may i have your phone number"
spoiler_ranges = [[5, 5], [25, 28], [34, 40], [53, 59]]
```

Bảng chỉ số các từ:

| Từ | Vị trí |
|---|---|
| `my` | 0–1 |
| `phone` | 3–7 |
| `number` | 9–14 |
| `is` | 16–17 |
| `01012345678` | 19–29 |
| `and` | 31–33 |
| `may` | 35–37 |
| `i` | 39 |
| `have` | 41–44 |
| `your` | 46–49 |
| `phone` | 51–55 |
| `number` | 57–62 |

Đối chiếu với các vùng che:

| Vùng | Khoảng | Chạm vào từ nào |
|---|---|---|
| 0 | 5–5 | `phone` (3–7) — **chỉ 1 ký tự `n`** |
| 1 | 25–28 | `01012345678` (19–29) — che giữa |
| 2 | 34–40 | dấu cách 34, `may` (35–37), dấu cách 38, `i` (39), dấu cách 40 → **2 từ trong 1 vùng** |
| 3 | 53–59 | `phone` (51–55) ở các ký tự 53–55, và `number` (57–62) ở các ký tự 57–59 → **1 vùng chạm 2 từ, mỗi từ đều bị che một phần** |

`clearWords = { my, number, is, and, have, your }`
(chú ý: `number` ở 9–14 nằm trọn ngoài vùng che → là từ sạch)

Diễn biến khi bấm:

| Bước | Từ lộ ra | Đã lộ trước? | Có trong `clearWords`? | Quan trọng? |
|---|---|---|---|---|
| 0 | `phone` | không | không | ✅ (1) |
| 1 | `01012345678` | không | không | ✅ (2) |
| 2 | `may` | không | không | ✅ (3) |
| 2 | `i` | không | không | ✅ (4) |
| 3 | `phone` | **có** (bước 0) | không | ❌ trùng lặp |
| 3 | `number` | không | **có** | ❌ đã lộ ở vùng không che |

→ Đáp án **4**.

---

## 4. Thuật toán

```
B1. Tạo mảng coverBy[] cùng độ dài message, khởi tạo -1.
    Với mỗi vùng che r: gán coverBy[i] = r cho mọi i trong [start, end].

B2. Duyệt message, tách từng từ theo dấu cách. Với mỗi từ, tính
    lastRange = max(coverBy[i]) trên các ký tự của từ.
      - lastRange == -1  -> thêm vào clearWords (tập từ sạch).
      - lastRange >= 0   -> thêm vào danh sách revealedAt[lastRange].

B3. Duyệt r = 0..rangeCount-1 (bấm lần lượt từ trái sang phải):
      với mỗi từ trong revealedAt[r] (đã sẵn thứ tự trái sang phải):
        firstReveal = alreadyRevealed.Add(word)   // true nếu lần đầu lộ
        nếu firstReveal && từ không có trong clearWords:
            answer++

B4. Trả về answer.
```

**Vì sao `revealedAt[r]` đã đúng thứ tự trái sang phải?**
Vì ở B2 ta duyệt các từ theo thứ tự xuất hiện trong `message`, nên khi thêm vào danh sách chúng
tự động giữ đúng thứ tự.

**Vì sao vẫn phải `Add` vào `alreadyRevealed` ngay cả khi từ không quan trọng?**
Vì điều kiện "không trùng với từ che đã lộ trước đó" xét trên **mọi từ che đã lộ**, không chỉ từ quan trọng.
(Thực tế ở đây kết quả không đổi — một từ trùng `clearWords` thì lần sau cũng vẫn bị loại vì lý do đó —
nhưng viết đúng theo đề để tránh bẫy.)

### Độ phức tạp

- Thời gian: `O(|message| + Σ độ dài các vùng che)` ≤ `O(20.000 + 20.000)`.
  (Tổng độ dài các vùng che không vượt quá độ dài `message` vì chúng không giao nhau.)
- Bộ nhớ: `O(|message|)` cho `coverBy` + các tập từ.

Thoải mái so với giới hạn của đề.

---

## 5. Những lỗi trong code cũ

```csharp
// Code cũ
for (int i = 0; i < spoiler_ranges.Length; i++)
{
    var message_change = message.Remove(spoiler_ranges[i, 0], spoiler_ranges[i, 1]);
    if (message_change.Split(" ").Any(x => x == message.Substring(spoiler_ranges[i, 0], spoiler_ranges[i, 1])))
        answer++;
}
```

| Lỗi | Giải thích | Sửa |
|---|---|---|
| `spoiler_ranges.Length` | Với mảng 2 chiều, `Length` là **tổng số phần tử** (số dòng × 2), không phải số dòng → lặp thừa và văng `IndexOutOfRange` | `spoiler_ranges.GetLength(0)` |
| `Substring(start, end)` | Tham số thứ 2 là **độ dài**, không phải chỉ số kết thúc. Vùng `[23, 28]` phải là độ dài `28 - 23 + 1 = 6` | `end - start + 1` |
| `Remove(start, end)` | Cùng lỗi trên | `end - start + 1` |
| `static solution` | `Program.cs` gọi qua instance (`new Solution().solution(...)`) → lỗi biên dịch CS0176 | bỏ `static` |
| **Sai logic gốc** | Coi nguyên chuỗi con của một vùng che là **một từ duy nhất**. Do đó không xử lý được: từ bị che một phần, nhiều từ trong một vùng, một từ trải qua nhiều vùng, và thứ tự bấm/chống trùng lặp | viết lại theo thuật toán mục 4 |

---

## 6. Bẫy cần nhớ khi làm lại bài này

1. **Che một phần vẫn là che** — đừng chỉ lấy chuỗi con trong `[start, end]`.
2. **Từ lộ ở vùng che cuối cùng chạm vào nó**, không phải vùng đầu tiên.
3. **Chỉ số `end` là bao gồm** — mọi `Substring`/`Remove` phải `+1`.
4. **Từ sạch = nằm trọn ngoài mọi vùng che**; mảnh nhìn thấy được của từ bị che không tính.
5. **Chống trùng lặp xét theo mọi từ che đã lộ**, và nhiều từ lộ cùng lúc thì xét trái → phải.
