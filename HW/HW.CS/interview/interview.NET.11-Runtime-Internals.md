# Phần 11 — Runtime Internals: CLR, JIT & Type System

[⬅️ Về mục lục](interview.NET.md) | Tiếp theo: [Phần 12 — Memory & GC ➡️](interview.NET.12-Memory-GC-Internals.md)

> **Track "deep understand".** File 01–10 trả lời *"cái gì / khi nào"*. Track 11–14 trả lời
> *"tồn tại để giải quyết vấn đề gì → bên dưới hoạt động ra sao → hệ quả nào bạn đo được"*.
>
> Mỗi câu theo khung: **❓ Vấn đề gốc → ⚙️ Cơ chế bên dưới → 💻 Code/đo đạc → ⚖️ Hệ quả thực chiến**.
> Demo chạy được: `dotnet run -- 11 all` (xem `Demos/Demo11_RuntimeInternals.cs`).

---

## 🗺️ Bản đồ: từ file `.cs` tới lệnh CPU

```
Program.cs
   │  Roslyn (csc) — compile time
   ▼
┌────────────────────────────────────────────────────────────┐
│  Assembly (.dll)  =  PE file                               │
│  ├─ CLI header                                             │
│  ├─ Metadata tables (TypeDef, MethodDef, Field, MemberRef…)│
│  ├─ Heaps (#Strings, #US, #Blob, #GUID)                    │
│  └─ IL code cho từng method body                           │
└────────────────────────────────────────────────────────────┘
   │  AssemblyLoadContext nạp → type loader dựng MethodTable từ metadata
   ▼
┌────────────────────────────────────────────────────────────┐
│  CLR (CoreCLR)                                             │
│  ├─ Type loader   → MethodTable / EEClass                  │
│  ├─ JIT (RyuJIT)  → Tier0 → (call counting) → Tier1        │
│  ├─ GC            → managed heap, write barrier            │
│  └─ EE services   → exception, interop, reflection         │
└────────────────────────────────────────────────────────────┘
   ▼
machine code trong code heap → CPU
```

**Câu chốt phỏng vấn:** *"C# không compile thẳng ra máy. Nó compile ra IL + metadata — một dạng
mã **tự mô tả** (self-describing). Chính metadata là thứ cho phép GC biết slot nào là reference,
reflection biết type có gì, và JIT sinh code tối ưu theo CPU thật đang chạy."*

---

## RT-1. Tại sao .NET cần IL? Nó giải quyết vấn đề gì?

**❓ Vấn đề gốc**: compile thẳng ra native thì (1) mỗi CPU/OS một binary, (2) không có metadata
→ không có GC chính xác, không reflection, không type-safety xuyên assembly, (3) không tối ưu
được theo CPU thật lúc chạy.

**⚙️ Cơ chế**: IL là bytecode dạng stack + **metadata mô tả mọi type/member**. Runtime dùng
metadata để:
- **GC chính xác (precise GC)**: biết chính xác slot nào trên stack / trong object là reference.
- **Type safety**: verify IL, type identity xuyên assembly.
- **JIT theo máy đích**: bật AVX-512 nếu CPU có, inline theo profile thật (dynamic PGO).

```csharp
// C#
int Add(int a, int b) => a + b;
```
```il
// IL (xem bằng ILSpy / ildasm / sharplab.io)
ldarg.1      // đẩy a
ldarg.2      // đẩy b
add
ret
```

**⚖️ Trade-off**: đổi lấy chi phí JIT lúc khởi động → sinh ra ReadyToRun và Native AOT (RT-9).

---

## RT-2. Assembly / metadata / token — bên trong .dll có gì?

**⚙️ Cơ chế**: `.dll` .NET là file PE có thêm **CLI header** trỏ tới metadata. Metadata là các
**bảng quan hệ** (giống một database nhỏ):

