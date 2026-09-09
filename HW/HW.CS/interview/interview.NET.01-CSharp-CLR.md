# Phần 1 — C# Ngôn ngữ & CLR

[⬅️ Về mục lục](interview.NET.md) | Tiếp theo: [Phần 2 — Async & Threading ➡️](interview.NET.02-Async-Threading.md)

> Mỗi câu: định nghĩa → điểm nhấn → code minh hoạ (❌ sai / ✅ đúng) → trade-off.
>
> 🔬 **Muốn đào sâu "bên dưới chạy ra sao"?** File này trả lời *cái gì / khi nào*. Phần cơ chế —
> object layout, MethodTable, boxing ở mức IL, JIT/tiering, generic sharing — nằm ở
> [Phần 11 — Runtime Internals](interview.NET.11-Runtime-Internals.md); còn GC, heap, LOH,
> finalizer, `Span<T>` ở [Phần 12 — Memory & GC Internals](interview.NET.12-Memory-GC-Internals.md).

---

## 1. Sự khác nhau giữa value type và reference type?

- **Value type** (struct, enum, int, bool, decimal, DateTime): lưu trực tiếp dữ liệu, gán = **copy giá trị**.
- **Reference type** (class, interface, delegate, array, string): lưu **tham chiếu** trỏ đến vùng nhớ heap, gán = copy địa chỉ.

```csharp
// Value type: copy giá trị → 2 biến độc lập
int a = 10;
int b = a;      // copy giá trị
b = 20;
Console.WriteLine(a); // 10 (a KHÔNG đổi)

// Reference type: copy tham chiếu → 2 biến trỏ cùng object
var list1 = new List<int> { 1, 2, 3 };
var list2 = list1;    // copy tham chiếu
list2.Add(4);
Console.WriteLine(list1.Count); // 4 (list1 CŨNG đổi!)

// Struct (value) vs class (reference) khi truyền vào method
struct PointStruct { public int X; }
class PointClass { public int X; }

void Mutate(PointStruct p) => p.X = 99;   // sửa bản sao
void Mutate(PointClass p)  => p.X = 99;   // sửa object gốc

var ps = new PointStruct { X = 1 };
Mutate(ps);
Console.WriteLine(ps.X); // 1  (không đổi - truyền bản sao)

var pc = new PointClass { X = 1 };
Mutate(pc);
Console.WriteLine(pc.X); // 99 (đổi - cùng object)
```

- **Trade-off**: value type nhỏ tránh GC pressure; reference type linh hoạt, chia sẻ state.

---

## 2. `struct` khác `class` như thế nào?

- `struct` = value type, không kế thừa (chỉ implement interface), không destructor.
- `class` = reference type, kế thừa đầy đủ, có thể null.

```csharp
// ❌ struct KHÔNG kế thừa được
struct MyStruct : BaseClass { }   // Compile error

// ✅ struct chỉ implement interface
struct Money : IComparable<Money>
{
    public decimal Amount { get; }
    public Money(decimal amount) => Amount = amount;
    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);
}

// struct nên IMMUTABLE (readonly struct để tránh bug copy)
readonly struct Point
{
    public int X { get; }
    public int Y { get; }
    public Point(int x, int y) => (X, Y) = (x, y);
}
```

- **Khi nào dùng struct**: object nhỏ (≤16 byte), immutable, vòng đời ngắn (`Point`, `DateTime`, `Guid`). Còn lại dùng `class`.

---

## 3. `string` là value type hay reference type? Tại sao "immutable"?

- `string` là **reference type** nhưng có ngữ nghĩa value (so sánh bằng giá trị, immutable).
- Mỗi thao tác "sửa" chuỗi tạo object mới.

