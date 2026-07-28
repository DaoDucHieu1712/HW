using HW.CS.Demos;

// Đăng ký các topic demo. Mỗi topic ứng với 1 file interview.
// (Hiện có file 01; thêm file khác vào danh sách này khi triển khai tiếp.)
var topics = new IDemoTopic[]
{
    new Demo01_CSharpClr(),
    new Demo02_AsyncThreading(),
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
    if (!int.TryParse(a[0], out int t) || t < 1 || t > topics.Length)
    {
        Console.WriteLine($"Topic không hợp lệ: {a[0]}");
        return;
    }
    var topic = topics[t - 1];

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
