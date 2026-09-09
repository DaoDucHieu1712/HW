# 04 — Template thuật toán

Copy vào là chạy. Mỗi mục có: **khi nào dùng** → **template** → **bẫy**.

Mục lục:
1. [Duyệt lưới (grid) & 4/8 hướng](#1-duyệt-lưới-grid)
2. [BFS — đường đi ngắn nhất trên đồ thị không trọng số](#2-bfs)
3. [DFS](#3-dfs)
4. [Binary search](#4-binary-search)
5. [Two pointer & sliding window](#5-two-pointer--sliding-window)
6. [Prefix sum & difference array](#6-prefix-sum--difference-array)
7. [Greedy + sort](#7-greedy--sort)
8. [Dynamic programming](#8-dynamic-programming)
9. [Backtracking / hoán vị / tổ hợp / bitmask](#9-backtracking--sinh-tổ-hợp)
10. [Union-Find](#10-union-find-dsu)
11. [Dijkstra](#11-dijkstra)
12. [Simulation](#12-simulation)
13. [Merge 2 mảng](#13-merge-2-mảng)

---

## 1. Duyệt lưới (grid)

```csharp
int[] dr = { -1, 1, 0, 0 };            // trên, dưới, trái, phải
int[] dc = { 0, 0, -1, 1 };

// 8 hướng
int[] dr8 = { -1, -1, -1, 0, 0, 1, 1, 1 };
int[] dc8 = { -1, 0, 1, -1, 1, -1, 0, 1 };

for (int d = 0; d < 4; d++)
{
    int nr = r + dr[d], nc = c + dc[d];
    if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;   // ⚠ CHECK BIÊN TRƯỚC
    if (visited[nr, nc] || grid[nr, nc] == 0) continue;
    // ...
}
```

> ⚠ Luôn check biên **trước** khi truy cập `grid[nr, nc]`, nếu không → `IndexOutOfRange`.
> ⚠ Đề hay dùng `(x, y)` — xác định rõ `x` là cột hay dòng trước khi code, sai trục là hỏng cả bài.

---

## 2. BFS

**Khi nào:** đường đi **ngắn nhất** khi mọi cạnh có trọng số bằng nhau; loang vùng; số bước tối thiểu.

### 2.1. BFS trên lưới, đếm số bước

```csharp
public int Bfs(int[,] grid, int rows, int cols)
{
    var dist = new int[rows, cols];
    for (int i = 0; i < rows; i++)
        for (int j = 0; j < cols; j++)
            dist[i, j] = -1;

    var q = new Queue<(int r, int c)>();
    q.Enqueue((0, 0));
    dist[0, 0] = 0;                       // ⚠ đánh dấu visited NGAY LÚC ENQUEUE

    int[] dr = { -1, 1, 0, 0 }, dc = { 0, 0, -1, 1 };

    while (q.Count > 0)
    {
        var (r, c) = q.Dequeue();

        if (r == rows - 1 && c == cols - 1) return dist[r, c];

        for (int d = 0; d < 4; d++)
        {
            int nr = r + dr[d], nc = c + dc[d];
            if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;
            if (grid[nr, nc] == 0) continue;        // tường
            if (dist[nr, nc] != -1) continue;       // đã thăm

            dist[nr, nc] = dist[r, c] + 1;
            q.Enqueue((nr, nc));
        }
    }

    return -1;
}
```

> ⚠ **Bẫy số 1 của BFS:** đánh dấu visited lúc `Dequeue` thay vì lúc `Enqueue`
> → cùng một ô bị đẩy vào hàng đợi nhiều lần → chậm hoặc sai.

### 2.2. BFS theo tầng (cần biết "bước thứ mấy")

```csharp
int step = 0;
while (q.Count > 0)
{
    int size = q.Count;                   // ⚠ chốt size TRƯỚC vòng for
    for (int i = 0; i < size; i++)
    {
        var cur = q.Dequeue();
        // ... enqueue hàng xóm
    }
    step++;
}
```

### 2.3. Multi-source BFS

Đẩy **tất cả** điểm xuất phát vào queue trước khi bắt đầu vòng lặp (ví dụ: cà chua chín, lửa lan).

### 2.4. 0-1 BFS

Cạnh trọng số chỉ 0 hoặc 1 → dùng `LinkedList<T>` làm deque:
cạnh 0 → `AddFirst`, cạnh 1 → `AddLast`. Nhanh hơn Dijkstra.

---

## 3. DFS

**Khi nào:** đếm thành phần liên thông, flood fill, tìm chu trình, duyệt cây.

```csharp
// Đệ quy — gọn, nhưng ⚠ StackOverflow khi độ sâu > ~10^4 (C# stack 1MB)
void Dfs(int r, int c)
{
    visited[r, c] = true;
    for (int d = 0; d < 4; d++)
    {
        int nr = r + dr[d], nc = c + dc[d];
        if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;
        if (visited[nr, nc] || grid[nr, nc] == 0) continue;
        Dfs(nr, nc);
    }
}
```

```csharp
// Khử đệ quy bằng Stack — an toàn với đồ thị lớn
var st = new Stack<(int r, int c)>();
st.Push((sr, sc));
visited[sr, sc] = true;

while (st.Count > 0)
{
    var (r, c) = st.Pop();
    for (int d = 0; d < 4; d++)
    {
        int nr = r + dr[d], nc = c + dc[d];
        if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;
        if (visited[nr, nc] || grid[nr, nc] == 0) continue;
        visited[nr, nc] = true;
        st.Push((nr, nc));
    }
}
```

Đếm số vùng liên thông:

```csharp
int regions = 0;
for (int i = 0; i < rows; i++)
    for (int j = 0; j < cols; j++)
        if (grid[i, j] == 1 && !visited[i, j]) { Dfs(i, j); regions++; }
```

---

## 4. Binary search

### 4.1. Tìm giá trị / biên (lower bound, upper bound)

```csharp
// Vị trí đầu tiên có a[i] >= target
static int LowerBound(int[] a, int target)
{
    int lo = 0, hi = a.Length;             // hi = n (exclusive)
    while (lo < hi)
    {
        int mid = lo + (hi - lo) / 2;      // ⚠ tránh (lo+hi) tràn int
        if (a[mid] < target) lo = mid + 1;
        else hi = mid;
    }
    return lo;                             // = n nếu không có
}

// Vị trí đầu tiên có a[i] > target
static int UpperBound(int[] a, int target)
{
    int lo = 0, hi = a.Length;
    while (lo < hi)
    {
        int mid = lo + (hi - lo) / 2;
        if (a[mid] <= target) lo = mid + 1;
        else hi = mid;
    }
    return lo;
}

// Số lần xuất hiện của target = UpperBound - LowerBound
```

### 4.2. **Binary search trên đáp án** — dạng ra thi nhiều nhất

Nhận diện: đề hỏi *"giá trị nhỏ nhất / lớn nhất sao cho ... khả thi"*, và có tính **đơn điệu**
(nếu `x` khả thi thì mọi `x' > x` cũng khả thi).

```csharp
static bool Feasible(long x, ...)  // x có thoả điều kiện không?
{
    // ...
}

long lo = 1, hi = 1_000_000_000_000L;      // ⚠ hi phải đủ lớn, dùng long
long answer = hi;

while (lo <= hi)
{
    long mid = lo + (hi - lo) / 2;
    if (Feasible(mid))
    {
        answer = mid;      // ghi nhận rồi thử nhỏ hơn
        hi = mid - 1;
    }
    else lo = mid + 1;
}
return answer;
```

Ví dụ kinh điển: "thời gian ít nhất để làm xong tất cả", "kích thước lớn nhất có thể chia",
"tốc độ ăn chuối nhỏ nhất".

> ⚠ Bẫy: `Feasible` tính tổng → ép `long`, tổng thường tràn int.
> ⚠ Bẫy: vòng lặp vô hạn do cập nhật sai `lo`/`hi`. Nhớ: **nhận `mid` thì phải loại `mid`** ở lần sau
> (`hi = mid - 1` / `lo = mid + 1`).

---

## 5. Two pointer & sliding window

### 5.1. Hai con trỏ trên mảng đã sort (tìm cặp có tổng = target)

```csharp
int l = 0, r = a.Length - 1;
while (l < r)
{
    int sum = a[l] + a[r];
    if (sum == target) return (l, r);
    if (sum < target) l++;
    else r--;
}
```

### 5.2. Sliding window co giãn (dãy con dài nhất thoả điều kiện)

```csharp
int left = 0, best = 0;
var window = new Dictionary<char, int>();

for (int right = 0; right < s.Length; right++)
{
    // 1. mở rộng: thêm s[right] vào window
    window.TryGetValue(s[right], out int c);
    window[s[right]] = c + 1;

    // 2. co lại khi vi phạm điều kiện
    while (window.Count > k)
    {
        if (--window[s[left]] == 0) window.Remove(s[left]);
        left++;
    }

    // 3. cập nhật kết quả
    best = Math.Max(best, right - left + 1);
}
```

### 5.3. Cửa sổ cố định kích thước `k`

```csharp
long sum = 0, best = long.MinValue;
for (int i = 0; i < n; i++)
{
    sum += a[i];
    if (i >= k) sum -= a[i - k];      // đẩy phần tử ra khỏi cửa sổ
    if (i >= k - 1) best = Math.Max(best, sum);
}
```

---

## 6. Prefix sum & difference array

### 6.1. Prefix sum 1D — truy vấn tổng đoạn `O(1)`

```csharp
long[] pre = new long[n + 1];
for (int i = 0; i < n; i++) pre[i + 1] = pre[i] + a[i];

long SumRange(int l, int r) => pre[r + 1] - pre[l];   // tổng a[l..r] bao gồm cả 2 đầu
```

### 6.2. Prefix sum 2D

```csharp
long[,] pre = new long[rows + 1, cols + 1];
for (int i = 0; i < rows; i++)
    for (int j = 0; j < cols; j++)
        pre[i + 1, j + 1] = g[i, j] + pre[i, j + 1] + pre[i + 1, j] - pre[i, j];

long Rect(int r1, int c1, int r2, int c2) =>
    pre[r2 + 1, c2 + 1] - pre[r1, c2 + 1] - pre[r2 + 1, c1] + pre[r1, c1];
```

### 6.3. Difference array — "cộng `v` vào đoạn `[l, r]`" nhiều lần

Thay vì `O(n)` mỗi truy vấn, làm `O(1)` mỗi truy vấn + `O(n)` tổng kết:

```csharp
long[] diff = new long[n + 1];

void AddRange(int l, int r, long v) { diff[l] += v; diff[r + 1] -= v; }

// Khôi phục mảng thật
long[] a = new long[n];
long run = 0;
for (int i = 0; i < n; i++) { run += diff[i]; a[i] = run; }
```

> Rất hợp với các đề dạng "có m lượt, mỗi lượt cộng điểm cho học sinh từ a đến b".

---

## 7. Greedy + sort

**Khi nào:** chọn cục bộ tốt nhất mỗi bước → tối ưu toàn cục. Nhận diện: đề cho "tối đa số việc làm được",
"số lần ít nhất", "chia sao cho ...".

Mẫu kinh điển — chọn nhiều khoảng không giao nhau nhất (**sort theo điểm KẾT THÚC**):

```csharp
Array.Sort(intervals, (a, b) => a[1].CompareTo(b[1]));

int count = 0, lastEnd = int.MinValue;
foreach (var it in intervals)
{
    if (it[0] >= lastEnd) { count++; lastEnd = it[1]; }
}
```

Mẫu gộp khoảng (**sort theo điểm BẮT ĐẦU**):

```csharp
Array.Sort(intervals, (a, b) => a[0].CompareTo(b[0]));

var merged = new List<int[]>();
foreach (var it in intervals)
{
    if (merged.Count > 0 && it[0] <= merged[^1][1])
        merged[^1][1] = Math.Max(merged[^1][1], it[1]);
    else
        merged.Add(new[] { it[0], it[1] });
}
```

> ⚠ Greedy dễ **sai âm thầm**. Trước khi code, thử nghĩ 1 phản ví dụ. Không chắc → làm DP cho an toàn.

---

## 8. Dynamic programming

Ba câu hỏi bắt buộc trả lời trước khi code: **trạng thái là gì / công thức chuyển / giá trị khởi tạo**.

### 8.1. 1D — leo cầu thang, nhà cướp

```csharp
long[] dp = new long[n + 1];
dp[0] = 0; dp[1] = a[0];
for (int i = 2; i <= n; i++)
    dp[i] = Math.Max(dp[i - 1], dp[i - 2] + a[i - 1]);
```

### 8.2. Knapsack 0/1 (mỗi món dùng 1 lần)

```csharp
int[] dp = new int[capacity + 1];
for (int i = 0; i < n; i++)
    for (int w = capacity; w >= weight[i]; w--)          // ⚠ duyệt NGƯỢC
        dp[w] = Math.Max(dp[w], dp[w - weight[i]] + value[i]);
return dp[capacity];
```

Knapsack **không giới hạn** (dùng lại được) → duyệt `w` **xuôi** từ `weight[i]` đến `capacity`.

### 8.3. DP trên lưới — số đường đi / chi phí nhỏ nhất

```csharp
long[,] dp = new long[rows, cols];
dp[0, 0] = g[0, 0];
for (int i = 0; i < rows; i++)
    for (int j = 0; j < cols; j++)
    {
        if (i == 0 && j == 0) continue;
        long best = long.MaxValue;
        if (i > 0) best = Math.Min(best, dp[i - 1, j]);
        if (j > 0) best = Math.Min(best, dp[i, j - 1]);
        dp[i, j] = best + g[i, j];
    }
```

### 8.4. LIS — dãy con tăng dài nhất, `O(n log n)`

```csharp
var tails = new List<int>();
foreach (int x in a)
{
    int pos = tails.BinarySearch(x);
    if (pos < 0) pos = ~pos;              // ⚠ BinarySearch trả về ~insertionIndex
    if (pos == tails.Count) tails.Add(x);
    else tails[pos] = x;
}
return tails.Count;                        // tails KHÔNG phải LIS thật, chỉ đúng độ dài
```

### 8.5. DP có nhớ (memoization) — khi công thức truy hồi dễ nghĩ hơn vòng lặp

```csharp
long[,] memo;
bool[,] done;

long Solve(int i, int j)
{
    if (i == n) return 0;
    if (done[i, j]) return memo[i, j];
    done[i, j] = true;                     // ⚠ dùng cờ riêng, đừng dùng "memo == 0" làm mốc
    return memo[i, j] = Math.Max(Solve(i + 1, j), Solve(i + 1, j + 1) + a[i]);
}
```

---

## 9. Backtracking & sinh tổ hợp

**Khi nào:** `n` rất nhỏ (≤ 10–20), cần duyệt hết mọi cách.

### 9.1. Hoán vị

```csharp
void Permute(List<int> cur, bool[] used)
{
    if (cur.Count == n) { Check(cur); return; }
    for (int i = 0; i < n; i++)
    {
        if (used[i]) continue;
        used[i] = true;  cur.Add(a[i]);
        Permute(cur, used);
        cur.RemoveAt(cur.Count - 1);  used[i] = false;   // ⚠ nhớ hoàn tác
    }
}
```

### 9.2. Tổ hợp chọn `k` trong `n`

```csharp
void Combine(int start, List<int> cur, int k)
{
    if (cur.Count == k) { Check(cur); return; }
    for (int i = start; i < n; i++)
    {
        cur.Add(a[i]);
        Combine(i + 1, cur, k);       // i + 1 => không lặp lại; i => cho phép lặp
        cur.RemoveAt(cur.Count - 1);
    }
}
```

### 9.3. Duyệt mọi tập con bằng bitmask (`n ≤ 20`)

```csharp
for (int mask = 0; mask < (1 << n); mask++)
{
    var subset = new List<int>();
    for (int i = 0; i < n; i++)
        if ((mask >> i & 1) == 1) subset.Add(a[i]);
    // ...
}
```

---

## 10. Union-Find (DSU)

**Khi nào:** gộp nhóm, đếm số nhóm, kiểm tra "cùng nhóm chưa", Kruskal MST.

```csharp
class Dsu
{
    private readonly int[] _parent;
    private readonly int[] _size;

    public Dsu(int n)
    {
        _parent = new int[n];
        _size = new int[n];
        for (int i = 0; i < n; i++) { _parent[i] = i; _size[i] = 1; }
    }

    public int Find(int x)
    {
        while (_parent[x] != x) x = _parent[x] = _parent[_parent[x]];  // path halving
        return x;
    }

    public bool Union(int a, int b)
    {
        int ra = Find(a), rb = Find(b);
        if (ra == rb) return false;                 // đã cùng nhóm
        if (_size[ra] < _size[rb]) (ra, rb) = (rb, ra);
        _parent[rb] = ra;
        _size[ra] += _size[rb];
        return true;
    }

    public int SizeOf(int x) => _size[Find(x)];
}
```

Đếm số nhóm: `Enumerable.Range(0, n).Count(i => dsu.Find(i) == i);`

---

## 11. Dijkstra

**Khi nào:** đường đi ngắn nhất, cạnh có trọng số **không âm**.

```csharp
public long[] Dijkstra(List<(int to, int w)>[] adj, int src, int n)
{
    long[] dist = new long[n];
    Array.Fill(dist, long.MaxValue);
    dist[src] = 0;

    var pq = new PriorityQueue<int, long>();
    pq.Enqueue(src, 0);

    while (pq.TryDequeue(out int u, out long d))
    {
        if (d > dist[u]) continue;                  // ⚠ lazy deletion — bỏ bản cũ

        foreach (var (v, w) in adj[u])
        {
            long nd = d + w;
            if (nd < dist[v])
            {
                dist[v] = nd;
                pq.Enqueue(v, nd);
            }
        }
    }

    return dist;
}
```

Có trọng số **âm** → Bellman-Ford. Cần mọi cặp, `n ≤ 400` → Floyd–Warshall:

```csharp
for (int k = 0; k < n; k++)                // ⚠ k là vòng NGOÀI CÙNG
    for (int i = 0; i < n; i++)
        for (int j = 0; j < n; j++)
            if (d[i, k] + d[k, j] < d[i, j])
                d[i, j] = d[i, k] + d[k, j];
```

---

## 12. Simulation

Dạng PCCP Lv1–Lv2 ra rất nhiều: đề mô tả luật, mình làm y hệt. Nguyên tắc:

1. **Đọc lại đề 2 lần**, gạch chân mọi từ "bao gồm", "trước", "đồng thời", "lần lượt từ trái sang phải".
2. **Viết từng bước thành hàm nhỏ** — dễ debug hơn một vòng `for` 60 dòng.
3. **Chạy tay ví dụ trong đề** trước khi viết code, ghi ra bảng trung gian.
4. Cẩn thận **thứ tự cập nhật**: nhiều thực thể di chuyển "đồng thời" → cần lưu trạng thái cũ,
   tính hết vào bản mới rồi mới gán đè. Cập nhật tại chỗ sẽ khiến thực thể sau "nhìn thấy" trạng thái mới
   của thực thể trước → sai.

```csharp
// Cập nhật đồng thời: KHÔNG sửa grid trực tiếp
var next = (int[,])grid.Clone();
for (int i = 0; i < rows; i++)
    for (int j = 0; j < cols; j++)
        next[i, j] = ComputeNext(grid, i, j);   // đọc grid CŨ
grid = next;
```

Xoay vòng chỉ số (mảng tròn): `next = (cur + step) % n;` — với `step` âm: `((cur + step) % n + n) % n`.

---

## 13. Merge 2 mảng

**"Merge" có 6 nghĩa khác nhau — chọn sai là chậm hoặc sai kết quả.** Bảng chọn:

| Đề muốn gì | Dùng | Độ phức tạp |
|---|---|---|
| Chỉ nối lại, không quan tâm thứ tự | `a.Concat(b).ToArray()` | `O(n + m)` |
| Nối rồi cần có thứ tự, **input chưa sort** | `Concat` → `Sort` | `O((n+m) log(n+m))` |
| **Cả 2 mảng đã sort** | **two pointer** | `O(n + m)` ✅ |
| Gộp vào mảng `a` có sẵn chỗ trống | two pointer **từ CUỐI** | `O(n + m)`, `O(1)` bộ nhớ |
| Gộp `k` mảng đã sort | `PriorityQueue` | `O(N log k)` |
| Gộp theo khoá (cộng dồn số lượng) | `Dictionary` | `O(n + m)` |

### 13.1. Nối đơn thuần

```csharp
int[] c = a.Concat(b).ToArray();          // LINQ, gọn nhất
int[] c = [..a, ..b];                     // C# 12 — ⚠ grader .NET 6 KHÔNG có

// Không LINQ (nhanh nhất, dùng khi n lớn)
int[] c = new int[a.Length + b.Length];
Array.Copy(a, 0, c, 0, a.Length);
Array.Copy(b, 0, c, a.Length, b.Length);
```

> ⚠ `Concat` **giữ** phần tử trùng; `Union` **bỏ** trùng. Đọc kỹ đề xem cần cái nào.
> ```csharp
> new[]{1,2,3}.Concat(new[]{2,3,4})   // [1,2,3,2,3,4]
> new[]{1,2,3}.Union(new[]{2,3,4})    // [1,2,3,4]
> new[]{1,2,3}.Intersect(new[]{2,3,4})// [2,3]
> new[]{1,2,3}.Except(new[]{2,3,4})   // [1]
> ```

### 13.2. Hai mảng **đã sort** → two pointer (đây mới là "merge" thật)

```csharp
static int[] MergeSorted(int[] a, int[] b)
{
    int[] res = new int[a.Length + b.Length];
    int i = 0, j = 0, k = 0;

    while (i < a.Length && j < b.Length)
        res[k++] = a[i] <= b[j] ? a[i++] : b[j++];    // <= để giữ ổn định (a trước b)

    while (i < a.Length) res[k++] = a[i++];           // ⚠ đừng quên 2 vòng vét
    while (j < b.Length) res[k++] = b[j++];

    return res;
}
```

> ⚠ Đã sort sẵn mà còn `Concat().OrderBy()` là phí một hệ số `log n`.
> Với `n = 10^6` đây chính là chỗ khiến bài bị timeout.

### 13.3. Merge tại chỗ vào mảng `a` (LeetCode 88) — duyệt **từ CUỐI**

`a` dài `m + n`, `m` phần tử đầu là dữ liệu, phần đuôi để trống.

```csharp
static void MergeInPlace(int[] a, int m, int[] b, int n)
{
    int i = m - 1, j = n - 1, k = m + n - 1;
    while (j >= 0)
        a[k--] = (i >= 0 && a[i] > b[j]) ? a[i--] : b[j--];
}
```

> ⚠ Đi từ **đầu** sẽ đè lên phần tử của `a` chưa xử lý. Đi từ **cuối** thì ô đang ghi luôn nằm
> sau ô đang đọc → an toàn, không cần mảng phụ.

### 13.4. Merge `k` mảng đã sort → PriorityQueue

```csharp
static int[] MergeK(int[][] lists)
{
    var pq = new PriorityQueue<(int list, int idx), int>();
    for (int i = 0; i < lists.Length; i++)
        if (lists[i].Length > 0) pq.Enqueue((i, 0), lists[i][0]);

    var res = new List<int>();
    while (pq.TryDequeue(out var cur, out int val))
    {
        res.Add(val);
        int next = cur.idx + 1;
        if (next < lists[cur.list].Length)
            pq.Enqueue((cur.list, next), lists[cur.list][next]);
    }
    return res.ToArray();
}
```

> Heap chỉ giữ tối đa `k` phần tử → `O(N log k)`, tốt hơn nhiều so với gộp đôi một `O(N·k)`.

### 13.5. Merge theo khoá (gộp 2 danh sách, cộng dồn giá trị)

```csharp
// Cách 1 — Dictionary, rõ ràng nhất
var merged = new Dictionary<string, int>();
foreach (var (name, qty) in listA.Concat(listB))
{
    merged.TryGetValue(name, out int cur);
    merged[name] = cur + qty;
}

// Cách 2 — LINQ 1 dòng
var merged = listA.Concat(listB)
    .GroupBy(x => x.name)
    .ToDictionary(g => g.Key, g => g.Sum(x => x.qty));
```

Ghép cặp theo vị trí (không phải theo khoá) thì dùng `Zip`:

```csharp
var pairs = a.Zip(b, (x, y) => x + y).ToArray();   // dừng ở mảng NGẮN hơn
```

### 13.6. Merge sort — dùng chính bước merge ở 13.2

```csharp
static void MergeSort(int[] a, int lo, int hi)      // [lo, hi)
{
    if (hi - lo <= 1) return;
    int mid = lo + (hi - lo) / 2;
    MergeSort(a, lo, mid);
    MergeSort(a, mid, hi);
    // gộp a[lo..mid) và a[mid..hi) vào mảng tạm rồi copy ngược lại
}
```

> Trong bài thi hầu như **không cần tự viết** — `Array.Sort` đã đủ nhanh.
> Chỉ viết tay khi cần đếm số **cặp nghịch thế** (inversion count) trong lúc merge.
