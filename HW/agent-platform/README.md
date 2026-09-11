# Agent Platform

An enterprise agentic AI platform: eight specialist agents orchestrated by a
LangGraph state machine, with tool calling, MCP servers, plugins, hooks, a
three-layer memory system, a skills registry and a command surface.

Built on **Claude Sonnet 5** (`claude-sonnet-5`), **LangGraph**, **ChromaDB**,
**MCP** and **OpenTelemetry**. Python 3.12.

```
/fixbug BUG-123   ->  ticket -> logs -> plan -> code -> build -> tests -> review -> pull request
```

---

## Quick start

```bash
cd agent-platform
python -m venv .venv && source .venv/bin/activate      # or scripts\bootstrap.ps1
pip install -r requirements.txt
cp env.example .env                                    # then fill in credentials

python scripts/healthcheck.py        # what is configured, what is missing
python cli.py --dry-run run "/fixbug DEMO-1"           # full graph, zero tokens
python cli.py run "/fixbug BUG-123"                    # for real
```

`--dry-run` replaces the model with a deterministic stub that satisfies each
agent's output schema. It proves every node executes and every state patch
merges. It says nothing about your code — never read a dry-run result as
evidence about a change.

### Commands

| Command | Workflow | What it does |
|---|---|---|
| `/fixbug <KEY>` | `bugfix` | Ticket to reviewed pull request, end to end |
| `/review [paths]` | `review` | Read-only review: correctness, security, performance, architecture |
| `/unittest <path>` | `unittest` | Generate tests for a file or symbol, then run them |
| `/newfeature "<req>"` | `feature` | Build a feature as a tested vertical slice |
| `/analyzelog` | `analyzelog` | Reconstruct an incident timeline from logs |

```bash
python cli.py list agents          # also: tools, workflows, commands, plugins, skills, mcps
python cli.py graph bugfix         # the workflow graph, as mermaid
python cli.py mcp health           # per-server reachability
python cli.py status               # everything that is wired up
```

Logs go to stderr, results to stdout, so `--json` output pipes cleanly.

---

## Architecture

### The dependency rule

Dependencies point inward. Agents depend on abstractions; adapters implement
them; `container.py` is the only module that knows how the pieces fit together.

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

Nothing in `agents/` imports the Anthropic SDK. Nothing in `tools/` knows what a
workflow is. `graph/nodes.py` imports agents only under `TYPE_CHECKING`, because
agents depend on the state the graph defines — the one place the arrows would
otherwise meet.

### Layers

| Package | Responsibility |
|---|---|
| `agents/` | One specialist each: prompt, tools, memory, state access, output schema |
| `workflows/` | The *shape* of a job: which agents run, in what order, what loops |
| `graph/` | State, nodes, routing, checkpointing, compilation |
| `tools/` | Capabilities agents can call; each independently testable |
| `mcps/` | MCP server wrappers, transports and registration |
| `plugins/` | Extensions: review lenses, migration guard |
| `hooks/` | Before/after events with veto, plus the audit trail |
| `memory/` | Short term, long term, vector |
| `prompts/` | Agent role prompts as Markdown, not string literals |
| `skills/` | Reusable domain knowledge loaded into prompts |
| `commands/` | Parse `/fixbug BUG-123` into workflow arguments |
| `llms/` | Provider-neutral contract plus the Claude adapter |
| `configs/` | Typed settings, YAML, logging and OpenTelemetry |
| `integrations/` | Jira, GitHub and Seq REST adapters |

### The eight agents

| Agent | Job | Tools |
|---|---|---|
| `supervisor` | Routes work when a fixed edge cannot express the decision | none, by design |
| `planner` | Investigates, then produces a short checkable plan | read-only |
| `coding` | Implements the plan, builds | read, write, build |
| `unittest` | Writes tests that would catch the defect, runs them | read, write, test |
| `review` | Judges correctness and risk; read-only on purpose | read-only |
| `log_analysis` | Reconstructs an incident timeline with quoted evidence | logs, search, database |
| `jira` | Turns a ticket into a stated objective | Jira |
| `documentation` | Writes the pull request body and doc updates | read, search, write |

Each declares a JSON Schema for its answer. The schema goes into the system
prompt *and* validates the reply, so a missing field is caught rather than
silently becoming an empty string downstream.

### The bug fix graph

```
START -> jira -> triage -> planner -> coding -> unittest -> plugins -> review
                              ^           |                              |
                              |           v                              v
                              +- iterate <-- (red build / red tests) --- (not approved)
                                                                         |
                                                                    (approved)
                                                                         v
                                                                  deliver -> END
```

Two routing decisions carry most of the design:

**A red test goes back to `coding`, not to `planner`.** The plan is usually
still right; re-planning throws away work that was already correct. Only a
review that rejects the *approach* costs a full iteration.

**Approval alone is never success.** `route_after_review` requires the model's
approval *and* a green build *and* green tests. A reviewer's opinion cannot
override the evidence, and absence of evidence is not a pass — a run with no
test results has not passed. Read-only workflows say so explicitly by setting
`verification_required: false`, rather than being quietly exempted.

The iteration budget is enforced by its own node, so no agent can talk the loop
into another round.

---

## Determinism and safety

Three boundaries never read the prompt, so a prompt-injected instruction cannot
argue its way past them:

