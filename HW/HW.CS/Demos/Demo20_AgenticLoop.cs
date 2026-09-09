using System.Text;

namespace HW.CS.Demos;

/// <summary>
/// Runnable demos cho mô hình "Loop Engineer" — kỹ sư vận hành một AI agent loop.
///
/// Toàn bộ file này KHÔNG gọi API thật. Phần "model" được thay bằng planner
/// tất định (scripted / rule-based) để output luôn giống nhau và bạn nhìn rõ
/// CƠ CHẾ của vòng lặp thay vì bị che bởi tính ngẫu nhiên của LLM.
///
/// Ánh xạ sang API thật (Claude Messages API):
///   Decision.Call      ≈ content block  type="tool_use"   (stop_reason="tool_use")
///   Observation        ≈ content block  type="tool_result"
///   Decision.IsFinal   ≈ stop_reason="end_turn"
///   history            ≈ mảng messages[] gửi lại TOÀN BỘ ở mỗi lượt
/// </summary>
public sealed class Demo20_AgenticLoop : IDemoTopic
{
    public string Title => "20 — Agentic Loop (Loop Engineer)";

    public IReadOnlyList<DemoItem> Items => new DemoItem[]
    {
        new("AL1.  Vòng lặp cốt lõi: plan → act → observe", AL1_CoreLoop),
        new("AL2.  Tool registry: mô tả tool CHÍNH LÀ API cho model", AL2_ToolRegistry),
        new("AL3.  Context phình theo O(n²) — cái giá của mỗi bước", AL3_ContextGrowth),
        new("AL4.  4 điều kiện dừng của một vòng lặp an toàn", AL4_StopConditions),
        new("AL5.  Budget guard: giới hạn bước & context", AL5_BudgetGuard),
        new("AL6.  Lỗi tool là DỮ LIỆU, không phải crash → agent tự sửa", AL6_SelfCorrection),
        new("AL7.  Loop detection: cắt vòng lặp chết", AL7_LoopDetection),
        new("AL8.  ReAct vs Plan-then-Execute", AL8_ReActVsPlanFirst),
        new("AL9.  Sub-agent: uỷ thác để giữ context cha nhỏ", AL9_SubAgentDelegation),
        new("AL10. Compaction: nén history khi chạm trần", AL10_Compaction),
        new("AL11. Approval gate + idempotency cho tool có side-effect", AL11_ApprovalAndIdempotency),
        new("AL12. Loop Engineer end-to-end: build → đọc lỗi → vá → build lại", AL12_LoopEngineerEndToEnd),
    };

    // ── AL1 ───────────────────────────────────────────────────────────────
    public static void AL1_CoreLoop()
    {
        Console.WriteLine("  Vòng lặp agentic rút gọn còn đúng 3 nhịp, lặp cho tới khi có điều kiện dừng:");
        Console.WriteLine("      while (!done) { decision = plan(history); obs = act(decision); history += obs; }");
        Console.WriteLine("  Model KHÔNG có trí nhớ giữa 2 lần gọi — 'trí nhớ' chính là history được gửi lại.");
        Console.WriteLine();

        var repo = Kit.BuildRepo();
        var tools = Kit.BuildTools(repo);

        var planner = new ScriptedPlanner(
            Decision.Act("Chưa biết repo có gì, liệt kê file trước đã.", "list_dir", "HW.CS"),
            Decision.Act("Program.cs là nơi đăng ký topic — đọc nó.", "read_file", "HW.CS/Program.cs"),
            Decision.Final("Đã đếm các dòng 'new DemoNN_...()' trong mảng topics.", "Program.cs đang đăng ký 6 topic."));

        var result = new AgentLoop(tools, planner).Run("Program.cs đang đăng ký bao nhiêu topic?");
        Kit.PrintResult(result);

        Console.WriteLine();
        Console.WriteLine("  Điểm mấu chốt: agent KHÔNG đoán câu trả lời ở bước 1. Nó lấy sự thật từ tool,");
        Console.WriteLine("  rồi mới kết luận. Bỏ nhịp 'observe' đi thì đó chỉ còn là một prompt đơn thuần.");
    }

    // ── AL2 ───────────────────────────────────────────────────────────────
    public static void AL2_ToolRegistry()
    {
        var repo = Kit.BuildRepo();
        var tools = Kit.BuildTools(repo);

        Console.WriteLine("  Model chỉ nhìn thấy tool qua phần MÔ TẢ. Đây là 'API contract' bạn gửi lên:");
        Console.WriteLine();
        Console.Write(tools.ToSchemaText());
        Console.WriteLine();
        Console.WriteLine("  ⇒ Mô tả mơ hồ = agent gọi sai tool. Đây là chỗ đáng đầu tư công sức nhất,");
        Console.WriteLine("    hơn hẳn việc tinh chỉnh system prompt.");
        Console.WriteLine();

        Console.WriteLine("  Gọi một tool KHÔNG tồn tại — registry trả observation, không ném exception:");
        var obs = tools.Invoke(new ToolCall("compile_and_deploy", "prod"));
        Console.WriteLine($"    ok={obs.Ok}");
        Console.WriteLine($"    {obs.Content}");
        Console.WriteLine();
        Console.WriteLine("  ⇒ Vì danh sách tool hợp lệ nằm ngay trong observation, agent tự chọn lại được.");
    }

