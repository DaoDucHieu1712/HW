# 🤖 Bộ Ôn Luyện Phỏng Vấn AI Engineer — LLM, Agent, MCP, Harness, Multi-agent

> Bộ câu hỏi chuyên sâu cho vị trí **AI Engineer / Agent Engineer / Loop Engineer**, viết theo
> cùng khung với track .NET: **❓ Vấn đề gốc → ⚙️ Cơ chế bên dưới → 💻 Code/đo đạc → ⚖️ Hệ quả thực chiến**.
>
> Bộ này **không** dạy "prompt hay". Nó trả lời câu hỏi mà interviewer thật sự khoan:
> *"Bên dưới nó chạy thế nào, hỏng ở đâu, đo bằng gì, và bạn đã vận hành cái gì thật?"*

[⬅️ Về mục lục .NET](interview.NET.md)

---

## 🗂️ Mục lục

| # | Chủ đề | File | Số câu |
|---|--------|------|--------|
| 1 | **Nền tảng LLM** — token/BPE, context window & KV cache, prefill vs decode, sampling, hallucination, context rot, thinking & effort, embedding, RAG, structured output, function calling, prompt caching, cost, latency, eval, rate limit, prompt injection | [interview.AI.01-LLM-Foundations.md](interview.AI.01-LLM-Foundations.md) | 18 |
| 2 | **Prompt & Context Engineering** — giải phẫu prompt, few-shot, cấu trúc, tool description, prompt cruft, compaction vs context editing vs memory, CLAUDE.md, progressive disclosure, anti-pattern, versioning, injection, tiếng Việt, hill-climbing | [interview.AI.02-Prompt-Context.md](interview.AI.02-Prompt-Context.md) | 16 |
| 3 | **Agentic Loop & Loop Engineer** — workflow vs agent, 5 mẫu workflow, manual loop, 4 cách xây agent, verify, điều kiện dừng, task budget, 6 chế độ hỏng, error feedback, tool surface, permission, observability, Loop Agentic, vai trò Loop Engineer, sub-agent, eval agent, idempotency, case study | [interview.AI.03-Agentic-Loop.md](interview.AI.03-Agentic-Loop.md) | 18 |
| 4 | **MCP chuyên sâu** — bài toán M×N, host/client/server, JSON-RPC & lifecycle, transport, 3 primitive server, 3 primitive client, scope & `.mcp.json`, namespace, chi phí context, auth & token passthrough, 6 rủi ro bảo mật, khi nào KHÔNG dùng, thiết kế server tốt, MCP connector trên API, versioning, debug | [interview.AI.04-MCP.md](interview.AI.04-MCP.md) | 16 |
| 5 | **Harness: Hooks · Settings · Commands · Skills · Plugins** — 6 cơ chế mở rộng & bảng quyết định, precedence settings, permission `allow/ask/deny`, bản đồ hook event, hợp đồng I/O của hook, matcher & loại hook, 5 hook đáng làm, bảo mật hook, `$ARGUMENTS`/`` !`cmd` ``, progressive disclosure của skill, skill vs command vs subagent vs hook vs MCP, frontmatter, subagent, plugin & marketplace, harness của team, debug | [interview.AI.05-Harness-Config.md](interview.AI.05-Harness-Config.md) | 17 |
| 6 | **Multi-agent, Sub-agent & Agent Team** — vì sao tách (cô lập context, không phải tốc độ), kinh tế học token, 6 topology, sub-agent vs team vs cross-session, kiến trúc agent team, giao thức handoff, task list & claim, 7 chế độ hỏng, thiết kế đối kháng, bảo mật giữa các agent, quy mô team, tái dùng subagent definition, quan sát, so với hệ phân tán cổ điển, khi nào KHÔNG dùng, case study | [interview.AI.06-MultiAgent-Teams.md](interview.AI.06-MultiAgent-Teams.md) | 16 |
| 7 | **Kiến trúc triển khai** — LLM gateway, vị trí trong Clean Architecture, agent là background job, schema hội thoại, resilience & degraded mode, quản trị chi phí, tracing, bảo mật & đa tenant, 4 tầng test, rollout & canary, caching nhiều tầng, human-in-the-loop, áp dụng vào project HW, bảng tra nhanh, 10 câu tự kiểm tra | [interview.AI.07-Impl-Architecture.md](interview.AI.07-Impl-Architecture.md) | 15 |
| 8 | **🎤 BỘ TRẢ LỜI PHỎNG VẤN** — 49 câu hỏi thực tế được trả lời sẵn theo 3 tầng (**30 giây → khi bị khoan → trade-off**), bám vào hai hệ agent CÓ THẬT trong repo: `HW.Application/Agents` và `agentic/` (LangGraph loop) | [interview.AI.08-QA-Answers.md](interview.AI.08-QA-Answers.md) | 49 |

**Tổng: ~116 câu chuyên sâu + 49 câu trả lời phỏng vấn + 3 case study.**

> **Đọc file 08 trước nếu bạn sắp đi phỏng vấn**, các file 01–07 là phần đào sâu để trả lời được
> khi interviewer khoan tiếp.

---

## 🎯 Ba tuyến câu hỏi và cách interviewer dùng chúng

