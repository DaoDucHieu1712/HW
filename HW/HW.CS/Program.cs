using HW.CS.Demos;

// Đăng ký các topic demo. Mỗi topic ứng với 1 file interview/interview.NET.NN-*.md.
// Số ở đầu Title (01, 02, 11…) chính là MÃ TOPIC dùng cho tham số dòng lệnh.
// Thêm file mới thì thêm một dòng vào mảng dưới đây.
var topics = new IDemoTopic[]
{
    new Demo01_CSharpClr(),
    new Demo02_AsyncThreading(),
    new Demo11_RuntimeInternals(),
    new Demo12_MemoryGc(),
    new Demo13_AsyncThreadingInternals(),
    new Demo14_FrameworkInternals(),
    new Demo20_AgenticLoop(),
};

// Cho phép chạy nhanh không tương tác:  dotnet run -- 01 all   |   dotnet run -- 01 3
if (args.Length >= 1)
{
    RunFromArgs(args);
    return;
}

while (true)
{
    Console.WriteLine();
    Console.WriteLine("╔══════════════════════════════════════════════╗");
    Console.WriteLine("║   .NET Interview — Demo Runner                 ║");
    Console.WriteLine("╚══════════════════════════════════════════════╝");
    for (int i = 0; i < topics.Length; i++)
        Console.WriteLine($"  {i + 1}. {topics[i].Title}");
    Console.WriteLine("  0. Thoát");
    Console.Write("Chọn topic: ");

    var input = Console.ReadLine();
    if (input is null or "0") break;
    if (!int.TryParse(input, out int t) || t < 1 || t > topics.Length)
    {
        Console.WriteLine("Lựa chọn không hợp lệ.");
        continue;
    }

    RunTopicMenu(topics[t - 1]);
}

void RunTopicMenu(IDemoTopic topic)
{
    while (true)
    {
        Console.WriteLine();
        Console.WriteLine($"── {topic.Title} ──");
        for (int i = 0; i < topic.Items.Count; i++)
            Console.WriteLine($"  {i + 1,2}. {topic.Items[i].Title}");
        Console.WriteLine("   a. Chạy TẤT CẢ");
        Console.WriteLine("   0. Quay lại");
        Console.Write("Chọn câu: ");

        var input = Console.ReadLine();
        if (input is null or "0") return;

        if (input.Equals("a", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in topic.Items) RunOne(item);
            continue;
        }

        if (int.TryParse(input, out int q) && q >= 1 && q <= topic.Items.Count)
            RunOne(topic.Items[q - 1]);
        else
            Console.WriteLine("Lựa chọn không hợp lệ.");
    }
}

void RunOne(DemoItem item)
{
    Console.WriteLine();
    Console.WriteLine($"▶ {item.Title}");
    Console.WriteLine(new string('─', 50));
    try { item.Run(); }
    catch (Exception ex) { Console.WriteLine($"[demo ném exception: {ex.GetType().Name}: {ex.Message}]"); }
    Console.WriteLine();
}

void RunFromArgs(string[] a)
{
    var topic = FindTopic(a[0]);
    if (topic is null)
    {
        Console.WriteLine($"Topic không hợp lệ: {a[0]}");
        Console.WriteLine($"Topic có sẵn: {string.Join(", ", topics.Select(TopicCode))}");
        return;
    }

    if (a.Length < 2 || a[1].Equals("all", StringComparison.OrdinalIgnoreCase))
    {
        foreach (var item in topic.Items) RunOne(item);
        return;
    }

    if (int.TryParse(a[1], out int q) && q >= 1 && q <= topic.Items.Count)
        RunOne(topic.Items[q - 1]);
    else
        Console.WriteLine($"Câu không hợp lệ: {a[1]}");
}

// Mã topic = phần số ở đầu Title, ví dụ "11 — Runtime Internals…" ⇒ "11".
string TopicCode(IDemoTopic t) => t.Title.Split(' ', 2)[0];

// Chấp nhận cả MÃ topic ("11", "01") lẫn VỊ TRÍ trong menu ("1", "3").
IDemoTopic? FindTopic(string key)
{
    var byCode = topics.FirstOrDefault(t => TopicCode(t).Equals(key, StringComparison.OrdinalIgnoreCase));
    if (byCode is not null) return byCode;

    return int.TryParse(key, out int index) && index >= 1 && index <= topics.Length
        ? topics[index - 1]
        : null;
}