    // ── AL3 ───────────────────────────────────────────────────────────────
    public static void AL3_ContextGrowth()
    {
        Console.WriteLine("  Mỗi bước lặp gửi lại TOÀN BỘ history ⇒ chi phí tích luỹ là O(n²), không phải O(n).");
        Console.WriteLine();

        var repo = Kit.BuildRepo();
        var tools = Kit.BuildTools(repo);
        var planner = new ScriptedPlanner(
            Decision.Act("Xem repo có gì.", "list_dir", "HW.CS"),
            Decision.Act("Đọc Program.cs.", "read_file", "HW.CS/Program.cs"),
            Decision.Act("Đọc file demo lớn nhất.", "read_file", "HW.CS/Demos/Demo01_CSharpClr.cs"),
            Decision.Act("Đọc luôn tài liệu interview.", "read_file", "HW.CS/interview/interview.NET.03-Collections-LINQ.md"),
            Decision.Final("Đã đủ dữ liệu.", "xong"));

        var loop = new AgentLoop(tools, planner, trace: false);
        var result = loop.Run("Khảo sát repo");

        Console.WriteLine($"  {"bước",-6}{"tool",-14}{"context tại bước",-20}{"đã gửi luỹ kế"}");
        Console.WriteLine("  " + new string('─', 62));
        int cumulative = 0;
        foreach (var (turn, tokens) in loop.ContextTimeline())
        {
            cumulative += tokens;
            string tool = turn.Decision.Call?.Tool ?? "(final)";
            Console.WriteLine($"  {turn.Step,-6}{tool,-14}{tokens,-20}{cumulative}");
        }
        Console.WriteLine();
        Console.WriteLine($"  Context cuối: {result.ContextTokens} token — nhưng tổng đã GỬI là {cumulative} token.");
        Console.WriteLine("  ⇒ Một tool trả về 50k token ở bước 2 sẽ bị trả tiền lại ở mọi bước sau đó.");
        Console.WriteLine("    Đó là lý do tool phải trả về BẢN TÓM TẮT, không phải dump thô (xem AL9, AL10).");
    }

    // ── AL4 ───────────────────────────────────────────────────────────────
    public static void AL4_StopConditions()
    {
        Console.WriteLine("  Một vòng lặp thiếu điều kiện dừng là một hoá đơn không giới hạn. Bốn điều kiện tối thiểu:");
        Console.WriteLine();

        var tools = Kit.BuildTools(Kit.BuildRepo());

        // 1. Agent tự tuyên bố xong.
        var r1 = new AgentLoop(tools,
            new ScriptedPlanner(
                Decision.Act("Đọc file.", "read_file", "HW.CS/Program.cs"),
                Decision.Final("Đủ thông tin rồi.", "6 topic")),
            trace: false).Run("goal");

        // 2. Hết ngân sách bước.
        var r2 = new AgentLoop(tools,
            new LambdaPlanner((_, _) => Decision.Act("Tìm tiếp...", "list_dir", "HW.CS")),
            new LoopBudget(MaxSteps: 3, RepeatLimit: 99),
            trace: false).Run("goal");

        // 3. Lặp lại cùng một lời gọi.
        var r3 = new AgentLoop(tools,
            new LambdaPlanner((_, _) => Decision.Act("Thử lại lần nữa...", "read_file", "HW.CS/khong-ton-tai.cs")),
            new LoopBudget(MaxSteps: 20, RepeatLimit: 2),
            trace: false).Run("goal");

        // 4. Context vượt trần.
        var r4 = new AgentLoop(tools,
            new LambdaPlanner((_, h) => Decision.Act("Đọc file to.", "read_file",
                h.Count % 2 == 0
                    ? "HW.CS/Demos/Demo01_CSharpClr.cs"
                    : "HW.CS/interview/interview.NET.03-Collections-LINQ.md")),
            new LoopBudget(MaxSteps: 50, MaxContextTokens: 300, RepeatLimit: 99),
            trace: false).Run("goal");

        Console.WriteLine($"  {"tình huống",-30}{"bước",-8}{"lý do dừng"}");
        Console.WriteLine("  " + new string('─', 78));
        foreach (var (name, r) in new[]
        {
            ("agent nói xong", r1),
            ("planner không bao giờ xong", r2),
            ("planner kẹt một chỗ", r3),
            ("tool trả về quá nhiều", r4),
        })
            Console.WriteLine($"  {name,-30}{r.Steps,-8}{r.StopReason}");

        Console.WriteLine();
        Console.WriteLine("  ⇒ Chỉ điều kiện #1 là 'thành công'. Ba cái còn lại là cầu chì — và trong thực tế");
        Console.WriteLine("    chúng nổ nhiều hơn bạn tưởng, nên đừng coi chúng là trường hợp hiếm.");
    }

    // ── AL5 ───────────────────────────────────────────────────────────────
    public static void AL5_BudgetGuard()
    {
        Console.WriteLine("  Budget không chỉ để chặn chi phí — nó biến agent thành hệ thống có ràng buộc thời gian.");
        Console.WriteLine();

        var tools = Kit.BuildTools(Kit.BuildRepo());

        // Planner "cần cù" nhưng không bao giờ kết luận — kịch bản hay gặp nhất.
        var planner = new LambdaPlanner((_, h) =>
            Decision.Act($"Chắc còn thiếu gì đó, xem thêm lần {h.Count + 1}.", "list_dir", "HW.CS"));

        Console.WriteLine("  Ngân sách 4 bước, RepeatLimit tắt:");
        var result = new AgentLoop(tools, planner, new LoopBudget(MaxSteps: 4, RepeatLimit: 99)).Run("Tìm bug");
        Kit.PrintResult(result);

        Console.WriteLine();
        Console.WriteLine("  Khi hết budget, đừng trả về rỗng — hãy trả về công việc dở dang:");
        Console.WriteLine($"    đã thu thập {result.History.Count(t => t.Observation is not null)} observation,");
        Console.WriteLine("    bàn giao lại cho người dùng (hoặc cho lượt lặp sau) thay vì vứt đi.");
    }

