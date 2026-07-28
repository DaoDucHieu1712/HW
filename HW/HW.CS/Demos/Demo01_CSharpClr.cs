using System.Buffers;
using System.Collections;
using System.Runtime;
using System.Text;

namespace HW.CS.Demos;

/// <summary>
/// Runnable demos for interview.NET.01-CSharp-CLR.md.
/// 25 core questions (Q1..Q25) + 15 deep-dive questions (D1..D15).
/// Each method prints what it demonstrates so you can read the output
/// while re-reading the question.
/// </summary>
public sealed class Demo01_CSharpClr : IDemoTopic
{
    public string Title => "01 — C# Ngôn ngữ & CLR";

    public IReadOnlyList<DemoItem> Items => new DemoItem[]
    {
        new("Q1.  Value type vs reference type", Q1_ValueVsReference),
        new("Q2.  struct vs class", Q2_StructVsClass),
        new("Q3.  string là reference type & immutable", Q3_StringImmutable),
        new("Q4.  Boxing / unboxing", Q4_BoxingUnboxing),
        new("Q5.  const vs readonly", Q5_ConstVsReadonly),
        new("Q6.  ref / out / in", Q6_RefOutIn),
        new("Q7.  var / dynamic / object", Q7_VarDynamicObject),
        new("Q8.  IEnumerable / ICollection / IList", Q8_EnumerableCollectionList),
        new("Q9.  GC generations (Gen 0/1/2)", Q9_GcGenerations),
        new("Q10. IDisposable / using", Q10_DisposableUsing),
        new("Q11. Finalizer + Dispose pattern", Q11_DisposePattern),
        new("Q12. == vs .Equals()", Q12_EqualsVsEqualEqual),
        new("Q13. Override Equals ⇒ override GetHashCode", Q13_EqualsHashCode),
        new("Q14. IEquatable<T>", Q14_IEquatable),
        new("Q15. Extension method", Q15_ExtensionMethod),
        new("Q16. Delegate / Func / Action / Predicate", Q16_Delegates),
        new("Q17. Event vs delegate", Q17_EventVsDelegate),
        new("Q18. Lambda & closure (capture bug)", Q18_LambdaClosure),
        new("Q19. Generic constraints (where T)", Q19_GenericConstraints),
        new("Q20. Covariance / contravariance", Q20_Variance),
        new("Q21. Nullable reference types", Q21_NullableReferenceTypes),
        new("Q22. ?? / ?. / ??=", Q22_NullOperators),
        new("Q23. record vs class", Q23_RecordVsClass),
        new("Q24. Pattern matching", Q24_PatternMatching),
        new("Q25. throw vs throw ex", Q25_ThrowVsThrowEx),

        new("D1.  'Value type luôn ở stack' — sai", D1_ValueTypeNotAlwaysStack),
        new("D2.  String interning", D2_StringInterning),
        new("D3.  StringBuilder O(n) vs +=", D3_StringBuilderPerf),
        new("D4.  Workstation vs Server GC", D4_WorkstationVsServerGc),
        new("D5.  Large Object Heap (LOH)", D5_LargeObjectHeap),
        new("D6.  Finalizer order / SuppressFinalize", D6_FinalizerOrder),
        new("D7.  Span<T> / Memory<T>", D7_SpanMemory),
        new("D8.  ref struct", D8_RefStruct),
        new("D9.  is vs as", D9_IsVsAs),
        new("D10. Static constructor", D10_StaticConstructor),
        new("D11. Closure capture trong for", D11_ClosureLoopCapture),
        new("D12. ValueTuple vs Tuple<>", D12_ValueTuple),
        new("D13. Nullable<T> (int?)", D13_NullableStruct),
        new("D14. checked / unchecked overflow", D14_CheckedUnchecked),
        new("D15. JIT vs AOT", D15_JitVsAot),
    };