| Bảng | Chứa gì |
|---|---|
| `TypeDef` | type khai báo trong assembly này |
| `TypeRef` | type tham chiếu từ assembly khác |
| `MethodDef` | method + RVA trỏ tới IL body |
| `Field`, `Param`, `Property`, `Event` | thành viên |
| `MemberRef` | member của assembly khác |
| `AssemblyRef` | assembly phụ thuộc |

Mỗi hàng có một **metadata token** 4 byte: `0x06000001` = (bảng `MethodDef` = 0x06, row 1).
IL gọi method bằng token chứ không phải địa chỉ → loader/JIT resolve token lúc chạy.

```csharp
var mi = typeof(string).GetMethod(nameof(string.Trim), Type.EmptyTypes)!;
Console.WriteLine($"0x{mi.MetadataToken:X8}");    // 0x06......
Console.WriteLine(typeof(int).Assembly.FullName); // System.Private.CoreLib
```

**⚖️ Hệ quả**: metadata là lý do reflection, serializer, DI container, EF Core, Mapster hoạt động
được — và cũng là lý do **trimming/AOT khó**: trimmer không biết code nào sẽ resolve token bằng
string lúc runtime.

---

## RT-3. AssemblyLoadContext — assembly được nạp thế nào?

**❓ Vấn đề gốc**: cần (1) cô lập plugin, (2) nạp 2 version khác nhau của cùng một lib, (3) **gỡ**
code đã nạp. AppDomain của .NET Framework quá nặng; `Assembly.Load` phẳng thì không cô lập.

**⚙️ Cơ chế**:
- `AssemblyLoadContext.Default` — nơi app + shared framework được nạp; resolve theo `*.deps.json`.
- ALC tuỳ biến (`isCollectible: true`) — nạp riêng, có thể `Unload()`.
- Thứ tự resolve: đã nạp trong ALC → `Load()` override → Default ALC → sự kiện `Resolving`.

```csharp
var alc = new AssemblyLoadContext("plugin", isCollectible: true);
var asm = alc.LoadFromAssemblyPath(pluginPath);
// … dùng xong, bỏ MỌI reference tới type trong asm rồi:
alc.Unload();   // chỉ thực sự giải phóng sau khi GC dọn hết
```

**⚖️ Bẫy thực chiến**: chỉ **một** reference còn sót (event handler, static cache, `Type` nằm
trong `Dictionary`) là ALC không unload được → "assembly leak".

---

## RT-4. Một object trên heap trông như thế nào?

**⚙️ Layout (CoreCLR, x64)**:

```
        ┌───────────────────────┐  ← địa chỉ - 8
        │ Object Header (8B)    │  sync block index: lock, hash code, GC bits
obj ──► ├───────────────────────┤  ← reference của bạn trỏ vào ĐÂY
        │ MethodTable* (8B)     │  "type handle" — object biết mình là type gì
        ├───────────────────────┤
        │ field 1 …             │  JIT sắp xếp lại field để giảm padding
        │ field 2 …             │
        └───────────────────────┘
```

- **Object nhỏ nhất trên x64 = 24 byte** (8 header + 8 MethodTable + tối thiểu 8 payload).
- `new object()` → 24 byte. **Boxed `int` cũng 24 byte** (4 byte dữ liệu + padding!).
- Array thêm 8 byte length; `string` lưu `int _stringLength` + dữ liệu char **inline**.

```csharp
long before = GC.GetAllocatedBytesForCurrentThread();
object o = new object();
Console.WriteLine(GC.GetAllocatedBytesForCurrentThread() - before); // 24
```

**⚖️ Hệ quả**: 1 triệu `int` boxed ≈ 24 MB thay vì 4 MB → xem RT-12 và MEM-21.

---

## RT-5. MethodTable là gì? `typeof(T)` trả về cái gì?