    // ── AL6 ───────────────────────────────────────────────────────────────
    public static void AL6_SelfCorrection()
    {
        Console.WriteLine("  Tool ném exception ⇒ vòng lặp chết. Tool TRẢ VỀ lỗi ⇒ vòng lặp học được.");
        Console.WriteLine("  Bí quyết: thông điệp lỗi phải chứa đủ dữ kiện để bước sau sửa được.");
        Console.WriteLine();

        var tools = Kit.BuildTools(Kit.BuildRepo());

        var planner = new LambdaPlanner((_, h) =>
        {
            string last = Kit.LastObservation(h);
            if (h.Count == 0)
                return Decision.Act("Đoán đường dẫn theo thói quen.", "read_file", "Demos/Demo01.cs");
            if (last.Contains("not found"))
                return Decision.Act("Đường dẫn sai. Không đoán nữa — liệt kê thư mục thật.", "list_dir", "HW.CS/Demos");
            if (last.Contains("Demo01_CSharpClr.cs"))
                return Decision.Act("Thấy tên thật rồi, đọc lại đúng đường dẫn.", "read_file", "HW.CS/Demos/Demo01_CSharpClr.cs");
            return Decision.Final("Đã đọc được file.", "Demo01_CSharpClr.cs implement IDemoTopic.");
        });

        var result = new AgentLoop(tools, planner).Run("Đọc file Demo01");
        Kit.PrintResult(result);

        Console.WriteLine();
        Console.WriteLine("  So sánh hai kiểu thông điệp lỗi cho cùng một sự cố:");
        Console.WriteLine("    ✗  \"File not found\"                      → agent chỉ còn cách đoán lại");
        Console.WriteLine("    ✓  \"not found. Files: HW.CS/...\"         → agent có đường đi tiếp ngay");
    }

    // ── AL7 ───────────────────────────────────────────────────────────────
    public static void AL7_LoopDetection()
    {
        Console.WriteLine("  Chế độ hỏng phổ biến nhất không phải agent làm SAI — mà là agent làm ĐI LÀM LẠI");
        Console.WriteLine("  đúng một lời gọi, vì observation không đổi nên 'suy nghĩ' cũng không đổi.");
        Console.WriteLine();

        var tools = Kit.BuildTools(Kit.BuildRepo());

        var stuck = new LambdaPlanner((_, _) =>
            Decision.Act("Chắc lần này grep sẽ ra...", "grep", "TODO_khong_ton_tai"));

        var result = new AgentLoop(tools, stuck, new LoopBudget(MaxSteps: 20, RepeatLimit: 2)).Run("Tìm TODO");
        Kit.PrintResult(result);

        Console.WriteLine();
        Console.WriteLine("  Detector ở đây chỉ là một Dictionary<toolCall, count> — vài dòng code,");
        Console.WriteLine($"  nhưng nó cắt {20 - result.Steps} bước lãng phí trong ví dụ này (20 → {result.Steps}).");
        Console.WriteLine("  Nâng cấp thực tế: hash cả (tool, args, observation) để bắt cả vòng lặp 2-3 nhịp.");
    }

    // ── AL8 ───────────────────────────────────────────────────────────────
    public static void AL8_ReActVsPlanFirst()
    {
        Console.WriteLine("  [A] Plan-then-Execute: lập kế hoạch ĐẦY ĐỦ ở bước 0, rồi thi hành mù.");
        Console.WriteLine();

        var toolsA = Kit.BuildTools(Kit.BuildRepo());
        var planFirst = new ScriptedPlanner(
            Decision.Act("Kế hoạch bước 1/3: đọc Demo03.", "read_file", "HW.CS/Demos/Demo03_Collections.cs"),
            Decision.Act("Kế hoạch bước 2/3: vá Demo03.", "patch_file", "add-namespace"),
            Decision.Act("Kế hoạch bước 3/3: build.", "run_build"),
            Decision.Final("Đã thi hành xong kế hoạch.", "Hoàn thành 3/3 bước."));
        Kit.PrintResult(new AgentLoop(toolsA, planFirst).Run("Làm HW.CS build xanh"));

        Console.WriteLine("  ⇒ Bước 1 báo file KHÔNG tồn tại, nhưng kế hoạch vẫn chạy tiếp bước 2, 3 rồi");
        Console.WriteLine("    kết luận 'hoàn thành'. Kế hoạch cứng không đọc được observation.");

        Console.WriteLine();
        Console.WriteLine("  [B] ReAct: quyết định lại sau MỖI observation.");
        Console.WriteLine();

        var toolsB = Kit.BuildTools(Kit.BuildRepo());
        var react = new LambdaPlanner((_, h) =>
        {
            string last = Kit.LastObservation(h);
            if (h.Count == 0)
                return Decision.Act("Kế hoạch ban đầu: đọc Demo03.", "read_file", "HW.CS/Demos/Demo03_Collections.cs");
            if (last.Contains("not found"))
                return Decision.Act("File không tồn tại ⇒ giả định sai. Hỏi lại thực địa bằng build.", "run_build");
            return Decision.Final("Build đã cho biết sự thật.", $"Trạng thái thật: {Kit.Clip(last, 55)}");
        });
        Kit.PrintResult(new AgentLoop(toolsB, react).Run("Làm HW.CS build xanh"));

        Console.WriteLine();
        Console.WriteLine("  ⇒ Plan-first rẻ hơn và dễ audit, nhưng chỉ đúng khi thế giới đúng như bạn tưởng.");
        Console.WriteLine("    Sửa lỗi trong codebase gần như không bao giờ rơi vào trường hợp đó.");
    }

