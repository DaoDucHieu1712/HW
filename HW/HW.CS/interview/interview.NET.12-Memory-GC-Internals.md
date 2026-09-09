# Phần 12 — Memory & Garbage Collector Internals

[⬅️ Phần 11 — Runtime Internals](interview.NET.11-Runtime-Internals.md) | [Về mục lục](interview.NET.md) | Tiếp theo: [Phần 13 — Async & Threading Internals ➡️](interview.NET.13-Async-Threading-Internals.md)

> Khung trả lời: **❓ Vấn đề gốc → ⚙️ Cơ chế bên dưới → 💻 Code/đo đạc → ⚖️ Hệ quả thực chiến**.
> Demo chạy được: `dotnet run -- 12 all` (xem `Demos/Demo12_MemoryGc.cs`).

---

## 🗺️ Bản đồ bộ nhớ của một process .NET

```
┌──────────────────────── PROCESS ────────────────────────┐
│                                                          │
│  Mỗi thread:  ┌──────────────┐                           │
│               │ STACK (1 MB) │ frame, local, byref       │
│               └──────────────┘  ← LIFO, tự dọn, không GC │
│                                                          │
│  MANAGED HEAP (do GC quản lý)                            │
│   ┌──────────────── SOH (Small Object Heap) ───────────┐ │
│   │  Gen 0  │  Gen 1  │        Gen 2                   │ │
│   └──────────────────────────────────────────────────-─┘ │
│   ┌──────────── LOH (≥ 85.000 byte, coi như Gen 2) ────┐ │
│   ┌──────────── POH (Pinned Object Heap, .NET 5+) ─────┐ │
│                                                          │
│  NATIVE / UNMANAGED                                      │
│   loader heap (MethodTable), code heap (JIT), native lib │
│   buffer OS, socket, file handle  ← GC KHÔNG biết        │
└──────────────────────────────────────────────────────────┘
```

**Câu chốt:** *"'Value type ở stack, reference type ở heap' là mô hình dạy học, không phải luật.
Cái thật sự quyết định là: **ai sở hữu vòng đời** (stack frame tự huỷ / GC dọn / bạn tự Dispose)
và **copy semantics**."*

---

## MEM-1. Stack và heap khác nhau ở đâu **về cơ chế**?

| | Stack | Managed heap |
|---|---|---|
| Cấp phát | dịch stack pointer (1 lệnh CPU) | bump pointer trong allocation context |
| Giải phóng | tự động khi ra khỏi frame | GC quyết định, phi định thời |
| Kích thước | 1 MB/thread (Windows, mặc định) | tới hạn RAM / GC heap hard limit |
| Cache locality | rất tốt (nóng trong L1) | phụ thuộc layout |
| Lỗi khi hết | `StackOverflowException` — **không catch được**, process chết | `OutOfMemoryException` — catch được |

```csharp
// StackOverflow không thể catch — CLR kill process ngay (không unwind an toàn được)
static void Recurse() => Recurse();
```

**⚖️ Hệ quả**: đệ quy sâu trên dữ liệu người dùng nhập = lỗ hổng DoS. Đổi sang vòng lặp + `Stack<T>`
tường minh (xem DSA-14).

---

## MEM-2. Tại sao `new` trong .NET nhanh hơn `malloc`?

**⚙️ Cơ chế — bump pointer + allocation context**:

```
Gen 0 segment:  [ đã dùng ][ ▼alloc_ptr ......... free ......... ][ limit ]

Cấp phát 1 object:
   1. ptr = alloc_ptr
   2. alloc_ptr += size          ← chỉ vài lệnh CPU, KHÔNG lock
   3. nếu vượt limit → xin allocation context mới / kích hoạt GC Gen 0
```

- Mỗi thread có **allocation context** riêng (một lát Gen 0) → cấp phát **không cần lock**.
- Bộ nhớ Gen 0 vừa được dọn nên **đang nằm trong cache CPU** → ghi rất nhanh.
- `malloc` phải duyệt free list, xử lý fragmentation, và thường phải lock.

**⚖️ Hệ quả**: *"Allocation rẻ, **collection** mới đắt."* Tối ưu không phải là "đừng bao giờ
allocate", mà là **đừng để object sống sót qua Gen 0** (đừng promote rác lên Gen 1/2).

