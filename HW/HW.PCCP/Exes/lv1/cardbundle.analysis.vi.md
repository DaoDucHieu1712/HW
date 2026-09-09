# Phân tích: Card Bundle (카드 뭉치 — Hai xấp bài)

> Đề bài: `HW.PCCP/Exes/lv1/cardbundle.md`
> File giải: `HW.PCCP/Solutions/cardbundle.cs`
> Chạy thử: `dotnet run --project HW.PCCP/HW.PCCP.csproj`

---

## 1. Đọc hiểu đề bài

### 1.1. Bối cảnh

Connie có **hai xấp bài**, mỗi lá bài ghi một từ tiếng Anh. Cậu ấy muốn biết có ghép được
một câu theo **đúng thứ tự mong muốn** (`goal`) từ hai xấp đó không.

### 1.2. Dữ liệu vào

| Tham số | Kiểu | Ý nghĩa |
|---|---|---|
| `cards1` | `string[]` | Xấp bài thứ nhất, **thứ tự cố định** |
| `cards2` | `string[]` | Xấp bài thứ hai, **thứ tự cố định** |
| `goal` | `string[]` | Dãy từ muốn tạo ra |

Ràng buộc:

- `1 ≤ cards1.Length, cards2.Length ≤ 10`
- `1 ≤` độ dài mỗi từ `≤ 10`
- **`cards1` và `cards2` chỉ chứa các từ khác nhau** — tức là toàn bộ từ trên cả hai xấp là **duy nhất**, không có từ nào lặp lại (kể cả giữa hai xấp).
- `2 ≤ goal.Length ≤ cards1.Length + cards2.Length`
- Mọi phần tử của `goal` đều nằm trong `cards1` hoặc `cards2`.
- Tất cả chuỗi chỉ gồm chữ thường.

### 1.3. Bốn luật chơi — dịch lại cho rõ

| Luật (đề) | Ý nghĩa thật sự |
|---|---|
| "Dùng bài lần lượt trong xấp mong muốn, theo thứ tự" | Ở mỗi lượt bạn được **chọn xấp nào** để rút, nhưng chỉ rút được lá **trên cùng** |
| "Một lá đã dùng thì không dùng lại" | Không tái sử dụng |
| "Không thể sang lá tiếp theo mà chưa dùng lá hiện tại" | **Không được bỏ qua lá nào** — đây là luật quan trọng nhất |
| "Không được đổi thứ tự trong xấp" | Xấp bài là bất biến |

> **Chốt lại:** mỗi xấp bài là một **hàng đợi (queue)**. Bạn chỉ được `Dequeue()` từ đầu,
> không được `Skip()`, không được `Peek()` ở giữa.

### 1.4. Cần trả về

`"Yes"` nếu ghép được `goal`, ngược lại `"No"`.

---

## 2. Mô hình hoá bài toán

Đây chính là bài toán **kiểm tra `goal` có phải là một phép trộn xen kẽ (interleaving)
của `cards1` và `cards2` hay không** — nhưng là **tiền tố** của phép trộn, vì `goal` có thể
ngắn hơn tổng số lá.

Đặt ba con trỏ:

- `i` — vị trí lá trên cùng còn lại của `cards1`
- `j` — vị trí lá trên cùng còn lại của `cards2`
- `k` — từ tiếp theo của `goal` cần khớp

Trạng thái `(i, j)` xác định luôn `k = i + j` (đã rút `i` lá từ xấp 1 và `j` lá từ xấp 2
thì đã ghép được `i + j` từ). Mỗi bước chỉ có **hai lựa chọn**:

```
(i, j) --rút cards1--> (i+1, j)   nếu cards1[i] == goal[i+j]
(i, j) --rút cards2--> (i, j+1)   nếu cards2[j] == goal[i+j]
```

Câu hỏi: có đường đi nào từ `(0, 0)` tới trạng thái có `i + j == goal.Length` không?

---

## 3. Điểm mấu chốt: vì sao **tham lam (greedy)** là đủ?

Đây là chỗ phân biệt "làm được bài" và "hiểu bài".

### 3.1. Nếu các từ có thể trùng nhau → tham lam SAI

Giả sử ràng buộc "các từ đều khác nhau" **không** tồn tại:

```
cards1 = ["a", "b"]
cards2 = ["a", "c"]
goal   = ["a", "c"]
```

Ở bước `k = 0`, **cả hai** xấp đều có đỉnh là `"a"`. Nếu tham lam luôn ưu tiên `cards1`:

```
rút cards1 -> i=1, j=0, k=1. goal[1]="c". cards1[1]="b" ✗, cards2[0]="a" ✗  ->  "No"  (SAI)
rút cards2 -> i=0, j=1, k=1. goal[1]="c". cards1[0]="a" ✗, cards2[1]="c" ✓  ->  "Yes" (ĐÚNG)
```