    // ── AL9 ───────────────────────────────────────────────────────────────
    public static void AL9_SubAgentDelegation()
    {
        Console.WriteLine("  Vấn đề: đọc thẳng 3 file thì nội dung thô nằm lại trong context cha mãi mãi.");
        Console.WriteLine("  Giải pháp: mỗi file giao cho một sub-agent; agent cha chỉ nhận về 1 dòng tóm tắt.");
        Console.WriteLine();

        var repo = Kit.BuildRepo();
        var childTools = Kit.BuildTools(repo);

        string[] targets =
        {
            "HW.CS/Program.cs",
            "HW.CS/Demos/Demo01_CSharpClr.cs",
            "HW.CS/interview/interview.NET.03-Collections-LINQ.md",
        };

        int inlineTokens = targets.Sum(p => Kit.EstimateTokens(repo.Read(p)));

        // Tool 'delegate' chạy trọn một vòng lặp con rồi CHỈ trả về kết luận.
        var parentTools = new ToolRegistry()
            .Register(new ToolSpec("delegate",
                "Giao 1 file cho sub-agent đọc; trả về đúng 1 dòng tóm tắt.",
                "<path>", false,
                path =>
                {
                    var child = new AgentLoop(childTools,
                        new ScriptedPlanner(
                            Decision.Act($"Đọc {path}.", "read_file", path),
                            Decision.Final("Đã đọc xong, rút gọn lại.", Kit.Summarize(repo.Read(path)))),
                        trace: false);
                    var r = child.Run($"Tóm tắt {path}");
                    return new Observation(true, $"[sub-agent: {r.Steps} bước, {r.ContextTokens} token nội bộ] {r.Answer}");
                }));

        int index = 0;
        var parent = new AgentLoop(parentTools,
            new LambdaPlanner((_, _) => index < targets.Length
                ? Decision.Act($"Uỷ thác file {index + 1}/{targets.Length}.", "delegate", targets[index++])
                : Decision.Final("Đã có đủ 3 bản tóm tắt.", "Khảo sát xong 3 file.")));

        var result = parent.Run("Khảo sát 3 file quan trọng của HW.CS");
        Kit.PrintResult(result);

        Console.WriteLine();
        Console.WriteLine($"  Nếu đọc thẳng trong context cha : ~{inlineTokens} token");
        Console.WriteLine($"  Uỷ thác cho sub-agent           : ~{result.ContextTokens} token");
        Console.WriteLine($"  Tiết kiệm                       : {100 - result.ContextTokens * 100 / Math.Max(1, inlineTokens)}%");
        Console.WriteLine();
        Console.WriteLine("  Cái giá phải trả: sub-agent khởi động từ context RỖNG. Nó không biết những gì");
        Console.WriteLine("  agent cha đã biết ⇒ prompt giao việc phải tự mang đủ ngữ cảnh, nếu không sẽ");
        Console.WriteLine("  tốn nhiều token hơn là đọc thẳng.");
    }

    // ── AL10 ──────────────────────────────────────────────────────────────
    public static void AL10_Compaction()
    {
        Console.WriteLine("  Khi history chạm trần, đừng cắt cụt (mất mục tiêu) — hãy NÉN:");
        Console.WriteLine("  giữ nguyên goal + N lượt gần nhất, các lượt cũ gộp thành một bản tóm tắt.");
        Console.WriteLine();

        var tools = Kit.BuildTools(Kit.BuildRepo());
        var planner = new ScriptedPlanner(
            Decision.Act("Liệt kê.", "list_dir", "HW.CS"),
            Decision.Act("Đọc Program.cs.", "read_file", "HW.CS/Program.cs"),
            Decision.Act("Đọc Demo01.", "read_file", "HW.CS/Demos/Demo01_CSharpClr.cs"),
            Decision.Act("Đọc tài liệu.", "read_file", "HW.CS/interview/interview.NET.03-Collections-LINQ.md"),
            Decision.Act("Grep IDemoTopic.", "grep", "IDemoTopic"),
            Decision.Final("Đủ rồi.", "xong"));

        const string goal = "Khảo sát repo";
        var loop = new AgentLoop(tools, planner, trace: false);
        loop.Run(goal);

        int before = loop.ContextTokens(goal);
        string compacted = loop.Compact(goal, keepLastTurns: 2);
        int after = Kit.EstimateTokens(compacted);

        Console.WriteLine($"  Trước nén : {before} token ({loop.History.Count} lượt đầy đủ)");
        Console.WriteLine($"  Sau nén   : {after} token (goal + tóm tắt + 2 lượt cuối)");
        Console.WriteLine();
        Console.WriteLine("  Nội dung sau khi nén:");
        foreach (var line in compacted.Split('\n'))
            Console.WriteLine($"    {line}");
        Console.WriteLine();
        Console.WriteLine("  ⇒ Ba thứ KHÔNG bao giờ được nén mất: mục tiêu gốc, ràng buộc do người dùng đặt ra,");
        Console.WriteLine("    và những gì agent ĐÃ THAY ĐỔI (để không làm lại lần hai).");
    }