```csharp
string s = "Hello";
string t = s;
s += " World";          // Tạo string MỚI, s trỏ object mới
Console.WriteLine(t);   // "Hello" (t vẫn trỏ object cũ)

// ❌ Nối chuỗi trong vòng lặp → tạo N object rác, O(n²)
string result = "";
for (int i = 0; i < 10000; i++)
    result += i;        // mỗi lần tạo string mới!

// ✅ Dùng StringBuilder → sửa buffer tại chỗ, O(n)
var sb = new StringBuilder();
for (int i = 0; i < 10000; i++)
    sb.Append(i);
string fast = sb.ToString();
```

- **Trade-off**: immutable → thread-safe, cache/intern được; nhưng nối nhiều lần tốn bộ nhớ → dùng `StringBuilder`.

---

## 4. Boxing và unboxing là gì? Ảnh hưởng hiệu năng?

- **Boxing**: value type → `object` (cấp phát heap + copy). **Unboxing**: ngược lại (cần ép kiểu).

```csharp
int number = 42;
object boxed = number;        // BOXING: cấp phát trên heap + copy
int unboxed = (int)boxed;     // UNBOXING: ép kiểu + copy về stack

// ❌ ArrayList gây boxing mỗi phần tử value type
var oldList = new ArrayList();
oldList.Add(1);               // boxing int → object
int x = (int)oldList[0];      // unboxing

// ✅ Generic List<int> KHÔNG boxing
var list = new List<int>();
list.Add(1);                  // không boxing, lưu int trực tiếp
int y = list[0];              // không unboxing

// Bẫy boxing ẩn: gọi method interface trên struct qua object
IComparable c = 5;            // boxing!
```

- **Trade-off**: boxing gây allocation + GC pressure. Generics ra đời để tránh boxing.

---

## 5. Sự khác nhau giữa `const` và `readonly`?

- `const`: compile-time, gán khi khai báo, ngầm `static`, **inline** vào nơi dùng.
- `readonly`: runtime, gán ở khai báo hoặc constructor, mỗi instance có thể khác.

```csharp
public class Config
{
    public const int MaxRetries = 3;        // compile-time, cố định
    public readonly DateTime CreatedAt;      // runtime
    public readonly string Environment;

    public Config(string env)
    {
        CreatedAt = DateTime.Now;            // ✅ gán readonly trong constructor
        Environment = env;
    }
}

// ⚠️ Bẫy const qua assembly: nếu AssemblyA có const PI = 3.14
// và AssemblyB dùng nó, đổi PI trong A mà KHÔNG build lại B
// → B vẫn giữ giá trị cũ (vì const bị inline lúc biên dịch B).
// → Với giá trị có thể đổi giữa các bản release, dùng static readonly.
public static readonly int Version = 2;
```

- **Trade-off**: `const` nhanh (inline) nhưng cứng nhắc; `readonly` linh hoạt hơn.

---

## 6. `ref`, `out`, và `in` khác nhau ra sao?

- `ref`: tham chiếu 2 chiều, biến phải khởi tạo trước.
- `out`: chỉ ra, không cần khởi tạo trước nhưng **bắt buộc gán** trong method.
- `in`: tham chiếu **chỉ đọc**, tối ưu cho struct lớn.

```csharp
// ref: đọc + ghi
void Double(ref int x) => x *= 2;
int n = 5;
Double(ref n);
Console.WriteLine(n); // 10

// out: trả nhiều giá trị (như TryParse)
if (int.TryParse("123", out int parsed))
    Console.WriteLine(parsed); // 123

// out phải được gán trong method
bool TryGetAge(string s, out int age)
{
    age = 0;                    // ✅ bắt buộc gán
    return int.TryParse(s, out age);
}

// in: readonly ref cho struct lớn, tránh copy
readonly struct BigStruct { public readonly long A, B, C, D; }
double Compute(in BigStruct s) => s.A + s.B; // không copy, không sửa được s
```

---

## 7. `var`, `dynamic`, và `object` khác nhau thế nào?

- `var`: suy luận kiểu **compile-time**, vẫn static typing.
- `dynamic`: bỏ kiểm tra kiểu lúc biên dịch, resolve **runtime**.
- `object`: kiểu gốc, cần ép kiểu tường minh.

