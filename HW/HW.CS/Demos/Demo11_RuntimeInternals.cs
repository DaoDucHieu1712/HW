using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HW.CS.Demos;

/// <summary>
/// Runnable demos cho interview.NET.11-Runtime-Internals.md (RT-1..RT-22).
/// Mọi con số đều được ĐO tại chỗ (allocation, thời gian, địa chỉ MethodTable)
/// để bạn đối chiếu với phần giải thích trong file markdown.
///
/// Lưu ý khi đọc số: chạy ở Release (`dotnet run -c Release`) mới đúng —
/// bản Debug tắt tối ưu và sinh state machine/closure khác.
/// </summary>
public sealed class Demo11_RuntimeInternals : IDemoTopic
{
    public string Title => "11 — Runtime Internals (CLR, JIT, Type System)";

    public IReadOnlyList<DemoItem> Items => new DemoItem[]
    {
        new("RT1.  IL & metadata token", RT1_Metadata),
        new("RT2.  Object layout — kích thước thật trên heap", RT2_ObjectLayout),
        new("RT3.  MethodTable / type handle", RT3_MethodTable),
        new("RT4.  Chi phí dispatch: static/virtual/interface/sealed", RT4_DispatchCost),
        new("RT5.  JIT lần đầu vs sau khi warm-up (tiering)", RT5_TieredCompilation),
        new("RT6.  Generic: chia sẻ code, static per closed type", RT6_GenericSharing),
        new("RT7.  Boxing — đo allocation thật", RT7_BoxingAllocation),
        new("RT8.  constrained. call: generic vs interface param", RT8_ConstrainedCall),
        new("RT9.  Nullable<T> khi box", RT9_NullableBoxing),
        new("RT10. Defensive copy của struct không readonly", RT10_DefensiveCopy),
        new("RT11. ref local / ref return (managed pointer)", RT11_RefSemantics),
        new("RT12. Delegate bên dưới + closure allocation", RT12_DelegateInternals),
        new("RT13. Reflection vs cached delegate vs direct", RT13_ReflectionCost),
        new("RT14. Static ctor & beforefieldinit", RT14_StaticCtor),
        new("RT15. Chi phí exception vs TryParse", RT15_ExceptionCost),
        new("RT16. String interning + Span (0 allocation)", RT16_StringInternSpan),
        new("RT17. ValueType.Equals reflection fallback", RT17_ValueTypeEquals),
        new("RT18. JIT / AOT / runtime feature hiện tại", RT18_RuntimeInfo),
    };

    // ── RT1 ───────────────────────────────────────────────────────────────
    public static void RT1_Metadata()
    {
        var mi = typeof(string).GetMethod(nameof(string.Trim), Type.EmptyTypes)!;
        Console.WriteLine($"MethodDef token   : 0x{mi.MetadataToken:X8}  (0x06 = bảng MethodDef)");
        Console.WriteLine($"TypeDef  token    : 0x{typeof(string).MetadataToken:X8}  (0x02 = bảng TypeDef)");
        Console.WriteLine($"Assembly của int  : {typeof(int).Assembly.GetName().Name}");
        Console.WriteLine($"Module            : {typeof(string).Module.Name}");
        Console.WriteLine($"Số type public    : {typeof(int).Assembly.GetExportedTypes().Length:N0} (chỉ trong CoreLib)");
        Console.WriteLine();
        Console.WriteLine("→ IL gọi method bằng TOKEN, loader/JIT resolve token lúc chạy.");
        Console.WriteLine("→ Chính metadata này là thứ cho phép reflection, GC chính xác, DI, EF Core hoạt động.");
    }

    // ── RT2 ───────────────────────────────────────────────────────────────
    public static void RT2_ObjectLayout()
    {
        Console.WriteLine("Đo bằng GC.GetAllocatedBytesForCurrentThread() — byte thật cấp phát trên heap:");
        Console.WriteLine($"  new object()          : {Alloc(() => new object())} byte   (8 header + 8 MethodTable + 8 tối thiểu)");
        Console.WriteLine($"  box một int           : {Alloc(() => { _sink = 42; })} byte   (4 byte dữ liệu + padding!)");
        Console.WriteLine($"  new byte[0]           : {Alloc(() => { _ = new byte[0]; })} byte   (thêm 8 byte length)");
        Console.WriteLine($"  new byte[100]         : {Alloc(() => { _ = new byte[100]; })} byte");
        Console.WriteLine($"  new string('a', 10)   : {Alloc(() => { _ = new string('a', 10); })} byte  (2 byte/char UTF-16 + null terminator)");
        Console.WriteLine($"  class 2 field int     : {Alloc(() => new TwoInts())} byte");
        Console.WriteLine($"  class 2 field long    : {Alloc(() => new TwoLongs())} byte");
        Console.WriteLine();
        Console.WriteLine("Kích thước của value type (không có header — nằm inline):");
        Console.WriteLine($"  sizeof(int)                    = {sizeof(int)}");
        Console.WriteLine($"  Unsafe.SizeOf<PointS>()        = {Unsafe.SizeOf<PointS>()}");
        Console.WriteLine($"  Unsafe.SizeOf<DateTime>()      = {Unsafe.SizeOf<DateTime>()}");
        Console.WriteLine($"  Unsafe.SizeOf<Guid>()          = {Unsafe.SizeOf<Guid>()}");
        Console.WriteLine($"  Unsafe.SizeOf<decimal>()       = {Unsafe.SizeOf<decimal>()}");
        Console.WriteLine();
        Console.WriteLine("→ Object nhỏ nhất trên x64 = 24 byte. Boxed int cũng 24 byte cho 4 byte dữ liệu (6× lãng phí).");
    }