    // ── AL11 ──────────────────────────────────────────────────────────────
    public static void AL11_ApprovalAndIdempotency()
    {
        Console.WriteLine("  Tool chia làm hai hạng: đọc (hoàn tác được) và ghi (không hoàn tác được).");
        Console.WriteLine("  Chỉ hạng thứ hai cần cổng phê duyệt — chặn cả hai thì agent thành vô dụng.");
        Console.WriteLine();

        var tools = Kit.BuildTools(Kit.BuildRepo());

        // Cổng phê duyệt: từ chối mọi tool có side-effect.
        bool Approve(ToolCall call)
        {
            bool ok = !tools.HasSideEffect(call.Tool);
            Console.WriteLine($"       gate : {call.Tool} → {(ok ? "cho phép (read-only)" : "TỪ CHỐI (side-effect)")}");
            return ok;
        }

        var planner = new LambdaPlanner((_, h) =>
        {
            string last = Kit.LastObservation(h);
            if (h.Count == 0) return Decision.Act("Xoá file thừa cho gọn.", "delete_file", "HW.CS/Program.cs");
            if (last.Contains("từ chối")) return Decision.Act("Bị chặn — chuyển sang cách chỉ-đọc.", "grep", "IDemoTopic");
            return Decision.Final("Không xoá được thì báo cáo lại thôi.", "Đề xuất (CHƯA thực hiện): xem lại Program.cs.");
        });

        Kit.PrintResult(new AgentLoop(tools, planner, approve: Approve).Run("Dọn dẹp repo"));

        Console.WriteLine();
        Console.WriteLine("  Idempotency — giao hàng at-least-once nghĩa là tool GHI có thể chạy hai lần:");
        var t2 = Kit.BuildTools(Kit.BuildBrokenRepo());
        Console.WriteLine($"    lần 1: {t2.Invoke(new ToolCall("patch_file", "add-namespace")).Content}");
        Console.WriteLine($"    lần 2: {t2.Invoke(new ToolCall("patch_file", "add-namespace")).Content}");
        Console.WriteLine();
        Console.WriteLine("  ⇒ Cùng một patch key ⇒ lần hai là no-op. Không có khoá này, một lần retry mạng");
        Console.WriteLine("    sẽ chèn namespace hai lần và tạo ra lỗi build mới do chính agent gây ra.");
    }

    // ── AL12 ──────────────────────────────────────────────────────────────
    public static void AL12_LoopEngineerEndToEnd()
    {
        Console.WriteLine("  Đây là hình dạng thật của công việc 'Loop Engineer': agent KHÔNG được cho biết");
        Console.WriteLine("  bug nằm ở đâu. Nó chỉ có một mục tiêu kiểm chứng được — build phải xanh — và");
        Console.WriteLine("  các tool để tự tìm ra. Mỗi vòng lặp: hỏi compiler → vá → hỏi lại.");
        Console.WriteLine();

        var repo = Kit.BuildBrokenRepo();
        var tools = Kit.BuildTools(repo);

        var planner = new LambdaPlanner((_, h) =>
        {
            string last = Kit.LastObservation(h);

            if (h.Count == 0)
                return Decision.Act("Chưa biết gì — chạy build để lấy tín hiệu đầu tiên.", "run_build");

            if (last.Contains("CS0246"))
                return Decision.Act("CS0246 ở IDemoTopic ⇒ file thiếu khai báo namespace. Vá.",
                                    "patch_file", "add-namespace");

            if (last.Contains("CS0103"))
                return Decision.Act("CS0103 'dict' ⇒ biến chưa khai báo. Vá.",
                                    "patch_file", "declare-dict");

            if (last.Contains("patched") || last.Contains("no-op"))
                return Decision.Act("Đã vá — build lại để XÁC NHẬN, không tự cho là xong.", "run_build");

            if (last.Contains("Build succeeded"))
                return Decision.Final("Build xanh ⇒ mục tiêu đã được kiểm chứng, dừng.",
                                      "HW.CS build thành công sau 2 lần vá (CS0246, CS0103).");

            return Decision.Final("Không diễn giải được output build.", "dừng an toàn: cần người xem lại.");
        });

        var result = new AgentLoop(tools, planner, new LoopBudget(MaxSteps: 10, RepeatLimit: 3))
            .Run("Làm cho HW.CS build xanh");
        Kit.PrintResult(result);

        Console.WriteLine();
        Console.WriteLine("  Ba tính chất khiến vòng lặp này chạy được — thiếu một cái là hỏng:");
        Console.WriteLine("    1. Mục tiêu KIỂM CHỨNG ĐƯỢC bằng máy (exit code của build), không phải 'code đẹp hơn'.");
        Console.WriteLine("    2. Tín hiệu GIÀU thông tin (mã lỗi + file + dòng) đủ để quyết định bước kế tiếp.");
        Console.WriteLine("    3. Agent luôn build LẠI sau khi vá — nguồn sự thật là compiler, không phải niềm tin.");
        Console.WriteLine();
        Console.WriteLine("  Đổi run_build thành `dotnet build` thật và patch_file thành ghi file thật là bạn");
        Console.WriteLine("  có đúng vòng lặp mà Claude Code đang chạy.");
    }

}