```csharp
var name = "Hello";     // compiler biết là string
name.Length;            // ✅ IntelliSense đầy đủ
// name = 5;            // ❌ compile error - đã là string

dynamic d = "Hello";
d.Length;               // ✅ OK lúc chạy
d.FooBar();             // ✅ compile OK nhưng ❌ RUNTIME crash (RuntimeBinderException)

object o = "Hello";
// o.Length;            // ❌ compile error - object không có Length
int len = ((string)o).Length; // phải ép kiểu
```

- **Trade-off**: `dynamic` linh hoạt (interop COM, JSON động) nhưng mất type safety, chậm hơn.

---

## 8. IEnumerable, ICollection, IList khác nhau ở đâu?

```csharp
IEnumerable<int> e = new[] { 1, 2, 3 };
foreach (var x in e) { }        // ✅ chỉ duyệt được
// e.Count; e.Add(4);           // ❌ không có

ICollection<int> c = new List<int> { 1, 2, 3 };
c.Add(4);                       // ✅ có Add, Remove, Count
Console.WriteLine(c.Count);     // ✅ 4
// c[0];                        // ❌ không truy cập theo index

IList<int> list = new List<int> { 1, 2, 3 };
list.Insert(0, 99);             // ✅ có index
Console.WriteLine(list[0]);     // ✅ 99
```

- **Nguyên tắc**: nhận tham số ở kiểu **hẹp nhất đủ dùng** (thường `IEnumerable<T>`) để linh hoạt.

---

## 9. Garbage Collector và các generation (Gen 0, 1, 2)?

- GC thu hồi object không còn tham chiếu. Chia generation để tối ưu.

```csharp
// Gen 0: object mới, thu hồi thường xuyên & nhanh
var temp = new byte[100];       // sinh ở Gen 0

// Object sống sót GC → thăng cấp Gen 1 → Gen 2
// Object ≥ 85KB → Large Object Heap (LOH), thuộc Gen 2
var large = new byte[100_000];  // vào LOH

// Kiểm tra generation
var obj = new object();
Console.WriteLine(GC.GetGeneration(obj)); // 0

GC.Collect(); // ❌ KHÔNG gọi thủ công trong production (trừ khi đo được lợi ích)
Console.WriteLine(GC.GetGeneration(obj)); // 1 (đã thăng cấp)
```

- **Điểm nhấn**: Gen 0 nhanh, Gen 2 tốn kém. Giữ object sống lâu không cần thiết → tăng chi phí Gen 2.

---

## 10. `IDisposable` và `using` dùng để làm gì?

- Giải phóng **unmanaged resource** (file, socket, DB connection) mà GC không quản lý.

```csharp
// ❌ Không dispose → rò rỉ file handle
var file = new StreamReader("data.txt");
var content = file.ReadToEnd();
// quên file.Dispose() → handle bị giữ

// ✅ using đảm bảo Dispose kể cả khi exception (biên dịch thành try/finally)
using (var reader = new StreamReader("data.txt"))
{
    var text = reader.ReadToEnd();
} // Dispose() tự gọi ở đây

// ✅ using declaration (C# 8) - dispose ở cuối scope
void Process()
{
    using var conn = new SqlConnection(connStr);
    conn.Open();
    // ... conn.Dispose() tự gọi khi hết method
}
```

---

## 11. Finalizer, Dispose và Dispose Pattern chuẩn?

```csharp
public class ResourceHolder : IDisposable
{
    private IntPtr _unmanagedHandle;   // unmanaged
    private StreamReader _managed;     // managed
    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);     // ✅ bỏ finalizer khỏi queue → không chạy 2 lần
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
            _managed?.Dispose();       // chỉ giải phóng managed khi gọi từ Dispose()
        // giải phóng unmanaged (luôn luôn)
        // FreeHandle(_unmanagedHandle);
        _disposed = true;
    }

    ~ResourceHolder() => Dispose(false); // finalizer: lưới an toàn, chỉ dọn unmanaged
}
```

