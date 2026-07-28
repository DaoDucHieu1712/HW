# Phần 7 — Cấu trúc dữ liệu & Giải thuật

[⬅️ Phần 6](interview.NET.06-DesignPatterns.md) | [Mục lục](interview.NET.md) | [Phần 8 — EF Core ➡️](interview.NET.08-EFCore.md)

> Trọng tâm middle: hiểu bản chất, biết **độ phức tạp collection .NET**, chọn đúng cấu trúc + nói được Big-O.

---

## 📊 Độ phức tạp & nền tảng

## DSA-1. Big-O notation là gì?

```csharp
// O(1) - hằng số: truy cập index, Dictionary lookup
int first = array[0];

// O(log n) - chia đôi: binary search
// O(n) - tuyến tính: duyệt 1 lần
foreach (var x in array) { }

// O(n log n) - sort hiệu quả
Array.Sort(array);

// O(n²) - vòng lặp lồng
for (int i = 0; i < n; i++)
    for (int j = 0; j < n; j++) { }
```

- **Thứ tự**: O(1) < O(log n) < O(n) < O(n log n) < O(n²) < O(2ⁿ) < O(n!). Bỏ hằng số: `O(2n+5)` → `O(n)`.

---

## DSA-2. Time complexity vs Space complexity?

```csharp
// ❌ O(n²) time, O(1) space - so từng cặp
bool HasDuplicateSlow(int[] arr)
{
    for (int i = 0; i < arr.Length; i++)
        for (int j = i + 1; j < arr.Length; j++)
            if (arr[i] == arr[j]) return true;
    return false;
}

// ✅ O(n) time, O(n) space - đánh đổi bộ nhớ lấy tốc độ
bool HasDuplicateFast(int[] arr)
{
    var seen = new HashSet<int>();
    foreach (var x in arr)
        if (!seen.Add(x)) return true; // Add trả false nếu đã có
    return false;
}
```

---

## DSA-3. Amortized complexity? Ví dụ `List<T>.Add`?

```csharp
var list = new List<int>(); // Capacity = 0
// Add thường O(1), nhưng khi đầy phải resize (cấp mảng gấp đôi + copy = O(n))
// Vì resize hiếm → AMORTIZED O(1)
for (int i = 0; i < 100; i++) list.Add(i); // resize ở 4,8,16,32,64,128...

// ✅ Biết trước số lượng → khởi tạo capacity, tránh resize
var optimized = new List<int>(100);
```

---

## 📦 Cấu trúc tuyến tính

## DSA-4. Array vs Linked List?

| Thao tác | Array | Linked List |
|----------|-------|-------------|
| Truy cập index | O(1) | O(n) |
| Chèn/xoá đầu | O(n) | O(1) |
| Chèn/xoá cuối | O(1)* | O(1) có tail |

```csharp
var list = new List<int> { 1, 2, 3 };  // array động - cache-friendly
int x = list[1];                         // O(1)

var linked = new LinkedList<int>();      // node + con trỏ
linked.AddFirst(1);                      // O(1)
linked.AddLast(2);                       // O(1)
// linked[1]; // ❌ không có index, phải duyệt O(n)
```

---

## DSA-5. Stack và Queue?

```csharp
// Stack (LIFO): undo/redo, DFS, kiểm tra ngoặc
var stack = new Stack<int>();
stack.Push(1); stack.Push(2);
Console.WriteLine(stack.Pop()); // 2 (vào sau ra trước)

// Kiểm tra ngoặc cân bằng
bool IsBalanced(string s)
{
    var st = new Stack<char>();
    foreach (var c in s)
    {
        if (c == '(') st.Push(c);
        else if (c == ')') { if (st.Count == 0) return false; st.Pop(); }
    }
    return st.Count == 0;
}

// Queue (FIFO): task queue, BFS
var queue = new Queue<int>();
queue.Enqueue(1); queue.Enqueue(2);
Console.WriteLine(queue.Dequeue()); // 1 (vào trước ra trước)
```

---

## DSA-6. Hash table hoạt động thế nào? Xử lý collision?