**⚙️ Cơ chế**: mỗi **closed type** có đúng một `MethodTable` (runtime type handle), chứa:
- con trỏ tới base MethodTable, kích thước instance, GC layout (field nào là reference),
- **method slot table** (vtable cho virtual method),
- interface map (interface → slot),
- con trỏ tới `EEClass` — thông tin "lạnh" (tên, metadata field) chỉ dùng khi reflection.

`obj.GetType()` = đọc MethodTable pointer. `typeof(T)` = token → `RuntimeType` bọc MethodTable đó.

```csharp
Console.WriteLine(ReferenceEquals(typeof(string), "a".GetType())); // True — 1 MethodTable duy nhất
Console.WriteLine(typeof(List<int>).TypeHandle.Value);    // địa chỉ MethodTable
Console.WriteLine(typeof(List<string>).TypeHandle.Value); // KHÁC — mỗi closed type một MT
```

**⚖️ Hệ quả**: static field là **per closed generic type** — `Cache<string>.Items` và
`Cache<int>.Items` là hai storage khác nhau. Đây là mẹo "generic static cache" mà BCL dùng
(`EqualityComparer<T>.Default`).

---

## RT-6. Method được gọi bằng cách nào? (static / virtual / interface / delegate)

| Kiểu gọi | IL | Bên dưới | Chi phí |
|---|---|---|---|
| static / non-virtual | `call` | nhảy thẳng địa chỉ | rẻ nhất, **inline được** |
| virtual | `callvirt` | MethodTable → slot → nhảy | thêm 1 lần dereference, khó inline |
| interface | `callvirt` trên interface | **Virtual Stub Dispatch**: stub tra interface map rồi cache | đắt nhất trong 3 |
| struct qua generic constraint | `constrained. call` | resolve tĩnh, **không box** | như static |
| delegate | `callvirt Invoke` | object chứa `_target` + `_methodPtr` | ~ virtual |

```csharp
// callvirt KHÔNG có nghĩa là gọi virtual:
string s = "x";
s.Trim();     // callvirt dù Trim không virtual — chỉ để ép null check
```

**⚙️ Devirtualization**: JIT biến virtual → direct khi biết chắc type (class `sealed`), hoặc
dynamic PGO thấy 99% call site là một type → **guarded devirtualization**:
`if (type == Foo) { inline } else { virtual call }`.

**⚖️ Thực chiến**: đánh `sealed` cho class không định kế thừa là tối ưu **miễn phí**. Trong vòng
lặp nóng, ưu tiên generic constraint hơn tham số kiểu interface.

---

## RT-7. JIT & Tiered Compilation — tại sao lần chạy đầu chậm?

**❓ Vấn đề gốc**: JIT tối ưu kỹ thì khởi động chậm; JIT nhanh thì code chạy chậm. Không thể vừa
nhanh startup vừa nhanh steady-state với **một** mức tối ưu duy nhất.

**⚙️ Cơ chế (.NET 8)**:

```
method được gọi lần đầu
   ▼
Tier-0  (Quick JIT: tối ưu tối thiểu, JIT rất nhanh, có bộ đếm call + thu PGO)
   │  gọi đủ ~30 lần  +  ~100ms lặng (TC_CallCountingDelayMs)
   ▼
Tier-1  (full optimization, dùng dữ liệu Dynamic PGO thu ở Tier-0)
```

- **OSR (On-Stack Replacement)**: method có vòng lặp dài đang chạy ở Tier-0 được thay code
  **ngay giữa vòng lặp**, không phải chờ lần gọi kế tiếp.
- **Dynamic PGO** (bật mặc định từ .NET 8): Tier-0 đếm "type nào hay đi qua call site này",
  Tier-1 dùng để guarded-devirtualize + inline.

```bash
DOTNET_TieredCompilation=0     # tắt tiering → JIT full ngay (đo steady-state)
DOTNET_TieredPGO=0
DOTNET_ReadyToRun=0
DOTNET_JitDisasm="MyMethod"    # in assembly do JIT sinh (release cũng được từ .NET 7)
```

