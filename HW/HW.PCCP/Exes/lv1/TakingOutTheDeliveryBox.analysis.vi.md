# Phân tích: Taking Out The Delivery Box (택배 상자 꺼내기 — Lấy thùng hàng ra)

> Đề bài: `HW.PCCP/Exes/lv1/TakingOutTheDeliveryBox.md`
> File giải: `HW.PCCP/Solutions/TakingOutTheDeliveryBox.cs`
> Chạy thử: `dotnet run --project HW.PCCP/HW.PCCP.csproj`

> ⚠️ File đề gốc bị lỗi OCR/dịch máy nặng (`1 ~ n` thành `1 ~`, `n`/`w`/`num` bị dính vào
> cuối câu như `.n`, `.w`, `.wwnw`, `.nwnum`). Mục 1 dưới đây là bản khôi phục lại nghĩa.

---

## 1. Đọc hiểu đề bài

### 1.1. Bối cảnh

Trong kho có `n` thùng hàng đánh số `1 → n`. Bạn xếp chúng vào một cái giá theo kiểu
**rắn bò (serpentine / boustrophedon)** — hết một tầng thì đảo chiều:

- **Tầng 1**: đi từ **trái sang phải**, đặt thùng `1, 2, …, w`
- **Tầng 2**: về đầu bên **phải**, đi từ **phải sang trái**, đặt `w+1, …, 2w`
- **Tầng 3**: lại từ **trái sang phải**
- … cứ thế cho đến khi hết `n` thùng. Tầng trên cùng có thể **thiếu**.

### 1.2. Dữ liệu vào

| Tham số | Kiểu | Ý nghĩa |
|---|---|---|
| `n` | `int` | Tổng số thùng hàng |
| `w` | `int` | Số thùng xếp được trên **một tầng** (chiều ngang) |
| `num` | `int` | Số hiệu thùng khách muốn lấy |

Ràng buộc:

- `2 ≤ n ≤ 100`
- `1 ≤ w ≤ 10`
- `1 ≤ num ≤ n`

### 1.3. Luật lấy hàng

> Muốn lấy thùng `A`, phải **dỡ hết mọi thùng nằm phía trên `A`** (cùng cột, tầng cao hơn)
> rồi mới lấy được `A`.

### 1.4. Cần trả về

**Tổng số thùng phải dỡ, tính cả `num`.**

Chữ "tính cả `num`" là lý do đáp án luôn `≥ 1`, không bao giờ bằng 0.

---

## 2. Dựng lại hình vẽ (file `ex1-1.png` / `ex2-1.png` không có trong repo)

### 2.1. Ví dụ #1 — `n = 22, w = 6`

```
             cột1  cột2  cột3  cột4  cột5  cột6
   tầng 4 :    ·     ·    22    21   [20]   19      ← phải→trái, chỉ có 4 thùng
   tầng 3 :   13    14    15    16   [17]   18      ← trái→phải
   tầng 2 :   12    11    10     9   [ 8]    7      ← phải→trái
   tầng 1 :    1     2     3     4     5     6      ← trái→phải
```

`num = 8` nằm ở **tầng 2, cột 5**. Phía trên nó trong **cột 5** có `17` (tầng 3) và
`20` (tầng 4). → dỡ `20`, `17`, rồi lấy `8` = **3**. ✓

### 2.2. Ví dụ #2 — `n = 13, w = 3`

```
             cột1  cột2  cột3
   tầng 5 :  [13]    ·     ·         ← trái→phải, chỉ có 1 thùng
   tầng 4 :  [12]   11    10         ← phải→trái
   tầng 3 :  [ 7]    8     9         ← trái→phải
   tầng 2 :  [ 6]    5     4         ← phải→trái
   tầng 1 :     1    2     3         ← trái→phải
```

`num = 6` nằm ở **tầng 2, cột 1**. Phía trên: `7`, `12`, `13`. → **4**. ✓

---

## 3. Mô hình hoá

Toàn bộ bài toán rút gọn về **hai phép đổi toạ độ** giữa "số hiệu thùng" và "(tầng, cột)".

### 3.1. Từ `(tầng k, cột c)` → số hiệu

Tầng `k` bắt đầu từ số `(k-1)*w + 1`. Vị trí của cột `c` trong tầng đó phụ thuộc chiều đi:

```
boxAt(k, c) = (k-1)*w + ( k lẻ ?  c  :  w + 1 - c )
```

- `k` **lẻ** → trái→phải → cột 1 là thùng đầu tiên của tầng.
- `k` **chẵn** → phải→trái → cột `w` là thùng đầu tiên, cột 1 là thùng cuối cùng.

Thùng đó **thực sự tồn tại** khi và chỉ khi `boxAt(k, c) ≤ n`.