- **Điểm nhấn**: object có finalizer sống thêm ≥1 chu kỳ GC → tốn kém; `SuppressFinalize` để tránh.

---

## 12. `==` và `.Equals()` khác nhau như thế nào?

```csharp
// Reference type mặc định: cả == và Equals so sánh THAM CHIẾU
var a = new object();
var b = new object();
Console.WriteLine(a == b);        // False
Console.WriteLine(a.Equals(b));   // False

// string OVERLOAD == để so sánh GIÁ TRỊ
string s1 = "hello";
string s2 = new string("hello".ToCharArray());
Console.WriteLine(s1 == s2);          // True (giá trị)
Console.WriteLine(ReferenceEquals(s1, s2)); // False (khác object)

// Khi override Equals PHẢI override GetHashCode
public class Person
{
    public string Name { get; set; }
    public override bool Equals(object obj) =>
        obj is Person p && p.Name == Name;
    public override int GetHashCode() => Name?.GetHashCode() ?? 0;
}
```

---

## 13. Tại sao override `Equals` phải override `GetHashCode`?

```csharp
// ❌ Override Equals nhưng KHÔNG override GetHashCode
public class BadKey
{
    public int Id;
    public override bool Equals(object obj) => obj is BadKey k && k.Id == Id;
    // thiếu GetHashCode!
}

var dict = new Dictionary<BadKey, string>();
dict[new BadKey { Id = 1 }] = "A";
var found = dict.ContainsKey(new BadKey { Id = 1 });
Console.WriteLine(found); // False! (hashcode khác → bucket khác → không tìm thấy)

// ✅ Override cả hai nhất quán
public class GoodKey
{
    public int Id;
    public override bool Equals(object obj) => obj is GoodKey k && k.Id == Id;
    public override int GetHashCode() => Id.GetHashCode();
}
```

- **Quy tắc**: object bằng nhau ⇒ hashcode bằng nhau.

---

## 14. `IEquatable<T>` để làm gì?

- Cung cấp `Equals(T)` type-safe, tránh boxing khi so sánh value type trong collection.

```csharp
public struct Temperature : IEquatable<Temperature>
{
    public double Celsius { get; }
    public Temperature(double c) => Celsius = c;

    public bool Equals(Temperature other) => Celsius == other.Celsius; // ✅ không boxing
    public override bool Equals(object obj) => obj is Temperature t && Equals(t);
    public override int GetHashCode() => Celsius.GetHashCode();
}

// List<Temperature>.Contains() dùng IEquatable.Equals → không boxing
var temps = new List<Temperature> { new(20), new(25) };
temps.Contains(new Temperature(20)); // nhanh, type-safe
```

---

## 15. Extension method là gì? Cơ chế hoạt động?

```csharp
public static class StringExtensions
{
    // method static + tham số đầu có 'this'
    public static bool IsNullOrEmpty(this string value)
        => string.IsNullOrEmpty(value);

    public static string Truncate(this string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";
}

// Gọi như method của instance (thực chất là syntactic sugar)
string name = "Hello World";
bool empty = name.IsNullOrEmpty();          // = StringExtensions.IsNullOrEmpty(name)
string t = name.Truncate(5);                // "Hello..."

// LINQ chính là extension method trên IEnumerable<T>
var evens = new[] { 1, 2, 3, 4 }.Where(x => x % 2 == 0);
```

- **Trade-off**: mở rộng type có sẵn không cần sửa; nhưng không truy cập được private member.

---

## 16. Delegate là gì? `Func`, `Action`, `Predicate` khác nhau?

```csharp
// Delegate = con trỏ hàm type-safe
Action greet = () => Console.WriteLine("Hi");        // không trả về
Action<string> log = msg => Console.WriteLine(msg);  // nhận tham số, void
Func<int, int, int> add = (a, b) => a + b;           // trả về int
Predicate<int> isEven = n => n % 2 == 0;             // trả về bool

greet();
log("Hello");
Console.WriteLine(add(2, 3));      // 5
Console.WriteLine(isEven(4));      // True

// Multicast delegate: gộp nhiều method
Action pipeline = () => Console.Write("A");
pipeline += () => Console.Write("B");
pipeline(); // AB
```