**⚖️ Hệ quả**: benchmark **phải warm-up**, nếu không bạn đang đo Tier-0. Đây cũng là lý do
request đầu tiên của API luôn chậm (JIT + DI compile + EF model build — xem FW-20).

---

## RT-8. JIT tối ưu những gì? (danh sách nên thuộc)

| Tối ưu | Ý nghĩa | Bạn giúp JIT bằng cách |
|---|---|---|
| **Inlining** | nhét body callee vào caller | method nhỏ, tránh `try/catch` lớn, tránh virtual |
| **Bounds-check elimination** | bỏ kiểm tra `i < arr.Length` | viết `for (int i = 0; i < arr.Length; i++)` |
| **Devirtualization** | virtual → direct | `sealed`, generic constraint |
| **Struct promotion** | struct nằm hẳn trong register | struct nhỏ, `readonly` |
| **Constant folding** | `if (typeof(T) == typeof(int))` biến mất trong generic value type | generic specialization |
| **Loop hoisting / unrolling** | đưa biểu thức bất biến ra ngoài vòng lặp | tránh side-effect ẩn trong vòng lặp |
| **Escape analysis → stack allocation** | object không "thoát" khỏi method có thể nằm trên stack (mở rộng dần từ .NET 9) | giữ object cục bộ |

```csharp
// Kinh điển: pattern này khó bỏ bounds-check
for (int i = 0; i <= arr.Length - 1; i++) { }   // JIT khó chứng minh
for (int i = 0; i <  arr.Length;     i++) { }   // ✅ bỏ được bounds check
foreach (var x in arr) { }                      // ✅ tốt nhất
```

---

## RT-9. JIT vs ReadyToRun vs Native AOT — chọn cái nào?

| | JIT | ReadyToRun (R2R) | Native AOT |
|---|---|---|---|
| Compile khi nào | runtime | build (pre-JIT) + JIT bù lúc chạy | build, native hoàn toàn |
| Startup | chậm nhất | nhanh | nhanh nhất (~ms) |
| Peak perf | **cao nhất** (PGO theo máy thật) | cao (rejit lên Tier-1) | hơi thấp hơn (không PGO động) |
| RAM | cao | trung bình | thấp nhất |
| Reflection / `Emit` / `MakeGenericType` | đầy đủ | đầy đủ | **hạn chế nặng** |
| Hợp với | web API chạy 24/7 | container restart thường xuyên | CLI, serverless, sidecar |

```xml
<PublishReadyToRun>true</PublishReadyToRun>   <!-- an toàn, hầu như luôn nên bật khi publish -->
<PublishAot>true</PublishAot>                 <!-- phải trim-safe: dùng source generator -->
<InvariantGlobalization>true</InvariantGlobalization>
```

**⚖️ Câu chốt**: *"Native AOT không đơn giản là 'nhanh hơn' — nó đổi peak throughput lấy startup
và RAM, và buộc bỏ dynamic code. Với API chạy dài, JIT + R2R thường thắng."*

---

## RT-10. Generic ở runtime: chia sẻ code hay nhân bản code?

**❓ Vấn đề gốc**: Java xoá kiểu (erasure) → phải box primitive. C++ template sinh code cho mọi
instantiation → code bloat, không generic xuyên binary. CLR chọn đường giữa.

**⚙️ Cơ chế**:
- **Value type instantiation** (`List<int>`, `List<double>`): JIT sinh **code riêng** cho từng T
  → không box, layout tối ưu, `if (typeof(T) == typeof(int))` bị fold thành hằng số.
- **Reference type instantiation** (`List<string>`, `List<Order>`): tất cả **chia sẻ một bản code**
  cho type ẩn `__Canon` (mọi reference đều là con trỏ 8 byte như nhau).
- Nhưng **MethodTable và static field vẫn riêng cho từng closed type** (RT-5).