    // ── Q1 ────────────────────────────────────────────────────────────────
    public static void Q1_ValueVsReference()
    {
        // Value type: gán = copy giá trị → 2 biến độc lập.
        int a = 10;
        int b = a;
        b = 20;
        Console.WriteLine($"value type  : a={a} (không đổi), b={b}");

        // Reference type: gán = copy tham chiếu → cùng object.
        var list1 = new List<int> { 1, 2, 3 };
        var list2 = list1;
        list2.Add(4);
        Console.WriteLine($"reference   : list1.Count={list1.Count} (cũng đổi!)");

        // Truyền vào method: struct copy, class chia sẻ.
        static void MutateStruct(PointStruct p) => p.X = 99;
        static void MutateClass(PointClass p) => p.X = 99;

        var ps = new PointStruct { X = 1 };
        MutateStruct(ps);
        Console.WriteLine($"struct arg  : ps.X={ps.X} (bản sao, không đổi)");

        var pc = new PointClass { X = 1 };
        MutateClass(pc);
        Console.WriteLine($"class arg   : pc.X={pc.X} (cùng object, đổi)");
    }

    // ── Q2 ────────────────────────────────────────────────────────────────
    public static void Q2_StructVsClass()
    {
        // struct chỉ implement interface (không kế thừa class).
        var m1 = new Money(10m);
        var m2 = new Money(25m);
        Console.WriteLine($"Money.CompareTo: {m1.CompareTo(m2)} (âm ⇒ m1 < m2)");

        var p = new Point(3, 4); // readonly struct — immutable
        Console.WriteLine($"readonly struct Point = ({p.X},{p.Y})");
        Console.WriteLine("→ struct dùng cho object nhỏ, immutable, đời ngắn (Point/DateTime/Guid).");
    }

    // ── Q3 ────────────────────────────────────────────────────────────────
    public static void Q3_StringImmutable()
    {
        string s = "Hello";
        string t = s;
        s += " World"; // tạo string MỚI
        Console.WriteLine($"s='{s}'  t='{t}'  (t giữ object cũ)");

        // ❌ nối trong loop → O(n²), rác GC. ✅ StringBuilder → O(n).
        var sb = new StringBuilder();
        for (int i = 0; i < 5; i++) sb.Append(i);
        Console.WriteLine($"StringBuilder: '{sb}'");
    }

    // ── Q4 ────────────────────────────────────────────────────────────────
    public static void Q4_BoxingUnboxing()
    {
        int number = 42;
        object boxed = number;      // BOXING: heap alloc + copy
        int unboxed = (int)boxed;   // UNBOXING
        Console.WriteLine($"boxed={boxed}, unboxed={unboxed}");

        // ArrayList boxing từng phần tử value type.
        var oldList = new ArrayList();
        oldList.Add(1);             // boxing
        int x = (int)oldList[0]!;   // unboxing
        Console.WriteLine($"ArrayList[0]={x} (đã box/unbox)");

        // Generic List<int> không boxing.
        var list = new List<int> { 1 };
        Console.WriteLine($"List<int>[0]={list[0]} (không boxing)");

        IComparable c = 5;          // boxing ẩn!
        Console.WriteLine($"IComparable c=5 → boxing ẩn, CompareTo(3)={c.CompareTo(3)}");
    }

    // ── Q5 ────────────────────────────────────────────────────────────────
    public static void Q5_ConstVsReadonly()
    {
        var cfg = new Config("Production");
        Console.WriteLine($"const MaxRetries={Config.MaxRetries} (inline lúc compile)");
        Console.WriteLine($"readonly CreatedAt={cfg.CreatedAt:O} (gán trong ctor)");
        Console.WriteLine($"readonly Environment={cfg.Environment}");
        Console.WriteLine($"static readonly Version={Config.Version} (đổi được giữa release)");
    }

    // ── Q6 ────────────────────────────────────────────────────────────────
    public static void Q6_RefOutIn()
    {
        static void Double(ref int v) => v *= 2;
        int n = 5;
        Double(ref n);
        Console.WriteLine($"ref  : n={n}");

        if (int.TryParse("123", out int parsed))
            Console.WriteLine($"out  : parsed={parsed}");

        var big = new BigStruct(1, 2, 3, 4);
        static long Compute(in BigStruct s) => s.A + s.B; // readonly ref, không copy
        Console.WriteLine($"in   : Compute(big)={Compute(big)} (không copy struct lớn)");
    }

