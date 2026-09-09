# 06 — Cách viết tắt (C# idioms)

Viết tắt để **gõ nhanh hơn và ít bug hơn**, không phải để code khó đọc.
Mỗi mục: `❌ dài` → `✅ ngắn`.

> ⚠ Đọc [§13 — Phiên bản C#](#13-phiên-bản-c-nào-dùng-được-gì) trước.
> Grader của Programmers thường là **.NET 6 / C# 10**. Một số cú pháp ở dưới là C# 12
> (chỉ chạy được ở máy bạn với .NET 8) — tôi có đánh dấu.

---

## 1. Khai báo biến

```csharp
// ❌
Dictionary<string, List<int>> map = new Dictionary<string, List<int>>();
// ✅ var
var map = new Dictionary<string, List<int>>();
// ✅ target-typed new (C# 9) — hay hơn khi là field, vì vẫn thấy kiểu
Dictionary<string, List<int>> map = new();

int a = 0, b = 0, c = 0;                  // khai báo nhiều biến 1 dòng
int l = 0, r = a.Length - 1;              // two pointer
```

## 2. Hoán đổi & gán nhiều

```csharp
// ❌
int tmp = a; a = b; b = tmp;
// ✅ tuple swap
(a, b) = (b, a);

// Áp dụng luôn cho phần tử mảng
(arr[i], arr[j]) = (arr[j], arr[i]);

// Deconstruct
var (r, c, d) = queue.Dequeue();
foreach (var (key, value) in map) { }
```

## 3. Toán tử gán rút gọn

```csharp
sum += x;   cnt -= 1;   p *= 2;   x /= 2;   x %= m;
x <<= 1;    mask |= 1 << k;       mask &= ~(1 << k);      mask ^= 1 << k;

best = Math.Max(best, cur);        // thay if (cur > best) best = cur;
i++;  ++i;                         // trong biểu thức: a[i++] lấy a[i] RỒI mới tăng

// ??= gán nếu đang null — mẫu "lazy init" 1 dòng
(adj[u] ??= new List<int>()).Add(v);
(revealedAt[r] ??= []).Add(word);   // C# 12
```

## 4. Ternary & null

```csharp
// ❌
if (x > 0) sign = 1; else sign = -1;
// ✅
int sign = x > 0 ? 1 : -1;

// Ternary lồng — chỉ 2 tầng, hơn nữa thì dùng switch
int cmp = a < b ? -1 : a > b ? 1 : 0;

// ?? null-coalescing
var list = map.GetValueOrDefault(k) ?? new List<int>();

// ?. null-conditional
int n = list?.Count ?? 0;

// Kết hợp trong return
return best == int.MaxValue ? -1 : best;
```

## 5. Expression-bodied member — hàm 1 dòng

```csharp
// ❌
static long Gcd(long a, long b) { if (b == 0) return a; return Gcd(b, a % b); }
// ✅
static long Gcd(long a, long b) => b == 0 ? a : Gcd(b, a % b);

static bool InBound(int r, int c) => r >= 0 && r < rows && c >= 0 && c < cols;
long SumRange(int l, int r) => pre[r + 1] - pre[l];
int Idx(int r, int c) => r * cols + c;        // ép lưới 2D thành 1D
```

## 6. Pattern matching — thay chuỗi `if` dài

```csharp
// ❌
if (c == '(' || c == '[' || c == '{')
// ✅
if (c is '(' or '[' or '{')

// Khoảng giá trị
if (x is >= 1 and <= 9)
if (c is >= 'a' and <= 'z')

// Phủ định
if (obj is not null)
if (s is not "")

// is + gán biến (dùng trong bài Important words: `is not { } words`)
if (revealedAt[r] is not { } words) continue;   // null -> bỏ qua; ngược lại gán vào words
if (map.TryGetValue(k, out var v) && v is > 0) { }
```

## 7. `switch` expression — thay `switch` khối

```csharp
// ❌
switch (dir) { case 'U': dr = -1; break; case 'D': dr = 1; break; ... }
// ✅
int dr = dir switch { 'U' => -1, 'D' => 1, _ => 0 };

// Nhiều nhánh gộp
string kind = c switch
{
    >= '0' and <= '9' => "digit",
    >= 'a' and <= 'z' => "lower",
    ' '               => "space",
    _                 => "other"
};

// Match theo tuple — rất gọn cho bài mô phỏng
var (nr, nc) = dir switch
{
    0 => (r - 1, c),
    1 => (r + 1, c),
    2 => (r, c - 1),
    _ => (r, c + 1)
};
```

## 8. Index & Range (`^`, `..`) — C# 8

```csharp
s[^1]                 // ký tự cuối       (thay s[s.Length - 1])
list[^1]              // phần tử cuối
list[^1][1] = x;      // sửa phần tử cuối
s[2..5]               // substring(2, 3)
s[start..(end + 1)]   // đề cho [start, end] inclusive
s[..^1]               // bỏ ký tự cuối
s[^3..]               // 3 ký tự cuối
arr[1..]              // mảng con từ index 1 (tạo mảng MỚI — cẩn thận trong vòng lặp)
```

## 9. Khởi tạo collection

```csharp
var list = new List<int> { 1, 2, 3 };
var map  = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
var set  = new HashSet<int> { 1, 2, 3 };
int[] dr = { -1, 1, 0, 0 };                       // không cần new int[]
var pairs = new List<(int, int)> { (0, 0), (1, 2) };

// Collection expression (C# 12 — CHỈ .NET 8)
int[] a = [1, 2, 3];
List<int> b = [];
int[] c = [..a, 4, 5];                            // spread
```

## 10. Vòng lặp & điều kiện gọn

```csharp
// Guard clause thay if lồng nhau — giảm 3 tầng thụt lề
foreach (var x in items)
{
    if (x is null) continue;
    if (!x.IsValid) continue;
    Process(x);
}

// for 1 dòng, không cần { }
for (int i = 0; i < n; i++) sum += a[i];

// Duyệt ngược
for (int i = n - 1; i >= 0; i--) { }

// Duyệt cặp (i, j) với j > i
for (int i = 0; i < n; i++)
for (int j = i + 1; j < n; j++) { }      // for lồng không cần { } cho vòng ngoài

// while gọn
while (q.TryDequeue(out var cur)) { }    // thay while (q.Count > 0) { var cur = q.Dequeue(); }
while (x % 2 == 0) x /= 2;
```

## 11. LINQ thay vòng lặp (xem thêm [03-linq.md](03-linq.md))

```csharp
// ❌ 5 dòng đếm
int cnt = 0; foreach (var x in a) if (x > 0) cnt++;
// ✅
int cnt = a.Count(x => x > 0);

int total  = a.Sum();
int mx     = a.Max();
var item   = people.MaxBy(p => p.Score);            // .NET 6+
bool ok    = s.All(char.IsDigit);                   // truyền method group, khỏi lambda
var nums   = line.Split(' ').Select(int.Parse).ToArray();
var rev    = new string(s.Reverse().ToArray());
var sorted = new string(s.OrderBy(c => c).ToArray());
var freq   = s.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
var arr    = Enumerable.Repeat(-1, n).ToArray();
var ids    = Enumerable.Range(0, n).ToArray();
```

## 12. Local function — gọn hơn tách hàm riêng, dùng chung được biến ngoài

```csharp
public int solution(int[,] grid)
{
    int rows = grid.GetLength(0), cols = grid.GetLength(1);
    var visited = new bool[rows, cols];

    // local function: nhìn thấy rows/cols/visited, khỏi truyền tham số
    bool Ok(int r, int c) => r >= 0 && r < rows && c >= 0 && c < cols && !visited[r, c];

    void Dfs(int r, int c)
    {
        visited[r, c] = true;
        for (int d = 0; d < 4; d++)
            if (Ok(r + dr[d], c + dc[d])) Dfs(r + dr[d], c + dc[d]);
    }

    Dfs(0, 0);
    return 0;
}
```

> Local function là cách viết tắt **an toàn nhất** trong bài thi: tránh được lỗi `static` field
> giữ trạng thái giữa các test case.

## 13. Phiên bản C# — nào dùng được gì

| Cú pháp | Từ C# | Có trên .NET 6 grader? |
|---|---|---|
| `var`, lambda, LINQ | 3.0 | ✅ |
| interpolation `$"{x}"` | 6.0 | ✅ |
| tuple `(a, b)`, deconstruct | 7.0 | ✅ |
| local function | 7.0 | ✅ |
| `out var` | 7.0 | ✅ |
| index `^1`, range `..` | 8.0 | ✅ |
| `??=`, switch expression | 8.0 | ✅ |
| `is not`, `is >= a and <= b` | 9.0 | ✅ |
| target-typed `new()` | 9.0 | ✅ |
| `MaxBy` / `MinBy` / `Chunk` | .NET 6 | ✅ |
| `PriorityQueue<,>` | .NET 6 | ✅ |
| collection expression `[1, 2]`, `[]` | 12.0 | ❌ (.NET 8 mới có) |
| primary constructor `class A(int x)` | 12.0 | ❌ |

> Nếu grader báo lỗi biên dịch lạ ở dòng có `[]` hoặc `[..]` → thay bằng `new List<int>()`.

---

## 14. Khi nào **đừng** viết tắt

| Tình huống | Lý do |
|---|---|
| Ternary lồng ≥ 3 tầng | Đọc lại 5 phút sau không hiểu → dùng `switch` expression |
| LINQ trong vòng lặp nóng | `.Where().Count()` bên trong `for` = `O(n^2)` âm thầm |
| `arr[1..]` trong vòng lặp | Mỗi lần cắt tạo **mảng mới** → `O(n^2)` bộ nhớ + thời gian |
| Gộp 3 phép biến đổi vào 1 dòng LINQ | Sai ở đâu không debug được. Tách ra, in ra xem |
| `a[i++]` trong biểu thức phức tạp | Thứ tự đánh giá dễ hiểu nhầm → sai chỉ số |
| Tên biến 1 ký tự cho logic dài | `l/r/i/j/d` cho vòng lặp thì OK, còn lại đặt tên rõ |

> Nguyên tắc: viết tắt **giảm số chỗ có thể gõ sai** thì dùng (`(a,b)=(b,a)`, `s[^1]`, `??=`).
> Viết tắt **giấu logic đi** thì bỏ.

---

## 15. Bảng tra nhanh — 20 cái dùng nhiều nhất

```csharp
(a, b) = (b, a)                          // swap
s[^1]                                    // phần tử cuối
s[l..(r + 1)]                            // đoạn [l, r] inclusive
x ??= new()                              // gán nếu null
(map[k] ??= []).Add(v)                   // lazy init + add
c is '(' or '[' or '{'                   // so nhiều giá trị
x is >= 1 and <= 9                       // trong khoảng
obj is not { } v                         // null-check + gán
best = Math.Max(best, cur)               // cập nhật max
d switch { 'U' => -1, _ => 0 }           // bảng tra
seen.Add(x)                              // trả false nếu đã có
map.TryGetValue(k, out var v)            // đọc an toàn
q.TryDequeue(out var cur)                // pop an toàn
a.Count(x => x > 0)                      // đếm có điều kiện
a.MaxBy(x => x.Score)                    // phần tử có key lớn nhất
s.All(char.IsDigit)                      // method group
line.Split(' ').Select(int.Parse)        // parse cả dòng
Enumerable.Repeat(-1, n).ToArray()       // mảng khởi tạo sẵn
new string(s.Reverse().ToArray())        // đảo chuỗi
bool Ok(int r, int c) => ...             // local function + expression body
```