// ── Hạ tầng dùng chung cho các demo ───────────────────────────────────────
// Phải là `file static class`: chỉ kiểu file-local mới được đặt kiểu file-local
// (LoopResult, FakeRepo, ToolRegistry…) trong chữ ký thành viên — CS9051.
file static class Kit
{
    public static void PrintResult(LoopResult r)
    {
        Console.WriteLine();
        Console.WriteLine($"  ── dừng: {r.StopReason} | {r.Steps} bước | context ~{r.ContextTokens} token");
        if (r.Answer is not null) Console.WriteLine($"  ── kết quả: {r.Answer}");
    }

    public static string LastObservation(IReadOnlyList<Turn> h)
        => h.Count == 0 ? "" : h[^1].Observation?.Content ?? "";

    public static string Summarize(string content)
    {
        var first = content.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "(rỗng)";
        return $"{content.Split('\n').Length} dòng, mở đầu: {Kit.Clip(first, 45)}";
    }

    public static string Clip(string s, int max)
    {
        s = s.Replace("\r", "").Replace("\n", " ⏎ ");
        return s.Length <= max ? s : s[..max] + "…";
    }

    public static int EstimateTokens(string s) => s.Length / 4;

    public static FakeRepo BuildRepo() => new FakeRepo()
        .Add("HW.CS/Program.cs", """
            using HW.CS.Demos;

            var topics = new IDemoTopic[]
            {
                new Demo01_CSharpClr(),
                new Demo02_AsyncThreading(),
                new Demo11_RuntimeInternals(),
                new Demo12_MemoryGc(),
                new Demo13_AsyncThreadingInternals(),
                new Demo14_FrameworkInternals(),
            };
            """)
        .Add("HW.CS/Demos/Demo01_CSharpClr.cs", """
            namespace HW.CS.Demos;

            public sealed class Demo01_CSharpClr : IDemoTopic
            {
                public string Title => "01 — C# Ngôn ngữ & CLR";
                public IReadOnlyList<DemoItem> Items => new DemoItem[]
                {
                    new("Q1.  Value type vs reference type", Q1_ValueVsReference),
                    new("Q4.  Boxing / unboxing", Q4_BoxingUnboxing),
                    // ... 38 câu khác, mỗi câu là một method static in ra output.
                };
            }
            """)
        .Add("HW.CS/interview/interview.NET.03-Collections-LINQ.md", """
            # 03 — Collections & LINQ

            ## Q1. List<T> vs Array
            List<T> bọc một mảng và tự nhân đôi capacity khi đầy...

            ## Q2. Dictionary<K,V> hoạt động thế nào
            Bucket + chaining, GetHashCode chọn bucket, Equals so khớp trong bucket...

            ## Q3. Deferred execution của LINQ
            Where/Select chỉ dựng iterator; chưa chạy cho tới khi enumerate...
            """);

    /// <summary>Repo cố tình có hai lỗi biên dịch để AL12 sửa dần.</summary>
    public static FakeRepo BuildBrokenRepo() => new FakeRepo()
        .Add("HW.CS/Demos/Demo03_Collections.cs", """
            public sealed class Demo03_Collections : IDemoTopic
            {
                public string Title => "03 — Collections & LINQ";
                public IReadOnlyList<DemoItem> Items => new DemoItem[]
                {
                    new("Q2. Dictionary bucket", Q2_Dictionary),
                };

                static void Q2_Dictionary() => Console.WriteLine(dict.Count);
            }
            """);

    public static ToolRegistry BuildTools(FakeRepo repo) => new ToolRegistry()
        .Register(new ToolSpec("list_dir",
            "Liệt kê đường dẫn file bắt đầu bằng prefix. Dùng khi CHƯA chắc file nằm ở đâu.",
            "<prefix>", false,
            prefix =>
            {
                var hits = repo.Paths.Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                return hits.Count == 0
                    ? new Observation(false, $"không có file nào dưới '{prefix}'. Prefix hợp lệ: HW.CS/")
                    : new Observation(true, string.Join(", ", hits));
            }))
        .Register(new ToolSpec("read_file",
            "Đọc toàn bộ nội dung một file theo đường dẫn CHÍNH XÁC (dùng list_dir nếu chưa chắc).",
            "<path>", false,
            path => repo.TryRead(path, out var content)
                ? new Observation(true, content)
                : new Observation(false, $"'{path}' not found. Files: {string.Join(", ", repo.Paths)}")))
        .Register(new ToolSpec("grep",
            "Tìm chuỗi con trong mọi file; trả về danh sách 'path:line'.",
            "<pattern>", false,
            pattern =>
            {
                var hits = repo.Grep(pattern).ToList();
                return hits.Count == 0
                    ? new Observation(false, $"không khớp '{pattern}' trong {repo.Paths.Count()} file")
                    : new Observation(true, string.Join(" | ", hits.Take(5)));
            }))
        .Register(new ToolSpec("run_build",
            "Biên dịch HW.CS. Trả về lỗi ĐẦU TIÊN kèm mã lỗi + file + dòng, hoặc 'Build succeeded'.",
            "(không tham số)", false,
            _ => new Observation(true, repo.Build())))
        .Register(new ToolSpec("patch_file",
            "Áp dụng một bản vá đã biết theo id. Idempotent: cùng id chạy lần hai là no-op.",
            "add-namespace | declare-dict", true,
            id => new Observation(true, repo.Patch(id))))
        .Register(new ToolSpec("delete_file",
            "Xoá vĩnh viễn một file. KHÔNG hoàn tác được.",
            "<path>", true,
            path => new Observation(true, $"đã xoá {path}")));
}