```csharp
// Hệ quả 1: static per closed type
static class Counter<T> { public static int Value; }
Counter<int>.Value = 1; Counter<string>.Value = 2;   // độc lập nhau

// Hệ quả 2: generic value type = zero-cost abstraction
static T Max<T>(T a, T b) where T : IComparable<T>
    => a.CompareTo(b) >= 0 ? a : b;   // T = int: constrained call, KHÔNG box
```

**⚖️ Hệ quả**: `List<int>` nhanh hơn `ArrayList` không phải vì "generic đẹp hơn" mà vì
**không boxing + code chuyên biệt**. Đổi lại: nhiều instantiation value type ⇒ tăng code size và
thời gian JIT (vấn đề thật với Native AOT).

---

## RT-11. Type system của CLR (CTS) — vì sao `int` cũng là object?

```
System.Object                 ← gốc của MỌI type
 ├─ System.ValueType          ← gốc của value type
 │   ├─ System.Enum → các enum
 │   └─ int, double, DateTime, Guid, struct của bạn…
 ├─ System.String, System.Array, delegate, class của bạn…
 └─ interface (bản thân không kế thừa Object, nhưng mọi instance đều là Object)
```

- **Unified type system**: `42.ToString()` hợp lệ vì `int : ValueType : Object`. Nhưng gọi member
  của `Object` trên value type ⇒ **cần box**, trừ khi struct override (RT-13).
- `Nullable<T>` là **struct được runtime hiểu đặc biệt**: box một `int?` đang null cho ra `null`
  chứ không phải một box rỗng.

```csharp
int? x = null;
object o = x;                  // o == null (!)
Console.WriteLine(o is null);  // True
```

---

## RT-12. Boxing — chính xác chuyện gì xảy ra?

**⚙️ Cơ chế**: IL `box` làm 3 việc: (1) cấp phát object trên heap, (2) ghi MethodTable của `int`,
(3) **copy** giá trị vào. `unbox.any` kiểm tra type rồi copy ngược ra.

```
int i = 42;        stack: [42]
object o = i;      heap:  [hdr][MT: Int32][42]     ← 24 byte, 1 lần alloc
int j = (int)o;    copy ngược ra stack — box KHÔNG sửa được tại chỗ
```

**Những chỗ boxing **ngầm** hay bị bỏ sót:**

```csharp
// 1) Gán struct vào biến kiểu interface
IComparable c = 42;                       // BOX

// 2) struct không override Equals/GetHashCode → ValueType.Equals reflection + box
myStruct.Equals(other);                   // implement IEquatable<T> để tránh

// 3) params object[] / string.Format kiểu cũ
string.Format("id={0}", 42);              // BOX (interpolation mới thì không)

// 4) Lấy enumerator của collection qua kiểu interface (enumerator struct bị box)
IEnumerable<int> e = list;                // foreach trên e → box enumerator

// 5) Đưa struct vào collection non-generic (ArrayList, Hashtable)

// 6) Enum → object khi log / khi comparer không phù hợp (đúng với .NET Framework)
```

**✅ Cách tránh**: generic + constraint, `IEquatable<T>`, `where T : struct`, interpolated string
handler, `EqualityComparer<T>.Default`, `Span<T>`.

**⚖️ Đo được**: `GC.GetAllocatedBytesForCurrentThread()` trước/sau vòng lặp — xem `dotnet run -- 11`.

---

## RT-13. `constrained.` prefix — vì sao generic không box mà interface thì box?

```csharp
struct Temp : IComparable<Temp> { … }

static int ViaInterface(IComparable<Temp> a, Temp b) => a.CompareTo(b);        // ❌ box khi truyền
static int ViaGeneric<T>(T a, T b) where T : IComparable<T> => a.CompareTo(b); // ✅ constrained. call
```