    // ── RT3 ───────────────────────────────────────────────────────────────
    public static void RT3_MethodTable()
    {
        Console.WriteLine($"typeof(string) == \"a\".GetType()  : {ReferenceEquals(typeof(string), "a".GetType())} (1 MethodTable duy nhất cho mỗi type)");
        Console.WriteLine($"MethodTable của List<int>        : 0x{typeof(List<int>).TypeHandle.Value.ToInt64():X}");
        Console.WriteLine($"MethodTable của List<string>     : 0x{typeof(List<string>).TypeHandle.Value.ToInt64():X}");
        Console.WriteLine($"MethodTable của List<object>     : 0x{typeof(List<object>).TypeHandle.Value.ToInt64():X}");
        Console.WriteLine("→ Mỗi CLOSED type có MethodTable riêng, dù code JIT có thể dùng chung (xem RT6).");
        Console.WriteLine();

        object o = new Derived();
        Console.WriteLine($"o.GetType()                      : {Clean(o.GetType())}  (đọc từ MethodTable pointer trong object)");
        Console.WriteLine($"Base type                        : {Clean(o.GetType().BaseType)}");
        Console.WriteLine($"Interfaces                       : {string.Join(", ", o.GetType().GetInterfaces().Select(Clean))}");
        Console.WriteLine($"RuntimeHelpers.GetHashCode(o)    : {RuntimeHelpers.GetHashCode(o)}  (hash sync-block, KHÔNG phải địa chỉ)");
    }

    // ── RT4 ───────────────────────────────────────────────────────────────
    public static void RT4_DispatchCost()
    {
        const int N = 20_000_000;
        var sealedShape = new SealedSquare(2);
        Base baseShape = new Derived();
        IShape iface = new SealedSquare(2);

        // warm-up để lên Tier-1 trước khi đo
        for (int i = 0; i < 100_000; i++) { StaticArea(2); sealedShape.Area(); baseShape.Area(); iface.Area(); }

        double sink = 0;
        Console.WriteLine($"  static call        : {Time(() => { for (int i = 0; i < N; i++) sink += StaticArea(2); }),7:F0} ms  (inline được)");
        Console.WriteLine($"  sealed class call  : {Time(() => { for (int i = 0; i < N; i++) sink += sealedShape.Area(); }),7:F0} ms  (JIT devirtualize)");
        Console.WriteLine($"  virtual call       : {Time(() => { for (int i = 0; i < N; i++) sink += baseShape.Area(); }),7:F0} ms  (vtable slot)");
        Console.WriteLine($"  interface call     : {Time(() => { for (int i = 0; i < N; i++) sink += iface.Area(); }),7:F0} ms  (virtual stub dispatch)");
        Console.WriteLine($"  [sink={sink:F0} — chỉ để JIT không loại bỏ vòng lặp]");
        Console.WriteLine();
        Console.WriteLine("→ Chênh lệch nhỏ ở đây, nhưng inline được hay không mới là điều quan trọng:");
        Console.WriteLine("  static/sealed cho phép JIT inline rồi tối ưu tiếp cả khối code xung quanh.");
        Console.WriteLine("  Kết luận thực chiến: đánh `sealed` cho class không định kế thừa = tối ưu miễn phí.");
    }

