using System.Buffers;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HW.CS.Demos;

/// <summary>
/// Runnable demos cho interview.NET.12-Memory-GC-Internals.md (MEM-1..MEM-20).
/// Nhiều demo gọi GC.Collect() một cách CÓ CHỦ Ý để quan sát hành vi —
/// đừng bắt chước điều đó trong code production (xem MEM-19).
/// </summary>
public sealed class Demo12_MemoryGc : IDemoTopic
{
    public string Title => "12 — Memory & Garbage Collector Internals";

    public IReadOnlyList<DemoItem> Items => new DemoItem[]
    {
        new("MEM1.  Cấu hình GC hiện tại & số liệu heap", MEM1_GcConfiguration),
        new("MEM2.  Allocation rẻ cỡ nào (bump pointer)", MEM2_AllocationSpeed),
        new("MEM3.  Generation của object & promote", MEM3_Generations),
        new("MEM4.  GC Gen N thu luôn mọi gen nhỏ hơn", MEM4_CollectionCounts),
        new("MEM5.  GC tìm object SỐNG, không tìm rác", MEM5_SurvivorsCostMore),
        new("MEM6.  Ngưỡng LOH 85.000 byte", MEM6_LargeObjectHeap),
        new("MEM7.  Pinned Object Heap & pinning", MEM7_PinnedObjectHeap),
        new("MEM8.  Finalizer làm object sống thêm 1 vòng GC", MEM8_Finalizer),
        new("MEM9.  WeakReference & ConditionalWeakTable", MEM9_WeakReferences),
        new("MEM10. Leak: event handler không unsubscribe", MEM10_EventLeak),
        new("MEM11. Leak: static cache giữ object mãi mãi", MEM11_StaticCacheLeak),
        new("MEM12. ArrayPool vs cấp phát mới", MEM12_ArrayPool),
        new("MEM13. stackalloc + pattern nhỏ-thì-stack/lớn-thì-pool", MEM13_StackallocPattern),
        new("MEM14. Mảng struct vs mảng class (cache locality)", MEM14_StructArrayVsClassArray),
        new("MEM15. Write barrier: gán reference vs gán value", MEM15_WriteBarrier),
        new("MEM16. Boxing dưới góc nhìn bộ nhớ", MEM16_BoxingMemory),
        new("MEM17. Pause duration & % time in GC", MEM17_PauseStats),
        new("MEM18. NoGCRegion (latency mode)", MEM18_NoGcRegion),
        new("MEM19. Cheat-sheet chẩn đoán memory leak", MEM19_DiagnosticsCheatSheet),
    };

    // ── MEM1 ──────────────────────────────────────────────────────────────
    public static void MEM1_GcConfiguration()
    {
        var info = GC.GetGCMemoryInfo();
        Console.WriteLine($"  IsServerGC              : {GCSettings.IsServerGC}  (ASP.NET Core mặc định true; console mặc định false)");
        Console.WriteLine($"  LatencyMode             : {GCSettings.LatencyMode}");
        Console.WriteLine($"  Số logical core         : {Environment.ProcessorCount}  (Server GC ⇒ mỗi core một heap)");
        string concurrent = AppContext.TryGetSwitch("System.GC.Concurrent", out bool c) ? c.ToString() : "mặc định (bật)";
        Console.WriteLine($"  Concurrent (background) : {concurrent}");
        Console.WriteLine();
        Console.WriteLine($"  Heap size hiện tại      : {info.HeapSizeBytes / 1024.0 / 1024:N1} MB");
        Console.WriteLine($"  Committed               : {info.TotalCommittedBytes / 1024.0 / 1024:N1} MB");
        Console.WriteLine($"  Tổng bộ nhớ managed     : {GC.GetTotalMemory(false) / 1024.0 / 1024:N1} MB");
        Console.WriteLine($"  Đã cấp phát trên thread : {GC.GetAllocatedBytesForCurrentThread() / 1024.0:N0} KB (tích luỹ từ lúc chạy)");
        Console.WriteLine($"  Working set (OS thấy)   : {Environment.WorkingSet / 1024.0 / 1024:N1} MB  ← gồm cả native, code, stack");
        Console.WriteLine();
        Console.WriteLine("  Số lần GC đã chạy: " +
                          $"Gen0={GC.CollectionCount(0)}, Gen1={GC.CollectionCount(1)}, Gen2={GC.CollectionCount(2)}");
        Console.WriteLine();
        Console.WriteLine("→ Bật Server GC: <ServerGarbageCollection>true</ServerGarbageCollection> hoặc DOTNET_gcServer=1.");
        Console.WriteLine("→ Trong container NHỚ giới hạn: DOTNET_GCHeapCount / GCHeapHardLimitPercent.");
    }

    // ── MEM2 ──────────────────────────────────────────────────────────────
    public static void MEM2_AllocationSpeed()
    {
        const int N = 5_000_000;

        for (int i = 0; i < 100_000; i++) _sink = new SmallObject(i);   // warm-up

        int gen0Before = GC.CollectionCount(0);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < N; i++) _sink = new SmallObject(i);
        sw.Stop();
        int gen0After = GC.CollectionCount(0);