- **`Workspace.resolve`** — resolves symlinks, rejects anything outside the
  configured root, refuses denied globs. Every path from a model goes through it.
- **`assert_read_only`** — parses the SQL. A single `SELECT` or `WITH … SELECT`,
  or the call is refused. Comments are stripped first, so a keyword hidden in one
  changes nothing.
- **`ToolGuard`** — a `BeforeToolExecution` hook that inspects the *call*.
  In read-only mode (`workspace.allow_writes: false`) writes and pull requests
  are vetoed platform-wide.

Pull requests open as drafts. `create_pull_request` refuses to run from `main`
or `master`. `git push --force` is never issued. The migration plugin inspects
schema changes and never applies them.

---

## Configuration

`configs/platform.yaml` is the source of truth; environment variables override
it (see `ENV_OVERRIDES` in `configs/settings.py`). The pieces worth knowing:

```yaml
llm:
  model: claude-sonnet-5     # exact model id -- never append a date suffix
  effort: high               # low | medium | high | xhigh | max
  thinking: true             # adaptive thinking; budget_tokens is rejected by this model
graph:
  max_iterations: 3          # how many times a run may go back around
  human_in_the_loop: false   # true pauses before the `human` node
workspace:
  root: .                    # what the agents may read and write
  allow_writes: true         # false = propose changes, write nothing
```

`configs/agents.yaml` narrows each agent's tools and skills.
`configs/mcps.yaml` declares the MCP servers — every one disabled by default.

### Cost

Effort is the first quality-trading lever. `high` is the sweet spot for most of
these agents; the review and coding agents are the ones that repay `xhigh`, and
`jira`/`documentation` run at `medium` because they are mostly transcription.
System prompts are built as cacheable blocks with the breakpoint on the last
stable one, and the volatile task text is a user message — so the cached prefix
survives between turns. Check `usage.cache_read_input_tokens` in the audit trail;
if it is zero across a run, something is invalidating the prefix.

---

## Extending it

### A tool

```python
class InspectQueuesTool(Tool):
    name = "inspect_queues"
    description = "List the message queues and their depth."   # written for the model
    read_only = True
    input_schema = {"type": "object", "properties": {"prefix": {"type": "string"}}, "required": []}

    def __init__(self, broker: BrokerClient) -> None:
        self.broker = broker          # injected, so the tool is testable alone

    def _execute(self, prefix: str | None = None) -> ToolResult:
        return ToolResult.success(self.name, self.broker.depths(prefix))
```

Register it in `tools/__init__.py :: build_default_tools`, then name it in the
relevant agent's `tools` list in `configs/agents.yaml`.

### A skill

Drop a folder into `skills/`:

```
skills/my_skill/
    skill.yaml      name, description, tags, applies_to
    prompt.md       the guidance injected into the system prompt
    examples/       optional worked examples
```

No code change. It is picked up at startup and any agent can list it.

### A plugin

Implement `register` / `validate` / `execute` / `shutdown`, then add its name to
`enabled_plugins`. A plugin that fails validation is disabled rather than left
half-wired, and one that raises during a stage is recorded as a failed result —
it cannot take a run down.

`LLMReviewPlugin` gives you a review lens for the cost of one `focus` string.

### An agent

Subclass `BaseAgent`, supply `build_prompt`, `apply` and `output_schema`, add it
to `AGENT_TYPES`, and write `prompts/templates/<name>.md`. The tool loop, hooks,
memory writes and error handling are inherited.

---

## Degradation

Optional dependencies are genuinely optional. The platform reports which path it
took rather than pretending:

| Missing | What happens |
|---|---|
| `langgraph` | `SimpleGraph` runs the same `GraphSpec`, with the same routing |
| `chromadb` | Vector memory falls back to a lexical index; `backend` says which |
| `mcp` | Stdio MCP servers report unhealthy with the reason |
| `opentelemetry` | Tracing becomes a no-op |
| `anthropic` | Only `--dry-run` works — the health check reports this as blocking |

---

## Tests

```bash
python -m pytest -q                    # 138 tests
python -m pytest tests/test_graph.py   # routing only, no model
```

Everything runs offline. `EchoLLM` replaces Claude, a temporary directory
replaces the repository, and no MCP server is contacted. The routing tests
assert on hand-built states, because routing is the part of the platform that
must behave identically every time.

---

## Layout

```
agent-platform/
├── agents/          eight specialists + registry
├── workflows/       five workflows + registry
├── graph/           state, nodes, edges, checkpoints, builder
├── tools/           ten tools + workspace sandbox + registry
├── mcps/            eight server wrappers, transports, manager
├── plugins/         security, performance, architecture, migration
├── hooks/           events, manager, audit trail, guards
├── memory/          short term, long term, vector, manager
├── prompts/         loader + one template per agent
├── skills/          seven skill packages
├── commands/        parsing, registry, five commands
├── llms/            contract, Claude adapter, offline stub
├── configs/         settings, YAML, logging, telemetry
├── integrations/    Jira, GitHub, Seq
├── logs/            platform.log, audit.jsonl
├── scripts/         bootstrap, healthcheck, index_codebase
├── tests/           138 tests, all offline
├── container.py     the composition root
├── cli.py           operator surface
└── main.py          programmatic entry point + self check
```