```csharp
// hash(key) → bucket index → O(1) trung bình
// Collision (2 key cùng bucket): .NET Dictionary dùng CHAINING (linked list trong bucket)

// GetHashCode tốt → ít collision → giữ O(1)
public class Point
{
    public int X, Y;
    // ✅ Hash phân bố đều
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public override bool Equals(object o) => o is Point p && p.X == X && p.Y == Y;
}
// ❌ Hash kém (luôn trả 0) → mọi key cùng bucket → O(n)!
```

---

## DSA-7. `Dictionary` và `HashSet` — độ phức tạp & khi nào dùng?

```csharp
// Cả hai: thêm/tìm/xoá O(1) trung bình
var dict = new Dictionary<string, int>(); // key → value
dict["a"] = 1;
dict.TryGetValue("a", out int v); // O(1)

var set = new HashSet<int> { 1, 2, 3 };   // phần tử duy nhất
set.Contains(2);                           // O(1)
set.UnionWith(new[] { 3, 4 });             // phép tập hợp

// Bài toán two-sum O(n) nhờ Dictionary
int[] TwoSum(int[] nums, int target)
{
    var map = new Dictionary<int, int>();
    for (int i = 0; i < nums.Length; i++)
    {
        if (map.TryGetValue(target - nums[i], out int j)) return new[] { j, i };
        map[nums[i]] = i;
    }
    return Array.Empty<int>();
}
```

---

## 🌳 Cấu trúc phi tuyến

## DSA-8. Binary Tree vs Binary Search Tree (BST)?

```csharp
public class TreeNode
{
    public int Value;
    public TreeNode? Left, Right;
}

// BST: trái < node < phải → tìm kiếm O(log n) nếu cân bằng
bool Search(TreeNode? node, int target)
{
    if (node == null) return false;
    if (target == node.Value) return true;
    return target < node.Value
        ? Search(node.Left, target)   // đi trái
        : Search(node.Right, target); // đi phải
}
// ⚠️ BST lệch (insert 1,2,3,4...) → suy biến O(n) như linked list
// → cần self-balancing: AVL, Red-Black (SortedDictionary/SortedSet dùng Red-Black)
```

---

## DSA-9. `SortedDictionary` vs `Dictionary`?

```csharp
var dict = new Dictionary<int, string> { [3] = "c", [1] = "a", [2] = "b" };
// Duyệt: thứ tự KHÔNG đảm bảo, O(1) lookup

var sorted = new SortedDictionary<int, string> { [3] = "c", [1] = "a", [2] = "b" };
foreach (var kv in sorted) Console.Write(kv.Key); // 123 (đã sắp!), O(log n)
```

- `SortedDictionary`: Red-Black tree, O(log n), giữ thứ tự key. `SortedList`: mảng sắp, tra cứu O(log n), chèn O(n).

---

## DSA-10. Heap (Priority Queue)?

```csharp
// Cây thoả heap property: min-heap cha ≤ con. Lấy min O(1), chèn/xoá O(log n)
var pq = new PriorityQueue<string, int>(); // (element, priority)
pq.Enqueue("thấp", 3);
pq.Enqueue("cao", 1);
pq.Enqueue("vừa", 2);
Console.WriteLine(pq.Dequeue()); // "cao" (priority nhỏ nhất ra trước)

// Ứng dụng: top-K phần tử lớn nhất
int[] TopK(int[] nums, int k)
{
    var minHeap = new PriorityQueue<int, int>();
    foreach (var n in nums)
    {
        minHeap.Enqueue(n, n);
        if (minHeap.Count > k) minHeap.Dequeue(); // giữ k lớn nhất
    }
    return minHeap.UnorderedItems.Select(x => x.Element).ToArray();
}
```

---

## DSA-11. Graph — biểu diễn và duyệt?

