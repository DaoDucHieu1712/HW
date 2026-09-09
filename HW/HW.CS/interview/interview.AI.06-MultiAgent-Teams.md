# Phần AI-06 — Multi-agent, Sub-agent và Agent Team

[⬅️ AI-05 — Harness](interview.AI.05-Harness-Config.md) | [⬅️ Mục lục AI](interview.AI.md) | Tiếp theo: [AI-07 — Kiến trúc triển khai ➡️](interview.AI.07-Impl-Architecture.md)

> Khung mỗi câu: **❓ Vấn đề gốc → ⚙️ Cơ chế bên dưới → 💻 Cấu hình/đo đạc → ⚖️ Hệ quả thực chiến**.
>
> **Luận điểm trung tâm:** lý do thật sự để dùng nhiều agent **không phải là tốc độ** — mà là
> **cô lập context**. Ai trả lời "để chạy song song cho nhanh" là đã bỏ lỡ điểm chính, vì
> multi-agent thường **chậm hơn** về wall-clock cho các việc phụ thuộc lẫn nhau.

---

## 🗺️ Bản đồ: ba mô hình phối hợp

```
A. SUB-AGENT (trong 1 phiên)          B. AGENT TEAM (nhiều phiên)      C. CROSS-SESSION
   ┌─────────┐                           ┌──────────┐                    ┌────┐  ┌────┐
   │  main   │──spawn──▶ ○ ○ ○           │   LEAD   │                    │ S1 │◀▶│ S2 │
   │ context │◀─result── ○ ○ ○           └────┬─────┘                    └────┘  └────┘
   └─────────┘                          ┌─────┼─────┐                    bạn tự mở, nhắn nhau
   kết quả TÓM TẮT quay về         ┌────▼─┐┌──▼──┐┌─▼────┐
   rác Ở LẠI bên kia               │ mate ││mate ││ mate │  ← mỗi cái 1 phiên đầy đủ
                                   └──┬───┘└──┬──┘└───┬──┘
                                      └───────┴───────┘  nhắn TRỰC TIẾP cho nhau
                                        + shared task list (claim, dependency)
```

**Câu chốt phỏng vấn:** *"Sub-agent = **uỷ thác**: kết quả quay về, rác ở lại. Agent team =
**cộng tác**: các phiên độc lập, nhắn trực tiếp cho nhau, tranh luận, tự nhận việc từ một danh
sách chung. Cái đầu rẻ hơn và đủ cho 90% trường hợp; cái sau đắt hơn nhiều và chỉ đáng khi công
việc cần **thảo luận**, không chỉ cần **kết quả**."*

---

## MA-1. Lý do thật sự để tách nhiều agent: cô lập context

**❓ Vấn đề gốc:** Một agent quét 200 file để trả lời "module auth nằm ở đâu" sẽ kéo 200 file đó
vào context, và **mọi lượt sau đều mang theo đống rác này** — chi phí tăng, chất lượng giảm
(context rot, LLM-6).

**⚙️ Ba lý do chính đáng để tách (xếp theo sức nặng):**

| # | Lý do | Vì sao một agent không làm được |
|---|---|---|
| 1 | **Cô lập context** | rác nghiên cứu ở lại bên kia; luồng chính chỉ nhận kết luận |
| 2 | **Độc lập nhận thức** | reviewer không nên nhìn thấy lý lẽ tự bào chữa của writer → chống anchoring |
| 3 | **Chuyên môn hoá** | mỗi agent có prompt/tool/model riêng; reviewer **không có** quyền `Edit` |

**⚠️ Lý do **không** chính đáng:** "song song cho nhanh". Nếu các subtask phụ thuộc nhau, phối hợp
sẽ nuốt hết phần lợi. Song song chỉ thắng khi công việc **thật sự độc lập**.

---

## MA-2. Kinh tế học: multi-agent tốn bao nhiêu?

**⚙️ Phép nhân token:** mỗi agent có context window riêng, tự nạp lại project context (CLAUDE.md,
MCP, skill) và nhận spawn prompt. Với `k` teammate, chi phí **xấp xỉ tuyến tính theo k** — và trên
thực tế một hệ multi-agent thường tiêu **cả chục lần** một phiên đơn cho cùng nhiệm vụ, vì:

