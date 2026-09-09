using System.Text;

namespace HW.PCCP.How;

/// <summary>
/// Demo chạy được của toàn bộ sổ tay trong folder How/.
/// Chạy: dotnet run --project HW.PCCP/HW.PCCP.csproj -- how
///
/// Mỗi Section tương ứng 1 file .md:
///   Section01 -> 01-csharp-cheatsheet.md
///   Section02 -> 02-collections.md
///   Section03 -> 03-linq.md
///   Section04 -> 04-algorithms.md
///   Section05 -> 05-pitfalls.md
///   Section06 -> 06-shorthand.md
/// </summary>
public static class HowDemo
{
    public static void RunAll()
    {
        Section01_Strings();
        Section01_CharsArraysMath();
        Section02_Collections();
        Section03_Linq();
        Section04_Algorithms();
        Section05_Pitfalls();
        Section06_Shorthand();

        Console.WriteLine("\n===== HẾT =====");
    }

    // ---------------------------------------------------------------------
    // Helper in ấn
    // ---------------------------------------------------------------------

    private static void Title(string s) => Console.WriteLine($"\n===== {s} =====");

    private static void Sub(string s) => Console.WriteLine($"\n-- {s}");

    private static void P(string label, object? value) =>
        Console.WriteLine($"  {label,-46} => {value}");

    private static string Fmt<T>(IEnumerable<T> xs) => "[" + string.Join(", ", xs) + "]";