    // ── RT5 ───────────────────────────────────────────────────────────────
    public static void RT5_TieredCompilation()
    {
        // Lần gọi đầu tiên của một method BAO GỒM cả thời gian JIT Tier-0 của nó.
        var sw = Stopwatch.StartNew();
        ColdMethod(1);
        double first = sw.Elapsed.TotalMicroseconds;

        sw.Restart();
        for (int i = 0; i < 1000; i++) ColdMethod(i);
        double next1000 = sw.Elapsed.TotalMicroseconds;

        // sau ~30 lần gọi + độ trễ call-counting, method được promote lên Tier-1
        for (int i = 0; i < 200_000; i++) ColdMethod(i);
        Thread.Sleep(150);                       // để call-counting delay trôi qua
        for (int i = 0; i < 200_000; i++) ColdMethod(i);

        sw.Restart();
        for (int i = 0; i < 1000; i++) ColdMethod(i);
        double hot1000 = sw.Elapsed.TotalMicroseconds;

        Console.WriteLine($"  Lần gọi ĐẦU TIÊN (gồm JIT Tier-0) : {first,9:F3} µs / 1 lần gọi");
        Console.WriteLine($"  Tier-0, chưa promote              : {next1000 / 1000,9:F3} µs / lần  (tổng {next1000:F0} µs cho 1000 lần)");
        Console.WriteLine($"  Tier-1, sau warm-up               : {hot1000 / 1000,9:F3} µs / lần  (tổng {hot1000:F0} µs cho 1000 lần)");
        Console.WriteLine();
        Console.WriteLine($"→ Lần gọi đầu đắt gấp ~{first / Math.Max(hot1000 / 1000, 0.0001):F0} lần một lần gọi ở Tier-1 — toàn bộ chênh lệch đó là JIT.");
        Console.WriteLine($"→ Tier-0 → Tier-1 nhanh thêm ~{next1000 / Math.Max(hot1000, 0.0001):F1} lần cho CÙNG một method.");
        Console.WriteLine("→ Hệ quả: benchmark KHÔNG warm-up = bạn đang đo Tier-0, không phải code thật chạy production.");
        Console.WriteLine("→ Tắt tiering để đo steady-state: DOTNET_TieredCompilation=0");
    }

    // ── RT6 ───────────────────────────────────────────────────────────────
    public static void RT6_GenericSharing()
    {
        GenericCounter<int>.Value = 1;
        GenericCounter<string>.Value = 2;
        GenericCounter<object>.Value = 3;
        Console.WriteLine("Static field là PER CLOSED TYPE (dù code JIT có thể dùng chung):");
        Console.WriteLine($"  GenericCounter<int>.Value    = {GenericCounter<int>.Value}");
        Console.WriteLine($"  GenericCounter<string>.Value = {GenericCounter<string>.Value}");
        Console.WriteLine($"  GenericCounter<object>.Value = {GenericCounter<object>.Value}");
        Console.WriteLine();

        Console.WriteLine("Generic value type được SPECIALIZE — điều kiện trên typeof(T) bị fold thành hằng:");
        Console.WriteLine($"  Describe<int>()     → {Describe<int>()}");
        Console.WriteLine($"  Describe<double>()  → {Describe<double>()}");
        Console.WriteLine($"  Describe<string>()  → {Describe<string>()}");
        Console.WriteLine();

        var openList = typeof(List<>);
        Console.WriteLine($"  Open generic        : {openList.Name}, IsGenericTypeDefinition={openList.IsGenericTypeDefinition}");
        Console.WriteLine($"  Closed qua MakeGenericType: {openList.MakeGenericType(typeof(int)).Name}");
        Console.WriteLine();
        Console.WriteLine("→ Reference type instantiation dùng chung code (__Canon) vì mọi reference đều là con trỏ 8 byte.");
        Console.WriteLine("→ Value type instantiation có code riêng ⇒ không box, layout tối ưu (đó là lý do List<int> nhanh hơn ArrayList).");
    }

    // ── RT7 ───────────────────────────────────────────────────────────────
    public static void RT7_BoxingAllocation()
    {
        const int N = 100_000;

        long boxed = Alloc(() =>
        {
            object[] arr = new object[N];
            for (int i = 0; i < N; i++) arr[i] = i;      // 1 box mỗi phần tử
        });

        long plain = Alloc(() =>
        {
            int[] arr = new int[N];
            for (int i = 0; i < N; i++) arr[i] = i;      // không box
        });

        Console.WriteLine($"  object[{N:N0}] gán int  : {boxed / 1024,7:N0} KB  ← mảng con trỏ + {N:N0} box");
        Console.WriteLine($"  int[{N:N0}]             : {plain / 1024,7:N0} KB");
        Console.WriteLine($"  → gấp {(double)boxed / plain:F1} lần");
        Console.WriteLine();

        var noEqA = new NoEquatable(1, "x");
        var noEqB = new NoEquatable(1, "x");
        var eqA = new WithEquatable(1, "x");
        var eqB = new WithEquatable(1, "x");

        long allocInterface = Alloc(() => { IComparable c = 42; _sink = c; });
        long allocNoEquatable = Alloc(() => { _ = noEqA.Equals(noEqB); });
        long allocEquatable = Alloc(() => { _ = eqA.Equals(eqB); });
        long allocFormat = Alloc(() => string.Format("{0}", 42));

        Console.WriteLine("Boxing ngầm hay bị bỏ sót:");
        Console.WriteLine($"  IComparable c = 42                : {allocInterface} byte");
        Console.WriteLine($"  struct KHÔNG có IEquatable.Equals : {allocNoEquatable} byte  (box cả hai vế)");
        Console.WriteLine($"  struct CÓ IEquatable<T>.Equals    : {allocEquatable} byte");
        Console.WriteLine($"  string.Format(mẫu, 42)            : {allocFormat} byte (box 42 + string kết quả)");
        Console.WriteLine();
        Console.WriteLine("→ Mỗi box không chỉ tốn 24 byte: nó thành một NODE trong đồ thị object mà GC phải duyệt mỗi lần mark.");
    }