```
tổng ≈ Σ (context riêng của mỗi agent)
     + chi phí điều phối (lead đọc kết quả, tổng hợp, nhắn tin)
     + công việc lặp (hai agent cùng đọc một file)
     + lượt hỏng phải làm lại
```

**⚠️ Chi tiết vận hành hay bị bỏ sót:** request của teammate **nằm ngoài nhóm TTL cache của hội
thoại chính** ⇒ mặc định cache của nó chỉ giữ 5 phút. Với team chạy dài, phải cân nhắc nâng TTL
cache của subagent lên 1 giờ (đổi lại giá ghi cache cao hơn).

**⚖️ Quy tắc quyết định:** *"Multi-agent đáng tiền khi giá trị của **kết quả tốt hơn** vượt hệ số
nhân chi phí. Nghiên cứu, review đa góc nhìn, gỡ lỗi có nhiều giả thuyết — đáng. Việc lặp đi lặp
lại hằng ngày, việc tuần tự, việc sửa cùng một file — không đáng."*

---

## MA-3. Sáu topology phối hợp và bài toán phù hợp

| Topology | Hình dạng | Hợp với | Rủi ro chính |
|---|---|---|---|
| **Single agent** | 1 | mặc định — **thử trước tiên** | nghẹt context ở việc lớn |
| **Orchestrator–worker** | 1 điều phối, n worker | số subtask không biết trước | điều phối thành nút cổ chai; worker thiếu ngữ cảnh |
| **Pipeline** | A → B → C, mỗi chặng chuyên biệt | quy trình có giai đoạn rõ (extract → validate → write) | lỗi tích luỹ qua các chặng |
| **Debate / adversarial** | n agent phản biện nhau | giả thuyết cạnh tranh, quyết định quan trọng | tốn kém; có thể "đồng thuận sai" |
| **Blackboard** | dùng chung một không gian trạng thái (task list, file) | công việc tự nhận, phụ thuộc động | tranh chấp ghi, cần khoá |
| **Hierarchical** | lead → sub-lead → worker | quy mô rất lớn | tam sao thất bản; khó debug |

**⚖️ Lời khuyên thực chiến:** bắt đầu **single**, chỉ tách khi thấy triệu chứng cụ thể (context
đầy, cần góc nhìn độc lập). *"Chọn topology trước khi có triệu chứng là over-engineering."*

---

## MA-4. Sub-agent vs Agent team vs Cross-session — chọn cái nào?

| | **Sub-agent** | **Agent team** | **Cross-session** |
|---|---|---|---|
| Context | riêng; kết quả trả về cho người gọi | riêng; **hoàn toàn độc lập** | riêng; các phiên bạn tự mở |
| Giao tiếp | trả kết quả về; sub-agent được đặt tên có thể nhắn nhau | **nhắn trực tiếp cho nhau** | nhắn giữa các phiên |
| Phối hợp | agent chính quản lý tất cả | **tự phối hợp** + **task list chung** (claim, dependency) | thủ công |
| Chi phí token | thấp hơn — kết quả được tóm tắt về | **cao** — mỗi teammate là một phiên đầy đủ | cao |
| Hợp với | việc tập trung, chỉ cần kết quả | việc cần **thảo luận, phản biện, chia sở hữu file** | bạn muốn tự lái từng phiên |

**⚖️ Thứ tự cân nhắc:** sub-agent → cross-session → agent team. Team là công cụ nặng nhất; dùng
nó khi teammate **thật sự cần nói chuyện với nhau**, không chỉ cần trả kết quả.

---

## MA-5. Agent team trong Claude Code: kiến trúc và giới hạn

**⚙️ Bốn thành phần:**

| Thành phần | Vai trò |
|---|---|
| **Team lead** | phiên chính — spawn teammate, chia việc, tổng hợp |
| **Teammates** | các phiên Claude Code **độc lập**, mỗi cái một context |
| **Task list** | danh sách việc dùng chung: pending / in-progress / completed, có **phụ thuộc** |
| **Mailbox** | file JSON mỗi agent một hộp thư; gửi thành công chỉ khi **ghi file thành công** |

**💻 Bật (mặc định tắt, còn là tính năng thử nghiệm):**
```jsonc
{ "env": { "CLAUDE_CODE_EXPERIMENTAL_AGENT_TEAMS": "1" },
  "teammateMode": "in-process" }   // hoặc "auto"/"tmux"/"iterm2" cho chế độ chia pane
```