        double nsPerAlloc = sw.Elapsed.TotalNanoseconds / N;
        Console.WriteLine($"  Cấp phát {N:N0} object nhỏ : {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"  ⇒ {nsPerAlloc:F2} ns / lần cấp phát  (chỉ vài lệnh CPU: bump pointer + kiểm tra limit)");
        Console.WriteLine($"  Số GC Gen0 đã chạy trong lúc đó : {gen0After - gen0Before}");
        Console.WriteLine();
        Console.WriteLine("→ Vì sao rẻ: mỗi thread có ALLOCATION CONTEXT riêng (một lát Gen 0) ⇒ cấp phát KHÔNG cần lock,");
        Console.WriteLine("  và vùng nhớ vừa được dọn nên đang nằm sẵn trong cache CPU.");
        Console.WriteLine("→ Câu chốt: allocation rẻ, COLLECTION mới đắt. Mục tiêu không phải 'đừng allocate',");
        Console.WriteLine("  mà là 'đừng để rác sống sót qua Gen 0'.");
    }

    // ── MEM3 ──────────────────────────────────────────────────────────────
    public static void MEM3_Generations()
    {
        var obj = new SmallObject(1);
        Console.WriteLine($"  Object vừa tạo                 → Gen {GC.GetGeneration(obj)}");

        GC.Collect(0, GCCollectionMode.Forced, blocking: true);
        Console.WriteLine($"  Sau 1 lần GC Gen 0 (sống sót)  → Gen {GC.GetGeneration(obj)}  ← đã được PROMOTE");

        GC.Collect(1, GCCollectionMode.Forced, blocking: true);
        Console.WriteLine($"  Sau 1 lần GC Gen 1 (sống sót)  → Gen {GC.GetGeneration(obj)}");

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        Console.WriteLine($"  Sau full GC                    → Gen {GC.GetGeneration(obj)}  (cao nhất là 2)");

        var big = new byte[100_000];
        Console.WriteLine($"  byte[100.000] (≥85.000 ⇒ LOH)  → Gen {GC.GetGeneration(big)}  ← LOH được coi như Gen 2 ngay từ đầu");

        GC.KeepAlive(obj);
        Console.WriteLine();
        Console.WriteLine("→ Object sống sót càng nhiều vòng GC càng leo lên gen cao ⇒ càng ít bị quét,");
        Console.WriteLine("  nhưng khi thật sự chết thì phải chờ tới full GC mới được dọn.");
        Console.WriteLine("→ 'Mid-life crisis' = object sống vừa đủ để bị promote rồi mới chết — kiểu rác TỆ NHẤT.");
    }

    // ── MEM4 ──────────────────────────────────────────────────────────────
    public static void MEM4_CollectionCounts()
    {
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        Console.WriteLine($"  Bắt đầu   : Gen0={g0}, Gen1={g1}, Gen2={g2}");

        GC.Collect(0, GCCollectionMode.Forced, blocking: true);
        Console.WriteLine($"  Sau GC(0) : Gen0={GC.CollectionCount(0)}, Gen1={GC.CollectionCount(1)}, Gen2={GC.CollectionCount(2)}  ← chỉ Gen0 tăng");

        GC.Collect(1, GCCollectionMode.Forced, blocking: true);
        Console.WriteLine($"  Sau GC(1) : Gen0={GC.CollectionCount(0)}, Gen1={GC.CollectionCount(1)}, Gen2={GC.CollectionCount(2)}  ← Gen0 VÀ Gen1 cùng tăng");

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        Console.WriteLine($"  Sau GC(2) : Gen0={GC.CollectionCount(0)}, Gen1={GC.CollectionCount(1)}, Gen2={GC.CollectionCount(2)}  ← cả ba cùng tăng");
        Console.WriteLine();
        Console.WriteLine("→ Luật quan trọng nhất về generation: **GC Gen N luôn thu luôn mọi gen nhỏ hơn N**.");
        Console.WriteLine("  Vì thế full GC (Gen 2) là cái đắt nhất và là thứ gây pause dài trong production.");
    }

    // ── MEM5 ──────────────────────────────────────────────────────────────
    public static void MEM5_SurvivorsCostMore()
    {
        const int N = 3_000_000;

        // (a) tạo rác chết ngay — không giữ reference nào
        for (int i = 0; i < N; i++) _sink = new SmallObject(i);
        var sw = Stopwatch.StartNew();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        double garbageOnly = sw.Elapsed.TotalMilliseconds;

        // (b) giữ N object SỐNG rồi mới thu
        var alive = new SmallObject[N];
        for (int i = 0; i < N; i++) alive[i] = new SmallObject(i);
        sw.Restart();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        double withSurvivors = sw.Elapsed.TotalMilliseconds;
        GC.KeepAlive(alive);

        Console.WriteLine($"  Full GC khi {N:N0} object đều là RÁC   : {garbageOnly,7:F2} ms");
        Console.WriteLine($"  Full GC khi {N:N0} object đều còn SỐNG : {withSurvivors,7:F2} ms");
        Console.WriteLine($"  → chậm hơn khoảng {withSurvivors / Math.Max(garbageOnly, 0.01):F1} lần");
        Console.WriteLine();
        Console.WriteLine("→ GC KHÔNG đi tìm rác — nó đi tìm object SỐNG (mark từ roots), phần còn lại mặc nhiên là rác.");
        Console.WriteLine("  ⇒ Chi phí GC tỉ lệ với số object SỐNG SÓT, không phải số object đã tạo.");
        Console.WriteLine("  ⇒ Cache lớn / list giữ lâu / static collection mới là thứ làm GC đắt, không phải 'nhiều new'.");
    }