```csharp
// Adjacency list (tiết kiệm cho đồ thị thưa)
var graph = new Dictionary<int, List<int>>
{
    [1] = new() { 2, 3 },
    [2] = new() { 4 },
    [3] = new() { 4 },
    [4] = new()
};

// BFS: queue, tìm đường ngắn nhất không trọng số
void Bfs(int start)
{
    var visited = new HashSet<int>();
    var queue = new Queue<int>();
    queue.Enqueue(start); visited.Add(start);
    while (queue.Count > 0)
    {
        var node = queue.Dequeue();
        Console.Write(node + " ");
        foreach (var next in graph[node])
            if (visited.Add(next)) queue.Enqueue(next);
    }
}

// DFS: stack/đệ quy, phát hiện chu trình, topological sort
void Dfs(int node, HashSet<int> visited)
{
    if (!visited.Add(node)) return;
    Console.Write(node + " ");
    foreach (var next in graph[node]) Dfs(next, visited);
}
```

---

## 🔀 Thuật toán kinh điển

## DSA-12. So sánh các thuật toán sắp xếp?

| Thuật toán | Trung bình | Worst | Space | Ổn định |
|-----------|-----------|-------|-------|---------|
| QuickSort | O(n log n) | O(n²) | O(log n) | Không |
| MergeSort | O(n log n) | O(n log n) | O(n) | Có |
| HeapSort | O(n log n) | O(n log n) | O(1) | Không |
| BubbleSort | O(n²) | O(n²) | O(1) | Có |

```csharp
// .NET Array.Sort dùng introsort (Quick + Heap + Insertion)
Array.Sort(arr);
// LINQ OrderBy dùng STABLE sort (giữ thứ tự phần tử bằng nhau)
var sorted = arr.OrderBy(x => x).ToList();

// QuickSort minh hoạ
void QuickSort(int[] a, int lo, int hi)
{
    if (lo >= hi) return;
    int pivot = a[hi], i = lo - 1;
    for (int j = lo; j < hi; j++)
        if (a[j] < pivot) { i++; (a[i], a[j]) = (a[j], a[i]); }
    (a[i + 1], a[hi]) = (a[hi], a[i + 1]);
    QuickSort(a, lo, i); QuickSort(a, i + 2, hi);
}
```

---

## DSA-13. Binary Search? Điều kiện áp dụng?

```csharp
// Yêu cầu: mảng ĐÃ SẮP. O(log n)
int BinarySearch(int[] arr, int target)
{
    int low = 0, high = arr.Length - 1;
    while (low <= high)
    {
        int mid = low + (high - low) / 2; // ✅ tránh overflow (KHÔNG dùng (low+high)/2)
        if (arr[mid] == target) return mid;
        if (arr[mid] < target) low = mid + 1;
        else high = mid - 1;
    }
    return -1;
}

// .NET built-in
int idx = Array.BinarySearch(sortedArr, target);
```

---

## DSA-14. Đệ quy — ưu nhược điểm? Stack overflow?

```csharp
// ❌ Đệ quy sâu → StackOverflowException (C# không tối ưu tail-call)
long FactorialRecursive(int n) => n <= 1 ? 1 : n * FactorialRecursive(n - 1);
// FactorialRecursive(100000); // crash!

// ✅ Chuyển sang vòng lặp
long FactorialIterative(int n)
{
    long result = 1;
    for (int i = 2; i <= n; i++) result *= i;
    return result;
}
```

---

## DSA-15. Dynamic Programming? Khác Divide & Conquer?

```csharp
// ❌ Fibonacci đệ quy thuần: O(2ⁿ) - tính lại bài con chồng lặp
int FibSlow(int n) => n <= 1 ? n : FibSlow(n - 1) + FibSlow(n - 2);

// ✅ DP với memoization: O(n) - lưu kết quả bài con
int FibDp(int n, Dictionary<int, int> memo)
{
    if (n <= 1) return n;
    if (memo.TryGetValue(n, out int cached)) return cached;
    return memo[n] = FibDp(n - 1, memo) + FibDp(n - 2, memo);
}

// ✅ DP tabulation (bottom-up)
int FibTab(int n)
{
    if (n <= 1) return n;
    int a = 0, b = 1;
    for (int i = 2; i <= n; i++) (a, b) = (b, a + b);
    return b;
}
```

