# 02 — Cấu trúc dữ liệu

## Bảng chọn nhanh

| Cần gì | Dùng | Thêm | Tìm | Xoá |
|---|---|---|---|---|
| Mảng động | `List<T>` | O(1)* | O(n) | O(n) |
| Kiểm tra "đã gặp chưa" | `HashSet<T>` | O(1) | O(1) | O(1) |
| Đếm tần suất / map khoá → giá trị | `Dictionary<K,V>` | O(1) | O(1) | O(1) |
| FIFO (BFS) | `Queue<T>` | O(1) | — | O(1) |
| LIFO (DFS, ngoặc, undo) | `Stack<T>` | O(1) | — | O(1) |
| Lấy min/max liên tục | `PriorityQueue<TElem,TPrio>` | O(log n) | O(1) peek | O(log n) |
| Tập có thứ tự, tìm phần tử gần nhất | `SortedSet<T>` | O(log n) | O(log n) | O(log n) |
| Map có thứ tự theo khoá | `SortedDictionary<K,V>` | O(log n) | O(log n) | O(log n) |
| Thêm/xoá hai đầu | `LinkedList<T>` | O(1) | O(n) | O(1) nếu có node |

\* amortized.

---

## 1. `List<T>`

```csharp
var list = new List<int>();
var list = new List<int> { 1, 2, 3 };
var list = new List<int>(capacity: n);        // biết trước size -> tránh realloc
var list = Enumerable.Repeat(0, n).ToList();

list.Add(x);
list.AddRange(other);
list.Insert(0, x);        // O(n)
list.RemoveAt(i);         // O(n)
list.Remove(value);       // xoá phần tử ĐẦU TIÊN bằng value
list.RemoveAll(x => x < 0);
list[i]
list.Count
list.Contains(x)          // O(n) — cần nhanh thì đổi sang HashSet
list.IndexOf(x)
list.Sort();
list.Sort((a, b) => b.CompareTo(a));
list.Reverse();
list.ToArray();
list.Clear();
```

> ⚠ **Không xoá phần tử khi đang `foreach`** → `InvalidOperationException`.
> Xoá thì duyệt ngược `for (int i = list.Count - 1; i >= 0; i--)`, hoặc dùng `RemoveAll`.

---

## 2. `Dictionary<K,V>` — đếm tần suất, tra cứu

```csharp
var map = new Dictionary<string, int>();

map["a"] = 1;                       // thêm hoặc ghi đè
map.Add("a", 1);                    // ⚠ ném exception nếu key đã có
map.TryGetValue(k, out int v)       // an toàn nhất
map.ContainsKey(k)
map.Remove(k)
map.Count
map.Keys / map.Values
```

### Mẫu đếm tần suất (dùng cực nhiều)

```csharp
var cnt = new Dictionary<string, int>();
foreach (string w in words)
{
    cnt.TryGetValue(w, out int c);   // không có key -> c = 0
    cnt[w] = c + 1;
}

// Hoặc LINQ:
var cnt = words.GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
```

### Mẫu "map → list" (adjacency, gom nhóm)

```csharp
var adj = new Dictionary<int, List<int>>();

void AddEdge(int u, int v)
{
    if (!adj.TryGetValue(u, out var lst)) adj[u] = lst = new List<int>();
    lst.Add(v);
}
```

### Duyệt

```csharp
foreach (var (k, v) in map) { }
foreach (var kv in map) { kv.Key; kv.Value; }

// Lấy key có value lớn nhất
string best = map.OrderByDescending(kv => kv.Value).First().Key;
```

> ⚠ **Thứ tự duyệt `Dictionary` không đảm bảo.** Cần thứ tự → `SortedDictionary` hoặc `OrderBy`.

---

## 3. `HashSet<T>` — "đã gặp chưa"

```csharp
var set = new HashSet<int>();

bool isNew = set.Add(x);      // trả FALSE nếu đã tồn tại — mẫu chống trùng 1 dòng
set.Contains(x)
set.Remove(x)
set.Count

set.UnionWith(other);         // hợp
set.IntersectWith(other);     // giao
set.ExceptWith(other);        // hiệu
set.IsSubsetOf(other);
```