    // ── RT8 ───────────────────────────────────────────────────────────────
    public static void RT8_ConstrainedCall()
    {
        const int N = 200_000;
        var a = new WithEquatable(1, "x");
        var b = new WithEquatable(2, "y");

        long viaInterface = Alloc(() =>
        {
            for (int i = 0; i < N; i++) _ = Rt11Helpers.CompareViaInterface(a, b);   // box tham số
        });
        long viaGeneric = Alloc(() =>
        {
            for (int i = 0; i < N; i++) _ = CompareViaGeneric(a, b);     // constrained. call
        });

        Console.WriteLine($"  Tham số kiểu interface : {viaInterface / 1024,7:N0} KB cho {N:N0} lần gọi  (mỗi lần 1 box)");
        Console.WriteLine($"  Generic + constraint   : {viaGeneric / 1024,7:N0} KB cho {N:N0} lần gọi  (0 allocation)");
        Console.WriteLine();
        Console.WriteLine("→ Cùng một struct, cùng một interface — khác nhau chỉ ở CÁCH GỌI.");
        Console.WriteLine("→ IL: `constrained. T callvirt` resolve tĩnh ra implementation của struct ⇒ gọi trên ref, không tạo object.");
    }

    // ── RT9 ───────────────────────────────────────────────────────────────
    public static void RT9_NullableBoxing()
    {
        int? hasValue = 42;
        int? isNull = null;

        object? boxedValue = hasValue;
        object? boxedNull = isNull;

        Console.WriteLine($"  int? = 42  → box → type thật : {boxedValue!.GetType().Name}  (Int32, KHÔNG phải Nullable`1!)");
        Console.WriteLine($"  int? = null → box → null?    : {boxedNull is null}");
        Console.WriteLine($"  Allocation khi box int? null : {Alloc(() => { int? n = null; _sink = n; })} byte");
        Console.WriteLine($"  Allocation khi box int? 42   : {Alloc(() => { int? n = 42; _sink = n; })} byte");
        Console.WriteLine();
        Console.WriteLine("→ Runtime xử lý Nullable<T> ĐẶC BIỆT: box giá trị bên trong, hoặc trả về null.");
        Console.WriteLine("→ Vì thế `(object)someNullableInt is int` = true khi có giá trị, và `typeof(int?)` chỉ xuất hiện ở compile time.");
    }

    // ── RT10 ──────────────────────────────────────────────────────────────
    public static void RT10_DefensiveCopy()
    {
        var holder = new StructHolder();
        holder.IncrementBoth(3);

        Console.WriteLine("Cùng một struct, cùng một method Increment(), khác nhau chỉ ở `readonly`:");
        Console.WriteLine($"  field KHÔNG readonly, gọi Increment() 3 lần : Value = {holder.WritableValue}  ✅ sửa đúng object");
        Console.WriteLine($"  field CÓ  readonly, gọi Increment() 3 lần   : Value = {holder.ReadonlyValue}  ⚠️ mất trắng!");
        Console.WriteLine();
        Console.WriteLine("→ Vì `Increment()` không phải readonly method, compiler KHÔNG dám gọi nó trên field readonly.");
        Console.WriteLine("  Nó COPY struct ra một biến tạm rồi gọi trên bản sao ⇒ thay đổi biến mất (DEFENSIVE COPY).");
        Console.WriteLine("  Đây là bug 'im lặng' — không warning, không exception, chỉ là dữ liệu sai.");
        Console.WriteLine();

        Console.WriteLine("Chi phí: copy ẩn xảy ra ở MỌI lần gọi member — với struct lớn trong vòng lặp nóng là rất đắt.");
        Console.WriteLine($"  Unsafe.SizeOf<BigMutable>()  = {Unsafe.SizeOf<BigMutable>()} byte  ← bị copy mỗi lần gọi member qua `in`/readonly field");
        Console.WriteLine($"  Unsafe.SizeOf<BigReadonly>() = {Unsafe.SizeOf<BigReadonly>()} byte  ← không copy");
        Console.WriteLine("  (JIT hiện đại đôi khi tự loại bỏ được copy nếu chứng minh được không ai quan sát thấy —");
        Console.WriteLine("   nhưng đừng phụ thuộc vào may mắn đó, và về SEMANTICS thì lỗi ở trên vẫn xảy ra.)");
        Console.WriteLine();
        Console.WriteLine("→ Quy tắc: LUÔN khai báo `readonly struct` (hoặc `readonly` từng member).");
        Console.WriteLine("  `in` chỉ thực sự có lợi khi struct LỚN *và* READONLY.");
    }