    private static string Fmt(int[,] m)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < m.GetLength(0); i++)
        {
            if (i > 0) sb.Append(" | ");
            for (int j = 0; j < m.GetLength(1); j++)
            {
                if (j > 0) sb.Append(' ');
                sb.Append(m[i, j]);
            }
        }
        return sb.ToString();
    }

    // =====================================================================
    // 01 — string
    // =====================================================================

    private static void Section01_Strings()
    {
        Title("01 — string");

        string s = "hello world";

        Sub("Cắt chuỗi");
        P("s.Length", s.Length);
        P("s[0] (char)", s[0]);
        P("s.Substring(6)", s.Substring(6));
        P("s.Substring(0, 5)  <- (start, ĐỘ DÀI)", s.Substring(0, 5));
        P("s[0..5]  (range, dễ đọc hơn)", s[0..5]);
        P("s[^5..]  (5 ký tự cuối)", s[^5..]);
        P("s[..^1]  (bỏ ký tự cuối)", s[..^1]);

        // ⚠ BẪY: đề PCCP cho [start, end] BAO GỒM cả hai đầu.
        const int start = 0, end = 4;
        P($"đề cho [{start},{end}] inclusive -> Substring(start, end-start+1)", s.Substring(start, end - start + 1));
        P($"đề cho [{start},{end}] inclusive -> s[start..(end+1)]", s[start..(end + 1)]);

        Sub("Tìm kiếm");
        P("s.Contains(\"wor\")", s.Contains("wor"));
        P("s.IndexOf('o')", s.IndexOf('o'));
        P("s.IndexOf(\"o\", 5)", s.IndexOf("o", 5));
        P("s.LastIndexOf('o')", s.LastIndexOf('o'));
        P("s.IndexOf('z')  (không thấy)", s.IndexOf('z'));
        P("s.Count(c => c == 'l')", s.Count(c => c == 'l'));

        Sub("Tách & ghép");
        P("\"a,b,,c\".Split(',')", Fmt("a,b,,c".Split(',')));
        P("... RemoveEmptyEntries", Fmt("a,b,,c".Split(',', StringSplitOptions.RemoveEmptyEntries)));
        P("\"a b\\tc\".Split()  (mọi whitespace)", Fmt("a b\tc".Split()));
        P("string.Join(\",\", 1..3)", string.Join(",", new[] { 1, 2, 3 }));

        Sub("Biến đổi (string immutable — luôn trả chuỗi MỚI)");
        P("s.ToUpper()", s.ToUpper());
        P("s.Replace('l', 'L')", s.Replace('l', 'L'));
        P("s.Remove(2, 3)  <- (start, ĐỘ DÀI)", s.Remove(2, 3));
        P("s.Insert(5, \"!!\")", s.Insert(5, "!!"));
        P("\"7\".PadLeft(3, '0')", "7".PadLeft(3, '0'));
        P("new string('a', 3)", new string('a', 3));
        P("đảo chuỗi", new string(s.Reverse().ToArray()));

        Sub("StringBuilder — BẮT BUỘC khi nối chuỗi trong vòng lặp");
        var sb = new StringBuilder();
        for (int i = 0; i < 5; i++) sb.Append(i).Append(',');
        sb.Length--;                        // xoá dấu phẩy cuối
        P("sb.ToString()", sb.ToString());
        P("lý do", "s += x trong vòng lặp là O(n^2) -> timeout");
    }

    // =====================================================================
    // 01 — char / mảng / Math
    // =====================================================================

    private static void Section01_CharsArraysMath()
    {
        Title("01 — char / mảng / Math");

        Sub("char");
        P("char.IsDigit('7')", char.IsDigit('7'));
        P("'7' - '0'  (char số -> int)", '7' - '0');
        P("(char)(7 + '0')  (int -> char số)", (char)(7 + '0'));
        P("'c' - 'a'  (chữ -> index 0..25)", 'c' - 'a');
        P("(char)('a' + 2)", (char)('a' + 2));

        // Mảng đếm 26 — nhanh hơn Dictionary khi chỉ có a..z
        int[] cnt = new int[26];
        foreach (char c in "banana") cnt[c - 'a']++;
        P("đếm 'banana' -> a,b,n", $"{cnt['a' - 'a']}, {cnt['b' - 'a']}, {cnt['n' - 'a']}");

        Sub("Mảng 1 chiều");
        int[] a = { 5, 3, 9, 1, 7 };
        P("a", Fmt(a));
        int[] filled = new int[5];
        Array.Fill(filled, -1);
        P("Array.Fill(new int[5], -1)", Fmt(filled));
        P("Enumerable.Range(0, 5)", Fmt(Enumerable.Range(0, 5)));
        P("Enumerable.Repeat(-1, 4)", Fmt(Enumerable.Repeat(-1, 4)));

        int[] sorted = (int[])a.Clone();
        Array.Sort(sorted);
        P("Array.Sort", Fmt(sorted));

        int[] desc = (int[])a.Clone();
        Array.Sort(desc, (x, y) => y.CompareTo(x));   // ⚠ đừng viết y - x: tràn int
        P("Array.Sort giảm dần (CompareTo, không phải y-x)", Fmt(desc));

        P("Array.IndexOf(a, 9)", Array.IndexOf(a, 9));
        P("Array.BinarySearch(sorted, 7)", Array.BinarySearch(sorted, 7));

        Sub("Mảng 2 chiều — int[,] vs int[][]");
        int[,] rect = { { 1, 2, 3 }, { 4, 5, 6 } };
        P("rect", Fmt(rect));
        P("rect.GetLength(0)  <- SỐ DÒNG", rect.GetLength(0));
        P("rect.GetLength(1)  <- SỐ CỘT", rect.GetLength(1));
        P("rect.Length  <- ⚠ TỔNG PHẦN TỬ, không phải số dòng!", rect.Length);

        int rows = rect.GetLength(0), cols = rect.GetLength(1);
        int[][] jag = Enumerable.Range(0, rows)
            .Select(i => Enumerable.Range(0, cols).Select(j => rect[i, j]).ToArray())
            .ToArray();
        P("int[,] -> int[][] (để dùng LINQ)", string.Join(" | ", jag.Select(Fmt)));
        P("jag.Length / jag[0].Length", $"{jag.Length} / {jag[0].Length}");
        P("tổng mỗi dòng (LINQ)", Fmt(jag.Select(r => r.Sum())));

        Sub("Math & số học");
        P("Math.Max(3, 7) / Math.Min(3, 7)", $"{Math.Max(3, 7)} / {Math.Min(3, 7)}");
        P("5 / 2  (chia nguyên)", 5 / 2);
        P("(double)5 / 2", (double)5 / 2);
        P("ceil(7/2) = (7 + 2 - 1) / 2", (7 + 2 - 1) / 2);
        P("-7 / 2  (C# cắt về 0)", -7 / 2);
        P("-7 % 2  (⚠ ra số ÂM)", -7 % 2);
        P("modulo dương: ((-7 % 3) + 3) % 3", ((-7 % 3) + 3) % 3);
        P("Gcd(12, 18) / Lcm(4, 6)", $"{Gcd(12, 18)} / {Lcm(4, 6)}");
        P("IsPrime(97) / IsPrime(91)", $"{IsPrime(97)} / {IsPrime(91)}");
        P("Sieve(30)", Fmt(Sieve(30)));

        Sub("Bit");
        int x = 0b1011;
        P("x = 0b1011", Convert.ToString(x, 2));
        P("x & 1  (lẻ?)", x & 1);
        P("1 << 4", 1 << 4);
        P("x | (1 << 2)  bật bit 2", Convert.ToString(x | (1 << 2), 2));
        P("x & ~(1 << 1)  tắt bit 1", Convert.ToString(x & ~(1 << 1), 2));
        P("(x >> 3) & 1  đọc bit 3", (x >> 3) & 1);
        P("PopCount", System.Numerics.BitOperations.PopCount((uint)x));
        P("Convert.ToInt32(\"1011\", 2)", Convert.ToInt32("1011", 2));

        Sub("Parse & format");
        P("int.Parse(\"42\")", int.Parse("42"));
        P("int.TryParse(\"x\", out v)", int.TryParse("x", out int _));
        P("42.ToString(\"D5\")", 42.ToString("D5"));
        P("4.2.ToString(\"F3\")", 4.2.ToString("F3"));
        P("Convert.ToInt32('7')  ⚠ MÃ ASCII, không phải 7", Convert.ToInt32('7'));
    }

    private static long Gcd(long a, long b) => b == 0 ? a : Gcd(b, a % b);

    private static long Lcm(long a, long b) => a / Gcd(a, b) * b;   // chia trước, tránh tràn

    private static bool IsPrime(int n)
    {
        if (n < 2) return false;
        for (int i = 2; (long)i * i <= n; i++)
            if (n % i == 0) return false;
        return true;
    }

    private static List<int> Sieve(int n)
    {
        bool[] composite = new bool[n + 1];
        for (int i = 2; (long)i * i <= n; i++)
            if (!composite[i])
                for (int j = i * i; j <= n; j += i)
                    composite[j] = true;

        var primes = new List<int>();
        for (int i = 2; i <= n; i++)
            if (!composite[i]) primes.Add(i);
        return primes;
    }

    // =====================================================================
    // 02 — Collections
    // =====================================================================

    private static void Section02_Collections()
    {
        Title("02 — Collections");

        Sub("List<T>");
        var list = new List<int> { 5, 3, 9 };
        list.Add(1);
        list.Insert(0, 7);
        list.RemoveAll(v => v == 3);
        P("list", Fmt(list));
        P("list[^1]  (phần tử cuối)", list[^1]);
        list.Sort();
        P("list.Sort()", Fmt(list));
        // ⚠ Không xoá phần tử khi đang foreach -> InvalidOperationException. Dùng RemoveAll.

        Sub("Dictionary — đếm tần suất");
        string[] words = { "a", "b", "a", "c", "b", "a" };
        var cnt = new Dictionary<string, int>();
        foreach (string w in words)
        {
            cnt.TryGetValue(w, out int c);      // key không có -> c = 0
            cnt[w] = c + 1;
        }
        P("đếm bằng TryGetValue", string.Join(", ", cnt.Select(kv => $"{kv.Key}={kv.Value}")));

        var cntLinq = words.GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
        P("đếm bằng GroupBy", string.Join(", ", cntLinq.Select(kv => $"{kv.Key}={kv.Value}")));
        P("key có count lớn nhất", cnt.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key);

        Sub("Dictionary<K, List<V>> — adjacency / gom nhóm");
        var adj = new Dictionary<int, List<int>>();
        void AddEdge(int u, int v)
        {
            if (!adj.TryGetValue(u, out var lst)) adj[u] = lst = new List<int>();
            lst.Add(v);
        }
        AddEdge(1, 2); AddEdge(1, 3); AddEdge(2, 3);
        P("adj", string.Join(" | ", adj.Select(kv => $"{kv.Key}->{Fmt(kv.Value)}")));

        Sub("HashSet — 'đã gặp chưa'");
        var seen = new HashSet<string>();
        var firstTimes = new List<string>();
        foreach (string w in words)
            if (seen.Add(w))                    // Add trả FALSE nếu đã có -> chống trùng 1 dòng
                firstTimes.Add(w);
        P("các từ xuất hiện lần đầu", Fmt(firstTimes));

        var s1 = new HashSet<int> { 1, 2, 3, 4 };
        var s2 = new HashSet<int> { 3, 4, 5 };
        var inter = new HashSet<int>(s1); inter.IntersectWith(s2);
        var union = new HashSet<int>(s1); union.UnionWith(s2);
        var except = new HashSet<int>(s1); except.ExceptWith(s2);
        P("giao / hợp / hiệu", $"{Fmt(inter)} / {Fmt(union)} / {Fmt(except)}");

        Sub("Queue — FIFO (BFS)");
        var q = new Queue<int>();
        q.Enqueue(1); q.Enqueue(2); q.Enqueue(3);
        P("Dequeue, Peek, Count", $"{q.Dequeue()}, {q.Peek()}, {q.Count}");
        P("TryDequeue (an toàn, không ném)", q.TryDequeue(out int qv) ? qv : -1);

        Sub("Stack — LIFO (DFS, ngoặc)");
        var st = new Stack<int>();
        st.Push(1); st.Push(2); st.Push(3);
        P("Pop, Peek, Count", $"{st.Pop()}, {st.Peek()}, {st.Count}");
        P("IsBalanced(\"{[()]}\")", IsBalanced("{[()]}"));
        P("IsBalanced(\"([)]\")", IsBalanced("([)]"));

        Sub("PriorityQueue (.NET 6+) — MIN-heap mặc định");
        var pq = new PriorityQueue<string, int>();
        pq.Enqueue("thấp", 5);
        pq.Enqueue("cao", 1);
        pq.Enqueue("giữa", 3);
        P("Dequeue theo priority tăng", string.Join(" < ", DrainPq(pq)));

        var maxPq = new PriorityQueue<string, int>();
        maxPq.Enqueue("A", -5); maxPq.Enqueue("B", -1); maxPq.Enqueue("C", -3);
        P("max-heap: đảo dấu priority", string.Join(" > ", DrainPq(maxPq)));

        // Nhiều tiêu chí: priority là tuple (so sánh lần lượt từng phần tử)
        var multi = new PriorityQueue<string, (int cost, int time)>();
        multi.Enqueue("x", (2, 9)); multi.Enqueue("y", (1, 5)); multi.Enqueue("z", (1, 2));
        P("priority là tuple (cost, time)", string.Join(" < ", DrainPq(multi)));

        Sub("SortedSet — tập có thứ tự");
        var ss = new SortedSet<int> { 10, 3, 7, 1, 9 };
        P("ss (tự sort)", Fmt(ss));
        P("Min / Max", $"{ss.Min} / {ss.Max}");
        P("GetViewBetween(3, 9)", Fmt(ss.GetViewBetween(3, 9)));
        P("phần tử >= 5 nhỏ nhất (lower_bound)", ss.GetViewBetween(5, int.MaxValue).Min);

        Sub("Sort nhiều tiêu chí");
        var people = new List<(int score, string name)>
        {
            (90, "cham"), (70, "an"), (90, "binh"), (70, "dung")
        };
        var ranked = people
            .OrderByDescending(p => p.score)     // điểm giảm dần
            .ThenBy(p => p.name)                 // rồi tên tăng dần
            .ToList();
        P("OrderByDescending().ThenBy()", string.Join(", ", ranked.Select(p => $"{p.name}({p.score})")));

        var copy = new List<(int score, string name)>(people);
        copy.Sort((a, b) =>
        {
            int c = b.score.CompareTo(a.score);
            return c != 0 ? c : string.Compare(a.name, b.name, StringComparison.Ordinal);
        });
        P("List.Sort với comparer (cùng kết quả)", string.Join(", ", copy.Select(p => $"{p.name}({p.score})")));
        P("lưu ý", "List.Sort KHÔNG ổn định; OrderBy thì ổn định");
    }

    private static List<string> DrainPq<TPrio>(PriorityQueue<string, TPrio> pq)
    {
        var order = new List<string>();
        while (pq.TryDequeue(out string? e, out _)) order.Add(e!);
        return order;
    }

    private static bool IsBalanced(string s)
    {
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
    }

    // =====================================================================
    // 03 — LINQ
    // =====================================================================

    private static void Section03_Linq()
    {
        Title("03 — LINQ");

        int[] a = { 5, -3, 9, 1, -7, 9 };

        Sub("Lọc / biến đổi");
        P("Where(x => x > 0)", Fmt(a.Where(x => x > 0)));
        P("Select(x => x * 2)", Fmt(a.Select(x => x * 2)));
        P("Select((x, i) => ...)  kèm INDEX", Fmt(a.Select((x, i) => $"{i}:{x}")));
        P("Distinct()", Fmt(a.Distinct()));
        P("Take(3) / Skip(3)", $"{Fmt(a.Take(3))} / {Fmt(a.Skip(3))}");
        P("Chunk(2)  (.NET 6+)", string.Join(" | ", a.Chunk(2).Select(Fmt)));
        P("Zip", Fmt(a.Take(3).Zip(new[] { 10, 20, 30 }, (x, y) => x + y)));
        P("SelectMany (làm phẳng)", Fmt(new[] { new[] { 1, 2 }, new[] { 3 } }.SelectMany(r => r)));

        Sub("Thống kê");
        P("Count(x => x > 0)", a.Count(x => x > 0));
        P("Sum(x => (long)x)  ⚠ ép long", a.Sum(x => (long)x));
        P("Max / Min / Average", $"{a.Max()} / {a.Min()} / {a.Average():F2}");
        P("Aggregate(1L, (acc, x) => acc * x)", a.Aggregate(1L, (acc, x) => acc * x));

        var people = new[] { (name: "an", score: 70), (name: "binh", score: 90) };
        P("MaxBy trả PHẦN TỬ (.NET 6+)", people.MaxBy(p => p.score).name);

        Sub("Kiểm tra");
        P("Any(x => x < 0)", a.Any(x => x < 0));
        P("All(x => x != 0)", a.All(x => x != 0));
        P("⚠ mảng RỖNG: All(...) trả TRUE", Array.Empty<int>().All(x => x > 100));
        P("SequenceEqual", new[] { 1, 2 }.SequenceEqual(new[] { 1, 2 }));

        Sub("Gom nhóm");
        string[] words = { "bat", "tab", "cat", "act", "dog" };
        var anagrams = words
            .GroupBy(w => new string(w.OrderBy(c => c).ToArray()))   // khoá = chuỗi đã sort
            .Select(g => Fmt(g));
        P("gom anagram", string.Join(" | ", anagrams));

        var lookup = words.ToLookup(w => w.Length);
        P("ToLookup — khoá thiếu trả dãy RỖNG", Fmt(lookup[99]));

        Sub("One-liner hay dùng trong đề");
        string s = "hello123";
        P("đảo chuỗi", new string(s.Reverse().ToArray()));
        P("tổng chữ số của \"12345\"", "12345".Sum(c => c - '0'));
        P("toàn số?", s.All(char.IsDigit));
        P("ký tự nhiều nhất", s.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key);
        P("khoá anagram (sort chuỗi)", new string(s.OrderBy(c => c).ToArray()));
        P("mọi phần tử khác nhau?", a.Distinct().Count() == a.Length);
        P("index của giá trị lớn nhất", a.Select((v, i) => (v, i)).MaxBy(t => t.v).i);
        P("top 3 lớn nhất", Fmt(a.OrderByDescending(x => x).Take(3)));
        P("parse cả dòng", Fmt("1 2 3".Split(' ').Select(int.Parse)));

        int run = 0;
        P("prefix sum bằng LINQ", Fmt(a.Select(x => run += x)));

        int[][] g = { new[] { 1, 2, 3 }, new[] { 4, 5, 6 } };
        int rows = g.Length, cols = g[0].Length;
        P("tổng mỗi cột", Fmt(Enumerable.Range(0, cols).Select(c => g.Sum(r => r[c]))));

        var rot = Enumerable.Range(0, cols)
            .Select(c => Enumerable.Range(0, rows).Select(r => g[rows - 1 - r][c]).ToArray())
            .ToArray();
        P("xoay 90° theo chiều kim đồng hồ", string.Join(" | ", rot.Select(Fmt)));

        Sub("Bẫy LINQ");
        P("lazy: query chạy LẠI mỗi lần duyệt", "-> .ToList() khi dùng nhiều lần");
        P("List.Contains trong vòng lặp", "-> O(n^2), đổi sang HashSet");
        P("ToDictionary khoá TRÙNG", "-> ném exception, dùng GroupBy/ToLookup");
    }

    // =====================================================================
    // 04 — Thuật toán
    // =====================================================================

    private static readonly int[] Dr = { -1, 1, 0, 0 };
    private static readonly int[] Dc = { 0, 0, -1, 1 };

    private static void Section04_Algorithms()
    {
        Title("04 — Thuật toán");

        // 1 = đi được, 0 = tường
        int[,] grid =
        {
            { 1, 1, 1, 1 },
            { 0, 0, 1, 0 },
            { 1, 1, 1, 1 },
            { 1, 0, 0, 1 }
        };

        Sub("BFS — số bước ngắn nhất trên lưới");
        P("grid (1 = đi được)", Fmt(grid));
        P("BFS (0,0) -> (3,3)", BfsShortest(grid));
        P("mấu chốt", "đánh dấu visited lúc ENQUEUE, không phải lúc DEQUEUE");

        Sub("BFS theo tầng — biết đang ở bước thứ mấy");
        P("số ô cách (0,0) đúng 2 bước", BfsCountAtDepth(grid, 2));

        Sub("DFS — đếm vùng liên thông");
        int[,] islands =
        {
            { 1, 1, 0, 0 },
            { 1, 0, 0, 1 },
            { 0, 0, 1, 1 },
            { 0, 1, 0, 0 }
        };
        P("islands", Fmt(islands));
        P("DFS đệ quy", CountRegionsRecursive(islands));
        P("DFS khử đệ quy bằng Stack (an toàn hơn)", CountRegionsIterative(islands));

        Sub("Binary search");
        int[] sorted = { 1, 3, 3, 3, 5, 7, 9 };
        P("mảng", Fmt(sorted));
        P("LowerBound(3)  vị trí đầu >= 3", LowerBound(sorted, 3));
        P("UpperBound(3)  vị trí đầu > 3", UpperBound(sorted, 3));
        P("số lần xuất hiện của 3", UpperBound(sorted, 3) - LowerBound(sorted, 3));
        P("LowerBound(4)  (không có)", LowerBound(sorted, 4));

        Sub("Binary search TRÊN ĐÁP ÁN — dạng ra thi nhiều nhất");
        int[] times = { 7, 10 };
        P("n=6 khách, quầy xử lý mất 7 và 10 phút", MinTimeToServe(times, 6));
        P("cách nghĩ", "'thời gian ít nhất để...' + đơn điệu -> chặt nhị phân trên đáp án");

        Sub("Two pointer");
        int[] pairArr = { 1, 3, 4, 6, 8, 11 };
        P("mảng đã sort", Fmt(pairArr));
        P("cặp có tổng = 10", TwoSumSorted(pairArr, 10));

        Sub("Sliding window co giãn");
        P("\"eceba\", dãy con dài nhất có <= 2 ký tự khác nhau", LongestWithKDistinct("eceba", 2));

        Sub("Sliding window cố định k");
        int[] w = { 1, -2, 5, 3, -1, 6 };
        P("mảng", Fmt(w));
        P("tổng cửa sổ 3 phần tử lớn nhất", MaxWindowSum(w, 3));

        Sub("Prefix sum 1D");
        int[] ps = { 2, 4, 6, 8, 10 };
        long[] pre = BuildPrefix(ps);
        P("mảng", Fmt(ps));
        P("tổng a[1..3] (inclusive)", pre[4] - pre[1]);

        Sub("Prefix sum 2D");
        int[,] m2 =
        {
            { 1, 2, 3 },
            { 4, 5, 6 },
            { 7, 8, 9 }
        };
        long[,] pre2 = BuildPrefix2D(m2);
        P("ma trận", Fmt(m2));
        P("tổng hình chữ nhật (1,1)-(2,2) = 5+6+8+9", RectSum(pre2, 1, 1, 2, 2));

        Sub("Difference array — cộng vào ĐOẠN nhiều lần, O(1) mỗi lần");
        long[] diff = new long[7 + 1];
        void AddRange(int l, int r, long v) { diff[l] += v; diff[r + 1] -= v; }
        AddRange(0, 2, 5);
        AddRange(1, 4, 3);
        AddRange(5, 6, -2);
        long[] restored = new long[7];
        long acc = 0;
        for (int i = 0; i < 7; i++) { acc += diff[i]; restored[i] = acc; }
        P("+5 vào [0,2], +3 vào [1,4], -2 vào [5,6]", Fmt(restored));

        Sub("Greedy — chọn nhiều khoảng không giao nhau nhất (sort theo ĐIỂM KẾT THÚC)");
        int[][] intervals =
        {
            new[] { 1, 3 }, new[] { 2, 5 }, new[] { 4, 7 }, new[] { 6, 8 }, new[] { 8, 10 }
        };
        P("khoảng", string.Join(" ", intervals.Select(i => $"[{i[0]},{i[1]}]")));
        P("số khoảng tối đa", MaxNonOverlapping(intervals));

        Sub("Greedy — gộp khoảng (sort theo ĐIỂM BẮT ĐẦU)");
        P("gộp lại", string.Join(" ", MergeIntervals(intervals).Select(i => $"[{i[0]},{i[1]}]")));

        Sub("DP — knapsack 0/1");
        int[] weight = { 3, 4, 5 }, value = { 4, 5, 6 };
        P("w=[3,4,5] v=[4,5,6], sức chứa 8", Knapsack01(weight, value, 8));
        P("mấu chốt", "duyệt w NGƯỢC (mỗi món dùng 1 lần); duyệt XUÔI = dùng lại được");

        Sub("DP — chi phí nhỏ nhất trên lưới");
        int[,] cost =
        {
            { 1, 3, 1 },
            { 1, 5, 1 },
            { 4, 2, 1 }
        };
        P("ma trận chi phí", Fmt(cost));
        P("đường rẻ nhất (chỉ đi phải/xuống)", MinPathSum(cost));

        Sub("DP — LIS O(n log n)");
        int[] lisArr = { 10, 9, 2, 5, 3, 7, 101, 18 };
        P("mảng", Fmt(lisArr));
        P("độ dài dãy con tăng dài nhất", Lis(lisArr));
        P("⚠", "List.BinarySearch không thấy -> trả về ~insertionIndex (số âm)");

        Sub("Backtracking — hoán vị / tổ hợp / bitmask");
        P("hoán vị của [1,2,3]", string.Join(" ", Permutations(new[] { 1, 2, 3 }).Select(Fmt)));
        P("chọn 2 trong [1,2,3,4]", string.Join(" ", Combinations(new[] { 1, 2, 3, 4 }, 2).Select(Fmt)));
        P("mọi tập con của [1,2,3] (bitmask)", string.Join(" ", Subsets(new[] { 1, 2, 3 }).Select(Fmt)));

        Sub("Union-Find (DSU)");
        var dsu = new Dsu(6);
        dsu.Union(0, 1); dsu.Union(1, 2); dsu.Union(3, 4);
        P("union (0,1) (1,2) (3,4) trên 6 đỉnh", "");
        P("0 và 2 cùng nhóm?", dsu.Find(0) == dsu.Find(2));
        P("0 và 3 cùng nhóm?", dsu.Find(0) == dsu.Find(3));
        P("kích thước nhóm chứa 0", dsu.SizeOf(0));
        P("số nhóm", Enumerable.Range(0, 6).Count(i => dsu.Find(i) == i));

        Sub("Dijkstra — cạnh có trọng số không âm");
        int n = 5;
        var adj = new List<(int to, int w)>[n];
        for (int i = 0; i < n; i++) adj[i] = new List<(int, int)>();
        void Edge(int u, int v, int w) { adj[u].Add((v, w)); adj[v].Add((u, w)); }
        Edge(0, 1, 4); Edge(0, 2, 1); Edge(2, 1, 2); Edge(1, 3, 5); Edge(3, 4, 3);
        long[] dist = Dijkstra(adj, 0, n);
        P("dist từ đỉnh 0", Fmt(dist.Select(d => d == long.MaxValue ? "INF" : d.ToString())));
        P("mấu chốt", "PriorityQueue không có decrease-key -> lazy deletion: if (d > dist[u]) continue");

        Sub("Merge 2 mảng — 6 kiểu khác nhau");
        int[] ma = { 1, 3, 5, 7 };
        int[] mb = { 2, 3, 6 };
        P("a / b (đều đã sort)", $"{Fmt(ma)} / {Fmt(mb)}");

        P("1. Concat (giữ trùng, KHÔNG sort)", Fmt(ma.Concat(mb)));
        P("   Union (BỎ trùng)", Fmt(ma.Union(mb)));
        P("   Intersect / Except", $"{Fmt(ma.Intersect(mb))} / {Fmt(ma.Except(mb))}");

        int[] copyJoin = new int[ma.Length + mb.Length];
        Array.Copy(ma, 0, copyJoin, 0, ma.Length);
        Array.Copy(mb, 0, copyJoin, ma.Length, mb.Length);
        P("   Array.Copy (không LINQ, nhanh hơn)", Fmt(copyJoin));

        P("2. MergeSorted two pointer O(n+m) ✅", Fmt(MergeSorted(ma, mb)));
        P("   Concat().OrderBy() O((n+m)log) ❌", Fmt(ma.Concat(mb).OrderBy(x => x)));
        P("   -> cùng kết quả, nhưng đã sort sẵn thì đừng sort lại", "");

        int[] withGap = { 1, 3, 5, 7, 0, 0, 0 };            // 4 phần tử thật + 3 ô trống
        MergeInPlace(withGap, 4, mb, 3);
        P("3. MergeInPlace vào [1,3,5,7,_,_,_] (từ CUỐI)", Fmt(withGap));

        int[][] klists = { new[] { 1, 5, 9 }, new[] { 2, 6 }, new[] { 3, 4, 10 } };
        P("4. MergeK 3 mảng bằng PriorityQueue", Fmt(MergeK(klists)));

        var listA = new[] { (name: "táo", qty: 2), (name: "lê", qty: 5) };
        var listB = new[] { (name: "táo", qty: 3), (name: "cam", qty: 1) };
        var mergedByKey = listA.Concat(listB)
            .GroupBy(x => x.name)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.qty));
        P("5. Merge theo khoá (cộng dồn qty)",
            string.Join(", ", mergedByKey.Select(kv => $"{kv.Key}={kv.Value}")));

        P("6. Zip — ghép theo VỊ TRÍ, dừng ở mảng ngắn", Fmt(ma.Zip(mb, (x, y) => x + y)));

        Sub("Simulation — cập nhật ĐỒNG THỜI");
        int[,] fire =
        {
            { 1, 0, 0, 0 },
            { 0, 0, 0, 0 },
            { 0, 0, 0, 1 }
        };
        P("trạng thái đầu (1 = đang cháy)", Fmt(fire));
        P("sau 1 bước lan (đúng)", Fmt(SpreadSimultaneous(fire)));
        P("sau 1 bước lan (SAI: sửa tại chỗ)", Fmt(SpreadInPlaceWrong(fire)));
        P("vì sao sai", "ô vừa cháy trong cùng lượt lại tiếp tục lan -> lan quá xa");
    }

    // --- BFS -------------------------------------------------------------

    private static int BfsShortest(int[,] grid)
    {
        int rows = grid.GetLength(0), cols = grid.GetLength(1);
        var dist = new int[rows, cols];
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                dist[i, j] = -1;

        var q = new Queue<(int r, int c)>();
        q.Enqueue((0, 0));
        dist[0, 0] = 0;                              // đánh dấu NGAY lúc enqueue

        while (q.Count > 0)
        {
            var (r, c) = q.Dequeue();
            if (r == rows - 1 && c == cols - 1) return dist[r, c];

            for (int d = 0; d < 4; d++)
            {
                int nr = r + Dr[d], nc = c + Dc[d];
                if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;   // biên TRƯỚC
                if (grid[nr, nc] == 0) continue;
                if (dist[nr, nc] != -1) continue;

                dist[nr, nc] = dist[r, c] + 1;
                q.Enqueue((nr, nc));
            }
        }

        return -1;
    }

    private static int BfsCountAtDepth(int[,] grid, int target)
    {
        int rows = grid.GetLength(0), cols = grid.GetLength(1);
        var visited = new bool[rows, cols];
        var q = new Queue<(int r, int c)>();
        q.Enqueue((0, 0));
        visited[0, 0] = true;

        int step = 0;
        while (q.Count > 0)
        {
            if (step == target) return q.Count;

            int size = q.Count;                      // chốt size TRƯỚC vòng for
            for (int i = 0; i < size; i++)
            {
                var (r, c) = q.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    int nr = r + Dr[d], nc = c + Dc[d];
                    if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;
                    if (grid[nr, nc] == 0 || visited[nr, nc]) continue;
                    visited[nr, nc] = true;
                    q.Enqueue((nr, nc));
                }
            }
            step++;
        }

        return 0;
    }

    // --- DFS -------------------------------------------------------------

    private static int CountRegionsRecursive(int[,] g)
    {
        int rows = g.GetLength(0), cols = g.GetLength(1);
        var visited = new bool[rows, cols];

        // local function: nhìn thấy rows/cols/visited, khỏi truyền tham số
        void Dfs(int r, int c)
        {
            visited[r, c] = true;
            for (int d = 0; d < 4; d++)
            {
                int nr = r + Dr[d], nc = c + Dc[d];
                if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;
                if (visited[nr, nc] || g[nr, nc] == 0) continue;
                Dfs(nr, nc);
            }
        }

        int regions = 0;
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                if (g[i, j] == 1 && !visited[i, j]) { Dfs(i, j); regions++; }
        return regions;
    }

    private static int CountRegionsIterative(int[,] g)
    {
        int rows = g.GetLength(0), cols = g.GetLength(1);
        var visited = new bool[rows, cols];
        var st = new Stack<(int r, int c)>();
        int regions = 0;

        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
            {
                if (g[i, j] == 0 || visited[i, j]) continue;

                regions++;
                st.Push((i, j));
                visited[i, j] = true;

                while (st.Count > 0)
                {
                    var (r, c) = st.Pop();
                    for (int d = 0; d < 4; d++)
                    {
                        int nr = r + Dr[d], nc = c + Dc[d];
                        if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;
                        if (visited[nr, nc] || g[nr, nc] == 0) continue;
                        visited[nr, nc] = true;
                        st.Push((nr, nc));
                    }
                }
            }

        return regions;
    }

    // --- Binary search ---------------------------------------------------

    private static int LowerBound(int[] a, int target)
    {
        int lo = 0, hi = a.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;             // tránh (lo + hi) tràn int
            if (a[mid] < target) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private static int UpperBound(int[] a, int target)
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

    /// <summary>Thời gian ít nhất để phục vụ hết n khách (mỗi quầy mất times[i] phút/khách).</summary>
    private static long MinTimeToServe(int[] times, int customers)
    {
        bool Feasible(long minutes)
        {
            long served = 0;
            foreach (int t in times)
            {
                served += minutes / t;
                if (served >= customers) return true;  // thoát sớm, tránh tràn
            }
            return served >= customers;
        }

        long lo = 1, hi = (long)times.Min() * customers;   // ⚠ ép long
        long answer = hi;

        while (lo <= hi)
        {
            long mid = lo + (hi - lo) / 2;
            if (Feasible(mid)) { answer = mid; hi = mid - 1; }   // nhận rồi thử nhỏ hơn
            else lo = mid + 1;
        }

        return answer;
    }

    // --- Two pointer / sliding window ------------------------------------

    private static string TwoSumSorted(int[] a, int target)
    {
        int l = 0, r = a.Length - 1;
        while (l < r)
        {
            int sum = a[l] + a[r];
            if (sum == target) return $"a[{l}]={a[l]} + a[{r}]={a[r]}";
            if (sum < target) l++;
            else r--;
        }
        return "không có";
    }

    private static int LongestWithKDistinct(string s, int k)
    {
        var window = new Dictionary<char, int>();
        int left = 0, best = 0;

        for (int right = 0; right < s.Length; right++)
        {
            window.TryGetValue(s[right], out int c);
            window[s[right]] = c + 1;

            while (window.Count > k)                       // co lại khi vi phạm
            {
                if (--window[s[left]] == 0) window.Remove(s[left]);
                left++;
            }

            best = Math.Max(best, right - left + 1);
        }

        return best;
    }

    private static long MaxWindowSum(int[] a, int k)
    {
        long sum = 0, best = long.MinValue;
        for (int i = 0; i < a.Length; i++)
        {
            sum += a[i];
            if (i >= k) sum -= a[i - k];                   // đẩy phần tử ra khỏi cửa sổ
            if (i >= k - 1) best = Math.Max(best, sum);
        }
        return best;
    }

    // --- Prefix sum ------------------------------------------------------

    private static long[] BuildPrefix(int[] a)
    {
        long[] pre = new long[a.Length + 1];
        for (int i = 0; i < a.Length; i++) pre[i + 1] = pre[i] + a[i];
        return pre;
    }

    private static long[,] BuildPrefix2D(int[,] g)
    {
        int rows = g.GetLength(0), cols = g.GetLength(1);
        var pre = new long[rows + 1, cols + 1];
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                pre[i + 1, j + 1] = g[i, j] + pre[i, j + 1] + pre[i + 1, j] - pre[i, j];
        return pre;
    }

    private static long RectSum(long[,] pre, int r1, int c1, int r2, int c2) =>
        pre[r2 + 1, c2 + 1] - pre[r1, c2 + 1] - pre[r2 + 1, c1] + pre[r1, c1];

    // --- Greedy ----------------------------------------------------------

    private static int MaxNonOverlapping(int[][] intervals)
    {
        var arr = intervals.Select(i => (int[])i.Clone()).ToArray();
        Array.Sort(arr, (a, b) => a[1].CompareTo(b[1]));      // sort theo ĐIỂM KẾT THÚC

        int count = 0, lastEnd = int.MinValue;
        foreach (var it in arr)
            if (it[0] >= lastEnd) { count++; lastEnd = it[1]; }
        return count;
    }

    private static List<int[]> MergeIntervals(int[][] intervals)
    {
        var arr = intervals.Select(i => (int[])i.Clone()).ToArray();
        Array.Sort(arr, (a, b) => a[0].CompareTo(b[0]));      // sort theo ĐIỂM BẮT ĐẦU

        var merged = new List<int[]>();
        foreach (var it in arr)
        {
            if (merged.Count > 0 && it[0] <= merged[^1][1])
                merged[^1][1] = Math.Max(merged[^1][1], it[1]);
            else
                merged.Add(new[] { it[0], it[1] });
        }
        return merged;
    }

    // --- DP --------------------------------------------------------------

    private static int Knapsack01(int[] weight, int[] value, int capacity)
    {
        int[] dp = new int[capacity + 1];
        for (int i = 0; i < weight.Length; i++)
            for (int w = capacity; w >= weight[i]; w--)        // NGƯỢC: mỗi món dùng 1 lần
                dp[w] = Math.Max(dp[w], dp[w - weight[i]] + value[i]);
        return dp[capacity];
    }

    private static long MinPathSum(int[,] g)
    {
        int rows = g.GetLength(0), cols = g.GetLength(1);
        var dp = new long[rows, cols];
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

        return dp[rows - 1, cols - 1];
    }

    private static int Lis(int[] a)
    {
        var tails = new List<int>();
        foreach (int x in a)
        {
            int pos = tails.BinarySearch(x);
            if (pos < 0) pos = ~pos;                          // ~ = vị trí chèn
            if (pos == tails.Count) tails.Add(x);
            else tails[pos] = x;
        }
        return tails.Count;                                   // tails chỉ đúng ĐỘ DÀI
    }

    // --- Backtracking ----------------------------------------------------

    private static List<List<int>> Permutations(int[] a)
    {
        var result = new List<List<int>>();
        var cur = new List<int>();
        var used = new bool[a.Length];

        void Go()
        {
            if (cur.Count == a.Length) { result.Add(new List<int>(cur)); return; }
            for (int i = 0; i < a.Length; i++)
            {
                if (used[i]) continue;
                used[i] = true; cur.Add(a[i]);
                Go();
                cur.RemoveAt(cur.Count - 1); used[i] = false;   // hoàn tác
            }
        }

        Go();
        return result;
    }

    private static List<List<int>> Combinations(int[] a, int k)
    {
        var result = new List<List<int>>();
        var cur = new List<int>();

        void Go(int start)
        {
            if (cur.Count == k) { result.Add(new List<int>(cur)); return; }
            for (int i = start; i < a.Length; i++)
            {
                cur.Add(a[i]);
                Go(i + 1);                                     // i + 1 = không lấy lại
                cur.RemoveAt(cur.Count - 1);
            }
        }

        Go(0);
        return result;
    }

    private static List<List<int>> Subsets(int[] a)
    {
        var result = new List<List<int>>();
        for (int mask = 0; mask < (1 << a.Length); mask++)
        {
            var subset = new List<int>();
            for (int i = 0; i < a.Length; i++)
                if ((mask >> i & 1) == 1) subset.Add(a[i]);
            result.Add(subset);
        }
        return result;
    }

    // --- Merge -----------------------------------------------------------

    /// <summary>Gộp 2 mảng ĐÃ SORT bằng two pointer — O(n + m).</summary>
    private static int[] MergeSorted(int[] a, int[] b)
    {
        int[] res = new int[a.Length + b.Length];
        int i = 0, j = 0, k = 0;

        while (i < a.Length && j < b.Length)
            res[k++] = a[i] <= b[j] ? a[i++] : b[j++];   // <= giữ ổn định (a đứng trước b)

        while (i < a.Length) res[k++] = a[i++];          // đừng quên 2 vòng vét
        while (j < b.Length) res[k++] = b[j++];

        return res;
    }

    /// <summary>Gộp b vào a tại chỗ (a dài m + n, đuôi để trống) — duyệt từ CUỐI.</summary>
    private static void MergeInPlace(int[] a, int m, int[] b, int n)
    {
        int i = m - 1, j = n - 1, k = m + n - 1;
        while (j >= 0)
            a[k--] = (i >= 0 && a[i] > b[j]) ? a[i--] : b[j--];
    }

    /// <summary>Gộp k mảng đã sort bằng heap — O(N log k).</summary>
    private static int[] MergeK(int[][] lists)
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

    // --- DSU -------------------------------------------------------------

    private sealed class Dsu
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
            while (_parent[x] != x) x = _parent[x] = _parent[_parent[x]];   // path halving
            return x;
        }

        public bool Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra == rb) return false;
            if (_size[ra] < _size[rb]) (ra, rb) = (rb, ra);                 // tuple swap
            _parent[rb] = ra;
            _size[ra] += _size[rb];
            return true;
        }

        public int SizeOf(int x) => _size[Find(x)];
    }

    // --- Dijkstra --------------------------------------------------------

    private static long[] Dijkstra(List<(int to, int w)>[] adj, int src, int n)
    {
        long[] dist = new long[n];
        Array.Fill(dist, long.MaxValue);
        dist[src] = 0;

        var pq = new PriorityQueue<int, long>();
        pq.Enqueue(src, 0);

        while (pq.TryDequeue(out int u, out long d))
        {
            if (d > dist[u]) continue;                        // lazy deletion

            foreach (var (v, w) in adj[u])
            {
                long nd = d + w;
                if (nd < dist[v]) { dist[v] = nd; pq.Enqueue(v, nd); }
            }
        }

        return dist;
    }

    // --- Simulation ------------------------------------------------------

    private static int[,] SpreadSimultaneous(int[,] grid)
    {
        int rows = grid.GetLength(0), cols = grid.GetLength(1);
        var next = (int[,])grid.Clone();                       // đọc grid CŨ, ghi vào next

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (grid[r, c] != 1) continue;
                for (int d = 0; d < 4; d++)
                {
                    int nr = r + Dr[d], nc = c + Dc[d];
                    if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;
                    next[nr, nc] = 1;
                }
            }

        return next;
    }

    private static int[,] SpreadInPlaceWrong(int[,] grid)
    {
        int rows = grid.GetLength(0), cols = grid.GetLength(1);
        var g = (int[,])grid.Clone();

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                if (g[r, c] != 1) continue;
                for (int d = 0; d < 4; d++)
                {
                    int nr = r + Dr[d], nc = c + Dc[d];
                    if (nr < 0 || nr >= rows || nc < 0 || nc >= cols) continue;
                    g[nr, nc] = 1;                             // ⚠ sửa ngay -> ô mới lại lan tiếp
                }
            }

        return g;
    }

    // =====================================================================
    // 05 — Bẫy
    // =====================================================================

    private static void Section05_Pitfalls()
    {
        Title("05 — Bẫy hay dính");

        Sub("Tràn int");
        int big = 2_000_000_000;
        int overflowed = big + big;                            // âm thầm quay vòng, KHÔNG ném
        P("int: 2_000_000_000 + 2_000_000_000", overflowed);
        P("long: (long)big + big", (long)big + big);

        int a = 100_000, b = 100_000;
        P("int a * b (tràn)", a * b);
        P("(long)a * b (ép TRƯỚC khi nhân)", (long)a * b);

        int[] arr = Enumerable.Repeat(1_000_000_000, 5).ToArray();
        int badSum = 0; foreach (int x in arr) badSum += x;
        long goodSum = 0; foreach (int x in arr) goodSum += x;
        P("Sum 5 phần tử 1e9 bằng int", badSum);
        P("Sum 5 phần tử 1e9 bằng long", goodSum);

        Sub("Số học");
        P("Math.Round(2.5)  (banker's rounding!)", Math.Round(2.5));
        P("Math.Round(2.5, AwayFromZero)", Math.Round(2.5, MidpointRounding.AwayFromZero));
        P("0.1 + 0.2 == 0.3", 0.1 + 0.2 == 0.3);
        P("Math.Abs(0.1 + 0.2 - 0.3) < 1e-9", Math.Abs(0.1 + 0.2 - 0.3) < 1e-9);
        P("(int)Math.Pow(10, 2)  (double -> sai số)", (int)Math.Pow(10, 2));

        Sub("Chỉ số / biên");
        int[,] m = { { 1, 2, 3 }, { 4, 5, 6 } };
        P("m.Length (⚠ tổng phần tử)", m.Length);
        P("m.GetLength(0) (số dòng)", m.GetLength(0));
        string s = "abcdefgh";
        P("đề cho [2,5] inclusive, Substring(2,5) SAI", s.Substring(2, 5));
        P("đúng: Substring(2, 5-2+1)", s.Substring(2, 5 - 2 + 1));

        Sub("Chia sẻ tham chiếu — bug âm thầm");
        int[][] bad = Enumerable.Repeat(new int[3], 2).ToArray();   // 2 dòng CÙNG 1 mảng
        bad[0][0] = 9;
        P("Repeat(new int[3], 2): sửa dòng 0 -> dòng 1 đổi theo", string.Join(" | ", bad.Select(Fmt)));

        int[][] good = Enumerable.Range(0, 2).Select(_ => new int[3]).ToArray();
        good[0][0] = 9;
        P("Range().Select(_ => new int[3]): đúng", string.Join(" | ", good.Select(Fmt)));

        Sub("HashSet so sánh theo tham chiếu hay giá trị");
        var byRef = new HashSet<int[]> { new[] { 1, 2 }, new[] { 1, 2 } };
        var byVal = new HashSet<(int, int)> { (1, 2), (1, 2) };
        P("HashSet<int[]>.Count (⚠ theo THAM CHIẾU)", byRef.Count);
        P("HashSet<(int,int)>.Count (theo GIÁ TRỊ)", byVal.Count);

        Sub("Collection rỗng / thứ tự");
        P("Array.Empty<int>().All(x => x > 100)", Array.Empty<int>().All(x => x > 100));
        P("List.Sort / Array.Sort", "KHÔNG ổn định — cần giữ thứ tự thì dùng OrderBy");
        P("Dictionary", "thứ tự duyệt KHÔNG đảm bảo — cần thứ tự thì SortedDictionary/OrderBy");
        P("foreach + Remove", "InvalidOperationException — dùng RemoveAll hoặc duyệt ngược");

        Sub("Hiệu năng (mốc: ~1e8 phép tính/giây)");
        P("s += x trong vòng lặp", "O(n^2) -> StringBuilder");
        P("list.Contains trong vòng lặp", "O(n^2) -> HashSet");
        P("list.RemoveAt(0)", "O(n) -> Queue.Dequeue");
        P("list.Min() mỗi vòng", "O(n^2) -> PriorityQueue");
        P("đệ quy sâu > ~1e4", "StackOverflow (không catch được) -> khử đệ quy bằng Stack");
    }

    // =====================================================================
    // 06 — Viết tắt
    // =====================================================================

    private static void Section06_Shorthand()
    {
        Title("06 — Cách viết tắt");

        Sub("Swap & deconstruct");
        int a = 1, b = 2;
        (a, b) = (b, a);                                       // thay 3 dòng có biến tmp
        P("(a, b) = (b, a)", $"a={a}, b={b}");

        int[] arr = { 10, 20, 30 };
        (arr[0], arr[2]) = (arr[2], arr[0]);
        P("swap phần tử mảng", Fmt(arr));

        var q = new Queue<(int r, int c, int d)>();
        q.Enqueue((1, 2, 3));
        var (r, c, d) = q.Dequeue();
        P("var (r, c, d) = q.Dequeue()", $"{r},{c},{d}");

        Sub("??= lazy init");
        var map = new Dictionary<string, List<int>>();
        void Add(string k, int v)
        {
            if (!map.TryGetValue(k, out var lst)) map[k] = lst = new List<int>();
            lst.Add(v);
        }
        Add("x", 1); Add("x", 2);
        P("TryGetValue + gán lồng", $"x -> {Fmt(map["x"])}");

        var buckets = new List<int>?[3];
        (buckets[1] ??= new List<int>()).Add(42);              // ??= : chỉ tạo khi đang null
        (buckets[1] ??= new List<int>()).Add(43);
        P("(buckets[1] ??= new()).Add(...)", Fmt(buckets[1]!));

        Sub("Ternary & null");
        int best = int.MaxValue;
        P("best == MaxValue ? -1 : best", best == int.MaxValue ? -1 : best);
        List<int>? maybeNull = null;
        P("maybeNull?.Count ?? 0", maybeNull?.Count ?? 0);

        Sub("Pattern matching");
        char ch = '[';
        P("ch is '(' or '[' or '{'", ch is '(' or '[' or '{');
        int x = 5;
        P("x is >= 1 and <= 9", x is >= 1 and <= 9);
        P("ch is not ' '", ch is not ' ');

        // `is not { } v` — null thì vào nhánh, ngược lại gán vào biến. Đúng mẫu dùng trong
        // bài "Important words to avoid spoilers": if (revealedAt[r] is not { } words) continue;
        var slots = new List<string>?[] { null, new List<string> { "aa", "bb" } };
        var collected = new List<string>();
        foreach (var slot in slots)
        {
            if (slot is not { } items) continue;               // null -> bỏ qua; ngược lại gán items
            collected.AddRange(items);
        }
        P("if (slot is not { } items) continue", Fmt(collected));

        Sub("switch expression");
        foreach (int dir in new[] { 0, 1, 2, 3 })
        {
            var (nr, nc) = dir switch
            {
                0 => (0 - 1, 0),
                1 => (0 + 1, 0),
                2 => (0, 0 - 1),
                _ => (0, 0 + 1)
            };
            Console.WriteLine($"  dir={dir} -> ({nr},{nc})");
        }
        P("phân loại ký tự", Classify('7') + ", " + Classify('k') + ", " + Classify(' '));

        Sub("Index ^ và Range ..");
        string s = "hello world";
        P("s[^1]", s[^1]);
        P("s[2..5]", s[2..5]);
        P("s[..^6]", s[..^6]);
        var lst = new List<int[]> { new[] { 1, 2 }, new[] { 3, 4 } };
        lst[^1][1] = 99;                                       // sửa phần tử cuối
        P("lst[^1][1] = 99", string.Join(" | ", lst.Select(Fmt)));

        Sub("Khởi tạo collection gọn");
        var dict = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        P("new Dictionary { [\"a\"] = 1 }", string.Join(", ", dict.Select(kv => $"{kv.Key}={kv.Value}")));
        int[] dr = { -1, 1, 0, 0 };                            // khỏi viết new int[]
        P("int[] dr = { -1, 1, 0, 0 }", Fmt(dr));

        // C# 12 (CHỈ .NET 8) — grader .NET 6 sẽ BÁO LỖI BIÊN DỊCH ở 3 dòng dưới
        int[] ce = [1, 2, 3];
        List<int> empty = [];
        int[] spread = [..ce, 4, 5];
        P("C# 12: [1,2,3] / [] / [..a, 4, 5]", $"{Fmt(ce)} / {Fmt(empty)} / {Fmt(spread)}");
        P("⚠", "collection expression là C# 12 — nộp lên grader .NET 6 phải đổi new List<int>()");

        Sub("Expression-bodied & local function");
        P("Gcd(48, 18) — expression body", Gcd(48, 18));
        int rows = 3, cols = 3;
        bool InBound(int rr, int cc) => rr >= 0 && rr < rows && cc >= 0 && cc < cols;
        P("local function InBound(1,2) / InBound(3,0)", $"{InBound(1, 2)} / {InBound(3, 0)}");
        P("vì sao nên dùng local function", "khỏi truyền tham số, tránh static field bẩn giữa test case");

        Sub("Vòng lặp gọn");
        var qq = new Queue<int>(new[] { 1, 2, 3 });
        var drained = new List<int>();
        while (qq.TryDequeue(out int v)) drained.Add(v);       // thay while (Count > 0) { Dequeue() }
        P("while (q.TryDequeue(out var v))", Fmt(drained));

        var pairs = new List<string>();
        for (int i = 0; i < 4; i++)
        for (int j = i + 1; j < 4; j++)                        // for lồng không cần { }
            pairs.Add($"{i}{j}");
        P("duyệt cặp j > i", Fmt(pairs));

        Sub("LINQ thay vòng lặp");
        int[] nums = { 5, -3, 9, 1 };
        P("nums.Count(x => x > 0)", nums.Count(x => x > 0));
        P("\"12345\".All(char.IsDigit)  (method group)", "12345".All(char.IsDigit));
        P("\"1 2 3\".Split(' ').Select(int.Parse)", Fmt("1 2 3".Split(' ').Select(int.Parse)));

        Sub("Khi nào ĐỪNG viết tắt");
        P("ternary lồng >= 3 tầng", "dùng switch expression");
        P("LINQ trong vòng lặp nóng", "O(n^2) âm thầm");
        P("arr[1..] trong vòng lặp", "mỗi lần tạo mảng MỚI -> O(n^2)");
        P("a[i++] trong biểu thức phức tạp", "dễ nhầm thứ tự đánh giá -> sai chỉ số");
    }

    private static string Classify(char c) => c switch
    {
        >= '0' and <= '9' => "digit",
        >= 'a' and <= 'z' => "lower",
        ' ' => "space",
        _ => "other"
    };
}