**⚙️ Hai chi tiết vận hành đáng nhớ:**
- Khi agent team đang bật, **một subagent được Claude đặt tên sẽ khởi chạy như một teammate** —
  nghĩa là team có thể hình thành cả khi bạn không yêu cầu. Muốn quay lại subagent thường thì đặt
  biến về `0`.
- **Teammate không kế thừa lịch sử hội thoại của lead.** Nó nạp lại project context (CLAUDE.md,
  MCP, skill) và nhận **spawn prompt**. Spawn prompt nghèo nàn = teammate làm sai việc.

**⚠️ Giới hạn phải biết (đây là phần phân biệt người đã dùng thật):**
- Không khôi phục teammate khi `/resume` (với chế độ in-process).
- **Một team cho mỗi phiên**, **không có team lồng nhau** — teammate không spawn được teammate.
- **Lead cố định** — không chuyển quyền lead được.
- Quyền được đặt **lúc spawn** theo lead; không đặt riêng cho từng teammate lúc spawn.
- Trạng thái task có thể trễ ⇒ task phụ thuộc bị kẹt; đôi khi lead tuyên bố xong sớm.

---

## MA-6. Giao thức bàn giao (handoff): teammate khởi động lạnh nên phải đưa gì?

**❓ Vấn đề gốc:** Bạn nói "spawn một agent review bảo mật" và nó trả về báo cáo lạc đề — vì nó
không biết bối cảnh 30 lượt trước của bạn.

**⚙️ Một spawn prompt tốt có sáu phần:**
```
1. VAI TRÒ + PHẠM VI   : "Rà soát bảo mật src/auth/. CHỈ ĐỌC, không sửa."
2. BỐI CẢNH ĐÃ BIẾT    : "App dùng JWT lưu trong cookie httpOnly; đã sửa refresh flow hôm qua."
3. ĐỊNH NGHĨA XONG     : "Báo cáo từng phát hiện: file:line, kịch bản khai thác, mức độ."
4. RÀNG BUỘC / ĐIỀU CẤM: "Không sửa file. Không chạy migration. Không báo cáo suy đoán."
5. ĐẦU RA MONG ĐỢI     : định dạng cụ thể để lead tổng hợp được
6. NGÕ CỤT ĐÃ BIẾT     : "Đã kiểm tra CORS, không phải nguyên nhân."
```

**⚖️ Nguyên tắc:** *"Chi phí bàn giao là chi phí thật. Nếu mô tả nhiệm vụ cho subagent tốn nhiều
công hơn tự làm, thì đừng tách."* Đây cũng là lý do sub-agent thắng ở việc **đọc nhiều, kết quả
gọn**, và thua ở việc **cần nhiều ngữ cảnh, kết quả nhỏ**.

---

## MA-7. Phối hợp qua task list dùng chung

**⚙️ Cơ chế:**
- Task có ba trạng thái: `pending` → `in progress` → `completed`, và có thể **phụ thuộc** task
  khác. Task pending còn phụ thuộc chưa xong thì **không claim được**.
- **Claim dùng khoá file** để tránh hai teammate cùng nhận một task (race condition).
- Khi một task hoàn thành, các task phụ thuộc nó **tự mở khoá**.
- Agent **không có bộ tool Task** thì phối hợp bằng **nhắn tin** thay vì task list.

**⚖️ Đây chính là mô hình **blackboard/work-queue** cổ điển**, và nó thừa hưởng đúng những vấn đề
cổ điển: task quá to thì không ai dám nhận, task quá nhỏ thì chi phí phối hợp vượt lợi ích, và
**task không được đánh dấu xong sẽ chặn cả nhánh phụ thuộc**. Cỡ task tốt = **một đơn vị tự chứa
có sản phẩm rõ ràng** (một hàm, một file test, một bản review).

---

## MA-8. Bảy chế độ hỏng của hệ multi-agent

| Chế độ hỏng | Nguyên nhân | Giảm thiểu |
|---|---|---|
| **Xung đột file** | hai teammate sửa cùng file | **chia sở hữu theo file** ngay trong spawn prompt |
| **Làm trùng việc** | không có task list, hoặc mô tả chồng lấn | task list + phạm vi rõ; mỗi agent một "lăng kính" |
| **Mất ngữ cảnh khi bàn giao** | spawn prompt nghèo | giao thức handoff 6 phần (MA-6) |
| **Kẹt phụ thuộc** | task không được đánh dấu xong | hook `TaskCompleted`/`TeammateIdle`; lead chủ động nhắc |
| **Lead dừng sớm** | lead tưởng đã xong | hook `Stop` exit 2; hoặc yêu cầu lead chờ |
| **Đồng thuận sai** | các agent hùa theo giả thuyết đầu tiên | thiết kế **đối kháng** (MA-9) |
| **Nhiễu điều phối** | quá nhiều teammate | 3–5 teammate; **ba người tập trung hơn năm người tản mát** |