---

## MEM-3. Generational hypothesis — vì sao có Gen 0/1/2?

**❓ Vấn đề gốc**: quét toàn bộ heap mỗi lần thu gom thì quá đắt.

**⚙️ Giả thuyết thế hệ** (đúng với phần lớn app): *hầu hết object chết rất trẻ; object đã sống lâu
thì có xu hướng sống tiếp*. Vậy: chỉ quét vùng "trẻ".

| Gen | Nội dung | Budget điển hình | Tần suất | Chi phí |
|---|---|---|---|---|
| 0 | object mới sinh | vài trăm KB – vài MB (theo cache size) | rất thường xuyên | < 1 ms |
| 1 | sống sót 1 lần GC — "vùng đệm" | vài MB | thỉnh thoảng | vài ms |
| 2 | sống lâu + LOH + POH | tới hạn heap | hiếm | 10 ms → hàng giây |

- Gen 0 đầy → **GC Gen 0**: object sống sót được **promote** lên Gen 1.
- Gen 1 đầy → **GC Gen 1** (thu cả Gen 0), sống sót → Gen 2.
- Gen 2 đầy → **full GC** (thu cả 0, 1, 2, LOH) — đây là cái gây pause dài.

**⚖️ Điều quan trọng nhất**: *một GC Gen N luôn thu luôn mọi gen nhỏ hơn N*. Vì thế "mid-life
crisis" (object sống vừa đủ lâu để bị promote rồi mới chết) là kiểu rác **tệ nhất** — ví dụ
cache có TTL ngắn, buffer per-request giữ hơi lâu.

---

## MEM-4. GC chạy như thế nào? (các pha)

```
1. TRIGGER      : Gen0 budget đầy / GC.Collect() / low memory từ OS
2. SUSPEND EE   : mọi thread managed bị đưa tới "safe point" rồi dừng  ← stop-the-world
3. MARK         : từ ROOTS đi theo đồ thị reference, đánh dấu object sống
4. PLAN         : quyết định compact hay chỉ sweep
5. RELOCATE     : (nếu compact) dồn object lại, CẬP NHẬT mọi reference trỏ tới chúng
6. SWEEP        : vùng còn lại thành free list / trả về OS
7. RESUME EE
```

**GC roots gồm:**
- Biến local & tham số đang sống trên **stack** của mọi thread (JIT ghi "GC info" cho từng method
  biết offset nào là reference tại từng safe point) + **thanh ghi CPU**.
- **Static field** (per closed generic type).
- **GC handle** (`GCHandle`, pinned handle, `WeakReference` là handle "yếu" — không phải root).
- **Finalization queue / f-reachable queue** (MEM-9).
- Object được native code giữ qua interop.

**⚖️ Câu chốt**: *"GC không đi tìm rác — nó đi tìm object **sống**, phần còn lại **mặc nhiên** là
rác. Vì vậy chi phí GC tỉ lệ với số object **sống sót**, không phải số object đã tạo."*
→ Tạo nhiều object chết trẻ **gần như miễn phí**; giữ nhiều object sống mới đắt.

---

## MEM-5. Vì sao GC phải "stop the world"? Safe point là gì?

**❓ Vấn đề gốc**: nếu thread đang chạy trong khi GC di chuyển object, một biến trong thanh ghi có
thể trỏ vào địa chỉ cũ → hỏng bộ nhớ.

**⚙️ Cơ chế**: CLR không dừng thread ở chỗ tuỳ ý. Nó chờ thread tới **safe point** — nơi JIT đã
ghi sẵn bản đồ "slot nào đang giữ reference":
- Ở mỗi lần gọi method (call site) — luôn có GC info.
- Trong vòng lặp không có call, JIT chèn **GC poll** để thread không "kẹt" quá lâu.
- Method **fully-interruptible** cho phép dừng ở nhiều điểm hơn (JIT chọn tuỳ tình huống).
- Thread đang trong native code (P/Invoke) coi như đã "an toàn" (preemptive mode) — GC không cần chờ.

**⚖️ Hệ quả thực chiến**: một vòng lặp tính toán rất dài, không gọi hàm, có thể **làm trễ toàn bộ
GC** (mọi thread khác đứng chờ). Đó là một nguyên nhân thật của "latency spike" khó hiểu.