- `Action<...>` = void, `Func<...,TResult>` = có trả về, `Predicate<T>` = `Func<T,bool>`.

---

## 17. Event khác Delegate như thế nào?

```csharp
public class Button
{
    // event: bên ngoài chỉ được += / -=, KHÔNG gán = hay invoke trực tiếp
    public event EventHandler? Clicked;

    public void OnClick() => Clicked?.Invoke(this, EventArgs.Empty); // chỉ publisher invoke
}

var btn = new Button();
btn.Clicked += (s, e) => Console.WriteLine("Clicked!"); // ✅ đăng ký
// btn.Clicked = null;      // ❌ compile error - không gán ngoài class
// btn.Clicked.Invoke(...); // ❌ compile error - không invoke ngoài class
btn.OnClick(); // "Clicked!"
```

- **Điểm nhấn**: event là lớp bao (encapsulation) quanh delegate → publisher kiểm soát việc phát.

---

## 18. Lambda expression và closure là gì?

```csharp
Func<int, int> multiplier = x => x * 2; // lambda

// Closure: lambda "bắt" biến từ scope ngoài
int factor = 10;
Func<int, int> scale = x => x * factor;  // capture 'factor'
Console.WriteLine(scale(5)); // 50
factor = 20;
Console.WriteLine(scale(5)); // 100 (capture BIẾN, không phải giá trị!)

// ❌ Bẫy capture biến vòng lặp 'for'
var actions = new List<Action>();
for (int i = 0; i < 3; i++)
    actions.Add(() => Console.Write(i)); // tất cả capture cùng 'i'
foreach (var a in actions) a();          // 333 (không phải 012!)

// ✅ Copy vào biến local trong vòng lặp
for (int i = 0; i < 3; i++)
{
    int copy = i;
    actions.Add(() => Console.Write(copy));
} // 012
```

---

## 19. Generic constraints (`where T`) có những loại nào?

```csharp
// where T : class - reference type
public class Repository<T> where T : class { }

// where T : struct - value type
public T? GetOrNull<T>(bool has, T val) where T : struct => has ? val : null;

// where T : new() - có constructor rỗng
public T Create<T>() where T : new() => new T();

// where T : BaseClass / IInterface
public void Save<T>(T entity) where T : IEntity, IComparable<T> { }

// Kết hợp nhiều constraint
public class Cache<TKey, TValue>
    where TKey : notnull
    where TValue : class, new()
{ }
```

---

## 20. Covariance và Contravariance (`out`/`in`) là gì?

```csharp
// Covariance (out T): gán Derived → Base cho OUTPUT
IEnumerable<string> strings = new List<string> { "a", "b" };
IEnumerable<object> objects = strings; // ✅ OK vì IEnumerable<out T>

// Contravariance (in T): gán Base → Derived cho INPUT
Action<object> printObj = o => Console.WriteLine(o);
Action<string> printStr = printObj;    // ✅ OK vì Action<in T>
printStr("hello");

// ❌ List<T> KHÔNG covariant (invariant)
// List<object> list = new List<string>(); // compile error

// Định nghĩa interface covariant
interface IProducer<out T> { T Produce(); }      // chỉ output
interface IConsumer<in T> { void Consume(T item); } // chỉ input
```

---

## 21. Nullable reference types (C# 8) giải quyết vấn đề gì?

```csharp
#nullable enable

string notNull = "hello";   // không được null
string? nullable = null;    // được phép null

// ❌ Compiler cảnh báo truy cập có thể null
void Print(string? name)
{
    Console.WriteLine(name.Length); // ⚠️ warning: có thể null
}

// ✅ Kiểm tra null trước
void PrintSafe(string? name)
{
    if (name is not null)
        Console.WriteLine(name.Length); // OK

    Console.WriteLine(name?.Length ?? 0); // hoặc null-conditional
}

// null-forgiving operator ! - khẳng định không null (dùng cẩn thận)
string value = GetValue()!; // "tôi chắc chắn không null"
```