    // ── RT11 ──────────────────────────────────────────────────────────────
    public static void RT11_RefSemantics()
    {
        int[] arr = { 1, 2, 3, 4, 5 };

        ref int slot = ref arr[2];      // managed pointer trỏ vào GIỮA mảng
        slot = 99;                      // ghi thẳng, không copy
        Console.WriteLine($"  Sau `ref int slot = ref arr[2]; slot = 99;` → arr = [{string.Join(", ", arr)}]");

        ref int found = ref FindMax(arr);
        found = 0;
        Console.WriteLine($"  Sau khi ghi 0 vào ref return của FindMax  → arr = [{string.Join(", ", arr)}]");

        // ref local trên struct trong mảng: sửa TẠI CHỖ, không copy
        var points = new PointS[3];
        ref var p = ref points[1];
        p.X = 7;
        Console.WriteLine($"  points[1].X sau khi sửa qua `ref var` = {points[1].X} (sửa tại chỗ)");
        Console.WriteLine();

        var span = arr.AsSpan(1, 3);
        span[0] = -1;
        Console.WriteLine($"  Span là cửa sổ trên CÙNG bộ nhớ → arr = [{string.Join(", ", arr)}]");
        Console.WriteLine($"  Allocation khi tạo span: {Alloc(() => { var s = arr.AsSpan(1, 3); _ = s.Length; })} byte");
        Console.WriteLine();
        Console.WriteLine("→ `ref` = managed pointer mà GC biết và cập nhật khi compact heap.");
        Console.WriteLine("→ Luật byref-safety (không nằm trên heap) chính là lý do Span<T> phải là `ref struct`,");
        Console.WriteLine("  và vì thế không dùng được trong async/yield.");
    }

    // ── RT12 ──────────────────────────────────────────────────────────────
    public static void RT12_DelegateInternals()
    {
        var user = new NamedThing("Alice");
        Action staticDel = StaticNoop;
        Action instanceDel = user.Noop;

        Console.WriteLine($"  static delegate  → Target = {staticDel.Target?.ToString() ?? "null"},  Method = {staticDel.Method.Name}");
        Console.WriteLine($"  instance delegate→ Target = {instanceDel.Target},  Method = {instanceDel.Method.Name}");
        Console.WriteLine("  → delegate instance GIỮ Target sống ⇒ `publisher.Event += sub.Handler` là nguồn memory leak kinh điển.");
        Console.WriteLine();

        Action multi = staticDel + instanceDel + staticDel;
        Console.WriteLine($"  Multicast invocation list: {multi.GetInvocationList().Length} phần tử (gọi tuần tự, chỉ giữ giá trị trả về cuối)");
        Console.WriteLine();

        long allocNoCapture = Alloc(() =>
        {
            for (int i = 0; i < 100; i++) { Func<int> f = static () => 1; _sink = f; }
        });
        long allocCapture = Alloc(() =>
        {
            for (int i = 0; i < 100; i++) { int local = i; Func<int> f = () => local; _sink = f; }
        });

        Console.WriteLine($"  Lambda KHÔNG capture, tạo 100 lần : {allocNoCapture,6:N0} byte  ← compiler CACHE delegate vào static field");
        Console.WriteLine($"  Lambda CÓ capture,    tạo 100 lần : {allocCapture,6:N0} byte  ← mỗi vòng: 1 closure class + 1 delegate");
        Console.WriteLine();
        Console.WriteLine("→ Trong hot path: tránh lambda capture; truyền state qua tham số (LINQ có overload nhận state, ví dụ ArrayPool/Task.Factory).");
    }

    // ── RT13 ──────────────────────────────────────────────────────────────
    public static void RT13_ReflectionCost()
    {
        const int N = 1_000_000;
        var thing = new NamedThing("Alice");
        var prop = typeof(NamedThing).GetProperty(nameof(NamedThing.Name))!;
        var getter = (Func<NamedThing, string>)Delegate.CreateDelegate(typeof(Func<NamedThing, string>), prop.GetMethod!);

        string? sink = null;
        for (int i = 0; i < 10_000; i++) { sink = thing.Name; sink = getter(thing); }   // warm-up

        double direct = Time(() => { for (int i = 0; i < N; i++) sink = thing.Name; });
        double viaDelegate = Time(() => { for (int i = 0; i < N; i++) sink = getter(thing); });
        double viaReflection = Time(() => { for (int i = 0; i < N / 10; i++) sink = (string)prop.GetValue(thing)!; }) * 10;

        Console.WriteLine($"  Gọi trực tiếp            : {direct,8:F0} ms cho {N:N0} lần   (1×)");
        Console.WriteLine($"  Cached delegate          : {viaDelegate,8:F0} ms                    ({viaDelegate / Math.Max(direct, 0.01),5:F1}×)");
        Console.WriteLine($"  PropertyInfo.GetValue    : {viaReflection,8:F0} ms (ước lượng)      ({viaReflection / Math.Max(direct, 0.01),5:F1}×)");
        var ageProp = typeof(NamedThing).GetProperty(nameof(NamedThing.Age))!;
        long allocGetValue = Alloc(() => { for (int i = 0; i < 1000; i++) _sink = ageProp.GetValue(thing); });
        Console.WriteLine($"  Allocation của 1000 lần GetValue trên property int : {allocGetValue:N0} byte (box mỗi lần vì API trả object)");
        Console.WriteLine($"  [sink={sink}]");
        Console.WriteLine();
        Console.WriteLine("→ Reflection trong hot path là bug hiệu năng. Cache lại delegate/Expression.Compile,");
        Console.WriteLine("  hoặc tốt nhất: dùng SOURCE GENERATOR để dời việc đọc metadata sang compile time (AOT-safe).");
    }