---

## MEM-6. Card table & write barrier — GC Gen 0 mà không quét cả heap bằng cách nào?

**❓ Vấn đề gốc**: khi thu Gen 0, một object Gen 2 có thể đang trỏ tới object Gen 0. Nếu không
biết, GC sẽ tưởng object Gen 0 đó là rác. Nhưng quét cả Gen 2 để tìm thì mất hết lợi ích.

**⚙️ Cơ chế**: mỗi lần bạn **gán một field kiểu reference**, JIT chèn **write barrier** — một
đoạn code nhỏ đánh dấu "vùng ~256 byte này (một *card*) vừa bị ghi" vào **card table**.
Khi thu Gen 0, GC chỉ quét những card bị đánh dấu ⇒ coi chúng như root bổ sung.

```csharp
class Node { public Node? Next; public int Value; }

node.Next  = other;   // ⚠️ có write barrier (reference)
node.Value = 42;      // ✅ không có write barrier (value type)
```

**⚖️ Hệ quả đo được**: ghi **reference field** đắt hơn ghi **value field** một chút. Trong cấu
trúc dữ liệu nóng, mảng `struct` thắng mảng `class` cả ở cache locality lẫn ở write barrier.

---

## MEM-7. Workstation vs Server GC, Background GC — chọn và cấu hình

| | Workstation | Server |
|---|---|---|
| Số heap | 1 | 1 heap **mỗi core** (mặc định) |
| Thread GC | dùng thread đang chạy | thread GC riêng, chạy song song |
| Mục tiêu | độ trễ thấp, ít CPU | **throughput** tối đa |
| Mặc định | app console/desktop | **ASP.NET Core** |

**Background GC (bật mặc định)**: phần lớn công việc thu Gen 2 chạy **song song** với app; chỉ
dừng app ở vài pha ngắn ⇒ giảm pause dài.

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
  <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
</PropertyGroup>
```
```bash
DOTNET_gcServer=1
DOTNET_GCHeapCount=4          # giới hạn số heap (quan trọng trong container!)
DOTNET_GCHeapHardLimit=0x…    # hoặc GCHeapHardLimitPercent
```

**⚖️ Bẫy container kinh điển**: pod giới hạn 2 CPU nhưng node có 64 core → Server GC (bản cũ) tạo
quá nhiều heap/thread → RAM và context switch tăng vọt. .NET hiện đại đã đọc cgroup limit, nhưng
vẫn nên set `GCHeapCount`/`HeapHardLimitPercent` tường minh và **`DOTNET_GCConserveMemory`** khi RAM chật.

---

## MEM-8. LOH và POH — vì sao tách riêng?

**LOH (Large Object Heap)** — object **≥ 85.000 byte** (thường là array):
- Copy object lớn rất đắt ⇒ LOH mặc định **không nén** (chỉ sweep) ⇒ **fragmentation**.
- Được thu cùng **Gen 2** ⇒ mỗi lần cấp phát LOH nhiều là kéo theo full GC.
- Cấp phát 1 mảng `double[10_000]` = 80.000 byte + header → vẫn SOH; `double[11_000]` → LOH.

**POH (Pinned Object Heap, .NET 5+)** — vùng riêng cho object **ghim lâu dài** (buffer I/O):
gom mọi thứ pinned vào một chỗ ⇒ SOH không bị "đục lỗ" bởi object bị ghim.

```csharp
// ✅ tái sử dụng buffer lớn thay vì cấp phát LOH liên tục
byte[] buf = ArrayPool<byte>.Shared.Rent(100_000);
try { /* dùng buf, chú ý buf.Length CÓ THỂ lớn hơn yêu cầu */ }
finally { ArrayPool<byte>.Shared.Return(buf); }

// ✅ buffer ghim cho interop/socket → cấp trên POH
byte[] pinned = GC.AllocateArray<byte>(4096, pinned: true);