**⚖️ Nhận xét đắt giá:** *"Chi phí phối hợp tăng theo **O(k²)** với số kênh giao tiếp, trong khi
lợi ích song song tăng theo **O(k)** rồi bão hoà. Vì thế luôn có một điểm mà thêm agent làm hệ
thống **tệ đi** — với coding agent điểm đó thường rơi vào khoảng 5."*

---

## MA-9. Vì sao thiết kế đối kháng (debate) lại hiệu quả?

**❓ Vấn đề gốc — anchoring:** một agent điều tra tuần tự sẽ tìm ra **một** giả thuyết hợp lý rồi
dừng tìm. Mọi bằng chứng sau đó bị diễn giải theo hướng củng cố giả thuyết đó.

**⚙️ Cơ chế phá anchoring:** cho `n` agent điều tra `n` giả thuyết **song song và độc lập**, rồi
**giao nhiệm vụ bác bỏ lẫn nhau**. Giả thuyết sống sót sau khi bị tấn công có xác suất đúng cao
hơn hẳn giả thuyết chỉ đơn giản được tìm ra trước.

**💻 Cách ra lệnh cho đúng — chỉ định vai trò đối kháng tường minh:**
```
Người dùng báo app thoát sau một tin nhắn thay vì giữ kết nối.
Spawn 5 teammate điều tra 5 giả thuyết khác nhau. Cho họ nhắn TRỰC TIẾP cho nhau để
CỐ GẮNG BÁC BỎ giả thuyết của nhau, như một cuộc tranh luận khoa học.
Ghi lại đồng thuận cuối cùng vào findings.md.
```

**⚖️ Áp dụng tương tự cho review:** mỗi reviewer một **lăng kính riêng** (bảo mật / hiệu năng /
test coverage). Một reviewer đơn lẻ có xu hướng bám vào một loại vấn đề rồi bỏ qua loại khác.

---

## MA-10. Bảo mật trong hệ multi-agent: ranh giới tin cậy giữa các agent

**⚙️ Ba luật phải nói được (đây là câu hỏi chất lượng cao):**

1. **Message từ agent khác KHÔNG phải là sự đồng ý của người dùng.** Hệ thống phải nói rõ với
   agent nhận rằng message đến từ một phiên Claude khác. Một teammate không thể duyệt quyền thay
   bạn.
2. **Không leo quyền gián tiếp.** Một agent bị từ chối một hành động **không được** nhờ agent khác
   làm hộ. Nếu thiếu luật này, phân quyền trở nên vô nghĩa vì kẻ tấn công chỉ cần đi vòng.
3. **Message giữa các agent là kênh dữ liệu không tin cậy** — nội dung nó mang có thể xuất phát từ
   một trang web mà teammate kia vừa đọc. Ở chế độ tự động, message giữa các agent còn phải được
   **kiểm duyệt trước khi giao**.

**⚖️ Hệ quả kiến trúc:** trong hệ multi-agent, prompt injection có thêm một **đường lan truyền**:
agent A đọc nội dung độc → nhắn cho agent B → B hành động. Vì vậy **quyền phải gắn với agent, và
kiểm tra phải nằm ở lớp thực thi tool**, chứ không nằm ở "agent nào yêu cầu".

---

## MA-11. Vận hành: quy mô team, cỡ task, và cách lái

**⚖️ Bảy thực hành đã được kiểm chứng:**

