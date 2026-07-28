# Phần 3 — Collections & LINQ

[⬅️ Phần 2](interview.NET.02-Async-Threading.md) | [Mục lục](interview.NET.md) | [Phần 4 — OOP cơ bản ➡️](interview.NET.04-OOP-Basics.md)

---

## 41. Khi nào dùng `List<T>`, `Dictionary<K,V>`, `HashSet<T>`?

```csharp
// List<T>: có thứ tự, index O(1), tìm kiếm O(n)
var list = new List<string> { "a", "b", "c" };
var item = list[1];                   // O(1)
bool has = list.Contains("b");        // O(n) - phải duyệt

// Dictionary<K,V>: tra cứu theo key O(1)
var dict = new Dictionary<int, string> { [1] = "Alice", [2] = "Bob" };
var name = dict[1];                   // O(1)
dict.TryGetValue(3, out var v);       // ✅ an toàn, không throw

// HashSet<T>: tập hợp duy nhất, kiểm tra tồn tại O(1)
var set = new HashSet<int> { 1, 2, 3 };
bool exists = set.Contains(2);        // O(1)
set.Add(2);                            // bỏ qua (đã có)
```

- **Nguyên tắc**: tra cứu nhiều theo key → Dictionary; loại trùng/check tồn tại → HashSet; danh sách theo thứ tự → List.

---

## 42. Deferred execution trong LINQ là gì?

```csharp
var numbers = new List<int> { 1, 2, 3 };

// Query KHÔNG chạy ngay khi khai báo
var query = numbers.Where(n => n > 1); // chưa thực thi

numbers.Add(4); // thêm SAU khi khai báo query

// Chỉ chạy khi ENUMERATE
foreach (var n in query) Console.Write(n); // 2 3 4 (thấy cả 4!)

// ⚠️ Enumerate nhiều lần → chạy lại query
var count1 = query.Count(); // chạy query
var list1 = query.ToList(); // chạy LẠI query

// ✅ ToList() để "đóng băng" kết quả khi cần dùng nhiều lần
var materialized = numbers.Where(n => n > 1).ToList();
```

---

## 43. `IEnumerable<T>` khác `IQueryable<T>` như thế nào?

```csharp
// ❌ IEnumerable: filter IN-MEMORY (load cả bảng về rồi mới lọc!)
IEnumerable<User> users = dbContext.Users; // đã là IQueryable nhưng...
var adults = users.Where(u => u.Age >= 18).ToList();
// Nếu ép AsEnumerable trước:
var bad = dbContext.Users.AsEnumerable().Where(u => u.Age >= 18).ToList();
// → SELECT * FROM Users (load hết) rồi lọc ở C# → tốn RAM/băng thông

// ✅ IQueryable: filter TẠI DATABASE (dịch sang SQL)
IQueryable<User> query = dbContext.Users;
var good = query.Where(u => u.Age >= 18).ToList();
// → SELECT * FROM Users WHERE Age >= 18 (DB lọc)
```

- **Điểm nhấn**: giữ `IQueryable` càng lâu càng tốt để DB làm việc; đừng `AsEnumerable()` sớm.

---

## 44. `First` vs `FirstOrDefault` vs `Single` vs `SingleOrDefault`?

```csharp
var list = new List<int> { 1, 2, 3 };

list.First();               // 1
list.First(x => x > 1);     // 2
// new List<int>().First(); // ❌ throw InvalidOperationException (rỗng)

list.FirstOrDefault();      // 1
new List<int>().FirstOrDefault(); // 0 (default, không throw)

list.Single(x => x == 2);   // 2 (đúng 1 phần tử)
// list.Single(x => x > 1); // ❌ throw (có 2 phần tử > 1)

list.SingleOrDefault(x => x == 99); // 0 (không có → default)
```

- **Khi nào dùng `Single`**: khẳng định tính duy nhất (tìm theo primary key/unique).

---

## 45. `Select` khác `SelectMany` ở đâu?

```csharp
var orders = new[]
{
    new { Id = 1, Items = new[] { "A", "B" } },
    new { Id = 2, Items = new[] { "C" } }
};

// Select: 1-1, tạo collection LỒNG
IEnumerable<string[]> nested = orders.Select(o => o.Items);
// [["A","B"], ["C"]]

// SelectMany: FLATTEN thành 1 chuỗi phẳng
IEnumerable<string> flat = orders.SelectMany(o => o.Items);
// ["A", "B", "C"]
```

---

## 46. `Where().First()` và `First(predicate)` khác nhau?