    // ── MEM6 ──────────────────────────────────────────────────────────────
    public static void MEM6_LargeObjectHeap()
    {
        // 85.000 byte là ngưỡng tính CẢ header — byte[84_000] vẫn SOH, byte[86_000] vào LOH
        var small = new byte[84_000];
        var large = new byte[86_000];

        Console.WriteLine($"  byte[84.000] → Gen {GC.GetGeneration(small)}   (Small Object Heap)");
        Console.WriteLine($"  byte[86.000] → Gen {GC.GetGeneration(large)}   (Large Object Heap — coi như Gen 2 ngay)");
        Console.WriteLine();

        var info = GC.GetGCMemoryInfo();
        for (int i = 0; i < info.GenerationInfo.Length; i++)
        {
            var g = info.GenerationInfo[i];
            string name = i switch { 0 => "Gen 0", 1 => "Gen 1", 2 => "Gen 2", 3 => "LOH  ", 4 => "POH  ", _ => $"gen{i}" };
            Console.WriteLine($"    {name} : size sau GC = {g.SizeAfterBytes / 1024.0,9:N1} KB, fragmentation = {g.FragmentationAfterBytes / 1024.0,8:N1} KB");
        }

        Console.WriteLine();
        Console.WriteLine("→ LOH mặc định KHÔNG nén (copy object lớn quá đắt) ⇒ dễ phân mảnh: byte sống ít nhưng heap vẫn phình.");
        Console.WriteLine("→ Cấp phát LOH liên tục (đọc file/HTTP body vào byte[] mỗi request) = kéo theo full GC liên tục.");
        Console.WriteLine("→ Cách chữa: ArrayPool / RecyclableMemoryStream / đọc theo stream, PipeReader.");
        Console.WriteLine("→ Nén LOH thủ công (rất đắt, hiếm khi cần):");
        Console.WriteLine("    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce; GC.Collect();");
        GC.KeepAlive(small);
        GC.KeepAlive(large);
    }

    // ── MEM7 ──────────────────────────────────────────────────────────────
    public static void MEM7_PinnedObjectHeap()
    {
        byte[] pinnedArray = GC.AllocateArray<byte>(4096, pinned: true);   // .NET 5+: cấp thẳng trên POH
        Console.WriteLine($"  GC.AllocateArray<byte>(4096, pinned: true) → Gen {GC.GetGeneration(pinnedArray)} (POH thuộc nhóm gen 2)");

        byte[] normal = new byte[4096];
        var handle = GCHandle.Alloc(normal, GCHandleType.Pinned);
        try
        {
            IntPtr addr1 = handle.AddrOfPinnedObject();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            IntPtr addr2 = handle.AddrOfPinnedObject();
            Console.WriteLine($"  Object bị ghim, địa chỉ trước GC : 0x{addr1.ToInt64():X}");
            Console.WriteLine($"  Object bị ghim, địa chỉ sau  GC : 0x{addr2.ToInt64():X}  ← KHÔNG đổi vì GC không được phép dời");
            Console.WriteLine($"  Giống nhau? {addr1 == addr2}");
        }
        finally { handle.Free(); }

        Console.WriteLine();
        Console.WriteLine("→ GC nén heap bằng cách DỒN object lại. Object bị ghim là 'cột bê tông' giữa đường");
        Console.WriteLine("  ⇒ để lại lỗ hổng ⇒ heap phình dù byte sống rất ít (fragmentation).");
        Console.WriteLine("→ .NET 5+ gom mọi thứ ghim lâu dài vào POH riêng để SOH không bị đục lỗ.");
        Console.WriteLine("→ Trong code ứng dụng: gần như không bao giờ tự pin — dùng Memory<T>/PipeReader/ArrayPool.");
    }

    // ── MEM8 ──────────────────────────────────────────────────────────────
    public static void MEM8_Finalizer()
    {
        WeakReference wrFinalizable = CreateFinalizable();
        WeakReference wrPlain = CreatePlain();

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        Console.WriteLine($"  Sau GC lần 1 — object KHÔNG có finalizer còn sống? {wrPlain.IsAlive}  (đã bị thu ngay)");
        Console.WriteLine("  (dùng WeakReference trackResurrection: true để nhìn thấy giai đoạn f-reachable)");
        Console.WriteLine($"  Sau GC lần 1 — object CÓ  finalizer còn sống?    {wrFinalizable.IsAlive}  ← vẫn sống: nằm trong f-reachable queue");

        GC.WaitForPendingFinalizers();                 // chờ finalizer thread chạy xong
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        Console.WriteLine($"  Sau khi finalizer chạy + GC lần 2                 : còn sống? {wrFinalizable.IsAlive}  ← lúc này mới thật sự bị thu");
        Console.WriteLine();

        // SuppressFinalize
        var disposed = new FinalizableResource(999);
        disposed.Dispose();          // gọi SuppressFinalize bên trong
        Console.WriteLine($"  Đã Dispose() ⇒ SuppressFinalize ⇒ object bị gỡ khỏi finalization queue (không tốn vòng GC thứ 2).");
        Console.WriteLine();
        Console.WriteLine("→ Object có finalizer LUÔN tốn ít nhất 2 vòng GC và LUÔN bị promote lên gen cao hơn.");
        Console.WriteLine("→ Chỉ có MỘT finalizer thread: một finalizer chậm/treo ⇒ kẹt hàng đợi ⇒ leak toàn app.");
        Console.WriteLine("→ Thứ tự finalizer KHÔNG xác định ⇒ trong Finalize() đừng đụng object managed khác.");
        Console.WriteLine("→ Tốt nhất: dùng SafeHandle, đừng tự viết finalizer.");
    }