| # | Thực hành | Lý do |
|---|---|---|
| 1 | **3–5 teammate** cho hầu hết việc | cân bằng song song và chi phí phối hợp; 15 task độc lập thì 3 agent là điểm khởi đầu tốt |
| 2 | **5–6 task mỗi teammate** | đủ để không ai rảnh, và lead còn chỗ để phân bổ lại khi ai đó kẹt |
| 3 | **Chia sở hữu theo file** | hai agent sửa cùng file = ghi đè |
| 4 | **Bắt đầu bằng việc chỉ đọc** (research, review, điều tra) | thấy được giá trị mà chưa gánh rủi ro ghi song song |
| 5 | **Pre-approve các thao tác thường dùng** | quyền của teammate bật lên **ở phiên lead** ⇒ không pre-approve là ngập prompt |
| 6 | **Đặt tên teammate rõ ràng** ngay khi spawn | để nhắn đúng người ở các lượt sau |
| 7 | **Theo dõi và lái** | để team chạy vô chủ lâu = rủi ro công cốc |

**⚙️ Thứ tự chọn model cho teammate:** (1) model bạn chỉ định trong prompt → (2) `model` trong
định nghĩa subagent (`inherit` = model của lead) → (3) biến `CLAUDE_CODE_SUBAGENT_MODEL` → (4)
model hiện tại của lead. Teammate **kế thừa effort của lead**.

---

## MA-12. Tái sử dụng định nghĩa subagent làm teammate

**⚙️ Định nghĩa một vai trò **một lần**, dùng được cả hai chỗ:**
```yaml
# .claude/agents/security-reviewer.md
---
name: security-reviewer
description: Rà soát bảo mật. Dùng khi cần audit thay đổi.
tools: Read, Grep, Glob, Bash
model: claude-opus-5
---
Chỉ đọc. Mỗi phát hiện: file:line, kịch bản khai thác, mức độ.
```
```
Spawn một teammate dùng agent type security-reviewer để audit module auth.
```

**⚠️ Không phải mọi trường đều được áp dụng như nhau:**
- `tools` **được áp dụng** — và với teammate in-process, hệ thống **thêm** `SendMessage` (và các
  tool Task nếu phiên có) vào danh sách đó.
- `model` được áp dụng khi prompt spawn không chỉ định.
- **Body**: teammate in-process thì body được **nối thêm** vào system prompt mặc định; teammate
  chia pane thì body **thay thế** system prompt.
- `skills` **không** được áp dụng cho teammate — nó nạp skill từ project/user settings.
- `mcpServers` chỉ áp dụng cho teammate chia pane; teammate in-process bỏ qua và dùng cấu hình
  project/user.

**⚖️ Bài học chung:** trong hệ multi-agent, **cùng một định nghĩa có hành vi khác nhau theo chế độ
chạy**. Đừng giả định — hãy kiểm chứng bằng `/context` trong phiên của teammate.

---

## MA-13. Đánh giá và quan sát hệ multi-agent

**⚙️ Ngoài các chỉ số ở AG-12, cần thêm:**

| Chỉ số | Vì sao |
|---|---|
| **Chi phí trên mỗi task hoàn thành, so với single agent** | câu hỏi duy nhất đáng trả lời: có đáng không? |
| **Tỉ lệ công việc trùng lặp** | hai agent cùng đọc/cùng làm = thiết kế phân chia sai |
| **Số vòng nhắn tin trước khi có kết luận** | tăng = phối hợp đang thất bại |
| **Tỉ lệ task bị kẹt (dependency chưa mở)** | phát hiện lỗi đánh dấu hoàn thành |
| **Độ phân kỳ kết luận giữa các agent** | trong thiết kế debate, phân kỳ cao là **tín hiệu tốt** trước khi hội tụ |

**💻 Truy vết:** dùng **một `trace_id` xuyên suốt**, mỗi agent là một span con, mỗi message giữa
agent là một sự kiện có `from`/`to`. Không có cái này thì hệ multi-agent là hộp đen — đây là lý do
nhiều team bỏ multi-agent sau vài tuần: họ không debug nổi.

---

## MA-14. So sánh với hệ phân tán cổ điển — giống và khác ở đâu?

| Khái niệm cổ điển | Tương ứng trong multi-agent | Khác biệt cốt lõi |
|---|---|---|
| Map-Reduce | fan-out/fan-in | map trong LLM **không tất định**; hai worker cùng input cho ra kết quả khác |
| Actor model | agent + mailbox | actor có protocol chặt; agent giao tiếp bằng **ngôn ngữ tự nhiên mơ hồ** |
| Saga / compensation | rollback hành động agent | compensation phải do **bạn** thiết kế; agent không tự biết cách hoàn tác |
| Work queue + worker pool | task list + claim | claim cần khoá; nhưng "xong" là **phán đoán của model**, không phải trạng thái khách quan |
| Consensus (Raft/Paxos) | debate/voting | không có bảo đảm toán học nào; đồng thuận có thể **cùng sai** |