- **DP**: bài con **chồng lặp**, lưu kết quả. **Divide&Conquer** (MergeSort): bài con **độc lập**.

---

## DSA-16. Greedy khác DP thế nào?

```csharp
// Greedy: chọn tối ưu CỤC BỘ mỗi bước - nhanh nhưng không luôn đúng
// ✅ Greedy đúng: đổi tiền với hệ mệnh giá "chuẩn" (1,2,5,10...)
int CoinChangeGreedy(int amount, int[] coins) // coins sắp giảm dần
{
    int count = 0;
    foreach (var coin in coins)
    {
        count += amount / coin;
        amount %= coin;
    }
    return count;
}
// ❌ Greedy SAI với mệnh giá bất kỳ (vd {1,3,4}, amount=6) → cần DP
```

---

## DSA-17. Two pointers và Sliding window?

```csharp
// Two pointers: mảng đã sắp → two-sum O(n)
int[] TwoSumSorted(int[] arr, int target)
{
    int left = 0, right = arr.Length - 1;
    while (left < right)
    {
        int sum = arr[left] + arr[right];
        if (sum == target) return new[] { left, right };
        if (sum < target) left++; else right--;
    }
    return Array.Empty<int>();
}

// Sliding window: chuỗi con dài nhất không lặp ký tự - O(n)
int LongestUnique(string s)
{
    var seen = new HashSet<char>();
    int left = 0, max = 0;
    for (int right = 0; right < s.Length; right++)
    {
        while (seen.Contains(s[right])) seen.Remove(s[left++]); // co window
        seen.Add(s[right]);
        max = Math.Max(max, right - left + 1);
    }
    return max;
}
```

---

## DSA-18. Phát hiện chu trình / duplicate?

```csharp
// Duplicate: HashSet O(n)
bool HasDuplicate(int[] arr) => arr.Length != arr.Distinct().Count();

// Floyd's cycle detection (rùa & thỏ) - O(1) space
bool HasCycle(ListNode? head)
{
    ListNode? slow = head, fast = head;
    while (fast?.Next != null)
    {
        slow = slow!.Next;         // đi 1 bước
        fast = fast.Next.Next;     // đi 2 bước
        if (slow == fast) return true; // gặp nhau → có chu trình
    }
    return false;
}
```

---

## 🧮 Áp dụng thực tế trong .NET

## DSA-19. Chọn collection .NET nào cho tình huống nào?

```csharp
// Tra cứu theo key nhanh          → Dictionary<K,V>
// Check tồn tại / loại trùng       → HashSet<T>
// Danh sách index, duyệt tuần tự   → List<T>
// Chèn/xoá 2 đầu nhiều             → LinkedList<T>
// Cần thứ tự sắp xếp               → SortedDictionary / SortedSet
// Ưu tiên                          → PriorityQueue
// Đa luồng đọc nhiều/ghi ít        → ConcurrentDictionary
var concurrent = new ConcurrentDictionary<int, string>();
concurrent.GetOrAdd(1, id => "value"); // thread-safe
```

---

## DSA-20. `List<T>` bên trong hoạt động thế nào? Capacity vs Count?

```csharp
var list = new List<int>();
Console.WriteLine($"Count={list.Count}, Capacity={list.Capacity}"); // 0, 0

for (int i = 0; i < 5; i++) list.Add(i);
Console.WriteLine($"Count={list.Count}, Capacity={list.Capacity}"); // 5, 8
// Count = số phần tử thực; Capacity = kích thước mảng T[] nội bộ
// Khi Count > Capacity → cấp mảng GẤP ĐÔI + copy (O(n) tại thời điểm đó)

// ✅ Biết trước → set capacity tránh resize nhiều lần
var optimized = new List<int>(1000);
```

---

[⬅️ Phần 6](interview.NET.06-DesignPatterns.md) | [Mục lục](interview.NET.md) | [Phần 8 — EF Core ➡️](interview.NET.08-EFCore.md)