    // ── MEM9 ──────────────────────────────────────────────────────────────
    public static void MEM9_WeakReferences()
    {
        var strong = new SmallObject(1);
        WeakReference<SmallObject> weak = Mem12Helpers.CreateWeak();

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        Console.WriteLine($"  Object có strong reference   : còn sống? {strong is not null}");
        Console.WriteLine($"  Object chỉ có WeakReference  : còn sống? {weak.TryGetTarget(out _)}  ← weak KHÔNG phải GC root");
        Console.WriteLine();

        // ConditionalWeakTable: gắn dữ liệu phụ mà không giữ key sống
        var table = new ConditionalWeakTable<SmallObject, string>();
        (WeakReference weakKey, string meta) = Mem12Helpers.AddToTableAndDrop(table);
        Console.WriteLine($"  ConditionalWeakTable tra cứu (lúc key còn sống) : {meta}");

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        Console.WriteLine($"  Sau khi bỏ mọi reference tới key → key còn sống? {weakKey.IsAlive}  ← table KHÔNG giữ key sống");
        Console.WriteLine($"  Số entry còn lại trong table : {table.Count()}  (entry tự biến mất theo key)");
        Console.WriteLine();
        Console.WriteLine("→ Dùng cho cache 'có thì tốt' (bitmap, kết quả parse đắt tiền).");
        Console.WriteLine("→ KHÔNG dùng cho cache nghiệp vụ: dùng IMemoryCache có SizeLimit + eviction policy để kiểm soát được.");
        GC.KeepAlive(strong);
    }

    // ── MEM10 ─────────────────────────────────────────────────────────────
    public static void MEM10_EventLeak()
    {
        var publisher = new EventPublisher();

        WeakReference wrLeaked = Mem12Helpers.SubscribeAndDrop(publisher, unsubscribe: false);
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        Console.WriteLine($"  Subscriber KHÔNG unsubscribe → còn sống sau GC? {wrLeaked.IsAlive}  ⚠️ LEAK");

        WeakReference wrOk = Mem12Helpers.SubscribeAndDrop(publisher, unsubscribe: true);
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        Console.WriteLine($"  Subscriber CÓ unsubscribe    → còn sống sau GC? {wrOk.IsAlive}  ✅ đã được thu");
        Console.WriteLine();
        Console.WriteLine("→ Cơ chế: `publisher.Event += sub.Handler` tạo một delegate có _target = sub (xem RT12).");
        Console.WriteLine("  Publisher giữ delegate ⇒ giữ luôn subscriber ⇒ subscriber KHÔNG BAO GIỜ chết");
        Console.WriteLine("  (dù bạn đã bỏ hết reference của mình tới nó).");
        Console.WriteLine("→ Nguy hiểm nhất khi publisher là SINGLETON và subscriber là scoped/per-request.");
        Console.WriteLine("→ Chữa: `-=` trong Dispose, hoặc dùng weak event pattern.");
        GC.KeepAlive(publisher);
    }

    // ── MEM11 ─────────────────────────────────────────────────────────────
    public static void MEM11_StaticCacheLeak()
    {
        long before = GC.GetTotalMemory(true);

        for (int i = 0; i < 20_000; i++)
            StaticCache.Items[$"key-{i}"] = new byte[1024];       // "cache" không TTL, không giới hạn

        long after = GC.GetTotalMemory(true);   // true = ép GC trước khi đo
        Console.WriteLine($"  Bộ nhớ trước : {before / 1024.0 / 1024:N1} MB");
        Console.WriteLine($"  Bộ nhớ sau   : {after / 1024.0 / 1024:N1} MB  (đã ép full GC — vẫn không giảm)");
        Console.WriteLine($"  Số phần tử trong static cache : {StaticCache.Items.Count:N0}");
        Console.WriteLine();

        StaticCache.Items.Clear();
        long cleared = GC.GetTotalMemory(true);
        Console.WriteLine($"  Sau khi Clear() + full GC     : {cleared / 1024.0 / 1024:N1} MB  ← chỉ khi BỎ ROOT thì GC mới thu được");
        Console.WriteLine();
        Console.WriteLine("→ static field là GC ROOT vĩnh viễn ⇒ mọi thứ nó giữ sẽ leo lên Gen 2 và ở lại đó tới khi app tắt.");
        Console.WriteLine("→ Đây là nguồn leak số 1 trong app .NET. Cache PHẢI có: size limit + eviction + TTL.");
        Console.WriteLine("→ Cách phát hiện: 2 lần `dotnet-gcdump` cách nhau vài phút, so delta theo type;");
        Console.WriteLine("  rồi `gcroot <address>` trong dotnet-dump để biết AI đang giữ object đó.");
    }