**⚙️ Cơ chế**: với `T` là struct, JIT sinh `constrained. T callvirt IComparable<T>::CompareTo`.
Runtime resolve **tĩnh** ra implementation của struct rồi gọi trực tiếp trên **địa chỉ** struct
(`ref this`) → không tạo object. Nếu struct **không** implement method đó (ví dụ chỉ có default
interface method) thì runtime **buộc phải box** để có `this` là object.

**⚖️ Câu chốt**: *"Interface trên struct rẻ hay đắt phụ thuộc bạn đi qua **generic constraint**
(rẻ) hay qua **biến kiểu interface** (box)."*

---

## RT-14. `readonly struct`, `in` param và **defensive copy**

**❓ Vấn đề gốc**: compiler phải bảo đảm bạn không sửa được một struct chỉ-đọc. Nếu struct không
`readonly`, cách duy nhất là **copy nó ra trước khi gọi method** → copy ẩn trong vòng lặp nóng.

```csharp
struct Mutable { public int V; public int Get() => V; }              // Get() không readonly
readonly struct Immutable { public readonly int V; public int Get() => V; }

class Holder
{
    readonly Mutable   _m;   // ⚠️ mỗi lần _m.Get() → COPY toàn bộ struct rồi mới gọi
    readonly Immutable _i;   // ✅ không copy
}

void Loop(in Mutable m)      // in = readonly ref
{
    for (int i = 0; i < 1_000_000; i++) m.Get();   // ⚠️ 1 triệu defensive copy
}
```

**✅ Quy tắc**: struct nên **luôn** khai báo `readonly struct` (hoặc `readonly` từng member).
`in` chỉ thực sự có lợi khi struct **lớn** *và* **readonly**.

---

## RT-15. `ref` thực sự là gì? (managed pointer / byref)

**⚙️ Cơ chế**: `ref` = **managed pointer** — con trỏ mà **GC biết** và sẽ cập nhật khi object bị
di chuyển lúc compact. Khác `T*` (unmanaged pointer, phải `fixed` để ghim).

```csharp
ref int slot = ref array[5];   // trỏ vào giữa array — GC vẫn move array được, ref tự cập nhật
slot = 42;                     // ghi thẳng, không copy

ref int Find(int[] a, int i) => ref a[i];      // ref return
ref readonly int Peek(int[] a) => ref a[0];    // ref readonly return
```

**Luật an toàn (byref safety)**: managed pointer **không được sống lâu hơn** vùng nhớ nó trỏ tới
→ không được làm field của class, không capture vào lambda, không box. Đó chính là định nghĩa
của **`ref struct`** — và vì `Span<T>` chứa một byref nên nó bắt buộc là `ref struct`
(⇒ không dùng trong `async`/`yield`, không làm field của class).

---

## RT-16. Delegate bên dưới là object gì?

```
Delegate object
 ├─ _target         : object nhận (null nếu static)
 ├─ _methodPtr      : con trỏ hàm
 └─ _invocationList : object[] khi multicast
```

```csharp
Action a = Console.WriteLine;   // _target = null (static)
var user = new User();
Action b = user.Print;          // _target = user ⇒ delegate GIỮ user sống (nguồn leak!)

Action multi = a + b;           // _invocationList = [a, b] — gọi tuần tự, chỉ trả về giá trị cuối
Console.WriteLine(multi.GetInvocationList().Length); // 2
```

- **Lambda không capture** → compiler cache vào **static field**, chỉ alloc một lần.
- **Lambda có capture** → sinh một **closure class**; mỗi lần tạo = 1 allocation (+ 1 delegate).

**⚖️ Leak kinh điển**: `publisher.Event += subscriber.Handler` khiến publisher giữ subscriber sống
mãi (xem MEM-13).

---

## RT-17. Reflection đắt ở đâu? Vì sao có Source Generator?

**⚙️ Cơ chế**: `GetMethod`/`GetProperty` phải vào `EEClass` + metadata, so khớp tên (string),
dựng `RuntimeMethodInfo`; `Invoke` phải box arguments vào `object[]`, kiểm tra type, dựng frame.