// Nén LOH (hiếm khi cần, rất đắt)
GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
GC.Collect();
```

**⚖️ Triệu chứng thực tế**: RAM tăng dần, `% Time in GC` cao, Gen 2 collection nhiều — thường do
đọc file/HTTP body vào `byte[]` lớn mỗi request. Cách chữa: `ArrayPool`, `RecyclableMemoryStream`,
đọc theo stream/`PipeReader` thay vì `ReadAllBytes`.

---

## MEM-9. Finalizer — tại sao nó làm object sống lâu hơn?

**⚙️ Cơ chế 2 vòng GC**:

```
GC #1: object có finalizer & không còn root
        → KHÔNG xoá. Chuyển từ finalization queue sang F-REACHABLE QUEUE
        → f-reachable queue là ROOT ⇒ object SỐNG TIẾP + bị promote lên gen cao hơn
        → finalizer thread lần lượt chạy Finalize()
GC #2: lúc này object mới thật sự bị thu
```

**⚖️ Hệ quả**:
- Object có finalizer **luôn** tốn ít nhất 2 lần GC và **luôn** bị promote → đẩy rác lên Gen 2.
- Chỉ **một** finalizer thread: một finalizer chậm/treo làm **kẹt hàng đợi** → leak toàn app.
- Thứ tự finalizer **không xác định** ⇒ trong `Finalize()` không được đụng vào object managed khác
  (chúng có thể đã bị finalize).

```csharp
public sealed class Handle : IDisposable
{
    IntPtr _native;
    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);   // ✅ đã dọn tay → gỡ khỏi finalization queue, tránh 2 vòng GC
    }
    ~Handle() => Release();          // chỉ là "lưới an toàn" khi ai đó quên Dispose
    void Release() { /* đóng _native */ }
}
```

**✅ Tốt hơn**: dùng `SafeHandle` — runtime lo critical finalization, chống handle recycling, và
bạn **không cần viết finalizer**.

---

## MEM-10. `IDisposable` liên quan gì tới GC? (câu bẫy)

**Câu trả lời đúng: gần như không liên quan.** GC quản lý **bộ nhớ managed**. `IDisposable` quản
lý **tài nguyên ngoài tầm GC**: file handle, socket, connection DB, lock, unmanaged memory,
subscription sự kiện.

- GC **không biết** một object 50 byte đang giữ một connection DB đắt đỏ ⇒ nó không thấy áp lực
  phải thu ⇒ bạn cạn connection pool trước khi GC chạy.
- `using` = `try/finally` gọi `Dispose()` **tất định** (deterministic).
- `await using` cho `IAsyncDisposable` — cần khi việc dọn dẹp là I/O (flush stream, đóng
  connection) và không được block thread.

```csharp
await using var conn = new SqlConnection(cs);   // đóng ngay khi ra khỏi scope
```

**⚖️ Liên hệ DI**: container **sở hữu** mọi `IDisposable` mà nó tạo — kể cả **transient**! Resolve
transient `IDisposable` từ **root provider** (singleton scope) = leak tới khi app tắt (xem FW-2).

---

## MEM-11. `WeakReference` & `ConditionalWeakTable` — cache mà không leak

```csharp
var weak = new WeakReference<byte[]>(new byte[1000]);
GC.Collect();
Console.WriteLine(weak.TryGetTarget(out _));   // False — GC đã thu