→ Khi có từ trùng, **lựa chọn sai ở một bước có thể giết cả lời giải**, phải quay lui (backtracking) hoặc DP.

### 3.2. Ràng buộc "mọi từ đều khác nhau" xoá bỏ mọi phân nhánh

Vì toàn bộ từ trên `cards1 ∪ cards2` là **duy nhất**, tại bất kỳ trạng thái `(i, j)` nào:

> `cards1[i]` và `cards2[j]` **không bao giờ bằng nhau** → nhiều nhất **một** trong hai
> có thể khớp `goal[k]`.

Nghĩa là cây quyết định có **hệ số phân nhánh đúng bằng 1**. Không có "lựa chọn" nào cả —
chỉ có **một con đường duy nhất** khả dĩ. Do đó:

- Nếu đi được tới cuối → `"Yes"`.
- Nếu kẹt giữa chừng → không tồn tại đường nào khác → `"No"` một cách chắc chắn.

> **Đây là lý do thuật toán hai con trỏ `O(n)` là đúng, chứ không phải "may mắn".**
> Ràng buộc của đề mới là thứ chứng minh tính đúng đắn, không phải bản thân vòng lặp.

### 3.3. Không cần dùng hết bài

`goal.Length` có thể nhỏ hơn `cards1.Length + cards2.Length`. Điều kiện dừng là
**`k` chạm hết `goal`**, không phải "hết bài". Lá thừa cứ để đó.

---

## 4. Chạy tay hai ví dụ

### 4.1. Ví dụ #1 → `"Yes"`

```
cards1 = ["i", "drink", "water"]
cards2 = ["want", "to"]
goal   = ["i", "want", "to", "drink", "water"]
```

| `k` | `goal[k]` | đỉnh `cards1` (`i`) | đỉnh `cards2` (`j`) | Hành động |
|---|---|---|---|---|
| 0 | `i` | `i` ✓ | `want` | rút xấp 1 → `i=1` |
| 1 | `want` | `drink` ✗ | `want` ✓ | rút xấp 2 → `j=1` |
| 2 | `to` | `drink` ✗ | `to` ✓ | rút xấp 2 → `j=2` |
| 3 | `drink` | `drink` ✓ | *(hết)* | rút xấp 1 → `i=2` |
| 4 | `water` | `water` ✓ | *(hết)* | rút xấp 1 → `i=3` |

`k = 5 == goal.Length` → **`"Yes"`**

### 4.2. Ví dụ #2 → `"No"`

```
cards1 = ["i", "water", "drink"]     <- water và drink bị đảo so với ví dụ 1
cards2 = ["want", "to"]
goal   = ["i", "want", "to", "drink", "water"]
```

| `k` | `goal[k]` | đỉnh `cards1` | đỉnh `cards2` | Hành động |
|---|---|---|---|---|
| 0 | `i` | `i` ✓ | `want` | rút xấp 1 → `i=1` |
| 1 | `want` | `water` ✗ | `want` ✓ | rút xấp 2 → `j=1` |
| 2 | `to` | `water` ✗ | `to` ✓ | rút xấp 2 → `j=2` |
| 3 | `drink` | `water` ✗ | *(hết)* | **kẹt** |

Muốn lấy `drink` thì buộc phải rút `water` trước (luật "không được bỏ qua lá"), nhưng
`goal` lại cần `drink` trước → **`"No"`**

---

## 5. Thuật toán — dùng `Queue<string>`

Mô hình ở mục 1.3 nói mỗi xấp bài **chính là** một hàng đợi, nên cứ hiện thực đúng như vậy:

```
deck1 = Queue(cards1)
deck2 = Queue(cards2)

foreach want in goal:
    if deck1 không rỗng && deck1.Peek() == want:  deck1.Dequeue()
    else if deck2 không rỗng && deck2.Peek() == want:  deck2.Dequeue()
    else: return "No"          // không xấp nào đưa được từ cần thiết -> bế tắc

return "Yes"
```

Ba điều đáng chú ý:

- **`Peek()` rồi mới `Dequeue()`** — đúng luật "chỉ nhìn và rút lá trên cùng". Không có
  API nào của `Queue<T>` cho phép lấy phần tử giữa, nên cấu trúc dữ liệu **tự nó chặn**
  cái bẫy lớn nhất của bài (mục 7.1).
- **`Count > 0` phải kiểm tra trước `Peek()`** — `Peek()` trên hàng đợi rỗng ném
  `InvalidOperationException`, và ví dụ #2 làm cạn `deck2` đúng ở bước thứ 4.