// ── Kiểu dữ liệu cốt lõi ──────────────────────────────────────────────────

/// <summary>Một lời gọi tool ≈ content block type="tool_use" trong Messages API.</summary>
file sealed record ToolCall(string Tool, string Args = "")
{
    public string Key => Args.Length == 0 ? $"{Tool}()" : $"{Tool}({Args})";
    public override string ToString() => Key;
}

/// <summary>Kết quả trả về cho model ≈ content block type="tool_result".</summary>
file sealed record Observation(bool Ok, string Content)
{
    public override string ToString() => Ok ? Content : $"ERROR: {Content}";
}

/// <summary>Một lượt suy nghĩ của model: hoặc gọi tool, hoặc chốt câu trả lời.</summary>
file sealed record Decision(string Thought, ToolCall? Call, string? FinalAnswer)
{
    public bool IsFinal => FinalAnswer is not null;

    public static Decision Act(string thought, string tool, string args = "")
        => new(thought, new ToolCall(tool, args), null);

    public static Decision Final(string thought, string answer)
        => new(thought, null, answer);
}

file sealed record Turn(int Step, Decision Decision, Observation? Observation);

file sealed record LoopBudget(int MaxSteps = 8, int MaxContextTokens = 100_000, int RepeatLimit = 2);

file sealed record LoopResult(
    string StopReason,
    string? Answer,
    int Steps,
    int ContextTokens,
    IReadOnlyList<Turn> History);

// ── Planner (chỗ mà LLM thật sẽ ngồi) ─────────────────────────────────────

file interface IPlanner
{
    Decision Next(string goal, IReadOnlyList<Turn> history);
}

/// <summary>Kịch bản cố định — dùng để minh hoạ đúng một luồng.</summary>
file sealed class ScriptedPlanner(params Decision[] script) : IPlanner
{
    private int _index;

    public Decision Next(string goal, IReadOnlyList<Turn> history)
        => _index < script.Length
            ? script[_index++]
            : Decision.Final("Hết kịch bản.", "(không có kết luận)");
}

/// <summary>Quyết định dựa trên history — mô phỏng hành vi ReAct một cách tất định.</summary>
file sealed class LambdaPlanner(Func<string, IReadOnlyList<Turn>, Decision> decide) : IPlanner
{
    public Decision Next(string goal, IReadOnlyList<Turn> history) => decide(goal, history);
}

// ── Tool registry ─────────────────────────────────────────────────────────

file sealed record ToolSpec(
    string Name,
    string Description,
    string ArgsHint,
    bool HasSideEffect,
    Func<string, Observation> Invoke);