    // ── MEM12 ─────────────────────────────────────────────────────────────
    public static void MEM12_ArrayPool()
    {
        const int Iterations = 1000;
        const int Size = 100_000;      // ≥ 85.000 ⇒ vào LOH nếu cấp phát mới

        long allocNew = Alloc(() =>
        {
            for (int i = 0; i < Iterations; i++) { var buf = new byte[Size]; buf[0] = 1; }
        });

        var pool = ArrayPool<byte>.Shared;
        long allocPooled = Alloc(() =>
        {
            for (int i = 0; i < Iterations; i++)
            {
                byte[] buf = pool.Rent(Size);
                try { buf[0] = 1; }
                finally { pool.Return(buf); }
            }
        });

        Console.WriteLine($"  new byte[{Size:N0}] × {Iterations:N0}      : {allocNew / 1024.0 / 1024,8:N1} MB  ← toàn bộ vào LOH");
        Console.WriteLine($"  ArrayPool.Rent/Return × {Iterations:N0}     : {allocPooled / 1024.0 / 1024,8:N1} MB");
        Console.WriteLine();

        byte[] rented = pool.Rent(1000);
        Console.WriteLine($"  ⚠️ Rent(1000) trả về mảng dài {rented.Length} — LUÔN dùng `Length` bạn yêu cầu, đừng dùng buf.Length!");
        Console.WriteLine("  ⚠️ Mảng mượn KHÔNG được xoá sẵn — có dữ liệu của người dùng trước. Dữ liệu nhạy cảm ⇒ Return(buf, clearArray: true).");
        pool.Return(rented);
        Console.WriteLine();
        Console.WriteLine("→ 3 lỗi pooling kinh điển: (1) quên Return, (2) Return rồi vẫn dùng tiếp (bug dữ liệu rất khó tìm),");
        Console.WriteLine("  (3) không xoá dữ liệu nhạy cảm trước khi trả.");
        Console.WriteLine("→ CHỈ pool cái LỚN hoặc cái ĐẮT. Object nhỏ đời ngắn thì Gen 0 đã gần như miễn phí rồi (MEM2).");
    }

    // ── MEM13 ─────────────────────────────────────────────────────────────
    public static void MEM13_StackallocPattern()
    {
        Console.WriteLine($"  Xử lý 128 byte bằng stackalloc : {Alloc(() => ProcessSmall(128))} byte cấp phát trên heap");
        Console.WriteLine($"  Xử lý 100.000 byte (dùng pool) : {Alloc(() => ProcessSmall(100_000))} byte cấp phát trên heap");
        Console.WriteLine();
        Console.WriteLine("  Pattern chuẩn (nhỏ thì stack, lớn thì pool):");
        Console.WriteLine("    const int MaxStack = 256;");
        Console.WriteLine("    byte[]? rented = null;");
        Console.WriteLine("    Span<byte> buf = size <= MaxStack ? stackalloc byte[MaxStack]");
        Console.WriteLine("                                      : (rented = ArrayPool<byte>.Shared.Rent(size));");
        Console.WriteLine("    try { … } finally { if (rented is not null) ArrayPool<byte>.Shared.Return(rented); }");
        Console.WriteLine();
        Console.WriteLine("→ ⚠️ KHÔNG BAO GIỜ stackalloc trong vòng lặp: stack cộng dồn tới khi thoát method.");
        Console.WriteLine("→ ⚠️ Kích thước stackalloc phải là hằng nhỏ (≤ ~1KB). Lấy theo input người dùng = StackOverflow = DoS,");
        Console.WriteLine("  và StackOverflowException KHÔNG catch được — process chết ngay.");
    }

    // ── MEM14 ─────────────────────────────────────────────────────────────
    public static void MEM14_StructArrayVsClassArray()
    {
        const int N = 2_000_000;

        var structs = new PointStruct[N];
        var classes = new PointClass[N];
        for (int i = 0; i < N; i++)
        {
            structs[i] = new PointStruct { X = i, Y = i, Z = i };
            classes[i] = new PointClass { X = i, Y = i, Z = i };
        }

        // Xáo trộn CÙNG một hoán vị cho cả hai mảng.
        // Mảng struct: dữ liệu bị DI CHUYỂN theo ⇒ thứ tự logic vẫn = thứ tự vật lý (vẫn liên tục).
        // Mảng class : chỉ CON TRỎ bị đổi chỗ ⇒ duyệt tuần tự nhưng nhảy lung tung trong bộ nhớ.
        var rng = new Random(12345);
        for (int i = N - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (structs[i], structs[j]) = (structs[j], structs[i]);
            (classes[i], classes[j]) = (classes[j], classes[i]);
        }

        double sink = 0;
        for (int i = 0; i < 100_000; i++) { sink += structs[i].X; sink += classes[i].X; }   // warm-up

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < N; i++) sink += structs[i].X + structs[i].Y + structs[i].Z;
        double structTime = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        for (int i = 0; i < N; i++) sink += classes[i].X + classes[i].Y + classes[i].Z;
        double classTime = sw.Elapsed.TotalMilliseconds;

