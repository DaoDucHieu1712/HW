# Hướng dẫn đào tạo — Agent Platform

> Tài liệu onboarding cho thành viên mới sử dụng **Agent Platform** trong solution HW.
> Đọc theo thứ tự. Mỗi phần có mục **✅ Checkpoint** — làm xong mới sang phần sau.
>
> Tài liệu gốc (tiếng Anh): [`README.md`](README.md) — *platform làm gì*;
> [`ARCHITECTURE.md`](ARCHITECTURE.md) — *vì sao nó được thiết kế như vậy*.

---

## Mục lục

0. [Lộ trình đào tạo](#0-lộ-trình-đào-tạo)
1. [Agent Platform là gì](#1-agent-platform-là-gì)
2. [7 nguyên tắc vàng](#2-7-nguyên-tắc-vàng)
3. [Cài đặt môi trường (Ngày 1)](#3-cài-đặt-môi-trường-ngày-1)
4. [Các lệnh và cách dùng](#4-các-lệnh-và-cách-dùng)
5. [Đọc kết quả một lần chạy](#5-đọc-kết-quả-một-lần-chạy)
6. [Kiến trúc cần nắm](#6-kiến-trúc-cần-nắm)
7. [Cấu hình](#7-cấu-hình)
8. [An toàn — những gì platform KHÔNG BAO GIỜ làm](#8-an-toàn--những-gì-platform-không-bao-giờ-làm)
9. [Chi phí token](#9-chi-phí-token)
10. [Mở rộng platform](#10-mở-rộng-platform)
11. [Quan sát và debug](#11-quan-sát-và-debug)
12. [Lỗi thường gặp (Troubleshooting)](#12-lỗi-thường-gặp-troubleshooting)
13. [Bài tập thực hành](#13-bài-tập-thực-hành)
14. [Checklist hoàn thành onboarding](#14-checklist-hoàn-thành-onboarding)

---

## 0. Lộ trình đào tạo

| Giai đoạn | Nội dung | Kết quả cần đạt |
|---|---|---|
| **Ngày 1** | Mục 1–3: hiểu platform, cài đặt, chạy `--dry-run` | `healthcheck` báo `ready`, dry-run chạy hết graph |
| **Ngày 2** | Mục 4–5: 5 lệnh, đọc kết quả, exit code | Tự chạy `/review` và `/analyzelog` thật trên code HW |
| **Ngày 3** | Mục 6–9: kiến trúc, cấu hình, an toàn, chi phí | Giải thích được vì sao test đỏ quay về `coding` chứ không về `planner` |
| **Tuần 1** | Mục 10–13: mở rộng, debug, bài tập | Hoàn thành bài tập 1–5, thêm được 1 skill |
| **Tuần 2** | Làm việc thật có mentor kèm | Chạy `/unittest` và `/fixbug` thật, tạo draft PR đầu tiên |

> **Quy tắc cho người mới:** trong **tuần đầu tiên**, chạy thật thì luôn đặt
> `AGENT_ALLOW_WRITES=false` (chế độ chỉ đề xuất), trừ khi có mentor ngồi cùng.

---

## 1. Agent Platform là gì

Agent Platform là một **CLI Python** (không phải server). Nó điều phối **8 agent chuyên biệt**
bằng một state machine **LangGraph** để làm các việc lập trình hằng ngày trên solution HW:
sửa bug, review code, sinh unit test, làm feature mới, phân tích log.

```
/fixbug BUG-123   ->  ticket -> logs -> plan -> code -> build -> tests -> review -> pull request
```

**Công nghệ:** Claude Sonnet 5 (`claude-sonnet-5`), LangGraph, ChromaDB, MCP, OpenTelemetry, Python 3.12.

### Vị trí trong repo

Repo này có **ba** hệ thống "agentic". Đừng nhầm lẫn:

| Thứ | Ở đâu | Là gì | Ai dùng |
|---|---|---|---|
| **Agent Platform** | `agent-platform/` | CLI Python, chạy độc lập, gọi Claude API | **Tài liệu này** |
| **Claude Code loops** | `.claude/` | Slash command / skill / hook cho Claude Code (`/fixbug`, `/gen-test`, `/verify`...) | Dev dùng Claude Code trong IDE/terminal |
| **HW.Agentic** | `HW.Agentic/` | Agent runtime viết bằng C#, là một project trong solution | Code của sản phẩm HW |

Agent Platform coi solution HW là **workspace**: nó đọc/ghi file C#, chạy `dotnet build` / `dotnet test`
trên đó. Nó không phải một phần của solution `.slnx`.

### ✅ Checkpoint 1
- [ ] Phân biệt được `agent-platform/`, `.claude/` và `HW.Agentic/`.
- [ ] Biết platform là CLI: chạy một lệnh rồi thoát, không có server.

---

## 2. 7 nguyên tắc vàng

Học thuộc. Mọi quyết định thiết kế của platform đều xuất phát từ các nguyên tắc này.

1. **Dry-run KHÔNG phải bằng chứng.** `--dry-run` thay model bằng một stub luôn trả về
   "build xanh, test xanh, review approve". Nó chỉ chứng minh graph *chạy được*, **không nói gì về code của bạn**.
   Kết quả dry-run luôn là `status=success`. Đừng bao giờ dùng nó để kết luận một thay đổi đúng.
2. **"Xong" là bằng chứng, không phải một câu nói.** Một run chỉ thành công khi reviewer approve **VÀ** build xanh
   **VÀ** test xanh. Không có kết quả test = **chưa pass** (không phải "pass mặc định").
3. **Người sửa không được tự chấm.** Agent `review` chỉ được đọc, không được sửa. Người review có quyền sửa
   sẽ ngừng ghi nhận lỗi.
4. **Người luôn là người merge.** Platform chỉ mở **draft** PR, không bao giờ merge, không force push,
   không apply migration, không ghi vào Jira.
5. **Ngân sách vòng lặp có giới hạn cứng.** `max_iterations` (mặc định 3) được đếm bởi một node riêng.
   Không agent nào "thuyết phục" được graph chạy thêm vòng.
6. **Ranh giới an toàn không đọc prompt.** `Workspace.resolve`, `assert_read_only` và `ToolGuard` kiểm tra
   *lời gọi tool*, nên prompt injection không vượt qua được.
7. **Không bao giờ commit bí mật.** `.env` đã nằm trong `.gitignore`. Token, API key chỉ để trong `.env`
   hoặc biến môi trường.

---

## 3. Cài đặt môi trường (Ngày 1)

### 3.1 Yêu cầu

| Thành phần | Phiên bản | Ghi chú |
|---|---|---|
| Python | **3.12** (khuyến nghị, giống Docker image) | `pyproject.toml` yêu cầu `>=3.12` |
| .NET SDK | 10 | Để đọc được `HW.slnx`; test host chạy trên ASP.NET Core 8 runtime |
| Git | bất kỳ | Tool VCS dùng để phát hiện branch và push |
| Docker Desktop | tuỳ chọn | Nếu muốn chạy platform trong container |

### 3.2 Cách A — Chạy trên máy (PowerShell, khuyến nghị)

```powershell
cd agent-platform
powershell -ExecutionPolicy Bypass -File scripts\bootstrap.ps1
```

Script `bootstrap.ps1` sẽ: tạo `.venv` → cài `requirements.txt` → copy `env.example` thành `.env`
(nếu chưa có) → chạy `healthcheck.py` → chạy toàn bộ test (offline).

Cài tay (nếu muốn tự làm từng bước):

```powershell
cd agent-platform
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
Copy-Item env.example .env
```

### 3.3 Điền file `.env`

Mở `agent-platform/.env`. Tối thiểu cần:

```dotenv
ANTHROPIC_API_KEY=sk-ant-...        # hoặc bỏ trống nếu đã `ant auth login`
AGENT_WORKSPACE_ROOT=..             # thư mục solution HW — nơi agent được đọc/ghi
AGENT_ALLOW_WRITES=false            # TUẦN ĐẦU: để false (chỉ đề xuất, không ghi file, không mở PR)
```

Jira / GitHub / Seq là **tuỳ chọn**. Thiếu thì tool tương ứng trả về lỗi "not configured"
rõ ràng, platform vẫn chạy.

> Biến môi trường thật (export trong shell) **luôn thắng** giá trị trong `.env`.

### 3.4 Kiểm tra

```powershell
python scripts/healthcheck.py                    # checklist: cái gì đã cấu hình, cái gì thiếu
python cli.py --dry-run run "/fixbug DEMO-1"     # chạy toàn bộ graph, 0 token
python -m pytest -q                              # toàn bộ test, chạy offline
```

Đọc output `healthcheck`:

| Nhãn | Ý nghĩa |
|---|---|
| `[  ok   ]` | Sẵn sàng |
| `[ warn  ]` | Thiếu nhưng platform tự fallback (ví dụ: không có `chromadb` thì dùng index lexical) |
| `[ FAIL  ]` | Chặn — phải sửa (ví dụ: thiếu gói `anthropic`, workspace không tồn tại) |

`python scripts/healthcheck.py --live` gọi model thật **một lần** để kiểm tra API key.

### 3.5 Cách B — Chạy bằng Docker

Chạy từ thư mục `HW/` (thư mục chứa `docker-compose.yml`):

```powershell
docker compose up -d                                                    # MariaDB, Redis, Seq
docker compose run --rm agent-platform status                           # xem platform đã nối những gì
docker compose run --rm agent-platform --dry-run run "/fixbug DEMO-1"   # dry-run
docker compose run --rm agent-platform run "/review HW.Api"             # chạy thật
```

- Dùng `docker compose run`, **không** dùng `up` — platform là CLI, chạy xong là thoát.
- Container đọc `agent-platform/.env` để lấy credential; địa chỉ Seq/Redis trong container đã được override sẵn.
- Workspace trong container là `/repo/HW`. Checkpoint, long-term memory và index Chroma nằm trong volume `agent-state`,
  nên không mất sau `run --rm`.

### ⚠️ Bẫy trên Windows: Git Bash làm hỏng dấu `/`

Nếu bạn dùng **Git Bash** (không phải PowerShell), MSYS sẽ tự đổi `"/fixbug DEMO-1"` thành đường dẫn Windows:

```
error: unknown command /c:/program.  Try: /analyzelog, /fixbug, ...
```

Ba cách tránh (đều đã kiểm chứng):

```bash
python cli.py --dry-run run "fixbug DEMO-1"                       # 1. bỏ dấu / — parser chấp nhận
MSYS_NO_PATHCONV=1 python cli.py run "/review HW.Api"             # 2. tắt path conversion
python cli.py fixbug DEMO-1 --max-iterations 2                    # 3. dùng lệnh tắt (không cần /)
```

PowerShell và `cmd` không bị lỗi này.

### ✅ Checkpoint 3
- [ ] `healthcheck.py` in ra `ready` (hoặc chỉ còn `warn`, không còn `FAIL`).
- [ ] Dry-run `/fixbug DEMO-1` in ra dòng dạng `[bugfix::...] status=success ...`.
- [ ] `pytest -q` xanh.
- [ ] `.env` **không** xuất hiện trong `git status`.

---

## 4. Các lệnh và cách dùng

### 4.1 Năm lệnh chính

Mỗi lệnh ánh xạ **đúng một** workflow. Lệnh làm gì thì workflow làm đúng việc đó.

| Lệnh | Alias | Workflow | Ghi file? | Khi nào dùng |
|---|---|---|---|---|
| `/fixbug <KEY>` | `/fix`, `/bugfix` | `bugfix` | Có + draft PR | Có ticket bug, cần sửa từ đầu đến cuối |
| `/review [paths]` | `/codereview` | `review` | **Không** | Review code trước khi commit/PR |
| `/unittest <path>` | `/gentest`, `/test` | `unittest` | Có (file test) | Bổ sung test cho code có sẵn |
| `/newfeature "<req>"` | `/feature` | `feature` | Có + draft PR | Làm tính năng mới dạng vertical slice |
| `/analyzelog` | `/analyselog`, `/logs`, `/trace` | `analyzelog` | **Không** | Có sự cố/exception, chưa biết nguyên nhân |

### 4.2 Cú pháp đầy đủ và ví dụ

**`/fixbug`**: sửa bug từ ticket đến PR đã review.

```powershell
python cli.py run "/fixbug BUG-123"
python cli.py run "/fixbug BUG-123 --correlation-id 8f2c1a --notes 'chỉ lỗi khi order rỗng'"
python cli.py run "/fixbug BUG-123 --max-iterations 2"
python cli.py fixbug BUG-123                     # lệnh tắt
```

Tuỳ chọn: `--correlation-id <id>`, `--notes <text>`, `--stack-trace <text>`, `--max-iterations <n>`.

**`/review`**: chỉ đọc, báo lỗi theo mức độ nghiêm trọng, không sửa gì.

```powershell
python cli.py run "/review HW.Application/Orders/CreateOrderHandler.cs HW.Api/Controllers/OrdersController.cs"
python cli.py run "/review HW.Application --objective 'tìm lỗi transaction và concurrency'"
python cli.py review HW.Api/Program.cs           # lệnh tắt
```

> Nên **luôn truyền path cụ thể**. Reviewer và các plugin chỉ xem các file có trong danh sách.

**`/unittest`**: sinh test xUnit cho một file hoặc một symbol, rồi chạy.

```powershell
python cli.py run "/unittest HW.Application/Orders/CreateOrderHandler.cs"
python cli.py run "/unittest HW.Domain/Orders/Order.cs --symbol AddLine --focus 'số lượng âm, trùng sản phẩm'"
```

Tuỳ chọn: `--symbol <name>`, `--framework xunit`, `--focus <text>`, `--max-iterations <n>`.

**`/newfeature`**: planner → coding → unittest → review → documentation.

```powershell
python cli.py run "/newfeature 'Thêm endpoint huỷ đơn hàng' --issue-key HW-42 --constraints 'dùng CQRS + outbox' --out-of-scope 'hoàn tiền'"
```

Tuỳ chọn: `--issue-key <KEY>`, `--constraints <text>`, `--out-of-scope <text>`, `--objective <text>`, `--max-iterations <n>`.

> Mô tả càng rõ phạm vi (`--constraints`, `--out-of-scope`) thì agent càng ít tự quyết thay bạn.
> Feature liên quan tới quyết định sản phẩm, nên cân nhắc bật human-in-the-loop (mục 7.2).

**`/analyzelog`**: dựng lại timeline sự cố, chỉ ra lỗi *đầu tiên thật sự*, kèm trích dẫn log.

```powershell
python cli.py run "/analyzelog --correlation-id 8f2c1a"
python cli.py run "/analyzelog 8f2c1a"                                  # correlation id dạng positional
python cli.py run "/analyzelog --query 'DbUpdateConcurrencyException' --since 120 --level Error"
```

Cần **ít nhất một** trong `--correlation-id` hoặc `--query`.

### 4.3 Luồng làm việc khuyến nghị

```
 exception / log lỗi, chưa rõ nguyên nhân
            │
            ▼
      /analyzelog  ──► root cause + correlation id   (chỉ đọc)
            │
            ▼
      /fixbug KEY --correlation-id <id>  ──► fix + test + draft PR
            │
            ▼
      /review <files>  ──► kiểm tra lại trước khi bạn mở PR ready-for-review
```

`/analyzelog` **cố ý chỉ đọc**. Khi đã tìm ra nguyên nhân, hãy chạy `/fixbug` với correlation id đó.
Đừng mong workflow chẩn đoán tự đi sửa code.

### 4.4 Lệnh vận hành

```powershell
python cli.py list agents        # cũng có: tools, workflows, commands, plugins, skills, mcps
python cli.py graph bugfix       # in graph của workflow dạng mermaid
python cli.py mcp health         # MCP server nào reachable
python cli.py status             # toàn bộ những gì đã được nối (JSON)
python cli.py workflow bugfix --set issue_key=BUG-1 --set notes=abc   # gọi thẳng workflow, bỏ qua parser lệnh
```

Cờ toàn cục (đặt **trước** subcommand): `--dry-run`, `--json`, `--config <path>`, `--log-level DEBUG`.

```powershell
python cli.py --json run "/review HW.Api/Program.cs" > review.json     # log ra stderr, kết quả ra stdout
```

### ✅ Checkpoint 4
- [ ] Chạy `list agents`, `list skills`, `graph feature`.
- [ ] Chạy dry-run cả 5 lệnh (mỗi lệnh một lần).
- [ ] Giải thích được vì sao `/review` và `/analyzelog` không ghi file.

---

## 5. Đọc kết quả một lần chạy

### 5.1 Dòng tóm tắt

```
[bugfix::8fcdbf812bc8] status=success iteration=1/3 changes=2 findings=1 tests=pass
```

| Trường | Ý nghĩa |
|---|---|
| `bugfix::8fcdbf812bc8` | workflow + **correlation id** (dùng để grep log và audit) |
| `status` | trạng thái cuối (bảng dưới) |
| `iteration=1/3` | đã quay lại bao nhiêu vòng / ngân sách |
| `changes` | số file agent đã thay đổi |
| `findings` | số phát hiện của review |
| `tests` | `pass` / `fail` / `n/a` (workflow chỉ đọc) |

Sau dòng tóm tắt là các mục **Plan**, **Root cause**, **Changes**, **Findings** (`[severity] path:line message`),
**Pull request**, **Errors**.

### 5.2 Trạng thái và exit code

| `status` | Exit code | Nghĩa là | Bạn làm gì |
|---|---|---|---|
| `success` | `0` | Approve + build xanh + test xanh | **Vẫn tự đọc diff và chạy lại test** trước khi mở PR ready |
| `failed` | `1` | Hết ngân sách, build/test đỏ, hoặc lỗi runtime | Đọc mục Errors, xem `logs/`, chạy lại với context rõ hơn |
| — | `2` | Sai cú pháp lệnh | Xem usage in ra ở stderr |
| `awaiting_human` | `3` | Graph dừng chờ người duyệt | Duyệt, rồi resume (cần checkpoint `sqlite`) |

> **`success` không có nghĩa là "merge luôn".** Nó có nghĩa là "đủ bằng chứng để một người xem xét".
> PR vẫn là draft. Người review cuối cùng là **bạn** và team.

### ✅ Checkpoint 5
- [ ] Biết lấy correlation id từ dòng tóm tắt.
- [ ] Biết exit code `3` nghĩa là gì.

---

## 6. Kiến trúc cần nắm

### 6.1 Quy tắc phụ thuộc

```
       commands  ->  workflows  ->  graph  ->  agents
                                                 |
                            +--------------------+--------------------+
                            v                    v                    v
                          tools               memory                llms
                            |                    |                    |
                      integrations            chroma            anthropic SDK
                          + MCP
```

- Phụ thuộc chỉ đi **vào trong**. `agents/` không import Anthropic SDK; `tools/` không biết workflow là gì.
- `container.py` là **nơi duy nhất** khởi tạo đối tượng (composition root).

| Package | Trách nhiệm |
|---|---|
| `agents/` | Mỗi agent một chuyên môn: prompt, tools, memory, output schema |
| `workflows/` | *Hình dạng* công việc: agent nào chạy, thứ tự nào, vòng lặp ở đâu |
| `graph/` | State, node, routing (`edges.py`), checkpoint |
| `tools/` | Năng lực agent gọi được, test độc lập được |
| `prompts/templates/` | Prompt vai trò của từng agent, **dạng Markdown** |
| `skills/` | Kiến thức domain nạp vào prompt (CQRS, EF Core, xUnit, MES...) |
| `plugins/` | Lăng kính review (security, performance, architecture), migration guard |
| `hooks/` | Sự kiện before/after, có quyền veto, audit trail |
| `memory/` | Short term / long term / vector |
| `configs/` | `platform.yaml`, `agents.yaml`, `mcps.yaml`, settings có kiểu |

### 6.2 Tám agent

| Agent | Việc | Tools |
|---|---|---|
| `supervisor` | Định tuyến khi edge cố định không đủ | không có, cố ý |
| `planner` | Điều tra rồi lập kế hoạch ngắn, kiểm chứng được | chỉ đọc |
| `coding` | Thực thi kế hoạch, build | đọc, ghi, build |
| `unittest` | Viết test bắt được lỗi, chạy test | đọc, ghi, test |
| `review` | Đánh giá đúng/sai và rủi ro | **chỉ đọc** |
| `log_analysis` | Dựng timeline sự cố kèm trích dẫn | logs, search, database (chỉ SELECT) |
| `jira` | Biến ticket thành mục tiêu rõ ràng | Jira (chỉ đọc) |
| `documentation` | Viết mô tả PR và cập nhật tài liệu | đọc, search, ghi |

Mỗi agent khai báo một **JSON Schema** cho câu trả lời. Schema vừa nằm trong prompt, vừa dùng để validate,
nên thiếu field sẽ bị bắt ngay thay vì âm thầm thành chuỗi rỗng.

### 6.3 Graph của `/fixbug`

```
START -> jira -> triage -> planner -> coding -> unittest -> plugins -> review
                              ^           |                              |
                              |           v                              v
                              +- iterate <-- (build/test đỏ) ---- (không approve)
                                                                         |
                                                                    (approve)
                                                                         v
                                                                  deliver -> END
```

Tự in graph thật: `python cli.py graph bugfix`.

### 6.4 Ba quy tắc routing (hay bị hỏi khi review)

Toàn bộ quyết định routing nằm trong `graph/edges.py`, là các hàm thuần không có I/O.

1. **Test đỏ quay về `coding`, không về `planner`.** Kế hoạch thường vẫn đúng; lập kế hoạch lại sẽ vứt bỏ
   phần việc đã đúng. Chỉ khi review bác bỏ *cách tiếp cận* mới tốn một vòng đầy đủ.
2. **Không có bằng chứng ≠ thành công.** `verification_passed` trả về `False` khi không có kết quả test.
   Workflow chỉ đọc (`review`, `analyzelog`) phải khai báo rõ `verification_required: false`.
3. **Approve + bằng chứng, không bao giờ chỉ một trong hai.** Ý kiến reviewer không thắng được build đỏ,
   và build xanh không thắng được một finding blocking.

### 6.5 Memory ba lớp

| Lớp | Câu hỏi | Lưu ở | Tuổi thọ |
|---|---|---|---|
| short term | vừa nói gì? | deque có giới hạn | một run |
| long term | biết gì về project? | `.state/long_term.json` | mãi mãi |
| vector | cái gì *liên quan*? | ChromaDB (`.state/chroma`) | mãi mãi |

Chỉ những thứ **đã xác nhận** (root cause chắc chắn, kết luận sự cố độ tin cậy cao) mới được ghi vào long term.
Muốn agent "nhớ" code tốt hơn, hãy index codebase (mục 11.3).

### ✅ Checkpoint 6
- [ ] Vẽ lại được graph `/fixbug` mà không nhìn tài liệu.
- [ ] Trả lời được: "Vì sao agent `review` không có quyền ghi?"
- [ ] Trả lời được: "Một run không có kết quả test thì được tính là gì?"

---

## 7. Cấu hình

### 7.1 Thứ tự ưu tiên (cái sau thắng)

1. Giá trị mặc định trong `configs/settings.py`
2. `configs/platform.yaml` (hoặc file truyền qua `--config`)
3. Biến môi trường / `.env`

### 7.2 Biến môi trường hay dùng

| Biến | Ghi đè | Mặc định | Ghi chú |
|---|---|---|---|
| `AGENT_MODEL` | `llm.model` | `claude-sonnet-5` | ID chính xác, **không** thêm hậu tố ngày |
| `AGENT_EFFORT` | `llm.effort` | `high` | `low` \| `medium` \| `high` \| `xhigh` \| `max` |
| `AGENT_DRY_RUN` | `llm.dry_run` | `false` | Tương đương cờ `--dry-run` |
| `AGENT_MAX_ITERATIONS` | `graph.max_iterations` | `3` | Ngân sách vòng lặp |
| `AGENT_HUMAN_IN_THE_LOOP` | `graph.human_in_the_loop` | `false` | `true` = dừng trước node `human` |
| `AGENT_WORKSPACE_ROOT` | `workspace.root` | `.` | Đặt `..` để trỏ vào solution HW |
| `AGENT_ALLOW_WRITES` | `workspace.allow_writes` | `true` | `false` = chỉ đề xuất, không ghi file, không mở PR |
| `AGENT_BUILD_COMMAND` | `workspace.build_command` | `dotnet build` | |
| `AGENT_TEST_COMMAND` | `workspace.test_command` | `dotnet test` | |
| `AGENT_LOG_LEVEL` | `observability.log_level` | `INFO` | |
| `JIRA_BASE_URL`, `JIRA_PROJECT`, `JIRA_EMAIL`, `JIRA_API_TOKEN` | integrations | — | Tuỳ chọn |
| `GITHUB_REPO`, `GITHUB_TOKEN` | integrations | — | Cần để mở draft PR |
| `SEQ_BASE_URL`, `SEQ_API_KEY` | integrations | — | Để `/analyzelog` đọc log từ Seq |

Danh sách đầy đủ: `ENV_OVERRIDES` trong `configs/settings.py`.

**Human-in-the-loop:** đặt `AGENT_HUMAN_IN_THE_LOOP=true` và `graph.checkpoint_backend: sqlite` trong
`platform.yaml`. Khi graph dừng (exit code `3`), run có thể resume trên cùng thread id. Nếu backend là `none`,
run chỉ báo `awaiting_human` rồi kết thúc.

### 7.3 Các file YAML

| File | Nội dung | Khi nào sửa |
|---|---|---|
| `configs/platform.yaml` | Model, memory, graph, workspace, plugin bật | Thay đổi hành vi toàn platform |
| `configs/agents.yaml` | Thu hẹp tools và skills cho từng agent | Cho/không cho agent dùng một tool |
| `configs/mcps.yaml` | 8 MCP server, **tất cả tắt mặc định** | Chỉ bật khi command/url và credential đã có |
| `configs/logging.yaml` | Cấu hình logging | Hiếm khi |

> Thay đổi file YAML trong repo là thay đổi **cho cả team** → phải qua PR.
> Thử nghiệm cá nhân thì dùng biến môi trường hoặc `--config my-local.yaml`.

---

## 8. An toàn — những gì platform KHÔNG BAO GIỜ làm

| Platform **không** | Cơ chế đảm bảo |
|---|---|
| Đọc/ghi ngoài workspace | `Workspace.resolve` giải symlink, từ chối path ngoài root và các glob bị cấm (`**/.git/**`, `**/*.pem`, `**/secrets/**`, `**/*.env`) |
| Chạy SQL ghi dữ liệu | `assert_read_only` chỉ cho một câu `SELECT` / `WITH … SELECT`, đã bỏ comment trước khi kiểm tra |
| Ghi file khi đang read-only | `ToolGuard` veto `write_file` và `create_pull_request` khi `allow_writes: false` |
| Nối lệnh shell | `Workspace.run` tách lệnh bằng `shlex`, chạy không qua shell |
| Mở PR từ `main`/`master` | `create_pull_request` từ chối |
| Merge PR | PR luôn mở ở dạng **draft** |
| Force push | Không bao giờ |
| Apply migration | Plugin migration chỉ kiểm tra và báo cáo |
| Ghi vào Jira | Chuyển trạng thái ticket là việc của người |

**Trách nhiệm của bạn:**
- Chạy thật trên **feature branch**, không bao giờ trên `main`.
- Đọc **toàn bộ** diff agent tạo ra trước khi đổi PR từ draft sang ready.
- Không dán secret, dữ liệu khách hàng, connection string production vào `--notes`, `--query`.
- Không trỏ `AGENT_WORKSPACE_ROOT` vào thư mục chứa dữ liệu nhạy cảm.
- Không tắt `ToolGuard`, không nới `denied_globs` nếu chưa được review.

---

## 9. Chi phí token

- **Effort là cần gạt đầu tiên.** `high` phù hợp với hầu hết agent; `review` và `coding` đáng dùng `xhigh`;
  `jira` / `documentation` chạy `medium` vì chủ yếu là chép lại.
- **Luôn dry-run trước** khi thử một cấu hình/workflow mới. Dry-run tốn 0 token.
- **Giảm `--max-iterations`** cho việc nhỏ (`--max-iterations 1` hoặc `2`).
- **Prompt caching:** system prompt được xếp phần ổn định trước, phần task sau. Xem
  `cache_read_input_tokens` trong `logs/audit.jsonl`. Nếu bằng `0` suốt một run thì có thứ gì đó đang phá cache,
  hãy báo cho maintainer.
- Truyền **path cụ thể** cho `/review` và `/unittest` thay vì cả project.

---

## 10. Mở rộng platform

> Mọi mở rộng đều phải qua PR, có test, và `pytest -q` xanh. Test chạy hoàn toàn offline
> (`EchoLLM` thay Claude, thư mục tạm thay repo).

### 10.1 Thêm một Skill (dễ nhất, không cần code)

```
skills/my_skill/
    skill.yaml      name, description, tags, applies_to
    prompt.md       hướng dẫn được chèn vào system prompt
    examples/       ví dụ mẫu (tuỳ chọn)
```

Ví dụ `skill.yaml` (theo mẫu `skills/cqrs/skill.yaml`):

```yaml
name: my_skill
version: "1.0"
description: >-
  Một câu mô tả skill này dạy agent điều gì.
tags: [dotnet, backend]
applies_to: [planner, coding, review]
```

Skill được nạp tự động khi khởi động. Muốn agent dùng nó thì thêm tên vào `skills:` của agent trong `configs/agents.yaml`.
Kiểm tra: `python cli.py --dry-run list skills`.

### 10.2 Sửa Prompt

Prompt nằm ở `prompts/templates/<agent>.md`, là Markdown thường. Sửa prompt là sửa **hành vi**, nên cần:
mô tả trong PR *hành vi nào thay đổi*, và chạy thật một ví dụ trước/sau để so sánh.

### 10.3 Thêm một Tool

```python
class InspectQueuesTool(Tool):
    name = "inspect_queues"
    description = "List the message queues and their depth."   # viết cho model đọc
    read_only = True
    input_schema = {"type": "object", "properties": {"prefix": {"type": "string"}}, "required": []}

    def __init__(self, broker: BrokerClient) -> None:
        self.broker = broker          # inject qua constructor để test độc lập được

    def _execute(self, prefix: str | None = None) -> ToolResult:
        return ToolResult.success(self.name, self.broker.depths(prefix))
```

1. Đăng ký trong `tools/__init__.py :: build_default_tools`.
2. Thêm tên tool vào `tools:` của agent liên quan trong `configs/agents.yaml`.
3. Viết test trong `tests/test_tools.py`.

Quy tắc: tool **không raise** cho lỗi nghiệp vụ, mà trả về `ToolResult(ok=False)`, để model biết mà sửa.
Tool có ghi thì `read_only = False`; nếu là tool ghi mới, hãy cân nhắc thêm vào `ToolGuard.write_tools`.

### 10.4 Thêm một Plugin

Implement `register` / `validate` / `execute` / `shutdown`, rồi thêm tên vào `enabled_plugins` trong `platform.yaml`.
Plugin lỗi khi validate sẽ bị tắt; plugin raise giữa chừng được ghi là kết quả thất bại, không làm sập run.
Muốn thêm một lăng kính review nhanh thì dùng `LLMReviewPlugin` với một chuỗi `focus`.

### 10.5 Thêm một Agent

Kế thừa `BaseAgent`, cài `build_prompt`, `apply`, `output_schema`, thêm vào `AGENT_TYPES`,
viết `prompts/templates/<name>.md`. Tool loop, hook, memory và xử lý lỗi đã được kế thừa sẵn.

> Thêm agent / sửa `graph/edges.py` là thay đổi kiến trúc. Hãy **trao đổi với maintainer trước** khi code.

---

## 11. Quan sát và debug

### 11.1 Log

| File | Nội dung |
|---|---|
| `logs/platform.log` | Log người đọc được |
| `logs/audit.jsonl` | Mỗi sự kiện một dòng JSON (agent, tool, LLM call, lỗi); credential đã được che |

```powershell
# Mọi sự kiện của một run (dùng correlation id từ dòng tóm tắt)
Select-String -Path logs\audit.jsonl -Pattern "8fcdbf812bc8"

# Tool nào đã bị gọi (các event: BeforeAgentExecution, AfterAgentExecution, BeforeToolExecution,
# AfterToolExecution, BeforeLLMCall, AfterLLMCall, OnError)
Select-String -Path logs\audit.jsonl -Pattern '"event": "AfterToolExecution"' | Select-Object -Last 20
```

Tăng mức log: `python cli.py --log-level DEBUG run "..."`.

### 11.2 Kiểm tra từng phần

```powershell
python scripts/healthcheck.py        # dependency, credential, workspace, workflow, MCP, Jira/GitHub
python cli.py mcp health             # từng MCP server
python cli.py mcp health seq         # một server
python main.py                       # self-check: dựng toàn bộ object graph + chạy /fixbug DEMO-1 offline
```

### 11.3 Index codebase cho vector memory

```powershell
python scripts/index_codebase.py --glob "*.cs" --glob "*.md"
python scripts/index_codebase.py --clear --glob "*.cs"          # xoá index cũ rồi index lại
```

Chưa index thì `recall` chỉ trả về những gì platform tự học được. Nên index lại sau những đợt refactor lớn.

---

## 12. Lỗi thường gặp (Troubleshooting)

| Triệu chứng | Nguyên nhân | Cách xử lý |
|---|---|---|
| `RuntimeError: the anthropic package is required` | Chưa cài dependency; khi thiếu `anthropic` chỉ có `--dry-run` chạy được | Kích hoạt `.venv`, `pip install -r requirements.txt` |
| `error: unknown command /c:/program...` | Git Bash đổi `/fixbug` thành path Windows | Bỏ dấu `/`, hoặc `MSYS_NO_PATHCONV=1`, hoặc dùng PowerShell (mục 3.5) |
| `error: an issue key is required` (exit 2) | Thiếu tham số bắt buộc | Xem usage in ra ở stderr |
| Dry-run báo `success` nhưng code chưa sửa | **Đúng như thiết kế.** Dry-run không gọi model | Chạy thật (bỏ `--dry-run`) |
| `tool 'write_file' is disabled: the platform is in read-only mode` | `AGENT_ALLOW_WRITES=false` | Đúng như mong muốn khi đang học; bật `true` khi có mentor |
| Build lỗi `MSB3021` / `MSB3027` | `HW.Api` đang chạy, khoá file DLL. **Đây không phải lỗi code** | Tắt process `HW.Api` rồi chạy lại. Chỉ `CS####` mới là lỗi code thật |
| `workspace ... does not exist` | `AGENT_WORKSPACE_ROOT` sai | Chạy local thì đặt `..`; trong Docker đã là `/repo/HW` |
| `integration jira/github — not configured` | Thiếu biến `JIRA_*` / `GITHUB_*` | Tuỳ chọn; điền vào `.env` nếu cần |
| `create_pull_request` bị từ chối | Đang ở `main`/`master` | Tạo feature branch trước khi chạy |
| `mcp <name>` unhealthy | Server bật nhưng thiếu command/credential | Tắt lại trong `mcps.yaml` hoặc bổ sung cấu hình |
| Run `failed` với `iteration=3/3` | Hết ngân sách vòng lặp | Đọc Findings/Errors, bổ sung `--notes` / `--constraints`, chia nhỏ việc |
| `cache_read_input_tokens` luôn bằng 0 | Prefix prompt bị thay đổi mỗi lượt | Báo maintainer, kèm correlation id |

---

## 13. Bài tập thực hành

Làm theo thứ tự. Ghi lại lệnh đã chạy và kết quả vào ghi chú onboarding của bạn.

**Bài 1: Dry-run toàn bộ (Ngày 1, 0 token)**
Chạy dry-run cả 5 lệnh. Với mỗi lệnh, dùng `python cli.py graph <workflow>` để in graph và đối chiếu với output.
*Câu hỏi:* Vì sao `/analyzelog` chỉ có 2 node và `iteration=0/1`?

**Bài 2: Review thật (Ngày 2, chỉ đọc)**
Đặt `AGENT_ALLOW_WRITES=false`. Chạy `/review` trên 1–2 file trong `HW.Application`.
*Yêu cầu:* Với mỗi finding, tự mở file và xác nhận nó **đúng hay sai**. Ghi lại ít nhất một finding bạn không đồng ý và lý do.

**Bài 3: Đọc audit trail**
Lấy correlation id từ bài 2. Dùng `Select-String` lọc `logs/audit.jsonl`.
*Câu hỏi:* Agent `review` đã gọi những tool nào? Bao nhiêu LLM call? `cache_read_input_tokens` là bao nhiêu?

**Bài 4: Phân tích log**
Khởi động `docker compose up -d` (có Seq), chạy `HW.Api`, tạo một request lỗi, lấy correlation id trong Seq
(`http://localhost:5341`). Chạy `/analyzelog <id>`.
*Yêu cầu:* So sánh "first real failure" agent đưa ra với stack trace gốc trong Seq.

**Bài 5: Thêm một skill**
Tạo `skills/hw_conventions/` với `skill.yaml` + `prompt.md` mô tả 3 quy ước code của team. Gắn vào agent `review`
trong `agents.yaml`. Kiểm tra bằng `list skills` và `list agents`. Chạy `pytest -q`. Mở PR (chưa merge) để mentor review.

**Bài 6: Sinh unit test (Tuần 2, có mentor)**
Trên feature branch, `AGENT_ALLOW_WRITES=true`. Chạy `/unittest` cho một handler chưa có test.
*Yêu cầu:* Sửa thử code gốc cho sai (ví dụ đảo điều kiện), chạy lại test. Test agent sinh ra **có đỏ không**?
Nếu không đỏ thì test đó vô dụng. Hoàn tác thay đổi thử.

**Bài 7: Fix bug end-to-end (Tuần 2, có mentor)**
Chọn một ticket bug nhỏ mentor chỉ định. Chạy `/fixbug KEY --max-iterations 2`. Đọc toàn bộ diff và mô tả draft PR.
*Yêu cầu:* Trình bày lại cho mentor: root cause là gì, test nào chứng minh fix, bạn có thay đổi gì trước khi chuyển PR sang ready.

---

## 14. Checklist hoàn thành onboarding

**Môi trường**
- [ ] `.venv` + `requirements.txt` đã cài, `healthcheck.py` không còn `FAIL`
- [ ] `.env` đã điền, **không** bị commit
- [ ] Chạy được bằng cả PowerShell và Docker
- [ ] `pytest -q` xanh

**Hiểu biết**
- [ ] Giải thích được 7 nguyên tắc vàng (mục 2)
- [ ] Vẽ lại graph `/fixbug` và 3 quy tắc routing (mục 6)
- [ ] Biết ý nghĩa `status` và exit code `0/1/2/3` (mục 5)
- [ ] Liệt kê được những gì platform không bao giờ làm (mục 8)

**Thực hành**
- [ ] Hoàn thành bài 1–5
- [ ] Hoàn thành bài 6–7 cùng mentor
- [ ] Có ít nhất một PR đã được review (bài 5)

**Ký xác nhận:** Thành viên: ________ · Mentor: ________ · Ngày: ________

---

## Phụ lục: Tham chiếu nhanh

```powershell
# Cài đặt
powershell -ExecutionPolicy Bypass -File scripts\bootstrap.ps1
python scripts/healthcheck.py [--live]

# Chạy
python cli.py [--dry-run] [--json] run "/fixbug BUG-123 [--correlation-id X] [--max-iterations N]"
python cli.py run "/review <path> [<path> ...]"
python cli.py run "/unittest <path> [--symbol Name] [--focus text]"
python cli.py run "/newfeature '<yêu cầu>' [--issue-key K] [--constraints t] [--out-of-scope t]"
python cli.py run "/analyzelog <correlation-id> | --query <text> [--since 60] [--level Error]"

# Vận hành
python cli.py list agents|tools|workflows|commands|plugins|skills|mcps
python cli.py graph <workflow>
python cli.py mcp health [server]
python cli.py status
python main.py                                   # self-check offline
python scripts/index_codebase.py --glob "*.cs"

# Docker (từ thư mục HW/)
docker compose run --rm agent-platform <các tham số giống cli.py>
```

**Người liên hệ:** Maintainer Agent Platform: ________ · Kênh hỏi đáp: ________