file sealed class ToolRegistry
{
    private readonly Dictionary<string, ToolSpec> _tools = new(StringComparer.OrdinalIgnoreCase);

    public ToolRegistry Register(ToolSpec spec)
    {
        _tools[spec.Name] = spec;
        return this;
    }

    public bool HasSideEffect(string tool) => _tools.TryGetValue(tool, out var spec) && spec.HasSideEffect;

    /// <summary>
    /// Không bao giờ ném ra ngoài: mọi thất bại đều thành Observation để vòng lặp còn cơ hội sửa.
    /// </summary>
    public Observation Invoke(ToolCall call)
    {
        if (!_tools.TryGetValue(call.Tool, out var spec))
            return new Observation(false,
                $"unknown tool '{call.Tool}'. Tool hợp lệ: {string.Join(", ", _tools.Keys)}");

        try
        {
            return spec.Invoke(call.Args);
        }
        catch (Exception ex)
        {
            return new Observation(false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public string ToSchemaText()
    {
        var sb = new StringBuilder();
        foreach (var t in _tools.Values)
        {
            sb.AppendLine($"    - {t.Name}({t.ArgsHint}){(t.HasSideEffect ? "   [side-effect]" : "")}");
            sb.AppendLine($"        {t.Description}");
        }
        return sb.ToString();
    }
}

// ── Vòng lặp ──────────────────────────────────────────────────────────────

file sealed class AgentLoop(
    ToolRegistry tools,
    IPlanner planner,
    LoopBudget? budget = null,
    bool trace = true,
    Func<ToolCall, bool>? approve = null)
{
    private readonly LoopBudget _budget = budget ?? new LoopBudget();
    private readonly List<Turn> _history = new();

    public IReadOnlyList<Turn> History => _history;

    public LoopResult Run(string goal)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int step = 0;

        while (true)
        {
            // ── Cầu chì 1: ngân sách bước.
            if (step >= _budget.MaxSteps)
                return Stop("hết ngân sách bước", null, step, goal);

            // ── Cầu chì 2: context vượt trần.
            int ctx = ContextTokens(goal);
            if (ctx > _budget.MaxContextTokens)
                return Stop($"context vượt trần ({ctx} > {_budget.MaxContextTokens} token)", null, step, goal);

            // ── PLAN: model nhìn goal + TOÀN BỘ history rồi quyết định bước kế tiếp.
            var decision = planner.Next(goal, _history);
            step++;

            if (decision.IsFinal)
            {
                if (trace) Console.WriteLine($"  [{step}] think: {decision.Thought}");
                _history.Add(new Turn(step, decision, null));
                return Stop("agent tự kết thúc (end_turn)", decision.FinalAnswer, step, goal);
            }

            var call = decision.Call!;

            // ── Cầu chì 3: lặp lại đúng một lời gọi.
            seen[call.Key] = seen.GetValueOrDefault(call.Key) + 1;
            if (seen[call.Key] > _budget.RepeatLimit)
                return Stop($"phát hiện lặp — {call.Key} gọi {seen[call.Key]} lần", null, step, goal);

            if (trace)
            {
                Console.WriteLine($"  [{step}] think: {decision.Thought}");
                Console.WriteLine($"       tool : {call.Key}");
            }

            // ── ACT (qua cổng phê duyệt nếu có).
            var obs = approve is not null && !approve(call)
                ? new Observation(false, "bị từ chối bởi approval gate — hãy chọn cách khác")
                : tools.Invoke(call);

            if (trace)
                Console.WriteLine($"       obs  : {Kit.Clip(obs.ToString(), 95)}");

            // ── OBSERVE: nối vào history — vòng sau planner sẽ nhìn thấy.
            _history.Add(new Turn(step, decision, obs));
        }
    }

    private LoopResult Stop(string reason, string? answer, int steps, string goal)
        => new(reason, answer, steps, ContextTokens(goal), _history);

    public int ContextTokens(string goal)
        => Kit.EstimateTokens(goal) + _history.Sum(TurnTokens);

    private static int TurnTokens(Turn t)
        => Kit.EstimateTokens(t.Decision.Thought)
         + Kit.EstimateTokens(t.Decision.Call?.Key ?? "")
         + Kit.EstimateTokens(t.Observation?.Content ?? "");

    /// <summary>Kích thước context TẠI mỗi bước — để thấy nó phình ra thế nào.</summary>
    public IEnumerable<(Turn Turn, int Tokens)> ContextTimeline()
    {
        int running = 0;
        foreach (var t in _history)
        {
            running += TurnTokens(t);
            yield return (t, running);
        }
    }

    /// <summary>Nén history: giữ goal + N lượt cuối, phần còn lại gộp thành một dòng.</summary>
    public string Compact(string goal, int keepLastTurns)
    {
        var old = _history.Take(Math.Max(0, _history.Count - keepLastTurns)).ToList();
        var kept = _history.Skip(old.Count).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"GOAL (không bao giờ nén): {goal}");
        if (old.Count > 0)
        {
            var toolsUsed = old.Where(t => t.Decision.Call is not null)
                               .Select(t => t.Decision.Call!.Tool)
                               .Distinct();
            sb.AppendLine($"TÓM TẮT {old.Count} lượt đầu: đã gọi {string.Join(", ", toolsUsed)}; "
                        + $"{old.Count(t => t.Observation?.Ok == true)} thành công, "
                        + $"{old.Count(t => t.Observation?.Ok == false)} lỗi.");
        }
        foreach (var t in kept)
            sb.AppendLine($"LƯỢT {t.Step}: {t.Decision.Call?.Key ?? "(final)"} → "
                        + $"{Kit.Clip(t.Observation?.Content ?? "-", 50)}");
        return sb.ToString().TrimEnd();
    }
}

// ── Repo giả lập (đứng sau các tool) ──────────────────────────────────────

file sealed class FakeRepo
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _appliedPatches = new(StringComparer.OrdinalIgnoreCase);

    public FakeRepo Add(string path, string content)
    {
        _files[path] = content;
        return this;
    }

    public IEnumerable<string> Paths => _files.Keys;

    public bool TryRead(string path, out string content) => _files.TryGetValue(path, out content!);

    public string Read(string path) => _files.TryGetValue(path, out var c) ? c : "";

    public IEnumerable<string> Grep(string pattern)
    {
        foreach (var (path, content) in _files)
        {
            var lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    yield return $"{path}:{i + 1}";
        }
    }

    private const string BrokenFile = "HW.CS/Demos/Demo03_Collections.cs";

    /// <summary>Trình biên dịch giả: trả về lỗi ĐẦU TIÊN còn lại, giống dotnet build thật.</summary>
    public string Build()
    {
        if (!_files.TryGetValue(BrokenFile, out var src))
            return "Build succeeded. 0 Warning(s), 0 Error(s)";

        if (!src.Contains("namespace HW.CS.Demos;"))
            return $"{BrokenFile}(1,36): error CS0246: The type or namespace name 'IDemoTopic' could not be found "
                 + "(are you missing a using directive or an assembly reference?)";

        if (!src.Contains("var dict ="))
            return $"{BrokenFile}(11,52): error CS0103: The name 'dict' does not exist in the current context";

        return "Build succeeded. 0 Warning(s), 0 Error(s)";
    }

    /// <summary>Vá theo id, có khoá idempotency để retry không áp dụng hai lần.</summary>
    public string Patch(string patchId)
    {
        if (!_appliedPatches.Add(patchId))
            return $"no-op: patch '{patchId}' đã được áp dụng trước đó (idempotency key trùng)";

        if (!_files.TryGetValue(BrokenFile, out var src))
            return $"patched: '{patchId}' — nhưng không có file mục tiêu, không đổi gì cả";

        _files[BrokenFile] = patchId switch
        {
            "add-namespace" => "namespace HW.CS.Demos;\n\n" + src,
            "declare-dict" => src.Replace(
                "static void Q2_Dictionary() => Console.WriteLine(dict.Count);",
                "static void Q2_Dictionary()\n    {\n        var dict = new Dictionary<string, int>();\n        Console.WriteLine(dict.Count);\n    }"),
            _ => src,
        };

        return $"patched: đã áp dụng '{patchId}' vào {BrokenFile}";
    }
}