    // ── Q7 ────────────────────────────────────────────────────────────────
    public static void Q7_VarDynamicObject()
    {
        var name = "Hello";                 // compile-time = string
        Console.WriteLine($"var    : name.Length={name.Length}");

        dynamic d = "Hello";
        Console.WriteLine($"dynamic: d.Length={d.Length} (resolve runtime)");
        try
        {
            d.FooBar();                     // compile OK, runtime crash
        }
        catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            Console.WriteLine("dynamic: gọi d.FooBar() → RuntimeBinderException lúc chạy");
        }

        object o = "Hello";
        int len = ((string)o).Length;       // phải ép kiểu
        Console.WriteLine($"object : ((string)o).Length={len}");
    }

    // ── Q8 ────────────────────────────────────────────────────────────────
    public static void Q8_EnumerableCollectionList()
    {
        IEnumerable<int> e = new[] { 1, 2, 3 };
        int sum = 0;
        foreach (var x in e) sum += x;      // chỉ duyệt
        Console.WriteLine($"IEnumerable: chỉ foreach, sum={sum}");

        ICollection<int> c = new List<int> { 1, 2, 3 };
        c.Add(4);                            // Add/Remove/Count
        Console.WriteLine($"ICollection: Count={c.Count}");

        IList<int> list = new List<int> { 1, 2, 3 };
        list.Insert(0, 99);                  // index
        Console.WriteLine($"IList      : list[0]={list[0]}");
        Console.WriteLine("→ nhận tham số ở kiểu HẸP nhất đủ dùng (thường IEnumerable<T>).");
    }

    // ── Q9 ────────────────────────────────────────────────────────────────
    public static void Q9_GcGenerations()
    {
        var obj = new object();
        Console.WriteLine($"gen sau khi tạo   : {GC.GetGeneration(obj)}");
        GC.Collect();                        // ❌ không gọi thủ công ở production
        Console.WriteLine($"gen sau GC.Collect: {GC.GetGeneration(obj)} (đã thăng cấp)");

        var large = new byte[100_000];       // ≥85KB → LOH (Gen 2)
        Console.WriteLine($"byte[100_000] gen : {GC.GetGeneration(large)} (LOH)");
    }

    // ── Q10 ───────────────────────────────────────────────────────────────
    public static void Q10_DisposableUsing()
    {
        // using declaration (C# 8) — Dispose ở cuối scope, kể cả khi exception.
        using var res = new FakeConnection("db://demo");
        res.DoWork();
        Console.WriteLine("→ ra khỏi scope: Dispose() tự gọi (xem dòng dưới)");
    }

    // ── Q11 ───────────────────────────────────────────────────────────────
    public static void Q11_DisposePattern()
    {
        var holder = new ResourceHolder();
        holder.Dispose();
        holder.Dispose(); // gọi 2 lần vẫn an toàn nhờ cờ _disposed
        Console.WriteLine("Dispose gọi 2 lần vẫn an toàn (idempotent). SuppressFinalize tránh chạy finalizer.");
    }

    // ── Q12 ───────────────────────────────────────────────────────────────
    public static void Q12_EqualsVsEqualEqual()
    {
        var a = new object();
        var b = new object();
        Console.WriteLine($"object: a==b={a == b}, a.Equals(b)={a.Equals(b)} (so sánh tham chiếu)");

        string s1 = "hello";
        string s2 = new string("hello".ToCharArray());
        Console.WriteLine($"string: s1==s2={s1 == s2} (giá trị), ReferenceEquals={ReferenceEquals(s1, s2)}");

        var p1 = new Person { Name = "Alice" };
        var p2 = new Person { Name = "Alice" };
        Console.WriteLine($"Person override Equals: p1.Equals(p2)={p1.Equals(p2)}");
    }

    // ── Q13 ───────────────────────────────────────────────────────────────
    public static void Q13_EqualsHashCode()
    {
        var bad = new Dictionary<BadKey, string> { [new BadKey { Id = 1 }] = "A" };
        Console.WriteLine($"BadKey  (thiếu GetHashCode): ContainsKey(Id=1)={bad.ContainsKey(new BadKey { Id = 1 })} ❌");

        var good = new Dictionary<GoodKey, string> { [new GoodKey { Id = 1 }] = "A" };
        Console.WriteLine($"GoodKey (đủ GetHashCode)   : ContainsKey(Id=1)={good.ContainsKey(new GoodKey { Id = 1 })} ✅");
        Console.WriteLine("→ Quy tắc: bằng nhau ⇒ hashcode bằng nhau.");
    }

    // ── Q14 ───────────────────────────────────────────────────────────────
    public static void Q14_IEquatable()
    {
        var temps = new List<Temperature> { new(20), new(25) };
        Console.WriteLine($"List<Temperature>.Contains(20)={temps.Contains(new Temperature(20))} (IEquatable, không boxing)");
    }

    // ── Q15 ───────────────────────────────────────────────────────────────
    public static void Q15_ExtensionMethod()
    {
        string name = "Hello World";
        Console.WriteLine($"IsNullOrEmpty() = {name.IsNullOrEmpty()}");
        Console.WriteLine($"Truncate(5)     = {name.Truncate(5)}");
        var evens = new[] { 1, 2, 3, 4 }.Where(x => x % 2 == 0);
        Console.WriteLine($"LINQ Where (cũng là extension method): {string.Join(",", evens)}");
    }

    // ── Q16 ───────────────────────────────────────────────────────────────
    public static void Q16_Delegates()
    {
        Action greet = () => Console.Write("Hi ");
        Action<string> log = msg => Console.Write($"[{msg}] ");
        Func<int, int, int> add = (a, b) => a + b;
        Predicate<int> isEven = x => x % 2 == 0;

        greet();
        log("Hello");
        Console.WriteLine($"add(2,3)={add(2, 3)}, isEven(4)={isEven(4)}");

        Action pipeline = () => Console.Write("A");
        pipeline += () => Console.Write("B");
        Console.Write("multicast: ");
        pipeline();
        Console.WriteLine();
    }

    // ── Q17 ───────────────────────────────────────────────────────────────
    public static void Q17_EventVsDelegate()
    {
        var btn = new Button();
        btn.Clicked += (_, _) => Console.WriteLine("Clicked! (subscriber)");
        // btn.Clicked = null;      // ❌ compile error ngoài class
        // btn.Clicked.Invoke(...); // ❌ compile error ngoài class
        btn.OnClick(); // chỉ publisher được invoke
    }

    // ── Q18 ───────────────────────────────────────────────────────────────
    public static void Q18_LambdaClosure()
    {
        int factor = 10;
        Func<int, int> scale = x => x * factor; // capture BIẾN, không phải giá trị
        Console.WriteLine($"scale(5) khi factor=10 → {scale(5)}");
        factor = 20;
        Console.WriteLine($"scale(5) khi factor=20 → {scale(5)} (capture biến!)");

        // ❌ 'for' capture cùng biến i.
        var bad = new List<Func<int>>();
        for (int i = 0; i < 3; i++) bad.Add(() => i);
        Console.WriteLine($"for bug   : {string.Join("", bad.Select(f => f()))} (kỳ vọng 012)");

        // ✅ copy biến local.
        var good = new List<Func<int>>();
        for (int i = 0; i < 3; i++) { int copy = i; good.Add(() => copy); }
        Console.WriteLine($"for fixed : {string.Join("", good.Select(f => f()))}");
    }

    // ── Q19 ───────────────────────────────────────────────────────────────
    public static void Q19_GenericConstraints()
    {
        static T Create<T>() where T : new() => new T();
        var person = Create<Person>();
        Console.WriteLine($"where T:new() → tạo {person.GetType().Name}");

        var cache = new Cache<string, Person>();
        cache.Set("a", new Person { Name = "Alice" });
        Console.WriteLine($"Cache<TKey:notnull, TValue:class,new()> → Get('a').Name={cache.Get("a")?.Name}");
    }

    // ── Q20 ───────────────────────────────────────────────────────────────
    public static void Q20_Variance()
    {
        IEnumerable<string> strings = new List<string> { "a", "b" };
        IEnumerable<object> objects = strings;            // covariance (out T)
        Console.WriteLine($"covariance   : IEnumerable<object> ← IEnumerable<string>, count={objects.Count()}");

        Action<object> printObj = o => Console.WriteLine($"contravariance: printObj nhận '{o}'");
        Action<string> printStr = printObj;               // contravariance (in T)
        printStr("hello");
    }

    // ── Q21 ───────────────────────────────────────────────────────────────
    public static void Q21_NullableReferenceTypes()
    {
        static void PrintSafe(string? name)
        {
            if (name is not null) Console.WriteLine($"len={name.Length}");
            Console.WriteLine($"name?.Length ?? 0 = {name?.Length ?? 0}");
        }
        PrintSafe("hello");
        PrintSafe(null);
        Console.WriteLine("→ compiler cảnh báo khi truy cập biến 'string?' chưa kiểm tra null.");
    }

    // ── Q22 ───────────────────────────────────────────────────────────────
    public static void Q22_NullOperators()
    {
        string? name = null;
        Console.WriteLine($"?? : name ?? \"Unknown\" = {name ?? "Unknown"}");
        Console.WriteLine($"?. : name?.Length = {(name?.Length is int l ? l.ToString() : "null")}");

        List<int>? list = null;
        list ??= new List<int>();  // gán nếu đang null
        list.Add(1);
        Console.WriteLine($"??=: list sau khởi tạo có {list.Count} phần tử");
    }

    // ── Q23 ───────────────────────────────────────────────────────────────
    public static void Q23_RecordVsClass()
    {
        var p1 = new PersonRecord("Alice", 30);
        var p2 = new PersonRecord("Alice", 30);
        Console.WriteLine($"record equality: p1==p2={p1 == p2} (class sẽ False)");

        var p3 = p1 with { Age = 31 };       // non-destructive mutation
        Console.WriteLine($"with expression: p3={p3}, p1.Age={p1.Age} (không đổi)");

        var pt = new PointRecord(1, 2);
        Console.WriteLine($"record struct  : {pt}");
    }

    // ── Q24 ───────────────────────────────────────────────────────────────
    public static void Q24_PatternMatching()
    {
        object value = 42;
        if (value is int i) Console.WriteLine($"type pattern    : i*2={i * 2}");

        static string Classify(int n) => n switch
        {
            < 0 => "Negative",
            0 => "Zero",
            > 0 and < 100 => "Small",
            _ => "Large"
        };
        Console.WriteLine($"switch/relational: 42→{Classify(42)}, 500→{Classify(500)}");

        var order = new OrderRecord(1500m, "Pending");
        bool big = order is { Total: > 1000, Status: "Pending" };
        Console.WriteLine($"property pattern : IsBigPending={big}");

        int[] arr = { 1, 2, 3 };
        Console.WriteLine($"list pattern     : arr is [1,_,3] = {arr is [1, _, 3]}");
    }

    // ── Q25 ───────────────────────────────────────────────────────────────
    public static void Q25_ThrowVsThrowEx()
    {
        static void Inner() => throw new InvalidOperationException("lỗi gốc ở Inner()");

        try
        {
            try { Inner(); }
            catch (Exception ex)
            {
                Console.WriteLine($"stack trace GỐC chứa Inner: {ex.StackTrace?.Contains("Inner")}");
                throw; // ✅ giữ nguyên stack trace (dùng 'throw;' không 'throw ex;')
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"rethrow bằng 'throw;' vẫn thấy Inner: {ex.StackTrace?.Contains("Inner")}");
        }
    }

    // ── D1 ────────────────────────────────────────────────────────────────
    public static void D1_ValueTypeNotAlwaysStack()
    {
        int local = 5;                       // stack
        var c = new Container { Field = 7 }; // c.Field nằm trên HEAP (inline trong object)
        object boxed = local;                // boxing → heap
        int[] arr = { 1, 2, 3 };             // phần tử value type nằm trên heap (trong array)
        Console.WriteLine($"local(stack)={local}, container.Field(heap)={c.Field}, boxed(heap)={boxed}, arr[0](heap)={arr[0]}");
        Console.WriteLine("→ Quan trọng là COPY SEMANTICS, không phải stack/heap.");
    }

    // ── D2 ────────────────────────────────────────────────────────────────
    public static void D2_StringInterning()
    {
        string a = "hello";
        string b = "hello";
        Console.WriteLine($"literal a,b ReferenceEquals={ReferenceEquals(a, b)} (intern pool)");

        string c = new string("hello".ToCharArray());
        Console.WriteLine($"new string c: ReferenceEquals(a,c)={ReferenceEquals(a, c)}, a==c={a == c}");

        string d = string.Intern(c);
        Console.WriteLine($"string.Intern(c): ReferenceEquals(a,d)={ReferenceEquals(a, d)}");
    }

    // ── D3 ────────────────────────────────────────────────────────────────
    public static void D3_StringBuilderPerf()
    {
        const int n = 20_000;

        var sw1 = System.Diagnostics.Stopwatch.StartNew();
        string s = "";
        for (int i = 0; i < n; i++) s += "x"; // O(n²)
        sw1.Stop();

        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        var sb = new StringBuilder();
        for (int i = 0; i < n; i++) sb.Append('x'); // O(n)
        _ = sb.ToString();
        sw2.Stop();

        Console.WriteLine($"+=          : {sw1.ElapsedMilliseconds} ms  (O(n²))");
        Console.WriteLine($"StringBuilder: {sw2.ElapsedMilliseconds} ms  (O(n))");
        Console.WriteLine("→ nối ÍT chuỗi cố định thì + / $\"\" là đủ (compiler tối ưu).");
    }

    // ── D4 ────────────────────────────────────────────────────────────────
    public static void D4_WorkstationVsServerGc()
    {
        Console.WriteLine($"IsServerGC        = {GCSettings.IsServerGC} (ASP.NET Core mặc định true)");
        Console.WriteLine($"LatencyMode       = {GCSettings.LatencyMode}");
        Console.WriteLine("Workstation: 1 heap, độ trễ thấp. Server: nhiều heap + thread GC (throughput).");
    }

    // ── D5 ────────────────────────────────────────────────────────────────
    public static void D5_LargeObjectHeap()
    {
        var big = new byte[100_000]; // ≥85KB → LOH
        Console.WriteLine($"byte[100_000] gen={GC.GetGeneration(big)} (LOH thuộc Gen 2)");

        var pool = ArrayPool<byte>.Shared;
        byte[] buffer = pool.Rent(100_000);
        try { buffer[0] = 1; Console.WriteLine("ArrayPool.Rent → tái sử dụng buffer, tránh cấp phát LOH liên tục"); }
        finally { pool.Return(buffer); }
    }

    // ── D6 ────────────────────────────────────────────────────────────────
    public static void D6_FinalizerOrder()
    {
        Console.WriteLine("GC không gọi finalizer theo thứ tự xác định.");
        Console.WriteLine("Object có finalizer sống thêm ≥1 GC cycle → tốn kém.");
        Console.WriteLine("Dispose() gọi GC.SuppressFinalize(this) để bỏ khỏi finalization queue (xem Q11).");
    }

    // ── D7 ────────────────────────────────────────────────────────────────
    public static void D7_SpanMemory()
    {
        string date = "2024-01-15";
        ReadOnlySpan<char> span = date.AsSpan();
        ReadOnlySpan<char> yearSpan = span.Slice(0, 4); // zero-allocation view
        int year = int.Parse(yearSpan);
        Console.WriteLine($"Span slice năm = {year} (không cấp phát string mới)");

        Span<int> numbers = stackalloc int[3] { 1, 2, 3 }; // trên stack
        int sum = 0;
        foreach (var x in numbers) sum += x;
        Console.WriteLine($"stackalloc Span<int> sum = {sum}");
        Console.WriteLine("→ Memory<T> dùng được trong async; Span<T> là ref struct nên KHÔNG.");
    }

    // ── D8 ────────────────────────────────────────────────────────────────
    public static void D8_RefStruct()
    {
        // ref struct bắt buộc chỉ sống trên stack:
        //   class Bad { Span<int> _span; }       // ❌ không làm field của class
        //   object o = span;                     // ❌ không boxing
        //   async Task F(Span<int> s) { await ...; } // ❌ không dùng trong async/lambda
        Span<int> s = stackalloc int[] { 10, 20 };
        Console.WriteLine($"Span<int> (ref struct) chỉ ở stack: s[0]={s[0]}, s[1]={s[1]}");
        Console.WriteLine("→ đảm bảo Span không 'thoát' stack frame khi vùng nhớ đã bị giải phóng.");
    }

    // ── D9 ────────────────────────────────────────────────────────────────
    public static void D9_IsVsAs()
    {
        object obj = "hello";

        string? s = obj as string;           // as: null nếu thất bại
        Console.WriteLine($"as   : (obj as string)?.Length = {s?.Length}");

        if (obj is string str)               // is pattern: check + gán 1 lần
            Console.WriteLine($"is   : pattern str.Length = {str.Length} (ưu tiên cách này)");

        object number = 42;
        Console.WriteLine($"as với value type cần nullable: (number as int?) = {number as int?}");
    }

    // ── D10 ───────────────────────────────────────────────────────────────
    public static void D10_StaticConstructor()
    {
        Console.WriteLine("Truy cập lần 1 → static ctor chạy:");
        _ = LazyInit.Value;
        Console.WriteLine("Truy cập lần 2 → KHÔNG chạy lại:");
        _ = LazyInit.Value;
        Console.WriteLine("→ CLR gọi static ctor đúng 1 lần, thread-safe (lock ngầm).");
    }

    // ── D11 ───────────────────────────────────────────────────────────────
    public static void D11_ClosureLoopCapture()
    {
        // foreach ĐÃ sửa từ C# 5 (mỗi vòng 1 biến mới); 'for' vẫn dính bug.
        var funcs = new List<Func<int>>();
        for (int i = 0; i < 3; i++) { int copy = i; funcs.Add(() => copy); }
        Console.WriteLine($"for + copy local: {string.Join(",", funcs.Select(f => f()))}");
    }

    // ── D12 ───────────────────────────────────────────────────────────────
    public static void D12_ValueTuple()
    {
        Tuple<int, string> t1 = Tuple.Create(1, "a"); // reference type, .Item1
        Console.WriteLine($"Tuple<>     : Item1={t1.Item1} (heap alloc)");

        (int Id, string Name) t2 = (1, "a");          // value type, đặt tên
        Console.WriteLine($"ValueTuple  : Id={t2.Id}, Name={t2.Name} (không heap alloc)");

        static (int min, int max) GetRange(int[] arr) => (arr.Min(), arr.Max());
        var (lo, hi) = GetRange(new[] { 3, 1, 5 });
        Console.WriteLine($"deconstruct : min={lo}, max={hi}");
    }

    // ── D13 ───────────────────────────────────────────────────────────────
    public static void D13_NullableStruct()
    {
        int? x = null;
        Console.WriteLine($"x.HasValue={x.HasValue}, GetValueOrDefault()={x.GetValueOrDefault()}");
        int? y = 5;
        Console.WriteLine($"y.Value={y.Value}");

        int? nothing = null;
        object? boxed = nothing;             // box thành null THẬT
        Console.WriteLine($"box int? null → boxed is null = {boxed is null}");
    }

    // ── D14 ───────────────────────────────────────────────────────────────
    public static void D14_CheckedUnchecked()
    {
        int max = int.MaxValue;
        int wrapped = unchecked(max + 1);    // wrap-around âm thầm
        Console.WriteLine($"unchecked: int.MaxValue+1 = {wrapped}");

        try { _ = checked(max + 1); }
        catch (OverflowException) { Console.WriteLine("checked  : ném OverflowException (dùng khi tính tiền/index)"); }
    }

    // ── D15 ───────────────────────────────────────────────────────────────
    public static void D15_JitVsAot()
    {
        Console.WriteLine("JIT       : IL → machine code lúc runtime, hỗ trợ reflection đầy đủ.");
        Console.WriteLine("Native AOT: <PublishAot>true</PublishAot>; native lúc build, khởi động nhanh, ít RAM.");
        Console.WriteLine("            đánh đổi: hạn chế reflection/dynamic, binary lớn.");
        Console.WriteLine("ReadyToRun: lai (pre-JIT một phần).");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Helper types cho các câu hỏi (đặt ngoài class demo để struct/class/record
//  và extension method hợp lệ).
// ═══════════════════════════════════════════════════════════════════════════

file struct PointStruct { public int X; }
file sealed class PointClass { public int X; }

file readonly struct Money(decimal amount) : IComparable<Money>
{
    public decimal Amount { get; } = amount;
    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);
}

file readonly struct Point(int x, int y)
{
    public int X { get; } = x;
    public int Y { get; } = y;
}

file sealed class Config(string env)
{
    public const int MaxRetries = 3;                 // compile-time
    public readonly DateTime CreatedAt = DateTime.Now; // runtime
    public readonly string Environment = env;
    public static readonly int Version = 2;
}

file readonly struct BigStruct(long a, long b, long c, long d)
{
    public readonly long A = a, B = b, C = c, D = d;
}

file sealed class Person
{
    public string Name { get; set; } = "";
    public override bool Equals(object? obj) => obj is Person p && p.Name == Name;
    public override int GetHashCode() => Name?.GetHashCode() ?? 0;
}

#pragma warning disable CS0659 // 'BadKey' overrides Equals but not GetHashCode — CỐ TÌNH để minh hoạ bug
file sealed class BadKey
{
    public int Id;
    public override bool Equals(object? obj) => obj is BadKey k && k.Id == Id;
    // ❌ cố tình thiếu GetHashCode() → dùng hash mặc định theo tham chiếu.
    // 2 instance khác nhau → hash khác → bucket khác → Dictionary KHÔNG tìm thấy.
}
#pragma warning restore CS0659

file sealed class GoodKey
{
    public int Id;
    public override bool Equals(object? obj) => obj is GoodKey k && k.Id == Id;
    public override int GetHashCode() => Id.GetHashCode();
}

file readonly struct Temperature(double celsius) : IEquatable<Temperature>
{
    public double Celsius { get; } = celsius;
    public bool Equals(Temperature other) => Celsius == other.Celsius; // không boxing
    public override bool Equals(object? obj) => obj is Temperature t && Equals(t);
    public override int GetHashCode() => Celsius.GetHashCode();
}

file sealed class Button
{
    public event EventHandler? Clicked;               // ngoài class chỉ += / -=
    public void OnClick() => Clicked?.Invoke(this, EventArgs.Empty);
}

file sealed class Cache<TKey, TValue>
    where TKey : notnull
    where TValue : class, new()
{
    private readonly Dictionary<TKey, TValue> _store = new();
    public void Set(TKey key, TValue value) => _store[key] = value;
    public TValue? Get(TKey key) => _store.TryGetValue(key, out var v) ? v : null;
}

file sealed class ResourceHolder : IDisposable
{
    // Trong thực tế field này được gán ở constructor; ở demo để null cho gọn.
    private readonly StreamReader? _managed = null;
    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing) _managed?.Dispose(); // chỉ giải phóng managed khi gọi từ Dispose()
        // giải phóng unmanaged ở đây (luôn luôn)
        _disposed = true;
    }

    ~ResourceHolder() => Dispose(false); // finalizer: lưới an toàn
}

file sealed class FakeConnection(string connectionString) : IDisposable
{
    public void DoWork() => Console.WriteLine($"working on {connectionString}");
    public void Dispose() => Console.WriteLine("FakeConnection.Dispose() được gọi");
}

file sealed class Container { public int Field; } // value-type field nằm trên heap (inline trong object)

file static class LazyInit
{
    public static readonly int Value;
    static LazyInit()
    {
        Console.WriteLine("  → static constructor chạy (1 lần duy nhất)");
        Value = 42;
    }
}

file record PersonRecord(string Name, int Age);
file record struct PointRecord(int X, int Y);
file record OrderRecord(decimal Total, string Status);

// Extension methods phải nằm trong static class không lồng nhau.
internal static class Demo01StringExtensions
{
    public static bool IsNullOrEmpty(this string value) => string.IsNullOrEmpty(value);
    public static string Truncate(this string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