**⚖️ Kết luận phải nói ra:** *"Multi-agent thừa hưởng **toàn bộ** khó khăn của hệ phân tán —
partial failure, race, thứ tự, quan sát — và **thêm** hai thứ mới: giao tiếp bằng ngôn ngữ tự
nhiên mơ hồ, và trạng thái 'đã xong' là một **ý kiến** chứ không phải một sự kiện. Vì vậy nó phải
được thiết kế thận trọng hơn hệ phân tán truyền thống, không phải dễ dãi hơn."*

---

## MA-15. Khi nào **không** dùng multi-agent

**⚖️ Sáu dấu hiệu rõ ràng nên ở lại với một agent:**
1. Công việc **tuần tự** — B cần kết quả của A.
2. Nhiều agent phải **sửa cùng một file**.
3. Công việc **ngắn**; chi phí bàn giao lớn hơn công việc.
4. Bạn **chưa** có một agent đơn chạy tốt. Multi-agent **khuếch đại** vấn đề của single agent,
   không sửa chúng.
5. Ngân sách chặt — hệ số nhân token là thật.
6. Chưa có truy vết/quan sát — bạn sẽ không debug được.

*"Multi-agent là tối ưu hoá cho một nút thắt cụ thể (context và góc nhìn). Áp dụng khi chưa có
nút thắt đó thì bạn chỉ mua thêm chi phí và phi tất định."*

---

## MA-16. Case study: review một PR lớn bằng agent team

**Yêu cầu:** PR 40 file, đụng cả API, DB và frontend. Cần review chất lượng cao trong 30 phút.

**⚙️ Thiết kế:**

```
1. CÓ ĐÁNG DÙNG TEAM KHÔNG?
   ✅ Việc CHỈ ĐỌC (không tranh chấp ghi) — điều kiện lý tưởng để bắt đầu với team
   ✅ Ba lăng kính thật sự độc lập, không phụ thuộc nhau
   ✅ Giá trị cao (bug lọt vào production đắt hơn nhiều lần chi phí token)

2. SPAWN — mỗi teammate một LĂNG KÍNH, dùng subagent definition có tools chỉ-đọc
   - security   : authz, xử lý input, secret, injection
   - performance: N+1, index, cấp phát trong vòng lặp, khoá DB
   - tests      : nhánh nào không được phủ, test có thật sự assert không

3. SPAWN PROMPT (theo giao thức MA-6)
   - phạm vi file cụ thể cho từng người (chống trùng lặp)
   - định dạng phát hiện: file:line + kịch bản hỏng + mức độ
   - CẤM: không sửa code, không báo cáo suy đoán không có bằng chứng

4. PHỐI HỢP
   - task list: 1 task/nhóm file, có phụ thuộc "chờ security xong mới kết luận authz"
   - cho phép nhắn trực tiếp: performance có thể hỏi security về đường đi của request

5. TỔNG HỢP (lead)
   - gộp, khử trùng lặp, xếp theo mức độ
   - ĐỐI CHIẾU: mỗi phát hiện phải trỏ tới file:line có thật (chống bịa)

6. ĐO
   - số phát hiện đúng / số phát hiện báo cáo (precision)  ← chỉ số quan trọng nhất
   - chi phí so với một reviewer đơn
   - có bỏ sót thứ mà người review bắt được không
```

**⚖️ Ba câu trả lời ghi điểm:**
- *"Bắt đầu bằng việc chỉ-đọc"* — tránh toàn bộ lớp lỗi xung đột ghi.
- *"Mỗi teammate một lăng kính, không phải một phần code"* — đây là cách chia đúng cho review:
  chia theo **loại vấn đề**, không chia theo file, vì bug hay nằm ở chỗ giao nhau.
- *"Precision quan trọng hơn recall trong review tự động"* — 30 phát hiện trong đó 25 là nhiễu sẽ
  làm team ngừng đọc báo cáo, và thế là công cụ chết.

---

[⬅️ AI-05](interview.AI.05-Harness-Config.md) | [⬅️ Mục lục AI](interview.AI.md) | Tiếp theo: [AI-07 — Kiến trúc triển khai ➡️](interview.AI.07-Impl-Architecture.md)
