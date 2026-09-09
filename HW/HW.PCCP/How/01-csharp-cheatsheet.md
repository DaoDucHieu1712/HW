# 01 — Cheatsheet hàm dựng sẵn C#

Chỉ liệt kê thứ **thực sự hay dùng** trong coding test. Nhóm theo kiểu dữ liệu.

---

## 1. `string`

### 1.1. Truy cập & cắt

```csharp
string s = "hello world";

s.Length                 // 11
s[0]                     // 'h'  (char, không phải string)
s.Substring(6)           // "world"      — từ index 6 đến hết
s.Substring(0, 5)        // "hello"      — (startIndex, ĐỘ DÀI)  ⚠ tham số 2 là LENGTH
s[0..5]                  // "hello"      — range operator, dễ đọc hơn
s[^5..]                  // "world"      — 5 ký tự cuối
s[..^1]                  // "hello worl" — bỏ ký tự cuối
```

> ⚠ **Bẫy kinh điển:** đề cho `[start, end]` **bao gồm cả hai đầu** →
> `s.Substring(start, end - start + 1)` hoặc `s[start..(end + 1)]`.

### 1.2. Tìm kiếm

```csharp
s.Contains("wor")            // true
s.StartsWith("he")           // true
s.EndsWith("ld")             // true
s.IndexOf('o')               // 4    — không thấy trả về -1
s.IndexOf("o", 5)            // 7    — tìm từ index 5
s.LastIndexOf('o')           // 7
s.IndexOfAny(new[]{'a','e'}) // vị trí đầu tiên trong tập ký tự
s.Count(c => c == 'l')       // 3    — LINQ, đếm ký tự
```

### 1.3. Tách & ghép

```csharp
"a,b,,c".Split(',')                                   // ["a","b","","c"]
"a,b,,c".Split(',', StringSplitOptions.RemoveEmptyEntries) // ["a","b","c"]
"a b\tc".Split()                                      // tách theo mọi whitespace
"a1b2c".Split('1', '2')                               // nhiều dấu tách

string.Join(",", new[] { 1, 2, 3 })                   // "1,2,3"
string.Join("", listOfChars)                          // ghép List<char> thành string
string.Concat(arr)                                    // ghép mảng string
```

### 1.4. Biến đổi (string là **immutable** — mọi hàm trả về chuỗi mới)

```csharp
s.ToUpper() / s.ToLower()
s.Trim() / s.TrimStart() / s.TrimEnd()
s.Trim('0')                       // bỏ ký tự '0' ở hai đầu
s.Replace("l", "L")
s.Replace('l', 'L')
s.Remove(2)                       // bỏ từ index 2 đến hết
s.Remove(2, 3)                    // bỏ 3 ký tự từ index 2   ⚠ (start, LENGTH)
s.Insert(2, "XY")
s.PadLeft(5, '0')                 // "00abc"  — hay dùng format số
s.PadRight(5, '_')
new string('a', 3)                // "aaa"
new string(charArray)             // char[] -> string
new string(s.Reverse().ToArray()) // đảo chuỗi
```

### 1.5. So sánh

```csharp
a == b                                       // so sánh NỘI DUNG (khác Java!)
string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
string.Compare(a, b)                         // <0, 0, >0 — dùng để sort
string.IsNullOrEmpty(s)
string.IsNullOrWhiteSpace(s)
```

### 1.6. `StringBuilder` — bắt buộc khi nối chuỗi trong vòng lặp

```csharp
using System.Text;

var sb = new StringBuilder();
for (int i = 0; i < n; i++) sb.Append(i).Append(',');
sb.AppendLine("x");
sb.Length--;             // xoá ký tự cuối
sb.Insert(0, "head");
sb[0] = 'H';             // sửa tại chỗ được (string thì không)
string result = sb.ToString();
```

> `s += x` trong vòng lặp `n = 10^5` → `O(n^2)` → **timeout**. Luôn dùng `StringBuilder`.

---

## 2. `char`

```csharp
char.IsDigit(c)          // '0'..'9'
char.IsLetter(c)
char.IsLetterOrDigit(c)
char.IsUpper(c) / char.IsLower(c)
char.IsWhiteSpace(c)
char.ToUpper(c) / char.ToLower(c)

c - '0'                  // char số -> int          '7' - '0' = 7
(char)(x + '0')          // int -> char số
c - 'a'                  // chữ cái -> 0..25        (index bảng đếm)
(char)('a' + i)          // 0..25 -> chữ cái
```

Mẫu đếm ký tự bằng mảng 26 (nhanh hơn Dictionary):

```csharp
int[] cnt = new int[26];
foreach (char c in s) cnt[c - 'a']++;
```

---

## 3. Mảng `T[]`

### 3.1. Khởi tạo

```csharp
int[] a = new int[n];                 // mặc định 0
int[] a = { 1, 2, 3 };
int[] a = Enumerable.Repeat(-1, n).ToArray();
int[] a = Enumerable.Range(0, n).ToArray();   // 0..n-1

Array.Fill(a, -1);                    // ⚠ mảng 1 chiều mới có Fill
```

### 3.2. Mảng 2 chiều — hai loại, đừng nhầm

```csharp
// A. Rectangular (đề PCCP hay truyền kiểu này)
int[,] g = new int[r, c];
g[i, j]
g.GetLength(0)   // số DÒNG
g.GetLength(1)   // số CỘT
g.Length         // ⚠ TỔNG SỐ PHẦN TỬ = r * c, KHÔNG phải số dòng

// B. Jagged (dễ dùng với LINQ hơn)
int[][] g = new int[r][];
for (int i = 0; i < r; i++) g[i] = new int[c];
g[i][j]
g.Length         // số dòng
g[i].Length      // số cột dòng i
```