    // ── RT14 ──────────────────────────────────────────────────────────────
    public static void RT14_StaticCtor()
    {
        Console.WriteLine("Class KHÔNG có static ctor tường minh (beforefieldinit):");
        Console.WriteLine($"  … chưa đụng tới field  → đã khởi tạo? {BeforeFieldInit.Initialized}");
        Console.WriteLine($"  Đọc field              → {BeforeFieldInit.Value}");
        Console.WriteLine();
        Console.WriteLine("Class CÓ static ctor tường minh (precise init):");
        Console.WriteLine($"  Đọc field              → {WithStaticCtor.Value}");
        Console.WriteLine("  (dòng log trong static ctor phải xuất hiện NGAY TRƯỚC dòng trên)");
        Console.WriteLine();
        Console.WriteLine("→ CLR bảo đảm static ctor chạy đúng MỘT lần, thread-safe ⇒ nền của singleton `static readonly Lazy<T>`.");
        Console.WriteLine("→ Đánh đổi: có static ctor tường minh ⇒ mất beforefieldinit ⇒ JIT chèn class-init check ở nhiều call site.");
        Console.WriteLine("→ Cảnh báo: exception trong static ctor ⇒ TypeInitializationException và type HỎNG VĨNH VIỄN trong process.");
    }

    // ── RT15 ──────────────────────────────────────────────────────────────
    public static void RT15_ExceptionCost()
    {
        const int N = 20_000;
        string[] inputs = { "123", "abc" };   // một nửa hợp lệ, một nửa không

        int sink = 0;
        double withException = Time(() =>
        {
            for (int i = 0; i < N; i++)
            {
                try { sink += int.Parse(inputs[i % 2]); }
                catch (FormatException) { sink += 0; }
            }
        });
        double withTryParse = Time(() =>
        {
            for (int i = 0; i < N; i++)
                sink += int.TryParse(inputs[i % 2], out int v) ? v : 0;
        });

        Console.WriteLine($"  int.Parse + catch : {withException,8:F1} ms cho {N:N0} lần ({N / 2:N0} exception)");
        Console.WriteLine($"  int.TryParse      : {withTryParse,8:F1} ms cho {N:N0} lần");
        Console.WriteLine($"  → chậm hơn khoảng {withException / Math.Max(withTryParse, 0.01):F0} lần");
        Console.WriteLine($"  [sink={sink}]");
        Console.WriteLine();
        Console.WriteLine("→ Chi phí nằm ở two-pass EH: unwind info, funclet, và THU THẬP STACK TRACE (đọc metadata + PDB).");
        Console.WriteLine("→ Exception cho trường hợp BẤT THƯỜNG; Result/TryParse cho luồng nghiệp vụ dự đoán được.");
    }

    // ── RT16 ──────────────────────────────────────────────────────────────
    public static void RT16_StringInternSpan()
    {
        string a = "hello";
        string b = "hello";
        string c = new string(new[] { 'h', 'e', 'l', 'l', 'o' });

        string runtimeOnly = new string("xyz-runtime".ToCharArray());
        bool notInterned = string.IsInterned(runtimeOnly) is null;

        Console.WriteLine($"  literal vs literal   : ReferenceEquals = {ReferenceEquals(a, b)}  (cùng object trong intern pool)");
        Console.WriteLine($"  literal vs runtime   : ReferenceEquals = {ReferenceEquals(a, c)}  (chuỗi tính lúc chạy KHÔNG tự intern)");
        Console.WriteLine($"  sau string.Intern(c) : ReferenceEquals = {ReferenceEquals(a, string.Intern(c))}");
        Console.WriteLine($"  chuỗi runtime chưa có trong pool  : {notInterned}");
        Console.WriteLine("  ⚠️ intern pool sống suốt đời process → KHÔNG intern dữ liệu người dùng nhập (leak).");
        Console.WriteLine();

        const string date = "2024-01-15T10:30:00";
        long allocSubstring = Alloc(() =>
        {
            for (int i = 0; i < 1000; i++) { _ = int.Parse(date.Substring(0, 4)); _ = int.Parse(date.Substring(5, 2)); }
        });
        long allocSpan = Alloc(() =>
        {
            for (int i = 0; i < 1000; i++) { _ = int.Parse(date.AsSpan(0, 4)); _ = int.Parse(date.AsSpan(5, 2)); }
        });

        Console.WriteLine("Parse với Substring vs với Span (1000 vòng, mỗi vòng 2 lần parse):");
        Console.WriteLine($"  Substring : {allocSubstring,7:N0} byte");
        Console.WriteLine($"  AsSpan    : {allocSpan,7:N0} byte  ← 0 allocation");
        Console.WriteLine();
        Console.WriteLine("→ Span cho phép 'cắt' mà không copy: đây là nền của parser/serializer hiệu năng cao trong .NET hiện đại.");
    }