Mẫu chống trùng lặp:

```csharp
foreach (var w in words)
    if (seen.Add(w))          // chỉ vào đây lần đầu tiên
        answer++;
```

> ⚠ `HashSet<int[]>` / `HashSet<List<T>>` so sánh theo **tham chiếu** → không dedupe được.
> Muốn dedupe theo nội dung: dùng `HashSet<(int, int)>` (tuple so sánh theo giá trị)
> hoặc `HashSet<string>` với khoá `$"{a},{b}"`.

---

## 4. `Queue<T>` — BFS

```csharp
var q = new Queue<int>();
q.Enqueue(x);
int head = q.Dequeue();       // ⚠ rỗng -> exception
q.Peek();
q.Count;
q.TryDequeue(out int x);      // an toàn
```

---

## 5. `Stack<T>` — DFS, ngoặc, monotonic stack

```csharp
var st = new Stack<int>();
st.Push(x);
int top = st.Pop();
st.Peek();
st.Count;
st.TryPop(out int x);
```

Mẫu kiểm tra ngoặc hợp lệ:

```csharp
var st = new Stack<char>();
foreach (char c in s)
{
    if (c is '(' or '[' or '{') st.Push(c);
    else
    {
        if (st.Count == 0) return false;
        char open = st.Pop();
        if ((c == ')' && open != '(') ||
            (c == ']' && open != '[') ||
            (c == '}' && open != '{')) return false;
    }
}
return st.Count == 0;
```

---

## 6. `PriorityQueue<TElement, TPriority>` (.NET 6+) — min-heap

```csharp
var pq = new PriorityQueue<string, int>();   // ưu tiên NHỎ ra trước

pq.Enqueue("task", 5);
string top = pq.Dequeue();                   // phần tử có priority nhỏ nhất
pq.Peek();
pq.Count;
pq.TryDequeue(out string e, out int p);
pq.EnqueueDequeue(e, p);                     // nhanh hơn Enqueue rồi Dequeue

// Max-heap: đảo dấu priority
pq.Enqueue(x, -value);

// Nhiều tiêu chí: priority là tuple
var pq = new PriorityQueue<int, (int cost, int time)>();

// Comparer tuỳ ý
var pq = new PriorityQueue<int, int>(Comparer<int>.Create((a, b) => b - a));
```

> ⚠ `PriorityQueue` **không** có hàm cập nhật priority (decrease-key).
> Với Dijkstra: cứ push bản mới, khi pop ra thì bỏ qua nếu `dist` đã tốt hơn (lazy deletion).

---

## 7. `SortedSet<T>` / `SortedDictionary<K,V>` — cần thứ tự

```csharp
var ss = new SortedSet<int>();
ss.Add(x);
ss.Min; ss.Max;
ss.GetViewBetween(lo, hi);          // các phần tử trong [lo, hi]
ss.GetViewBetween(x, int.MaxValue).Min;   // phần tử >= x nhỏ nhất (lower_bound)

var sd = new SortedDictionary<string, int>();   // duyệt theo key tăng dần
```

`SortedList<K,V>` truy cập theo index được (`sl.Keys[i]`) nhưng chèn O(n) — chỉ dùng khi ít chèn.

---

## 8. Sắp xếp với comparer

```csharp
// Nhiều tiêu chí — cách dễ đọc nhất
people.Sort((a, b) =>
{
    int c = b.Score.CompareTo(a.Score);   // điểm giảm dần
    if (c != 0) return c;
    return string.Compare(a.Name, b.Name, StringComparison.Ordinal); // tên tăng dần
});

// Hoặc LINQ
var sorted = people
    .OrderByDescending(p => p.Score)
    .ThenBy(p => p.Name)
    .ToList();
```

> ⚠ Comparer viết `a - b` sẽ **tràn int** khi giá trị lớn → luôn dùng `a.CompareTo(b)`.
>
> ⚠ `List.Sort` / `Array.Sort` **không ổn định** (unstable). Cần giữ thứ tự gốc →
> dùng LINQ `OrderBy` (ổn định) hoặc thêm index làm khoá phụ.