Chuyển `int[,]` → `int[][]` khi muốn xài LINQ:

```csharp
int rows = m.GetLength(0), cols = m.GetLength(1);
int[][] j = Enumerable.Range(0, rows)
    .Select(i => Enumerable.Range(0, cols).Select(k => m[i, k]).ToArray())
    .ToArray();
```

Mảng `bool` 2 chiều cho `visited`:

```csharp
bool[,] visited = new bool[r, c];
```

### 3.3. Hàm `Array`

```csharp
Array.Sort(a);                        // tăng dần, in-place
Array.Sort(a, (x, y) => y - x);       // giảm dần  (⚠ tràn int nếu giá trị lớn -> dùng y.CompareTo(x))
Array.Sort(keys, values);             // sort keys, values đi theo
Array.Reverse(a);
Array.IndexOf(a, value);              // -1 nếu không thấy
Array.BinarySearch(a, value);         // mảng phải đã sort; âm nếu không thấy
Array.Copy(src, dst, len);
int[] b = (int[])a.Clone();
int[] b = a.ToArray();                // LINQ, cũng là copy nông
Array.Exists(a, x => x > 5);
Array.Find(a, x => x > 5);
Array.TrueForAll(a, x => x > 0);
```

---

## 4. `Math` & số học

```csharp
Math.Max(a, b) / Math.Min(a, b)
Math.Abs(x)
Math.Pow(2, 10)          // trả DOUBLE -> (int)Math.Pow(...) có thể sai vì làm tròn
Math.Sqrt(x)             // double
Math.Floor / Math.Ceiling / Math.Round
Math.Round(2.5)          // = 2  ⚠ banker's rounding!
Math.Round(2.5, MidpointRounding.AwayFromZero)  // = 3 (kiểu toán học)
Math.DivRem(a, b, out int rem)
```

### 4.1. Chia lấy trần / sàn với số nguyên

```csharp
int up   = (a + b - 1) / b;   // ceil(a/b), a,b > 0 — tránh double
int down = a / b;             // C# cắt về 0 với số dương
```

> ⚠ Với số **âm**, `-7 / 2 == -3` và `-7 % 2 == -1` (khác Python). Cần modulo dương:
> `((x % m) + m) % m`

### 4.2. Kiểu số — chọn đúng để khỏi tràn

| Kiểu | Max | Khi nào dùng |
|---|---|---|
| `int` | ~2.1 × 10^9 | mặc định |
| `long` | ~9.2 × 10^18 | tổng/tích có thể vượt 2×10^9 |

```csharp
long sum = 0;
foreach (int x in a) sum += x;         // n=10^5, x=10^9 -> tràn int chắc chắn

long p = (long)a * b;                  // ⚠ phải ép TRƯỚC khi nhân
int INF = int.MaxValue / 2;            // chia 2 để cộng thêm không tràn
```

### 4.3. GCD / LCM

```csharp
static long Gcd(long a, long b) => b == 0 ? a : Gcd(b, a % b);
static long Lcm(long a, long b) => a / Gcd(a, b) * b;   // chia trước để tránh tràn
```

### 4.4. Kiểm tra số nguyên tố / sàng

```csharp
static bool IsPrime(int n)
{
    if (n < 2) return false;
    for (int i = 2; (long)i * i <= n; i++)
        if (n % i == 0) return false;
    return true;
}

// Sàng Eratosthenes: mọi số nguyên tố <= n, O(n log log n)
bool[] composite = new bool[n + 1];
for (int i = 2; (long)i * i <= n; i++)
    if (!composite[i])
        for (int j = i * i; j <= n; j += i)
            composite[j] = true;
```

### 4.5. Bit

```csharp
x & 1              // lẻ?
x >> 1             // chia 2
1 << k             // 2^k   ⚠ k >= 31 phải dùng 1L << k
x | (1 << k)       // bật bit k
x & ~(1 << k)      // tắt bit k
x ^ (1 << k)       // đảo bit k
(x >> k) & 1       // đọc bit k
System.Numerics.BitOperations.PopCount((uint)x)   // đếm bit 1
Convert.ToString(x, 2)                            // sang chuỗi nhị phân
Convert.ToInt32("1011", 2)                        // nhị phân -> int
```

---

## 5. Parse & format

```csharp
int.Parse("42")
long.Parse("42")
double.Parse("4.2")
int.TryParse(s, out int v)          // an toàn, trả bool

x.ToString()
x.ToString("D5")                    // "00042"  — pad số 0
x.ToString("F2")                    // "4.20"
$"{x,5}"                            // căn phải trong 5 ô

Convert.ToInt32(c)                  // ⚠ với char trả về MÃ ASCII, không phải chữ số!
```

---

## 6. Tuple & deconstruct — rất hay dùng cho BFS / sort nhiều khoá

```csharp
var p = (x: 1, y: 2);
var (a, b) = p;

var q = new Queue<(int r, int c, int d)>();
q.Enqueue((0, 0, 0));
var (r, c, d) = q.Dequeue();

// Tuple so sánh theo thứ tự phần tử -> sort nhiều tiêu chí miễn phí
var list = new List<(int score, string name)>();
list.Sort();                                  // score tăng, rồi name tăng
list.Sort((u, v) => v.score.CompareTo(u.score));  // score giảm
```