// Gắn dữ liệu phụ vào object mà KHÔNG giữ nó sống (runtime dùng cho closure, dynamic…)
var table = new ConditionalWeakTable<User, Metadata>();
table.Add(user, meta);   // user chết ⇒ meta cũng được thu
```

- `WeakReference` **không** phải root ⇒ object vẫn bị thu bình thường.
- `WeakReference(target, trackResurrection: true)` — "long weak reference", theo dõi qua cả
  finalization.
- `ConditionalWeakTable<TKey,TValue>`: key giữ **yếu**, và value không giữ key sống (dù value trỏ
  ngược tới key).

**⚖️ Dùng khi**: cache "có thì tốt" (bitmap, kết quả parse). **Không dùng** cho cache nghiệp vụ —
dùng `IMemoryCache` với size limit + eviction policy để kiểm soát được.

---

## MEM-12. Memory leak trong .NET — 8 nguồn thường gặp & cách bắt

| Nguồn | Vì sao rò | Cách chữa |
|---|---|---|
| `static` collection/dictionary | root vĩnh viễn | TTL, size limit, `IMemoryCache` |
| Event handler không unsubscribe | publisher giữ subscriber (RT-16) | `-=` trong `Dispose`, weak event |
| Closure capture "hơi nhiều" | lambda giữ cả `this` | capture biến cục bộ, không capture `this` |
| `HttpClient` new mỗi request | cạn socket (TIME_WAIT), DNS cũ | `IHttpClientFactory` (FW-10) |
| Timer / `BackgroundService` không huỷ | callback giữ state sống | `Dispose` timer, dùng `CancellationToken` |
| Captive dependency (singleton giữ scoped) | scoped sống mãi + DbContext phình | `IServiceScopeFactory` (FW-2) |
| Task không bao giờ complete | state machine + closure treo | timeout + cancellation |
| `string.Intern` dữ liệu người dùng | intern pool không bao giờ giải phóng | đừng intern |

**Quy trình chẩn đoán (production):**

```bash
dotnet-counters monitor -p <pid> System.Runtime          # gen sizes, alloc rate, % time in GC
dotnet-gcdump collect -p <pid>                           # snapshot heap, mở bằng VS / PerfView
dotnet-dump collect -p <pid>                             # dump đầy đủ
# trong dotnet-dump analyze:
#   dumpheap -stat            → type nào chiếm nhiều byte nhất
#   gcroot <address>          → AI đang giữ object này sống  ← câu hỏi quan trọng nhất
#   dumpasync                 → state machine async đang treo
```

**⚖️ Cách phân biệt leak thật vs "GC chưa chạy"**: chụp 2 gcdump cách nhau vài phút dưới tải, so
sánh delta theo type. Leak thật thì **số instance của một type tăng đều và không bao giờ giảm**.

---

## MEM-13. Pinning & fragmentation

```csharp
byte[] data = new byte[1024];
unsafe
{
    fixed (byte* p = data)      // ghim trong phạm vi block — GC không di chuyển được data
    {
        NativeCall(p, data.Length);
    }
}

var handle = GCHandle.Alloc(data, GCHandleType.Pinned);  // ghim lâu dài — nguy hiểm hơn
try { … } finally { handle.Free(); }
```

**⚙️ Vì sao hại**: GC compact bằng cách **dồn** object; object bị ghim là "cột bê tông" giữa
đường ⇒ để lại lỗ hổng ⇒ heap phình dù byte sống ít.

**✅ Cách hiện đại**: `GC.AllocateArray<T>(n, pinned: true)` (vào POH) hoặc `ArrayPool` + ghim
ngắn hạn. Với I/O, `Memory<T>`/`PipeReader` đã lo phần buffer cho bạn.

---

## MEM-14. `Span<T>` / `Memory<T>` giải quyết vấn đề gì?

**❓ Vấn đề gốc**: mọi thao tác "lấy một phần dữ liệu" trong .NET cũ đều **copy**:
`Substring`, `Array.Copy`, `ToArray`, `Split` — tạo rác khủng khiếp trong parser/serializer.

**⚙️ Cơ chế**: `Span<T>` = `(ref T pointer, int length)` — một **cửa sổ** trỏ vào bộ nhớ đã có,
**không copy, không allocate**, hoạt động thống nhất trên: array, `stackalloc`, native memory, string.

```csharp
ReadOnlySpan<char> s = "2024-01-15".AsSpan();
int year  = int.Parse(s[..4]);       // 0 allocation
int month = int.Parse(s.Slice(5,2)); // 0 allocation
// so với: int.Parse(str.Substring(0,4)) → 1 string mới mỗi lần
```

| | `Span<T>` | `Memory<T>` |
|---|---|---|
| Là gì | `ref struct` (chỉ sống trên stack) | struct thường |
| Dùng trong `async`/`yield` | ❌ không được | ✅ được |
| Field của class | ❌ | ✅ |
| Lấy span | — | `mem.Span` |

**Vì sao `Span<T>` không dùng được trong async?** Nó chứa một **managed pointer** (RT-15); state
machine async là object **trên heap**, mà byref không được sống trên heap → luật byref-safety cấm.

**✅ Bộ ba đi cùng nhau**: `Span<T>` (xử lý), `ArrayPool<T>` (mượn buffer), `ReadOnlySequence<T>` /
`System.IO.Pipelines` (dữ liệu đến theo nhiều mảnh — chính là nền của Kestrel, xem FW-4).

---

## MEM-15. `stackalloc` — cấp phát trên stack

```csharp
Span<byte> buffer = stackalloc byte[256];   // ✅ 0 GC allocation, tự huỷ khi ra khỏi method
// ⚠️ KHÔNG BAO GIỜ stackalloc trong vòng lặp (stack cộng dồn tới khi thoát method)
// ⚠️ Kích thước phải là hằng nhỏ (≤ ~1KB). Kích thước theo input người dùng = StackOverflow = DoS