```csharp
// Tương đương về logic, First(predicate) gọn hơn
var a = list.Where(x => x > 5).First();
var b = list.First(x => x > 5); // ưu tiên - dừng ngay khi tìm thấy

// Với EF Core cả hai dịch tương tự: SELECT TOP 1 ... WHERE
```

---

## 47. `GroupBy` trả về gì?

```csharp
var people = new[]
{
    new { Name = "Alice", City = "HN" },
    new { Name = "Bob", City = "HN" },
    new { Name = "Carol", City = "HCM" }
};

// Trả về IEnumerable<IGrouping<TKey, TElement>>
var groups = people.GroupBy(p => p.City);
foreach (var g in groups)
{
    Console.WriteLine($"{g.Key}: {g.Count()} người"); // HN: 2, HCM: 1
    foreach (var p in g) Console.WriteLine($"  {p.Name}");
}

// ⚠️ Với EF Core, GroupBy phức tạp có thể không dịch được → client evaluation
```

---

## 48. `ToList()` và `AsEnumerable()` khác nhau trong EF?

```csharp
// ToList(): thực thi query NGAY, materialize về memory
var list = dbContext.Users.Where(u => u.Age > 18).ToList();
// từ đây mọi thao tác là in-memory

// AsEnumerable(): chuyển sang LINQ-to-Objects nhưng vẫn DEFERRED
var query = dbContext.Users.AsEnumerable().Where(u => u.Age > 18);
// ⚠️ Where chạy IN-MEMORY (sau khi load cả bảng!)
```

---

## 49. `yield return` hoạt động thế nào?

```csharp
// Iterator lazy - trả từng phần tử, "tạm dừng" state của method
public IEnumerable<int> GetNumbers()
{
    Console.WriteLine("Bắt đầu");
    yield return 1;      // trả 1, tạm dừng
    Console.WriteLine("Tiếp");
    yield return 2;      // trả 2, tạm dừng
}

foreach (var n in GetNumbers()) Console.WriteLine(n);
// Bắt đầu → 1 → Tiếp → 2

// ✅ Hỗ trợ stream vô hạn (tiết kiệm bộ nhớ)
public IEnumerable<int> Fibonacci()
{
    int a = 0, b = 1;
    while (true) { yield return a; (a, b) = (b, a + b); }
}
var first10 = Fibonacci().Take(10).ToList();
```

---

## 50. Cách tránh N+1 query problem?

```csharp
// ❌ N+1: 1 query lấy orders + N query lấy customer mỗi order
var orders = dbContext.Orders.ToList();      // 1 query
foreach (var o in orders)
    Console.WriteLine(o.Customer.Name);       // N query (lazy load mỗi lần!)

// ✅ Cách 1: Include (eager loading) - 1 query với JOIN
var withInclude = dbContext.Orders
    .Include(o => o.Customer)
    .ToList();

// ✅ Cách 2: Projection - chỉ lấy field cần
var projected = dbContext.Orders
    .Select(o => new { o.Id, CustomerName = o.Customer.Name })
    .ToList();
```

---

## 51. `OrderBy` rồi `Where` có khác `Where` rồi `OrderBy`?

```csharp
// Kết quả giống nhau, nhưng NÊN Where trước để giảm số phần tử cần sort
// ❌ Sort cả triệu dòng rồi mới lọc
var slow = data.OrderBy(x => x.Date).Where(x => x.IsActive);

// ✅ Lọc trước, sort ít dòng hơn
var fast = data.Where(x => x.IsActive).OrderBy(x => x.Date);

// Với EF Core, query optimizer của DB thường tự tối ưu, nhưng viết đúng vẫn tốt
```

---

## 52. `Any()` khác `Count() > 0` như thế nào?

```csharp
// ❌ Count() duyệt TOÀN BỘ collection
if (list.Count() > 0) { }

// ✅ Any() dừng ngay khi có phần tử đầu tiên
if (list.Any()) { }

// Với EF Core:
// Any()  → SELECT EXISTS(...)  ← nhanh
// Count() → SELECT COUNT(*) ... ← đếm hết

bool hasAdult = dbContext.Users.Any(u => u.Age >= 18); // EXISTS, dừng sớm
```

- **Điểm nhấn**: cần biết "có tồn tại không" → luôn dùng `Any()`, không dùng `Count() > 0`.

---

[⬅️ Phần 2](interview.NET.02-Async-Threading.md) | [Mục lục](interview.NET.md) | [Phần 4 — OOP cơ bản ➡️](interview.NET.04-OOP-Basics.md)