        Console.WriteLine($"  (đã xáo trộn cùng một hoán vị cho cả hai mảng — mô phỏng dữ liệu thật)");
        Console.WriteLine($"  Duyệt {N:N0} phần tử — mảng struct : {structTime,7:F1} ms");
        Console.WriteLine($"  Duyệt {N:N0} phần tử — mảng class  : {classTime,7:F1} ms  ({classTime / Math.Max(structTime, 0.01):F1}× chậm hơn)");
        Console.WriteLine($"  [sink={sink}]");
        Console.WriteLine();
        long structBytes = (long)N * Unsafe.SizeOf<PointStruct>();
        long classBytes = (long)N * 8 + (long)N * 40;   // mảng con trỏ + mỗi object (16 header + 24 payload)
        Console.WriteLine($"  Bộ nhớ: mảng struct ≈ {structBytes / 1024 / 1024,3} MB — MỘT object duy nhất, dữ liệu liên tục");
        Console.WriteLine($"          mảng class  ≈ {classBytes / 1024 / 1024,3} MB — {N * 8L / 1024 / 1024} MB con trỏ + {N:N0} object rời rạc (16B header mỗi cái)");
        Console.WriteLine($"  Số object GC phải duyệt khi mark: struct = 1, class = {N + 1:N0}");
        Console.WriteLine();
        Console.WriteLine("→ Lý do KHÔNG phải 'struct nhanh hơn' mà là CACHE LINE 64 byte: đọc 1 phần tử là kéo luôn phần tử kế bên.");
        Console.WriteLine("  Mảng class buộc CPU phải 'pointer chasing' tới các địa chỉ rải rác ⇒ cache miss liên tục.");
    }

    // ── MEM15 ─────────────────────────────────────────────────────────────
    public static void MEM15_WriteBarrier()
    {
        const int N = 50_000_000;
        var nodes = new Node[1000];
        for (int i = 0; i < nodes.Length; i++) nodes[i] = new Node();
        var target = new Node();

        for (int i = 0; i < 1_000_000; i++) { nodes[i % 1000].Next = target; nodes[i % 1000].Value = i; }  // warm-up

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < N; i++) nodes[i % 1000].Value = i;          // value field: KHÔNG có write barrier
        double valueTime = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        for (int i = 0; i < N; i++) nodes[i % 1000].Next = target;      // reference field: CÓ write barrier
        double refTime = sw.Elapsed.TotalMilliseconds;

        Console.WriteLine($"  Gán VALUE field  {N:N0} lần : {valueTime,7:F0} ms");
        Console.WriteLine($"  Gán REFERENCE    {N:N0} lần : {refTime,7:F0} ms  ({refTime / Math.Max(valueTime, 0.01):F2}×)");
        Console.WriteLine();
        Console.WriteLine("→ Mỗi lần gán một field kiểu reference, JIT chèn WRITE BARRIER: đánh dấu 'card' ~256 byte vừa bị ghi.");
        Console.WriteLine("→ Nhờ card table đó, GC Gen 0 chỉ cần quét những card bị đánh dấu thay vì quét toàn bộ Gen 2.");
        Console.WriteLine("  (Không có nó, mỗi GC Gen 0 sẽ phải duyệt cả heap ⇒ mất sạch ý nghĩa của generation.)");
        Console.WriteLine("→ Chênh lệch đo được ở đây chỉ vài phần trăm — write barrier là code rất ngắn.");
        Console.WriteLine("  Điều đáng nhớ KHÔNG phải con số, mà là: gán reference có chi phí ẩn, còn gán value thì không,");
        Console.WriteLine("  và chính cơ chế này mới làm cho generational GC khả thi.");
        Console.WriteLine("→ Hệ quả thực chiến: trong cấu trúc dữ liệu nóng, mảng struct thắng mảng class ở CẢ cache locality");
        Console.WriteLine("  LẪN write barrier (xem MEM14).");
        GC.KeepAlive(nodes);
    }

    // ── MEM16 ─────────────────────────────────────────────────────────────
    public static void MEM16_BoxingMemory()
    {
        const int N = 1_000_000;

        long allocBoxed = Alloc(() =>
        {
            var arr = new object[N];
            for (int i = 0; i < N; i++) arr[i] = i;
            _sink = arr;
        });
        long allocPlain = Alloc(() =>
        {
            var arr = new int[N];
            for (int i = 0; i < N; i++) arr[i] = i;
            _sink = arr;
        });

        Console.WriteLine($"  object[] chứa {N:N0} int boxed : {allocBoxed / 1024.0 / 1024,6:N1} MB");
        Console.WriteLine($"  int[]    chứa {N:N0} int       : {allocPlain / 1024.0 / 1024,6:N1} MB");
        Console.WriteLine($"  → gấp {(double)allocBoxed / allocPlain:F1} lần bộ nhớ");
        Console.WriteLine();

        // Chi phí GC: mark phase phải duyệt từng box
        var boxedArr = new object[N];
        for (int i = 0; i < N; i++) boxedArr[i] = i;
        var sw = Stopwatch.StartNew();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        double markBoxed = sw.Elapsed.TotalMilliseconds;
        GC.KeepAlive(boxedArr);
        boxedArr = null!;

        var plainArr = new int[N];
        sw.Restart();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        double markPlain = sw.Elapsed.TotalMilliseconds;
        GC.KeepAlive(plainArr);

        Console.WriteLine($"  Full GC khi giữ sống object[] boxed : {markBoxed,6:F1} ms  ({N:N0} object phải mark)");
        Console.WriteLine($"  Full GC khi giữ sống int[]          : {markPlain,6:F1} ms  (1 object phải mark)");
        Console.WriteLine();
        Console.WriteLine("→ Boxing không chỉ tốn 24 byte cho 4 byte dữ liệu.");
        Console.WriteLine("  Nó biến một GIÁ TRỊ thành một NODE trong đồ thị object mà GC phải duyệt lại ở MỌI lần mark.");
    }

    // ── MEM17 ─────────────────────────────────────────────────────────────
    public static void MEM17_PauseStats()
    {
        // tạo tải rồi đọc số liệu pause
        for (int i = 0; i < 200_000; i++) _sink = new byte[100];
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        var info = GC.GetGCMemoryInfo();
        Console.WriteLine($"  Generation vừa thu       : {info.Generation}");
        Console.WriteLine($"  Concurrent (background)? : {info.Concurrent}");
        Console.WriteLine($"  Compacted?               : {info.Compacted}");
        Console.WriteLine($"  Heap size sau GC         : {info.HeapSizeBytes / 1024.0 / 1024:N1} MB");
        Console.WriteLine($"  Fragmentation            : {info.FragmentedBytes / 1024.0 / 1024:N2} MB");
        Console.WriteLine($"  % thời gian dành cho GC  : {info.PauseTimePercentage:F2} %   ← > 10% là đáng lo");

        var pauses = info.PauseDurations;
        for (int i = 0; i < pauses.Length; i++)
            Console.WriteLine($"  Pause[{i}]                 : {pauses[i].TotalMilliseconds:F3} ms");

        Console.WriteLine();
        Console.WriteLine("  Bộ chỉ số phải biết đọc trong production (dotnet-counters monitor System.Runtime):");
        Console.WriteLine("    alloc-rate            > vài trăm MB/s liên tục ⇒ đáng xem lại");
        Console.WriteLine("    time-in-gc            > 10%                    ⇒ có vấn đề");
        Console.WriteLine("    gen-2-gc-count        tăng đều theo tải        ⇒ rác leo lên Gen 2");
        Console.WriteLine("    gc-heap-size          tăng đơn điệu            ⇒ LEAK");
        Console.WriteLine("    loh-size              tăng                     ⇒ buffer lớn cấp phát mỗi request");
    }

    // ── MEM18 ─────────────────────────────────────────────────────────────
    public static void MEM18_NoGcRegion()
    {
        Console.WriteLine($"  LatencyMode trước : {GCSettings.LatencyMode}");

        bool started = false;
        try { started = GC.TryStartNoGCRegion(16 * 1024 * 1024); }
        catch (Exception ex) { Console.WriteLine($"  Không vào được NoGCRegion: {ex.GetType().Name}"); }

        if (started)
        {
            Console.WriteLine($"  LatencyMode trong : {GCSettings.LatencyMode}");
            int before = GC.CollectionCount(0);
            for (int i = 0; i < 100_000; i++) _sink = new SmallObject(i);   // cấp phát trong ngân sách đã xin
            Console.WriteLine($"  Số GC Gen0 xảy ra trong vùng : {GC.CollectionCount(0) - before}  ← mục tiêu là 0");

            if (GCSettings.LatencyMode == GCLatencyMode.NoGCRegion) GC.EndNoGCRegion();
            Console.WriteLine($"  LatencyMode sau   : {GCSettings.LatencyMode}");
        }

        Console.WriteLine();
        Console.WriteLine("→ NoGCRegion: xin trước một ngân sách bộ nhớ để chạy đoạn code cực nhạy latency (trading, realtime).");
        Console.WriteLine("  Vượt ngân sách ⇒ GC vẫn chạy và vùng bị huỷ. Đây là công cụ chuyên dụng, không dùng bừa.");
        Console.WriteLine();
        Console.WriteLine("→ ⚠️ GC.Collect() trong production gần như LUÔN sai: nó ép full blocking GC và phá vỡ");
        Console.WriteLine("  heuristic đã được tinh chỉnh nhiều năm. Ngoại lệ hiếm: sau khi xả một lượng lớn bộ nhớ");
        Console.WriteLine("  chắc chắn không lặp lại (nạp xong file cấu hình khổng lồ lúc khởi động).");
    }

    // ── MEM19 ─────────────────────────────────────────────────────────────
    public static void MEM19_DiagnosticsCheatSheet()
    {
        Console.WriteLine("  ① ĐO — dotnet-counters (không cần dừng app, chạy được trên production)");
        Console.WriteLine("     dotnet-counters monitor -p <pid> System.Runtime");
        Console.WriteLine("       gc-heap-size / gen-0|1|2-size / loh-size / alloc-rate / time-in-gc");
        Console.WriteLine();
        Console.WriteLine("  ② KHOANH VÙNG — snapshot heap");
        Console.WriteLine("     dotnet-gcdump collect -p <pid>        (nhẹ, mở bằng Visual Studio / PerfView)");
        Console.WriteLine("     → chụp 2 lần cách nhau vài phút DƯỚI TẢI, so delta theo type");
        Console.WriteLine("     → leak thật = số instance của một type tăng đều và KHÔNG BAO GIỜ giảm");
        Console.WriteLine();
        Console.WriteLine("  ③ TÌM THỦ PHẠM — dump đầy đủ");
        Console.WriteLine("     dotnet-dump collect -p <pid>  →  dotnet-dump analyze <file>");
        Console.WriteLine("       dumpheap -stat            type nào chiếm nhiều byte nhất");
        Console.WriteLine("       dumpheap -type <Tên>      liệt kê địa chỉ instance");
        Console.WriteLine("       gcroot <address>          ★ AI đang giữ object này sống");
        Console.WriteLine("       dumpasync                 state machine async đang treo");
        Console.WriteLine("       eeheap -gc                cấu trúc heap theo generation");
        Console.WriteLine();
        Console.WriteLine("  ④ 8 NGUỒN LEAK THƯỜNG GẶP");
        Console.WriteLine("     static collection · event handler không -= · closure giữ this");
        Console.WriteLine("     · HttpClient new mỗi lần · timer/BackgroundService không huỷ");
        Console.WriteLine("     · captive dependency (singleton giữ scoped) · Task không bao giờ complete");
        Console.WriteLine("     · string.Intern dữ liệu người dùng");
        Console.WriteLine();
        Console.WriteLine("  ⑤ NGUYÊN TẮC: đo → khoanh vùng → sửa → đo lại. Đừng tối ưu theo cảm giác.");
        Console.WriteLine("     Trong phỏng vấn, kể được QUY TRÌNH này giá trị hơn kể tên API.");
    }

    // ── Helper ────────────────────────────────────────────────────────────

    private static object? _sink;

    private static long Alloc(Action action)
    {
        action();
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateFinalizable() => new(new FinalizableResource(1), trackResurrection: true);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreatePlain() => new(new SmallObject(1));

    private static int ProcessSmall(int size)
    {
        const int MaxStack = 256;
        byte[]? rented = null;
        Span<byte> buffer = size <= MaxStack
            ? stackalloc byte[MaxStack]
            : (rented = ArrayPool<byte>.Shared.Rent(size));
        try
        {
            buffer[0] = 1;
            return buffer.Length;
        }
        finally
        {
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Helper types
// ═══════════════════════════════════════════════════════════════════════════

file sealed class SmallObject(int value)
{
    public int Value = value;
}

file struct PointStruct { public double X; public double Y; public double Z; }   // 24 byte
file sealed class PointClass { public double X; public double Y; public double Z; }  // 24 byte payload + 16 byte header

file sealed class Node
{
    public Node? Next;    // gán field này ⇒ có write barrier
    public int Value;     // gán field này ⇒ không có write barrier
}

file sealed class FinalizableResource(int id) : IDisposable
{
    private readonly int _id = id;

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);   // gỡ khỏi finalization queue ⇒ không tốn vòng GC thứ 2
    }

    ~FinalizableResource() => Release();   // chỉ là lưới an toàn khi ai đó quên Dispose

    private void Release() { /* giải phóng tài nguyên native ứng với _id */ }
}

file sealed class EventPublisher
{
    public event Action? Ping;
    public void Raise() => Ping?.Invoke();
}

file static class Mem12Helpers
{
    /// <summary>Tạo object CHỈ được tham chiếu yếu (tạo trong method riêng để nó thật sự hết root).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static WeakReference<SmallObject> CreateWeak() => new(new SmallObject(2));

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (WeakReference WeakKey, string Meta) AddToTableAndDrop(ConditionalWeakTable<SmallObject, string> table)
    {
        var key = new SmallObject(3);
        table.Add(key, "metadata gắn kèm");
        table.TryGetValue(key, out string? meta);
        return (new WeakReference(key), meta ?? "(không có)");
    }

    /// <summary>Tạo subscriber, đăng ký event, rồi BỎ mọi reference của chúng ta tới nó.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static WeakReference SubscribeAndDrop(EventPublisher publisher, bool unsubscribe)
    {
        var subscriber = new EventSubscriber();
        publisher.Ping += subscriber.OnPing;
        if (unsubscribe) publisher.Ping -= subscriber.OnPing;
        return new WeakReference(subscriber);
    }
}

file sealed class EventSubscriber
{
    private readonly byte[] _payload = new byte[10_000];   // để nhìn rõ trong heap dump
    public void OnPing() => _payload[0]++;
}

file static class StaticCache
{
    // ⚠️ static = GC root vĩnh viễn. Đây là nguồn memory leak số 1 trong app .NET.
    public static readonly Dictionary<string, byte[]> Items = new();
}