// ✅ Pattern chuẩn: nhỏ thì stack, lớn thì pool
const int MaxStack = 256;
byte[]? rented = null;
Span<byte> buf = size <= MaxStack
    ? stackalloc byte[MaxStack]
    : (rented = ArrayPool<byte>.Shared.Rent(size));
try { … }
finally { if (rented is not null) ArrayPool<byte>.Shared.Return(rented); }
```

`[SkipLocalsInit]` bỏ việc zero-init vùng stack (nhanh hơn, nhưng bạn phải tự bảo đảm ghi trước khi đọc).

---

## MEM-16. Pooling: `ArrayPool`, `ObjectPool`, `MemoryPool`

| Pool | Dùng cho | Ghi chú |
|---|---|---|
| `ArrayPool<T>.Shared` | buffer `T[]` tạm | mảng trả về **có thể lớn hơn** yêu cầu; **không** được zero sẵn |
| `ArrayPool<T>.Create(...)` | pool riêng, kiểm soát bucket | tránh tranh chấp với shared |
| `ObjectPool<T>` (Microsoft.Extensions) | object đắt để tạo (`StringBuilder`) | cần policy reset state |
| `MemoryPool<T>` | `IMemoryOwner<T>` cho code async | trả về `Memory<T>` |
| `RecyclableMemoryStream` | thay `MemoryStream` lớn | tránh LOH hoàn toàn |

**⚠️ 3 lỗi pooling kinh điển**:
1. **Quên `Return`** → pool cạn, quay về cấp phát mới (chỉ chậm hơn, không crash).
2. **`Return` rồi vẫn dùng tiếp** → hai nơi cùng ghi một buffer → bug dữ liệu **rất khó tìm**.
3. **Không xoá dữ liệu nhạy cảm** trước khi trả (`Return(buf, clearArray: true)`) → rò dữ liệu
   sang request khác.

**⚖️ Khi nào pooling phản tác dụng**: object nhỏ, đời ngắn → Gen 0 đã gần như miễn phí, pooling
chỉ thêm code + giữ object sống lâu hơn (đẩy lên Gen 2). **Chỉ pool cái lớn hoặc cái đắt.**

---

## MEM-17. `struct` hay `class`? — quyết định theo bộ nhớ

```csharp
// Array of struct: dữ liệu liên tục, 1 lần cache miss cho nhiều phần tử
Point3D[] points = new Point3D[1_000_000];   // 1 object, 24 MB liên tục

// Array of class: mảng con trỏ + 1 triệu object rải rác → pointer chasing
Point3DClass[] objs = new Point3DClass[1_000_000]; // 8 MB con trỏ + 1 triệu × 32 byte rời rạc
```

Duyệt tuần tự mảng struct thường nhanh hơn mảng class **nhiều lần** — không phải vì "struct nhanh
hơn", mà vì **cache line 64 byte**: đọc một phần tử là kéo luôn các phần tử kế bên.

**Quy tắc chọn struct**: (1) nhỏ (≲ 16–24 byte, hoặc lớn hơn nếu luôn truyền bằng `in`/`ref`),
(2) immutable (`readonly struct`), (3) đời ngắn hoặc nằm trong mảng lớn, (4) không cần null,
(5) không cần đa hình.

---

## MEM-18. Boxing dưới góc nhìn bộ nhớ (nối RT-12)

```csharp
long before = GC.GetAllocatedBytesForCurrentThread();

object[] boxed = new object[1_000_000];
for (int i = 0; i < boxed.Length; i++) boxed[i] = i;   // 1 triệu box