---

## 22. Phân biệt `??`, `?.`, và `??=`?

```csharp
// ?? null-coalescing: trả vế phải nếu vế trái null
string name = null;
string display = name ?? "Unknown"; // "Unknown"

// ?. null-conditional: trả null nếu object null (tránh NRE)
int? length = name?.Length;         // null (không crash)
string upper = name?.ToUpper();     // null

// ??= null-coalescing assignment: gán nếu đang null
List<int> list = null;
list ??= new List<int>();           // khởi tạo nếu null
list.Add(1);

// Kết hợp
string result = user?.Profile?.DisplayName ?? "Guest";
```

---

## 23. `record` khác `class` như thế nào (C# 9)?

```csharp
// record: value-based equality tự động
public record Person(string Name, int Age);

var p1 = new Person("Alice", 30);
var p2 = new Person("Alice", 30);
Console.WriteLine(p1 == p2);        // True! (so sánh theo property)
                                     // class thì sẽ là False

// with expression: tạo bản sao có sửa đổi (non-destructive)
var p3 = p1 with { Age = 31 };
Console.WriteLine(p3);              // Person { Name = Alice, Age = 31 }
Console.WriteLine(p1.Age);          // 30 (p1 không đổi)

// record struct (C# 10) cho value type
public record struct Point(int X, int Y);
```

- **Khi nào dùng**: DTO, immutable data, event/message. Class cho object có behavior + mutable state.

---

## 24. Pattern matching trong C# gồm những gì?

```csharp
object value = 42;

// Type pattern
if (value is int i) Console.WriteLine(i * 2);

// Switch expression + relational + logical pattern
string Classify(int n) => n switch
{
    < 0 => "Negative",
    0 => "Zero",
    > 0 and < 100 => "Small",
    >= 100 => "Large"
};

// Property pattern
record Order(decimal Total, string Status);
bool IsBigPending(Order o) => o is { Total: > 1000, Status: "Pending" };

// List pattern (C# 11)
int[] arr = { 1, 2, 3 };
if (arr is [1, _, 3]) Console.WriteLine("bắt đầu 1, kết thúc 3");
```

---

## 25. Sự khác nhau giữa `throw` và `throw ex` trong catch?

```csharp
try { DoWork(); }
catch (Exception ex)
{
    // ❌ throw ex - RESET stack trace về dòng này (mất thông tin gốc)
    // throw ex;

    // ✅ throw - GIỮ NGUYÊN stack trace gốc
    throw;

    // ✅ hoặc wrap với inner exception để thêm context
    // throw new ApplicationException("Xử lý thất bại", ex);
}
```

- **Điểm nhấn**: `throw;` bảo toàn stack trace → dễ debug. `throw ex;` xoá dấu vết nơi lỗi thực sự xảy ra.

---

# 🔍 Câu hỏi đào sâu — Phần 1

> Những câu interviewer hay "khoan" tiếp. Nắm bản chất, đừng học vẹt.

## 1D-1. "Value type luôn nằm trên stack" — đúng hay sai?

**Sai.** Vị trí phụ thuộc ngữ cảnh:

```csharp
class Container
{
    public int Field;  // ⚠️ Field value type này nằm trên HEAP (inline trong object)
}

void Method()
{
    int local = 5;                 // stack
    var c = new Container();       // c.Field nằm trên heap
    object boxed = local;          // boxing → heap
    int[] arr = { 1, 2, 3 };       // phần tử value type nằm trên heap (trong array)
}
```

- **Câu chốt**: "Điều quan trọng không phải stack/heap mà là **copy semantics**."

---

## 1D-2. String interning là gì? `==` giữa 2 string hoạt động ra sao?

