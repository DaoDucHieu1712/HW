# 05 — Bẫy hay dính & checklist

## 1. Bẫy về chỉ số và biên

| Bẫy | Triệu chứng | Cách tránh |
|---|---|---|
| `Substring(start, end)` | Cắt sai / `ArgumentOutOfRange` | Tham số 2 là **ĐỘ DÀI**: `end - start + 1` (nếu `end` inclusive) |
| `Remove(start, end)` | Như trên | Cũng là `(start, LENGTH)` |
| `int[,].Length` | Lặp thừa, `IndexOutOfRange` | Số dòng là `GetLength(0)`, số cột là `GetLength(1)` |
| Đề cho `[start, end]` inclusive | Sót 1 phần tử | Vòng lặp `for (i = start; i <= end; i++)` |
| Đề đánh số từ 1 | Lệch 1 ô | Chuyển sang 0-based ngay khi đọc, hoặc cấp mảng `n + 1` phần tử |
| Quên check biên trước khi đọc `grid` | Crash | `if (nr < 0 \|\| nr >= rows \|\| ...) continue;` **đặt trước** |
| `(lo + hi) / 2` | Tràn int khi `hi` lớn | `lo + (hi - lo) / 2` |

## 2. Bẫy về kiểu số

```csharp
// TRÀN INT — dính nhiều nhất
int sum = 0; foreach (int x in a) sum += x;   // ❌ n=10^5, x=10^9
long sum = 0;                                 // ✅

int p = a * b;                                // ❌ nhân trong int rồi mới gán long
long p = (long)a * b;                         // ✅ ép TRƯỚC khi nhân

Array.Sort(a, (x, y) => y - x);               // ❌ tràn khi giá trị lớn
Array.Sort(a, (x, y) => y.CompareTo(x));      // ✅

int inf = int.MaxValue;
dist = inf + w;                               // ❌ tràn thành số âm
int inf = int.MaxValue / 2;                   // ✅
```