### 3.2. Từ số hiệu → `(tầng, cột)`

```
k = (num - 1) / w + 1            // tầng, đếm từ 1
p = (num - 1) % w                // vị trí thứ p (đếm từ 0) theo THỨ TỰ ĐẶT của tầng đó
c = k lẻ ? p + 1 : w - p         // đổi sang cột vật lý
```

Kiểm chứng với `num = 8, w = 6`: `k = 7/6 + 1 = 2` (chẵn), `p = 7 % 6 = 1`,
`c = 6 - 1 = 5`. ✓ Đúng như hình.

### 3.3. Đáp án

Gọi `F = ⌈n / w⌉` là tầng cao nhất. Đáp án là **số tầng từ `k` lên tới `F` mà cột `c`
có thùng**:

```
answer = |{ t ∈ [k, F] : boxAt(t, c) ≤ n }|
```

---

## 4. Điểm mấu chốt: hai cái bẫy làm sai bài

Đây là chỗ phân biệt "làm được" và "hiểu bài". Cả hai bẫy đều **không** lộ ra ở ví dụ mẫu
nếu code sai theo đúng kiểu — nên phải chủ động nghĩ ra.

### 4.1. Bẫy #1 — "vị trí thứ p" **không phải** là "cột c"

Rất dễ viết:

```csharp
int p = (num - 1) % w;        // rồi dùng thẳng p + 1 làm cột   ← SAI
```

Tầng chẵn đặt **ngược**, nên thùng thứ `p` (theo thứ tự đặt) của tầng chẵn nằm ở cột
`w - p`, chứ không phải `p + 1`. Bỏ qua phép lật này thì ví dụ #1 hoá thành "cột 2":

```
cột 2 có: 2 (tầng1), 11 (tầng2), 14 (tầng3), · (tầng4 khuyết — chỉ chiếm cột 3..6)
đếm từ tầng 2 lên  →  11, 14  →  trả về 2   ✗  (đúng phải là 3)
```