```csharp
string a = "hello";
string b = "hello";
Console.WriteLine(ReferenceEquals(a, b)); // True! (cùng object trong intern pool)

string c = new string("hello".ToCharArray());
Console.WriteLine(ReferenceEquals(a, c)); // False (object mới, không intern)
Console.WriteLine(a == c);                // True (== so sánh giá trị)

string d = string.Intern(c);              // chủ động intern
Console.WriteLine(ReferenceEquals(a, d)); // True
```

---

## 1D-3. Tại sao `StringBuilder` nhanh hơn? Khi nào KHÔNG cần?

```csharp
// ❌ O(n²): nối trong vòng lặp
string s = "";
for (int i = 0; i < 10000; i++) s += i; // 10000 object rác

// ✅ O(n): StringBuilder
var sb = new StringBuilder();
for (int i = 0; i < 10000; i++) sb.Append(i);

// ✅ KHÔNG cần StringBuilder khi nối ít chuỗi cố định
// (compiler tối ưu thành string.Concat)
string name = firstName + " " + lastName;      // OK
string msg = $"User {id} logged in at {time}";  // interpolation OK
```

---

## 1D-4. Workstation GC vs Server GC?

```xml
<!-- csproj: bật Server GC cho app throughput cao (ASP.NET Core mặc định) -->
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
  <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
</PropertyGroup>
```

- **Workstation**: 1 heap, độ trễ thấp (desktop). **Server**: nhiều heap + nhiều thread GC (throughput). **Concurrent/Background**: thu Gen 2 song song app → giảm pause.

---

## 1D-5. Large Object Heap (LOH) và fragmentation?

```csharp
// Object ≥ 85KB → LOH, thuộc Gen 2, mặc định KHÔNG nén → phân mảnh
var big = new byte[100_000]; // vào LOH

// ✅ Dùng ArrayPool để tái sử dụng buffer lớn, tránh cấp phát LOH liên tục
var pool = ArrayPool<byte>.Shared;
byte[] buffer = pool.Rent(100_000);
try { /* dùng buffer */ }
finally { pool.Return(buffer); }

// Nén LOH thủ công (hiếm khi cần)
GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
GC.Collect();
```

---

## 1D-6. Dispose Pattern + thứ tự Finalizer (xem câu 11)

- GC không gọi finalizer theo thứ tự xác định; object có finalizer sống thêm ≥1 GC cycle. `GC.SuppressFinalize` bỏ khỏi finalization queue. (Code đầy đủ ở câu 11.)

---

## 1D-7. `Span<T>` và `Memory<T>` giải quyết vấn đề gì?

```csharp
// ❌ Substring tạo string mới (allocation)
string date = "2024-01-15";
string year = date.Substring(0, 4); // cấp phát string mới

// ✅ Span: slice zero-allocation (view trên bộ nhớ có sẵn)
ReadOnlySpan<char> span = date.AsSpan();
ReadOnlySpan<char> yearSpan = span.Slice(0, 4); // không cấp phát
int y = int.Parse(yearSpan);

// Span cũng dùng cho stackalloc
Span<int> numbers = stackalloc int[3] { 1, 2, 3 }; // trên stack, không heap

// Memory<T>: dùng được trong async (Span là ref struct nên KHÔNG)
async Task ProcessAsync(Memory<byte> data) { await Task.Delay(1); }
```

---

## 1D-8. `ref struct` là gì? Tại sao `Span<T>` là `ref struct`?

```csharp
ref struct MySpan { }

// ref struct BẮT BUỘC chỉ sống trên stack:
// ❌ Không làm field của class
class Bad { Span<int> _span; } // compile error
// ❌ Không boxing
// object o = mySpan;          // compile error
// ❌ Không dùng trong async/lambda
// async Task F(Span<int> s) { await ...; } // compile error
```

- **Lý do**: đảm bảo `Span` không "thoát" khỏi stack frame khi vùng nhớ nó trỏ tới có thể đã bị giải phóng → an toàn bộ nhớ.

---

## 1D-9. `is` và `as` khác nhau? Cái nào tốt hơn?