| Tuyến | File | Câu hỏi đặc trưng | Phân biệt điều gì |
|---|---|---|---|
| **Nền tảng** | 01, 02 | "Vì sao `temperature = 0` vẫn không deterministic?" · "Prompt cache khớp theo gì?" | đã đọc tài liệu hay chỉ dùng theo cảm tính |
| **Hệ thống** | 03, 04, 05, 06 | "Agent khác workflow ở đâu?" · "Thứ này nên là skill hay hook?" · "Vì sao dùng multi-agent?" | đã **xây** agent thật hay mới demo |
| **Vận hành** | 07 | "Chi phí tăng gấp đôi, bạn tìm nguyên nhân thế nào?" · "Model bị thay thế, bạn migrate ra sao?" | đã **vận hành** ở production hay chưa |

---

## 🧠 Mười câu trả lời "chốt hạ" nên thuộc

1. **LLM là gì** — hàm `P(token tiếp | mọi token trước)` chạy lặp. Prompt, RAG, tool, agent đều
   chỉ là cách sắp xếp lại chuỗi token đầu vào. *(AI-01)*
2. **Vì sao output đắt hơn input ~5×** — prefill song song và compute-bound; decode tuần tự và
   memory-bandwidth-bound. Muốn nhanh thì cắt output, không phải cắt input. *(LLM-3)*
3. **Prompt cache khớp theo tiền tố** — thứ tự `tools → system → messages`; một byte đổi trong
   tiền tố huỷ toàn bộ phần sau. Kiểm chứng bằng `cache_read_input_tokens`. *(LLM-13)*
4. **Context là ngân sách, không phải thùng chứa** — nhiều context làm chất lượng tụt (lost in
   the middle, nhiễu, context poisoning). *(LLM-6)*
5. **Agent = LLM + tool + vòng lặp + điều kiện dừng.** Bỏ **verify** thì nó chỉ là cỗ máy sinh
   hành động tự tin. Chất lượng agent tỉ lệ với chất lượng verifier, không tỉ lệ với độ dài
   prompt. *(AG-5)*
6. **Workflow trước, agent sau** — agent chỉ xứng đáng khi task không đặc tả trước được **và**
   sai lầm phát hiện/khôi phục được. *(AG-1, AG-2)*
7. **Model xác suất, harness tất định** — thứ gì phải đúng 100% lần thì thuộc về hook/permission,
   không thuộc về prompt. *(HR-1)*
8. **Multi-agent là để cô lập context**, không phải để chạy nhanh; chi phí phối hợp tăng O(k²)
   trong khi lợi ích song song bão hoà. *(MA-1, MA-8)*
9. **Prompt injection không vá được bằng prompt** — lệnh và dữ liệu dùng chung một kênh, nên
   ranh giới thật phải nằm ở quyền của tool và kiểm duyệt hành động ra ngoài. *(LLM-18)*
10. **Chỉ số đúng là cost per completed task**, không phải cost per request. *(LLM-14)*

---

## 🧭 Lộ trình học đề xuất

```
Tuần 1   file 01              →  nền tảng: token, context, cache, cost  (đọc kỹ nhất)
Tuần 2   file 02              →  context engineering — phần quyết định chất lượng agent
Tuần 3   file 03              →  agentic loop; TỰ VIẾT manual loop bằng C#, không copy
Tuần 4   file 04 + 05         →  MCP + harness; tự viết 1 MCP server nhỏ và 3 hook
Tuần 5   file 06              →  multi-agent; chạy thử một team review PR và ĐO chi phí
Tuần 6   file 07              →  kiến trúc; áp vào project HW, vẽ ra giấy
Tuần 7   ôn 10 câu "chốt hạ"  →  nói to, không nhìn tài liệu
```

---

## 🔬 Cách luyện cho ra kết quả (quan trọng hơn việc đọc)

| Việc | Vì sao |
|---|---|
| **Tự viết manual agent loop** (AG-3) bằng C#, chạy được | 9 điểm đánh dấu trong đoạn code đó chính là 9 câu hỏi phỏng vấn |
| **Đo token thật** bằng `count_tokens` trên prompt tiếng Việt của bạn | con số thật đáng giá hơn mọi lý thuyết |
| **Bật/tắt prompt cache và so `cache_read_input_tokens`** | thấy tận mắt cơ chế prefix |
| **Viết 3 hook** (format, chặn lệnh nguy hiểm, quality gate ở `Stop`) | hiểu ranh giới xác suất/tất định bằng tay |
| **Dựng 1 eval 30 ca** cho một task thật rồi sửa prompt và đo lại | đây là kỹ năng phân biệt lớn nhất |
| **Chạy một agent team review PR rồi so chi phí với single agent** | trả lời được "có đáng không" bằng số liệu |

---

## ⚠️ Ghi chú về độ tươi của thông tin

API và harness thay đổi nhanh. Những chỗ trong bộ đề này có **ngày tháng hoặc phiên bản cụ thể**
(model id, giá, tên tham số, tên hook event, cấu trúc plugin) cần được kiểm chứng lại trước khi
dùng làm khẳng định chắc chắn. Riêng những điểm sau **đã lỗi thời** và biết chúng lỗi thời chính
là một câu trả lời tốt:

| Kiến thức cũ | Hiện tại |
|---|---|
| `thinking: {type:"enabled", budget_tokens: N}` | `thinking: {type:"adaptive"}` + `output_config.effort` |
| `temperature` / `top_p` / `top_k` | đã gỡ trên các model đời mới (400) |
| Prefill assistant turn để ép JSON | đã gỡ (400) — dùng structured outputs |
| `output_format` | `output_config.format` |
| SSE transport cho MCP | streamable HTTP |
| Mirrored queue / các mẹo prompt "hãy suy nghĩ từng bước" | quorum queue / thinking gốc của model |

*"Biết một API đã thay đổi, và biết vì sao nó thay đổi, giá trị hơn thuộc lòng API hiện tại."*