    // ── RT17 ──────────────────────────────────────────────────────────────
    public static void RT17_ValueTypeEquals()
    {
        const int N = 300_000;
        var noEq1 = new NoEquatable(1, "x");
        var noEq2 = new NoEquatable(1, "x");
        var eq1 = new WithEquatable(1, "x");
        var eq2 = new WithEquatable(1, "x");

        bool sink = false;
        for (int i = 0; i < 10_000; i++) { sink ^= noEq1.Equals(noEq2); sink ^= eq1.Equals(eq2); }  // warm-up

        double slow = Time(() => { for (int i = 0; i < N; i++) sink ^= noEq1.Equals(noEq2); });
        double fast = Time(() => { for (int i = 0; i < N; i++) sink ^= eq1.Equals(eq2); });

        Console.WriteLine($"  struct có field reference, KHÔNG IEquatable<T> : {slow,7:F0} ms / {N:N0} lần  ← ValueType.Equals dùng REFLECTION");
        Console.WriteLine($"  struct implement IEquatable<T>                 : {fast,7:F0} ms / {N:N0} lần");
        Console.WriteLine($"  → nhanh hơn khoảng {slow / Math.Max(fast, 0.01):F0} lần   [sink={sink}]");
        Console.WriteLine();
        Console.WriteLine($"  Allocation 1000 lần Equals (không IEquatable) : {Alloc(() => { for (int i = 0; i < 1000; i++) _ = noEq1.Equals(noEq2); })} byte (box cả 2 vế)");
        Console.WriteLine($"  Allocation 1000 lần Equals (có IEquatable)    : {Alloc(() => { for (int i = 0; i < 1000; i++) _ = eq1.Equals(eq2); })} byte");
        Console.WriteLine();
        Console.WriteLine("→ Dictionary/HashSet dùng EqualityComparer<T>.Default, comparer này ƯU TIÊN IEquatable<T>.");
        Console.WriteLine("  Struct làm key mà quên IEquatable<T> = box + reflection ở MỌI lần lookup.");
        Console.WriteLine("→ Cách nhanh nhất: dùng `record struct` — compiler sinh sẵn Equals/GetHashCode/IEquatable.");
    }