Console.WriteLine((GC.GetAllocatedBytesForCurrentThread() - before) / 1024 / 1024 + " MB");
// ≈ 8 MB (mảng con trỏ) + 24 MB (1 triệu box) = ~32 MB  — thay vì 4 MB nếu là int[]
```

Mỗi box còn kéo theo: **áp lực Gen 0 → GC thường xuyên hơn**, và nếu mảng sống lâu thì **1 triệu
object bị promote lên Gen 2** ⇒ mark phase phải duyệt 1 triệu object mỗi full GC.

**⚖️ Câu chốt phỏng vấn**: *"Boxing không chỉ tốn 24 byte — nó biến một giá trị 4 byte thành một
**node trong đồ thị object** mà GC phải duyệt lại ở mọi lần mark."*

---

## MEM-19. Tuning & latency mode

```csharp
Console.WriteLine(GCSettings.IsServerGC);
Console.WriteLine(GCSettings.LatencyMode);   // Interactive / SustainedLowLatency / Batch

// Vùng không GC — cho đoạn code cực nhạy latency (trading, realtime)
if (GC.TryStartNoGCRegion(totalSize: 50 * 1024 * 1024))
{
    try { /* code nhạy cảm — KHÔNG được vượt quá budget đã xin */ }
    finally { if (GCSettings.LatencyMode == GCLatencyMode.NoGCRegion) GC.EndNoGCRegion(); }
}
```

**Đọc số liệu GC ngay trong code:**
```csharp
var info = GC.GetGCMemoryInfo();
Console.WriteLine($"heap={info.HeapSizeBytes / 1_000_000} MB, " +
                  $"gen2 count={GC.CollectionCount(2)}, " +
                  $"pause cuối={info.PauseDurations[0].TotalMilliseconds} ms, " +
                  $"% time in GC={info.PauseTimePercentage}");
```

**⚠️ `GC.Collect()` trong code production gần như luôn sai** — nó ép full blocking GC, phá vỡ
heuristic đã tinh chỉnh nhiều năm. Ngoại lệ: benchmark, hoặc sau một sự kiện "vừa xả rất nhiều
bộ nhớ, chắc chắn không lặp lại" (nạp xong file cấu hình khổng lồ lúc khởi động).

---

## MEM-20. Đo lường — bộ chỉ số bạn phải biết đọc

| Chỉ số | Nguồn | Đáng lo khi |
|---|---|---|
| Allocation rate (B/s) | `dotnet-counters` `alloc-rate` | > vài trăm MB/s liên tục |
| `% Time in GC` | `dotnet-counters` | > 10% |
| Gen 2 collection count | `GC.CollectionCount(2)` | tăng đều đặn theo tải |
| Gen 2 / LOH size | `gc-heap-size`, gcdump | tăng đơn điệu = leak |
| Pause duration | `GC.GetGCMemoryInfo().PauseDurations` | p99 > SLA |
| ThreadPool queue length | `dotnet-counters` | > 0 kéo dài (xem ASY-9) |

```csharp
// Micro-benchmark đúng chuẩn
[MemoryDiagnoser]                 // BenchmarkDotNet: in ra Gen0/1/2 + Allocated
public class Bench { [Benchmark] public void Method() { … } }
```

**⚖️ Nguyên tắc**: *đừng tối ưu theo cảm giác*. Thứ tự đúng: đo (counters) → khoanh vùng (gcdump/
trace) → sửa → đo lại. Trong phỏng vấn, kể được **quy trình này** giá trị hơn kể được tên API.

---

## ✅ Checklist tự kiểm tra — Phần 12

- [ ] Vẽ được sơ đồ SOH/LOH/POH + gen 0/1/2 và nói được "GC Gen N thu luôn mọi gen nhỏ hơn".
- [ ] Giải thích bump-pointer allocation và vì sao "allocation rẻ, survival mới đắt".
- [ ] Kể được 7 pha của GC và danh sách GC roots.
- [ ] Giải thích card table + write barrier và hệ quả với mảng class vs mảng struct.
- [ ] Nói được finalizer làm object sống thêm ít nhất một vòng GC, và vì sao `SuppressFinalize`.
- [ ] Kể 5 nguồn memory leak và quy trình `dotnet-counters → gcdump → gcroot`.
- [ ] Giải thích `Span<T>` là gì và vì sao nó không dùng được trong `async`.

➡️ Tiếp: [Phần 13 — Async & Threading Internals](interview.NET.13-Async-Threading-Internals.md)