| Cách gọi | Chi phí tương đối |
|---|---|
| Gọi trực tiếp | 1× |
| Cached delegate (`Delegate.CreateDelegate`) | ~1–2× |
| `Expression.Compile()` (nhớ cache!) | ~1–3× |
| `MethodInfo.Invoke` | ~100–1000× + allocation |
| `Activator.CreateInstance(Type)` | ~10–50× |

```csharp
// ❌ reflection trong hot path
var v = typeof(User).GetProperty("Name")!.GetValue(user);

// ✅ compile một lần, cache theo type
static readonly Func<User, string> GetName =
    (Func<User, string>)Delegate.CreateDelegate(
        typeof(Func<User, string>), typeof(User).GetProperty("Name")!.GetMethod!);
```

**⚖️ Vì sao Source Generator ra đời**: dời việc "đọc metadata + sinh code" từ **runtime** sang
**compile time** → zero reflection, trim/AOT-safe, không JIT warmup. Đó là lý do
`System.Text.Json` source-gen, `LoggerMessage`, `RegexGenerator`, `RequestDelegateGenerator` của
Minimal API đều đi hướng này (xem FW-8, FW-11, FW-19).

---

## RT-18. Static constructor & `beforefieldinit`

```csharp
class A { public static int X = Compute(); }                   // beforefieldinit — CLR chạy "lúc nào đó trước" khi X được đọc
class B { static B() { X = Compute(); } public static int X; } // static ctor tường minh → chạy CHÍNH XÁC trước lần dùng đầu tiên
```

- CLR bảo đảm static ctor chạy **đúng một lần, thread-safe** (lock nội bộ) → nền tảng cho
  singleton kiểu `static readonly Lazy<T>`.
- Có static ctor tường minh ⇒ mất `beforefieldinit` ⇒ JIT phải chèn **class-init check** ở nhiều
  call site ⇒ chậm hơn chút trong hot path.
- **Deadlock được**: hai static ctor chờ nhau chéo (A dùng B, B dùng A) → treo, không exception.
- Exception trong static ctor ⇒ `TypeInitializationException`, và type **hỏng vĩnh viễn** trong
  process đó (mọi lần dùng sau đều ném lại).

---

## RT-19. Exception bên dưới đắt cỡ nào? Vì sao "đừng dùng cho control flow"?

**⚙️ Cơ chế two-pass**:
1. **Pass 1 (search)**: đi ngược stack, hỏi từng frame "có catch khớp không?" — cần **unwind info**
   + **funclet**; đồng thời thu thập stack trace (đọc metadata → tên method; PDB → file/line).
2. **Pass 2 (unwind)**: chạy `finally` từng frame rồi nhảy vào catch.

Chi phí thực tế: ném + bắt một exception ≈ **hàng chục micro giây** (10.000×–100.000× so với một
lệnh `return`), và `try` **cản inlining** của method chứa nó.

```csharp
// ❌ dùng exception cho luồng bình thường (validate input người dùng)
try { value = int.Parse(s); } catch { value = 0; }

// ✅
if (!int.TryParse(s, out value)) value = 0;
```

**⚖️ Ngưỡng thực chiến**: exception cho **trường hợp bất thường** (mất kết nối, dữ liệu hỏng);
Result pattern cho **luồng nghiệp vụ dự đoán được** (xem DP-18). Trong project HW,
`DomainException` → middleware map ra status code là đúng chỗ: lỗi *ngoại lệ*, không phải hot path.

---

## RT-20. `string` bên dưới & interning

```
"hello"  →  [hdr][MT: String][_stringLength = 5]['h','e','l','l','o','\0']
```
- Dữ liệu char **nằm inline** trong object (không phải con trỏ tới mảng khác) ⇒ đọc rất rẻ, nhưng
  mọi thao tác "sửa" đều tạo object mới.