- Chia số nguyên: `5 / 2 == 2`. Cần thực: `(double)a / b`.
- Số âm: `-7 / 2 == -3`, `-7 % 2 == -1`. Modulo dương: `((x % m) + m) % m`.
- `Math.Pow` trả `double` → sai số làm tròn. Luỹ thừa nguyên thì tự nhân trong vòng lặp.
- So sánh `double` bằng `==` là sai → dùng `Math.Abs(a - b) < 1e-9`.
- `Math.Round(2.5) == 2` (banker's rounding) → dùng `MidpointRounding.AwayFromZero`.

## 3. Bẫy về hiệu năng

| Việc | Sai | Đúng |
|---|---|---|
| Nối chuỗi trong vòng lặp | `s += x` → `O(n^2)` | `StringBuilder` |
| Kiểm tra tồn tại | `list.Contains(x)` → `O(n)` | `HashSet.Contains` → `O(1)` |
| Xoá đầu danh sách | `list.RemoveAt(0)` → `O(n)` | `Queue.Dequeue` → `O(1)` |
| Lấy min liên tục | `list.Min()` mỗi vòng → `O(n^2)` | `PriorityQueue` |
| LINQ trong vòng lặp nóng | `.Where().Count()` lặp lại | Tính trước ra Dictionary/mảng |
| Sort trong vòng lặp | `O(n^2 log n)` | Sort một lần ngoài vòng |
| `Console.WriteLine` nhiều | I/O chậm | Gom vào `StringBuilder`, in một lần |

## 4. Bẫy về logic

- **Đề nói "đồng thời"** → phải tính trên trạng thái cũ, không cập nhật tại chỗ (xem [04 §12](04-algorithms.md#12-simulation)).
- **Đề nói "lần lượt từ trái sang phải"** → thứ tự xử lý là một phần của lời giải, không được sort lại.
- **Chống trùng lặp** thường xét trên **mọi** phần tử đã gặp, không chỉ phần tử được tính vào đáp án.
- **Điều kiện "chỉ cần một"** vs **"tất cả"** — đọc kỹ, đây là chỗ hay hiểu ngược.
- **Trường hợp biên:** `n = 0`, `n = 1`, mảng rỗng, chuỗi rỗng, tất cả phần tử bằng nhau,
  không có đáp án (trả về `-1` hay `0`? đề nói gì?).
- **Tie-break:** khi nhiều đáp án cùng tốt, đề luôn nói chọn cái nào (nhỏ nhất / theo alphabet).
  Bỏ qua là sai một nửa số test.
- **`visited` đánh dấu lúc Enqueue**, không phải lúc Dequeue.
- **Đệ quy sâu > ~10^4** → `StackOverflowException` (không catch được, process chết luôn).
  Đổi sang vòng lặp + `Stack`.

## 5. Bẫy về C# nói riêng

```csharp
// Chia sẻ tham chiếu — sửa 1 dòng thì mọi dòng đổi theo
var rows = Enumerable.Repeat(new int[m], n).ToArray();      // ❌ n dòng CÙNG 1 mảng
var rows = Enumerable.Range(0, n).Select(_ => new int[m]).ToArray();  // ✅

// Clone mảng 2 chiều là copy NÔNG với jagged
int[][] copy = (int[][])g.Clone();       // ❌ các dòng vẫn dùng chung
int[][] copy = g.Select(r => r.ToArray()).ToArray();   // ✅

// HashSet của mảng so sánh theo THAM CHIẾU
var set = new HashSet<int[]>();          // ❌ không dedupe được
var set = new HashSet<(int, int)>();     // ✅ tuple so sánh theo giá trị

// Sửa collection khi đang foreach -> InvalidOperationException
foreach (var x in list) if (x < 0) list.Remove(x);   // ❌
list.RemoveAll(x => x < 0);                          // ✅

// static field giữ trạng thái giữa các test case của grader
static List<int> _cache = new();         // ❌ test sau ăn dữ liệu test trước
```

- `List.Sort` / `Array.Sort` **không ổn định**; `OrderBy` thì ổn định.
- `List.BinarySearch` không thấy → trả về **số âm** = `~insertionIndex`, dùng `~pos` để lấy vị trí chèn.
- `Dictionary` **không có thứ tự** duyệt xác định.
- `arr.All(...)` trên mảng rỗng trả `true`.

---

## 6. Checklist trước khi nộp

**Đọc đề**
- [ ] Đã đọc lại phần ràng buộc (`n` tối đa bao nhiêu → chọn được độ phức tạp)?
- [ ] Chỉ số 0-based hay 1-based? Khoảng inclusive hay exclusive?
- [ ] Tie-break và trường hợp "không có đáp án" trả về gì?

**Code**
- [ ] Kiểu trả về đúng như chữ ký hàm (`int` / `long` / `int[]` / `string`)?
- [ ] Tổng / tích có thể tràn `int` không → đã đổi `long` chưa?
- [ ] Nối chuỗi trong vòng lặp → đã dùng `StringBuilder`?
- [ ] `Contains` trên `List` trong vòng lặp → đã đổi `HashSet`?
- [ ] Đệ quy có thể sâu quá không?
- [ ] Không còn `Console.WriteLine` debug sót lại?
- [ ] Không dùng `static` field lưu trạng thái?

**Kiểm thử**
- [ ] Chạy đúng **cả** ví dụ trong đề (không chỉ ví dụ đầu)?
- [ ] Thử `n = 1` / mảng 1 phần tử / mọi phần tử giống nhau?
- [ ] Thử input lớn nhất theo ràng buộc → có kịp thời gian không?

---

## 7. Quy trình làm bài (khuyến nghị)

1. **5 phút đọc đề** — không code. Viết ra: input, output, ràng buộc, định nghĩa lạ trong đề.
2. **Chạy tay ví dụ 1** ra giấy/comment. Nếu ra khác đáp án của đề → mình hiểu sai đề, đọc lại.
3. **Chốt độ phức tạp mục tiêu** từ ràng buộc (bảng ở [README](README.md#ước-lượng-độ-phức-tạp-mốc-nhớ-nhanh)).
4. **Viết thuật toán bằng tiếng Việt** thành 3–5 bước trước khi gõ code.
5. **Code**, ưu tiên rõ ràng hơn ngắn gọn.
6. **Test cả 2 ví dụ + biên**, rồi mới nộp.

> Kinh nghiệm: PCCP Lv1–Lv2 hầu hết **không khó về thuật toán**, mà khó ở chỗ **đọc đúng đề**.
> Thời gian bỏ ra đọc kỹ đề luôn rẻ hơn thời gian debug.