```csharp
object obj = "hello";

// as: trả null nếu thất bại (không throw)
string s = obj as string;
if (s != null) Console.WriteLine(s.Length);

// ❌ Anti-pattern: check is rồi cast lại (ép kiểu 2 lần)
if (obj is string)
    Console.WriteLine(((string)obj).Length);

// ✅ is pattern: vừa check vừa gán (1 lần)
if (obj is string str)
    Console.WriteLine(str.Length);
```

---

## 1D-10. Static constructor chạy khi nào? Thread-safe không?

```csharp
public class Singleton
{
    public static readonly Singleton Instance;

    // Static constructor: CLR gọi 1 LẦN, thread-safe (có lock ngầm),
    // ngay trước lần đầu truy cập type
    static Singleton()
    {
        Instance = new Singleton();
        Console.WriteLine("Static ctor chạy 1 lần");
    }
}

// ⚠️ Exception trong static ctor → TypeInitializationException,
// type KHÔNG dùng được cả app
```

---

## 1D-11. Closure capture biến vòng lặp — bug kinh điển (xem câu 18)

```csharp
// foreach ĐÃ được sửa từ C# 5 (mỗi vòng 1 biến mới) → in 0,1,2
// nhưng 'for' vẫn dính bug → cần copy biến local
var funcs = new List<Func<int>>();
for (int i = 0; i < 3; i++)
{
    int copy = i;               // ✅ bắt buộc copy
    funcs.Add(() => copy);
}
```

---

## 1D-12. Tuple `(int, int)` (ValueTuple) khác `Tuple<int,int>`?

```csharp
// Tuple<>: reference type, immutable, .Item1
Tuple<int, string> t1 = Tuple.Create(1, "a");
Console.WriteLine(t1.Item1); // heap allocation

// ValueTuple: value type (struct), đặt tên field, deconstruction
(int Id, string Name) t2 = (1, "a");
Console.WriteLine(t2.Id);    // đặt tên rõ ràng, không allocation heap

// Trả nhiều giá trị + deconstruct
(int min, int max) GetRange(int[] arr) => (arr.Min(), arr.Max());
var (lo, hi) = GetRange(new[] { 3, 1, 5 });
```

---

## 1D-13. `Nullable<T>` (`int?`) được cài đặt thế nào?

```csharp
int? x = null;
Console.WriteLine(x.HasValue);  // False
Console.WriteLine(x.GetValueOrDefault()); // 0

int? y = 5;
Console.WriteLine(y.Value);     // 5

// ⚠️ Bẫy boxing: int? với HasValue=false → box thành null THẬT
int? nothing = null;
object boxed = nothing;         // boxed == null (không phải Nullable)
Console.WriteLine(boxed is null); // True
```

---

## 1D-14. `checked` / `unchecked` và integer overflow?

```csharp
int max = int.MaxValue;

// Mặc định unchecked: wrap-around âm thầm
int wrapped = max + 1;
Console.WriteLine(wrapped); // -2147483648 (!!)

// ✅ checked: ném OverflowException
try
{
    int overflow = checked(max + 1);
}
catch (OverflowException)
{
    Console.WriteLine("Tràn số!");
}
```

- Bật toàn project: `<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>`. Quan trọng khi tính tiền, index.

---

## 1D-15. JIT vs AOT (Native AOT) khác nhau?

```xml
<!-- Native AOT: biên dịch sẵn native, khởi động nhanh, ít RAM -->
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

```bash
dotnet publish -r win-x64 -c Release
```

- **JIT**: IL → machine code lúc runtime, hỗ trợ reflection đầy đủ. **AOT**: native lúc build, khởi động nhanh, ít RAM; đánh đổi: hạn chế reflection/dynamic, binary lớn. **ReadyToRun**: lai (pre-JIT một phần).

---

[⬅️ Về mục lục](interview.NET.md) | Tiếp theo: [Phần 2 — Async & Threading ➡️](interview.NET.02-Async-Threading.md)