- **Intern pool**: literal trong IL (`ldstr`) được intern tự động → hai literal giống nhau là
  **cùng object**. Chuỗi tính lúc runtime **không** tự intern.

```csharp
string a = "hello", b = "hello";
Console.WriteLine(ReferenceEquals(a, b));                 // True (intern pool)
string c = new string(new[] { 'h','e','l','l','o' });
Console.WriteLine(ReferenceEquals(a, c));                 // False
Console.WriteLine(ReferenceEquals(a, string.Intern(c)));  // True

// ⚠️ intern pool sống suốt đời process → KHÔNG intern dữ liệu do user nhập (leak)
```

**✅ Không alloc**: `ReadOnlySpan<char>` để cắt chuỗi (`s.AsSpan(3, 5)`) thay `Substring`;
`string.Create(len, state, (span, st) => …)` để build chuỗi đúng một lần.

---

## RT-21. Equality bên dưới: vì sao struct phải implement `IEquatable<T>`?

**⚙️ Cơ chế `ValueType.Equals`**: nếu struct **chỉ có field blittable** → runtime so sánh bit
(nhanh). Nếu có **bất kỳ field reference nào** → fallback **reflection**, duyệt từng field → chậm
gấp hàng chục lần **và box cả hai vế**.

```csharp
// ❌ chậm + box
struct Bad { public string Name; public int Age; }

// ✅ nhanh, không box
readonly struct Good : IEquatable<Good>
{
    public string Name { get; init; }
    public int Age { get; init; }
    public bool Equals(Good o) => Age == o.Age && Name == o.Name;
    public override bool Equals(object? o) => o is Good g && Equals(g);
    public override int GetHashCode() => HashCode.Combine(Name, Age);
}
// hoặc đơn giản: record struct — compiler sinh sẵn tất cả
```

**⚖️ Liên hệ**: `Dictionary<TKey,…>` dùng `EqualityComparer<T>.Default`, comparer này **ưu tiên**
`IEquatable<T>` → key là struct mà quên `IEquatable<T>` = box mỗi lần lookup.

---

## RT-22. Interop, pinning & `SafeHandle` (nhanh)

- Gọi P/Invoke ⇒ **GC transition**: thread chuyển sang "preemptive mode" để GC không phải chờ
  native code. `[SuppressGCTransition]` bỏ bước này cho call cực ngắn (rủi ro: chặn GC).
- **Marshalling**: string/struct phải đổi layout. `[LibraryImport]` (source generator, .NET 7+)
  sinh code marshal lúc compile → AOT-safe, thay cho `[DllImport]`.
- **Pinning** (`fixed`, `GCHandle.Alloc(…, Pinned)`): ghim object để GC không di chuyển → gây
  **fragmentation** (MEM-14). .NET 5+ có POH riêng cho buffer ghim lâu dài.
- **`SafeHandle`** hơn hẳn finalizer thủ công: critical finalization, chống handle-recycling,
  ref-count để không đóng handle đang dùng.

---

## ✅ Checklist tự kiểm tra — Phần 11

- [ ] Vẽ được đường đi `.cs → IL + metadata → MethodTable → Tier0 → Tier1 → machine code`.
- [ ] Nói được object header + MethodTable pointer, và vì sao object nhỏ nhất là 24 byte.
- [ ] Phân biệt virtual dispatch / interface dispatch / `constrained. call`.
- [ ] Nêu 4 chỗ boxing ngầm và cách đo bằng `GC.GetAllocatedBytesForCurrentThread()`.
- [ ] Giải thích generic sharing (`__Canon`) vs specialization, và hệ quả với static field.
- [ ] Giải thích defensive copy của `readonly struct` / `in`.
- [ ] Nói được vì sao Source Generator đang thay thế reflection trong .NET hiện đại.

➡️ Tiếp: [Phần 12 — Memory & GC Internals](interview.NET.12-Memory-GC-Internals.md)