    // ── RT18 ──────────────────────────────────────────────────────────────
    public static void RT18_RuntimeInfo()
    {
        Console.WriteLine($"  Framework            : {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"  Runtime identifier   : {RuntimeInformation.RuntimeIdentifier}");
        Console.WriteLine($"  Process architecture : {RuntimeInformation.ProcessArchitecture}  (ARM64 ⇒ memory model YẾU, xem ASY-13)");
        Console.WriteLine($"  OS                   : {RuntimeInformation.OSDescription}");
        Console.WriteLine($"  Logical cores        : {Environment.ProcessorCount}");
        Console.WriteLine();
        Console.WriteLine($"  IsDynamicCodeSupported : {RuntimeFeature.IsDynamicCodeSupported}  (false ⇒ Native AOT)");
        Console.WriteLine($"  IsDynamicCodeCompiled  : {RuntimeFeature.IsDynamicCodeCompiled}   (true ⇒ có JIT)");
        Console.WriteLine();
        Console.WriteLine("  Biến môi trường điều khiển JIT (đặt trước khi chạy):");
        Console.WriteLine("    DOTNET_TieredCompilation=0   → JIT full ngay từ đầu (đo steady-state)");
        Console.WriteLine("    DOTNET_TieredPGO=0           → tắt dynamic PGO");
        Console.WriteLine("    DOTNET_ReadyToRun=0          → bỏ qua code R2R có sẵn, JIT lại tất cả");
        Console.WriteLine("    DOTNET_JitDisasm=\"TênMethod\" → in assembly do JIT sinh");
    }

    // ── Helper dùng chung ─────────────────────────────────────────────────

    /// <summary>Nơi "đổ" object ra ngoài để JIT không loại bỏ boxing (escape analysis).</summary>
    private static object? _sink;

    /// <summary>Bỏ phần tên bị mangling của type file-local (`&lt;File&gt;HASH__Ten`).</summary>
    private static string Clean(Type? t)
    {
        if (t is null) return "null";
        int i = t.Name.LastIndexOf("__", StringComparison.Ordinal);
        return i >= 0 ? t.Name[(i + 2)..] : t.Name;
    }

    /// <summary>Đo số byte cấp phát trên heap bởi <paramref name="action"/> (thread hiện tại).</summary>
    private static long Alloc(Action action)
    {
        action();                                   // chạy trước 1 lần để JIT + khởi tạo static
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>Đo thời gian (ms) của một action.</summary>
    private static double Time(Action action)
    {
        var sw = Stopwatch.StartNew();
        action();
        return sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>Method "lạnh" dùng cho demo tiering — không inline để thấy rõ chi phí JIT lần đầu.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long ColdMethod(int seed)
    {
        long acc = seed;
        for (int i = 0; i < 32; i++) acc = acc * 31 + i;
        return acc;
    }

    private static double StaticArea(double side) => side * side;
    private static void StaticNoop() { }

    private static string Describe<T>()
        => typeof(T) == typeof(int) ? "int — code JIT chuyên biệt cho Int32"
         : typeof(T) == typeof(double) ? "double — code JIT chuyên biệt cho Double"
         : typeof(T).IsValueType ? "value type khác — vẫn specialize"
         : "reference type — dùng chung code __Canon";

    private static int CompareViaGeneric<T>(T a, T b) where T : IComparable<T> => a.CompareTo(b);

    private static ref int FindMax(int[] arr)
    {
        int idx = 0;
        for (int i = 1; i < arr.Length; i++) if (arr[i] > arr[idx]) idx = i;
        return ref arr[idx];
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Helper types
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Helper phải là file-local vì signature của chúng dùng type file-local.</summary>
file static class Rt11Helpers
{
    public static int CompareViaInterface(IComparable<WithEquatable> a, WithEquatable b) => a.CompareTo(b);
    public static long SumIn(in BigMutable s) => s.First();
    public static long SumIn(in BigReadonly s) => s.First();
}

file sealed class TwoInts { public int A = 1; public int B = 2; }
file sealed class TwoLongs { public long A = 1; public long B = 2; }

file struct PointS { public int X; public int Y; }

file interface IShape { double Area(); }
file class Base : IShape { public virtual double Area() => 1; }
file sealed class Derived : Base { public override double Area() => 2; }
file sealed class SealedSquare(double side) : IShape
{
    private readonly double _side = side;
    public double Area() => _side * _side;
}

file static class GenericCounter<T> { public static int Value; }

file struct NoEquatable(int id, string name)          // có field reference ⇒ ValueType.Equals dùng reflection
{
    public int Id = id;
    public string Name = name;
}

file readonly struct WithEquatable(int id, string name) : IEquatable<WithEquatable>, IComparable<WithEquatable>
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public bool Equals(WithEquatable other) => Id == other.Id && Name == other.Name;
    public override bool Equals(object? obj) => obj is WithEquatable w && Equals(w);
    public override int GetHashCode() => HashCode.Combine(Id, Name);
    public int CompareTo(WithEquatable other) => Id.CompareTo(other.Id);
}

file struct MutableCounter
{
    public int Value;
    public void Increment() => Value++;               // KHÔNG readonly ⇒ gây defensive copy
}

file sealed class StructHolder
{
    private readonly MutableCounter _readonlyField = new();   // ⚠️ readonly ⇒ defensive copy
    private MutableCounter _writableField = new();            // ✅ không readonly

    public int ReadonlyValue => _readonlyField.Value;
    public int WritableValue => _writableField.Value;

    public void IncrementBoth(int times)
    {
        for (int i = 0; i < times; i++)
        {
            _readonlyField.Increment();   // compiler copy ra biến tạm rồi mới gọi → thay đổi mất
            _writableField.Increment();   // gọi thẳng trên field → thay đổi giữ lại
        }
    }
}

#pragma warning disable CS0649 // các field còn lại chỉ để struct đủ lớn cho demo

/// <summary>256 byte, method KHÔNG readonly ⇒ mỗi lần gọi qua `in` là một defensive copy 256 byte.</summary>
file struct BigMutable
{
    public long F00, F01, F02, F03, F04, F05, F06, F07, F08, F09, F10, F11, F12, F13, F14, F15;
    public long F16, F17, F18, F19, F20, F21, F22, F23, F24, F25, F26, F27, F28, F29, F30, F31;
    public BigMutable(long seed) { F00 = seed; F31 = seed; }
    public long First() => F00;                 // chỉ đọc 1 field — nhưng vẫn phải copy cả struct
}

/// <summary>Cùng kích thước, nhưng `readonly struct` ⇒ compiler KHÔNG cần copy.</summary>
file readonly struct BigReadonly
{
    public readonly long F00, F01, F02, F03, F04, F05, F06, F07, F08, F09, F10, F11, F12, F13, F14, F15;
    public readonly long F16, F17, F18, F19, F20, F21, F22, F23, F24, F25, F26, F27, F28, F29, F30, F31;
    public BigReadonly(long seed) { F00 = seed; F31 = seed; }
    public long First() => F00;
}

#pragma warning restore CS0649

file sealed class NamedThing(string name)
{
    public string Name { get; } = name;
    public int Age { get; } = 30;
    public void Noop() { }
    public override string ToString() => $"NamedThing({Name})";
}

file static class BeforeFieldInit
{
    public static readonly int Value = 42;
    public static bool Initialized => true;
}

file static class WithStaticCtor
{
    public static readonly int Value;
    static WithStaticCtor()
    {
        Console.WriteLine("  [static ctor của WithStaticCtor đang chạy…]");
        Value = 42;
    }
}
