# 03 — LINQ hay dùng

`using System.Linq;`

LINQ giúp viết ngắn, nhưng **lười (lazy)** và có overhead. Quy tắc:
- Vòng lặp nóng (`n ≥ 10^6`, trong DFS/BFS) → viết `for` tay.
- Xử lý gom nhóm / sắp xếp / thống kê một lần → dùng LINQ thoải mái.

---

## 1. Lọc / biến đổi

```csharp
arr.Where(x => x > 0)                    // lọc
arr.Select(x => x * 2)                   // map
arr.Select((x, i) => (x, i))             // map kèm INDEX — rất hay dùng
arr.SelectMany(row => row)               // làm phẳng mảng lồng
arr.Distinct()
arr.Take(3) / arr.Skip(3)
arr.TakeWhile(x => x > 0) / arr.SkipWhile(...)
arr.Reverse()
arr.Chunk(3)                             // .NET 6+: chia thành các mảng 3 phần tử
arr.Zip(other, (a, b) => a + b)
```

## 2. Thống kê

```csharp
arr.Count()  /  arr.Count(x => x > 0)
arr.Sum()    /  arr.Sum(x => (long)x)    // ⚠ ép long để khỏi tràn
arr.Max()    /  arr.Min()
arr.Average()                            // trả double
arr.MaxBy(x => x.Score)                  // .NET 6+: trả PHẦN TỬ, không phải giá trị
arr.MinBy(x => x.Score)
arr.Aggregate((a, b) => a * b)           // reduce
arr.Aggregate(1L, (acc, x) => acc * x)   // reduce có seed
```

## 3. Kiểm tra

```csharp
arr.Any()                    // có phần tử nào không
arr.Any(x => x < 0)
arr.All(x => x > 0)          // ⚠ mảng RỖNG -> All trả TRUE
arr.Contains(x)
arr.SequenceEqual(other)     // so sánh 2 dãy theo phần tử
```

## 4. Lấy phần tử

```csharp
arr.First()  / arr.FirstOrDefault()       // OrDefault: rỗng -> 0/null, không ném
arr.Last()   / arr.LastOrDefault()
arr.Single() / arr.SingleOrDefault()      // ném nếu có > 1
arr.ElementAtOrDefault(i)
```

## 5. Sắp xếp

```csharp
arr.OrderBy(x => x)
arr.OrderByDescending(x => x.Score).ThenBy(x => x.Name)
arr.OrderBy(x => x, Comparer<T>.Create((a, b) => ...))
```

> `OrderBy` là **stable** — phần tử bằng nhau giữ nguyên thứ tự gốc. `List.Sort` thì không.

## 6. Gom nhóm — mẫu cực hay dùng

```csharp
// Đếm tần suất
var freq = words.GroupBy(w => w)
                .ToDictionary(g => g.Key, g => g.Count());

// Gom theo khoá
var byLen = words.GroupBy(w => w.Length)
                 .ToDictionary(g => g.Key, g => g.ToList());

// Nhóm đông nhất
var top = words.GroupBy(w => w)
               .OrderByDescending(g => g.Count())
               .ThenBy(g => g.Key)          // tie-break để kết quả xác định
               .First().Key;

// Lookup (1 khoá -> nhiều giá trị, không ném khi thiếu khoá)
var lookup = edges.ToLookup(e => e.From, e => e.To);
foreach (int v in lookup[u]) { }          // khoá không tồn tại -> dãy rỗng
```

## 7. Sinh dãy

```csharp
Enumerable.Range(0, n)                    // 0..n-1
Enumerable.Range(1, n)                    // 1..n
Enumerable.Repeat(-1, n).ToArray()
Enumerable.Empty<int>()
```

## 8. Chuyển đổi

```csharp
.ToArray() / .ToList() / .ToHashSet()
.ToDictionary(x => x.Id, x => x)          // ⚠ khoá TRÙNG -> ném exception
```

---

## 9. Một số one-liner hay dùng trong đề

```csharp
// Đảo chuỗi
new string(s.Reverse().ToArray())

// Tổng chữ số
s.Sum(c => c - '0')

// Chuỗi có phải toàn số
s.All(char.IsDigit)

// Ký tự xuất hiện nhiều nhất
s.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key

// Sắp xếp chuỗi (dùng làm khoá anagram)
string key = new string(s.OrderBy(c => c).ToArray());

// Top-k lớn nhất
arr.OrderByDescending(x => x).Take(k)

// Index của giá trị lớn nhất
int idx = arr.Select((v, i) => (v, i)).MaxBy(t => t.v).i;

// Kiểm tra mọi phần tử khác nhau
arr.Distinct().Count() == arr.Length

// Tổng tiền tố (prefix sum)
int run = 0;
int[] pre = arr.Select(x => run += x).ToArray();

// Ma trận: tổng mỗi dòng / mỗi cột (jagged)
var rowSums = g.Select(r => r.Sum()).ToArray();
var colSums = Enumerable.Range(0, cols).Select(c => g.Sum(r => r[c])).ToArray();

// Xoay ma trận 90° theo chiều kim đồng hồ (jagged)
var rot = Enumerable.Range(0, cols)
    .Select(c => Enumerable.Range(0, rows).Select(r => g[rows - 1 - r][c]).ToArray())
    .ToArray();
```

---

## 10. Bẫy LINQ

| Bẫy | Hậu quả | Cách tránh |
|---|---|---|
| Lazy evaluation | Query chạy lại mỗi lần duyệt | `.ToList()` khi dùng nhiều lần |
| `.Where().Count()` trong vòng lặp | `O(n^2)` âm thầm | Tính trước ra Dictionary |
| `.Contains()` trên `List` | `O(n)` mỗi lần | Đổi sang `HashSet` |
| `Sum()` trên `int` | Tràn int | `Sum(x => (long)x)` |
| `First()` trên dãy rỗng | Exception | `FirstOrDefault()` + kiểm tra |
| `ToDictionary` khoá trùng | Exception | `GroupBy` trước, hoặc `ToLookup` |
| Sửa collection đang duyệt | Exception | Tách ra list mới |