Tin tốt: **cả hai ví dụ mẫu đều bắt được lỗi này** (ví dụ #2 trả `3` thay vì `4`).
Đây là loại lỗi tự lộ ra ngay khi chạy thử.

### 4.2. Bẫy #2 — quên rằng tầng trên cùng có thể khuyết ← **bẫy thật sự**

Sau khi có `floor` và `topFloor`, cái công thức trông hiển nhiên nhất là:

```csharp
return topFloor - floor + 1;      // "đếm số tầng từ num lên đỉnh"   ← SAI
```

Nó **chạy đúng cả hai ví dụ mẫu**:

| Ví dụ | `topFloor - floor + 1` | Đáp án đúng |
|---|---|---|
| #1 (`22, 6, 8`) | `4 - 2 + 1 = 3` | 3 ✓ |
| #2 (`13, 3, 6`) | `5 - 2 + 1 = 4` | 4 ✓ |

Lý do: trong cả hai ví dụ, cột chứa `num` tình cờ **có thùng ở tận tầng trên cùng**
(`20` ở ví dụ #1, `13` ở ví dụ #2). Đề bài không hề đưa ví dụ nào mà cột đích bị khuyết ở đỉnh.

Case tự chế làm nó gãy:

```
n = 20, w = 6, num = 3
tầng 4 (chẵn, còn 2 thùng) xếp phải→trái  →  cột6 = 19, cột5 = 20, cột 1..4 TRỐNG
cột 3 chỉ có: 3, 10, 15        →  đáp án 3
công thức sai cho: 4 - 1 + 1 = 4      ✗
```

> **Bài học:** "chạy đúng 2 ví dụ mẫu" không chứng minh được gì. Bẫy nguy hiểm là bẫy
> mà ví dụ mẫu **không** chạm tới.

### 4.3. Bẫy #2b — tầng khuyết thì khuyết **từ phía nào?**

Ngay cả khi đã nhớ xử lý tầng khuyết, vẫn còn một tầng bẫy nữa. Tầng chẵn xếp
**phải→trái**, nên khi thiếu thùng thì **phía TRÁI** bị trống:

```
n = 22, w = 6, F = 4 (chẵn), còn 4 thùng
→ chiếm cột 3,4,5,6      (KHÔNG phải cột 1,2,3,4)
```

Người mới thường mặc định "hàng cuối lúc nào cũng lấp từ trái". Công thức
`boxAt(t, c) ≤ n` ở mục 3.1 **tự động** xử lý đúng chuyện này — đó là lý do nên
kiểm tra tồn tại bằng số hiệu thùng thay vì đếm cột bằng tay.

### 4.3. Vì sao ba nhóm test case lại chia như vậy

Bảng chấm điểm của đề chính là bản đồ của hai cái bẫy trên:

| Nhóm | Điểm | Ràng buộc thêm | Bẫy nào bị vô hiệu hoá |
|---|---|---|---|
| #1 | 10% | `w = 1` | Chỉ có 1 cột → cả hai bẫy biến mất. Đáp án luôn `n - num + 1` |
| #2 | 20% | `n` chia hết cho `w` | Không có tầng khuyết → bẫy 4.2 và 4.3 biến mất, đáp án **luôn** là `F - k + 1` |
| #3 | 70% | không | Cả ba bẫy đều sống |

> `return F - k + 1` (bẫy 4.2) **ăn trọn 30%** — qua sạch nhóm #1 và #2, qua luôn cả hai
> ví dụ mẫu, rồi trượt nhóm #3. Cấu trúc điểm của đề nói thẳng cho bạn biết:
> **toàn bộ 70% nằm ở việc xử lý đúng tầng trên cùng khuyết.**

---

## 5. Thuật toán

### 5.1. Cách chuẩn — leo cột, `O(n / w)`

Bám sát đúng nghĩa vật lý "dỡ từng thùng bên trên":

```
k = (num - 1) / w + 1
p = (num - 1) % w
c = (k lẻ) ? p + 1 : w - p

count = 0
for t = k to ceil(n / w):
    box = (t-1)*w + ( t lẻ ? c : w + 1 - c )
    if box <= n: count++

return count
```

Ba điều đáng chú ý:

- **Vòng lặp bắt đầu từ chính `t = k`** — vì đáp án tính cả `num`. Bắt đầu từ `k+1`
  là lỗi off-by-one kinh điển của bài này.
- **`boxAt` được tính lại từ đầu mỗi tầng**, không cộng dồn — tránh phải suy nghĩ về
  bước nhảy `2w - 2c + 1` / `2c - 1` xen kẽ ở mục 6.2.
- Điều kiện `box <= n` là **chỗ duy nhất** xử lý tầng khuyết. Không cần `if` riêng.

Độ phức tạp: thời gian `O(n / w) ≤ 100` vòng, bộ nhớ `O(1)`.

### 5.2. Bản C#

```csharp
public int Solution(int n, int w, int num)
{
    int floor  = (num - 1) / w + 1;
    int offset = (num - 1) % w;
    int col    = floor % 2 == 1 ? offset + 1 : w - offset;

    int topFloor = (n + w - 1) / w;          // ceil(n / w), tránh double
    int count    = 0;

    for (int t = floor; t <= topFloor; t++)
    {
        int box = (t - 1) * w + (t % 2 == 1 ? col : w + 1 - col);
        if (box <= n) count++;
    }

    return count;
}
```

### 5.3. Bản `O(1)` — chỉ tầng trên cùng mới có thể khuyết

Vì **duy nhất** tầng `F` có thể thiếu thùng, mọi tầng `k … F-1` chắc chắn đủ:

```csharp
int topFloor = (n + w - 1) / w;
int topBox   = (topFloor - 1) * w + (topFloor % 2 == 1 ? col : w + 1 - col);

return (topFloor - floor + 1) - (topBox > n ? 1 : 0);
```

Nhanh hơn nhưng khó đọc hơn, và với `n ≤ 100` thì vòng lặp ở 5.2 đã là tức thời.
Nên biết để trả lời khi được hỏi *"nếu `n` lên tới `10^18` thì sao?"*.

### 5.4. Bản mô phỏng — dựng hẳn lưới `F × w`

`n ≤ 100` nên hoàn toàn thoải mái dựng mảng 2 chiều rồi đọc kết quả:

```csharp
int topFloor = (n + w - 1) / w;
var grid = new int[topFloor + 1, w + 1];      // 0 = trống

int box = 1;
for (int t = 1; t <= topFloor && box <= n; t++)
    if (t % 2 == 1)
        for (int c = 1; c <= w && box <= n; c++) grid[t, c] = box++;
    else
        for (int c = w; c >= 1 && box <= n; c--) grid[t, c] = box++;

// tìm num, rồi đếm ngược lên
```

Chậm hơn nhưng **khó sai nhất**: bạn chỉ cần code đúng "cách xếp", không cần suy ra
công thức toạ độ nào. Trong phòng thi, nếu không chắc về mục 3.2 thì đây là lựa chọn an toàn.

---

## 6. Chạy tay hai ví dụ (theo thuật toán 5.1)

### 6.1. Ví dụ #1 — `n = 22, w = 6, num = 8` → `3`

`floor = 7/6 + 1 = 2`, `offset = 7 % 6 = 1`, tầng chẵn → `col = 6 - 1 = 5`.
`topFloor = ⌈22/6⌉ = 4`.

| `t` | chiều | `boxAt(t, 5)` | `≤ 22`? | `count` |
|---|---|---|---|---|
| 2 | phải→trái | `6 + (6+1-5) = 8` | ✓ | 1 |
| 3 | trái→phải | `12 + 5 = 17` | ✓ | 2 |
| 4 | phải→trái | `18 + 2 = 20` | ✓ | 3 |

→ **3** ✓

### 6.2. Ví dụ #2 — `n = 13, w = 3, num = 6` → `4`

`floor = 5/3 + 1 = 2`, `offset = 5 % 3 = 2`, tầng chẵn → `col = 3 - 2 = 1`.
`topFloor = ⌈13/3⌉ = 5`.

| `t` | chiều | `boxAt(t, 1)` | `≤ 13`? | `count` |
|---|---|---|---|---|
| 2 | phải→trái | `3 + 3 = 6` | ✓ | 1 |
| 3 | trái→phải | `6 + 1 = 7` | ✓ | 2 |
| 4 | phải→trái | `9 + 3 = 12` | ✓ | 3 |
| 5 | trái→phải | `12 + 1 = 13` | ✓ | 4 |

→ **4** ✓

Để ý bước nhảy giữa các tầng: `6 → 7 → 12 → 13`, chênh lệch `+1, +5, +1` — **không đều**.
Đây là lý do không nên cộng dồn mà nên tính lại `boxAt` mỗi tầng.

---

## 7. Các cách KHÔNG dùng được

| Ý tưởng sai | Vì sao sai |
|---|---|
| `return topFloor - floor + 1` | Bỏ qua tầng trên cùng khuyết → trượt nhóm #3 (70%) |
| Dùng `offset + 1` làm cột cho mọi tầng | Quên đảo chiều tầng chẵn (bẫy 4.1) |
| Cho rằng tầng trên cùng luôn lấp từ cột 1 | Sai khi `topFloor` chẵn (bẫy 4.2) |
| `return n - num + 1` | Chỉ đúng khi `w = 1`. Đây là lời giải của **riêng nhóm #1** |
| Đếm ngược từ `num` xuống | Sai hướng — dỡ hàng là dỡ từ **trên xuống**, thùng bên dưới `num` không liên quan |
| `Math.Ceiling((double)n / w)` | Chạy đúng ở đây nhưng dính lỗi làm tròn `double` khi `n` lớn. Dùng `(n + w - 1) / w` |

---

## 8. Bẫy cần nhớ

1. **Tầng chẵn xếp ngược** — `(num-1) % w` cho *thứ tự đặt*, không phải *cột*. Phải lật: `c = w - p`.
2. **Tầng trên cùng có thể khuyết, và khuyết ở phía nào phụ thuộc tính chẵn lẻ của tầng đó.**
   Kiểm tra bằng `boxAt(t, c) ≤ n` là xử lý đúng cả hai trường hợp mà không cần `if`.
3. **Tính cả `num`** → vòng lặp bắt đầu từ `t = floor`, không phải `floor + 1`. Đáp án tối thiểu là `1`.
4. **`num` có thể nằm ngay tầng trên cùng** → đáp án `1`. Kiểm tra riêng case này.
5. **Đánh số tầng/cột từ 1, còn `%` và `/` cho kết quả từ 0** — mọi công thức ở mục 3 đều
   dùng `num - 1` rồi `+1` lại. Nhầm chỗ này là lệch nguyên một cột.
6. **`w = 1`** → mọi thùng chung một cột, đáp án `n - num + 1`. Dùng làm case kiểm thử nhanh.
7. **Chạy đúng cả 2 ví dụ mẫu vẫn có thể sai** — không ví dụ mẫu nào có cột đích bị khuyết
   ở tầng trên cùng, nên `return topFloor - floor + 1` qua sạch cả hai (mục 4.2).
   Bắt buộc tự chế case kiểm thử:

   | Case | Cột của `num` | Đúng | Công thức ngây thơ |
   |---|---|---|---|
   | `n=20, w=6, num=3` | 3 → có `3, 10, 15` | **3** | 4 ✗ |
   | `n=20, w=6, num=19` | 6 → `num` ở ngay đỉnh | **1** | 1 ✓ |
   | `n=13, w=3, num=1` | 1 → có `1, 6, 7, 12, 13` | **5** | 5 ✓ |
   | `n=13, w=3, num=3` | 3 → có `3, 4, 9, 10` | **4** | 5 ✗ |
   | `n=13, w=3, num=2` | 2 → có `2, 5, 8, 11` | **4** | 4 ✗ (tính 5) |
   | `n=5, w=1, num=2` | 1 → `2, 3, 4, 5` | **4** | 4 ✓ |