- **Thứ tự kiểm tra `deck1` hay `deck2` trước không quan trọng** — theo mục 3.2, tối đa
  một nhánh có thể đúng.

### Độ phức tạp

- Thời gian: `O(goal.Length × L)` với `L ≤ 10` là độ dài từ (chi phí so sánh chuỗi).
  Thực tế `≤ 20 × 10 = 200` phép so sánh ký tự → tức thời.
- Bộ nhớ: `O(n + m)` cho hai hàng đợi. `Queue<T>(IEnumerable<T>)` copy mảng vào bộ đệm
  nội bộ nên **không** sửa `cards1`/`cards2` gốc.

---

## 6. Các cách viết khác và đánh đổi

### 6.1. Hai con trỏ — cùng thuật toán, bộ nhớ `O(1)`

```csharp
int i = 0, j = 0;

for (int k = 0; k < goal.Length; k++)
{
    if (i < cards1.Length && cards1[i] == goal[k]) i++;
    else if (j < cards2.Length && cards2[j] == goal[k]) j++;
    else return "No";
}

return "Yes";
```

Hoàn toàn tương đương: `i` ⟷ `deck1.Count` đã rút, `Peek()` ⟷ `cards1[i]`,
`Dequeue()` ⟷ `i++`, `Count > 0` ⟷ `i < cards1.Length`.

Bỏ được `O(n)` bộ nhớ phụ, đổi lại phải **tự** giữ kỷ luật "chỉ đọc `cards1[i]`" —
không có gì ngăn bạn lỡ tay viết `cards1[i + 1]`. Với `n ≤ 10` thì bộ nhớ không đáng kể,
nên bản `Queue` diễn đạt ý đồ tốt hơn.

### 6.2. DP `O(n × m)` — lời giải cho bản tổng quát (nếu từ được phép trùng)

Khi bỏ ràng buộc "các từ đều khác nhau", bài này thành bài
**Interleaving String** kinh điển:

```
reach[i, j] = true nếu ghép được goal[0..i+j-1] bằng i lá xấp 1 và j lá xấp 2

reach[0, 0] = true
reach[i, j] = (i > 0 && reach[i-1, j] && cards1[i-1] == goal[i+j-1])
           || (j > 0 && reach[i, j-1] && cards2[j-1] == goal[i+j-1])

đáp án = tồn tại (i, j) với i + j == goal.Length và reach[i, j] == true
```

Nên biết cách này để trả lời khi người phỏng vấn hỏi:
*"Nếu hai xấp bài có từ trùng nhau thì sao?"*

### 6.3. Cách KHÔNG dùng được

| Ý tưởng sai | Vì sao sai |
|---|---|
| Nối `cards1 + cards2` rồi kiểm tra `goal` là tập con | Mất hoàn toàn thông tin thứ tự |
| Đếm tần suất từ (`Dictionary<string,int>`) | Ví dụ #1 và #2 có **cùng bộ từ**, chỉ khác thứ tự, nhưng kết quả khác nhau |
| `goal.All(g => cards1.Contains(g) \|\| cards2.Contains(g))` | Ràng buộc đề đã đảm bảo điều này **luôn đúng** → luôn trả `"Yes"` |
| Kiểm tra `goal` là subsequence của từng xấp riêng lẻ | Sai bản chất: `goal` được trộn từ **cả hai** xấp |

---

## 7. Bẫy cần nhớ

1. **Chỉ được rút lá trên cùng** — không phải "tìm từ đó ở đâu trong xấp". Đây là điểm chết của ví dụ #2.
2. **Điều kiện dừng là hết `goal`**, không phải hết bài — `goal` được phép ngắn hơn.
3. **Phải kiểm tra xấp còn bài TRƯỚC khi nhìn lá trên cùng** — `deck.Peek()` trên hàng đợi rỗng ném `InvalidOperationException` (bản hai con trỏ thì `IndexOutOfRangeException`). Ví dụ #2 làm cạn xấp 2 ở đúng bước `k = 3`.
4. **So sánh chuỗi bằng `==`** trong C# là so sánh **giá trị** (`string` override toán tử), không phải tham chiếu — chỗ này an toàn. Nhưng nếu chuyển sang `object`/generic thì phải dùng `string.Equals`/`EqualsOrdinal`.
5. **Trả về chuỗi `"Yes"`/`"No"`**, đúng chữ hoa chữ thường, không phải `bool`.
6. **Ràng buộc "các từ đều khác nhau" là điều kiện đủ để tham lam đúng** — nếu đề bỏ ràng buộc này, code sẽ sai. Hiểu chỗ này mới là hiểu bài.
