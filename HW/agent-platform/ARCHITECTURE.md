# Architecture

Why the platform is shaped the way it is. The README covers what it does; this
covers the decisions, including the ones that cost something.

---

## 1. The core loop

Every workflow is the same four beats: **gather → act → verify → decide**.

```
planner   gather   investigate, then state a plan and how it will be checked
coding    act      make the change
unittest  verify   produce evidence
review    decide   is this finished, or does it go back around?
```

The value is in the fourth beat being a *separate* step with *separate*
authority. An agent that both makes a change and judges it will judge it
favourably. So the review agent is read-only — it cannot fix what it finds, and
therefore has no incentive to stop recording findings.

### Routing on evidence

`graph/edges.py` is the only place that decides where a run goes next, and every
router is a plain function over state with no I/O. Three rules matter:

**Absence of evidence is not success.** `verification_passed` returns `False`
for a state with no test results. The naive implementation — `all(r["success"]
for r in results)` — returns `True` for an empty list, which means a run that
never built anything reports as green. That default is the single most dangerous
line you can write in a platform like this.

**A red test goes back to `coding`, not `planner`.** A failing test usually means
the change is wrong, not the plan. Re-planning discards the work that was
already correct and burns an iteration re-deriving it. Only a review that
rejects the *approach* costs a full loop.

**Approval plus evidence, never either alone.** `route_after_review` requires the
model to approve *and* the build and tests to be green. The reviewer's opinion
cannot override the evidence, and the evidence cannot override a blocking
finding.

Read-only workflows (`review`, `analyzelog`) produce no build evidence, so they
declare `verification_required: false` in their seed state. That is an explicit
opt-out on the record, not a special case hidden in the router.

### The iteration budget

`iteration_node` is its own node on the edge back to the planner. Counting there
rather than inside an agent means no agent can talk the loop into another round,
and the budget is visible in the graph rather than buried in a prompt.

---

## 2. State

`AgentState` is a `TypedDict`, not a class, because that is what LangGraph
merges: a node returns a *partial* state and the graph applies it with the
reducers declared in the annotations.

```python
code_changes: Annotated[list[CodeChange], _append]   # nodes accumulate
context:      Annotated[dict[str, Any], _merge]      # nodes contribute keys
status:       str                                    # nodes replace
```

Returning a patch rather than mutating shared state is what makes checkpointing,
resuming and concurrent branches possible. `SimpleGraph.merge_state` implements
the same three reducers, so the built-in runtime and LangGraph produce identical
results from the same `GraphSpec`.

Keeping the state flat and JSON-serialisable is what lets a run be interrupted
for human review and picked up later on the same thread id.

---

## 3. Two graph runtimes, one spec

A workflow declares a `GraphSpec` — nodes, edges, conditional edges. The builder
compiles it to LangGraph's `StateGraph` when the package is installed, and to
`SimpleGraph` otherwise.

This is not a fallback bolted on for convenience. It means:

- routing logic exists once, in `graph/edges.py`, and is tested without either runtime;
- the platform is demonstrable and testable without the full dependency tree;
- `spec.validate()` catches an edge to a node that does not exist at build time,
  in both runtimes, rather than mid-run.

`SimpleGraph` caps itself at 60 steps. A routing bug should end a run with a
clear error, not spin.

---

## 4. Agents

An agent is the composition of five things, and nothing else:

| | |
|---|---|
| prompt template | `prompts/templates/<name>.md`, Markdown on disk |
| tools | names resolved against the shared registry |
| memory | a `MemoryManager`, not three separate stores |
| state | reads `AgentState`, returns a patch |
| output schema | JSON Schema, in the prompt *and* used to validate the reply |

`BaseAgent` owns the turn: build the prompt, run the tool loop, parse and
validate the answer, emit hooks, write to memory. A subclass supplies
`build_prompt`, `apply` and `output_schema`. That is deliberate — every agent
must behave identically under failure, and the way to guarantee that is to give
them one implementation of it.

### Prompts live on disk

Role prompts are Markdown files, not string literals. A prompt change then shows
up in a diff as a prompt change, a non-engineer can edit one, and the prompts can
be reviewed as prose — which is what they are.

### The tool loop

Three details in `BaseAgent._turn` are load-bearing:

1. **The assistant turn is replayed verbatim.** `response.raw_content` goes back
   as-is, because the API needs the original blocks to continue the turn.
2. **Every tool result goes in one user message.** Splitting parallel tool
   results across messages silently teaches the model to stop making parallel
   calls.
3. **A failed tool comes back as `is_error: true`, not an exception.** The model
   can usually recover from "no such file" if you tell it; it cannot recover from
   an aborted run.

### Prompt caching

`system_blocks` puts stable content first (role, skills, output contract) and
places the cache breakpoint on the last block. The volatile task text is a *user
message*, so it sits after the breakpoint and never invalidates the cached
prefix. There is a test for this, because it is the kind of thing that regresses
invisibly — the only symptom is the bill.

---

## 5. Memory

Three layers, because there are three different questions:

| Layer | Question | Backing | Lifetime |
|---|---|---|---|
| short term | what did we just say? | bounded deque | one run |
| long term | what do we know about this project? | one JSON file | forever |
| vector | what is *relevant* to this? | ChromaDB | forever |

Short-term memory is bounded on purpose. An unbounded transcript is the most
common way an agent platform quietly runs out of context halfway through a
workflow: dropping the oldest turns is a predictable failure mode, an
overflowing window is not.

Long-term memory is a single JSON file in the default deployment — diffable,
greppable, survivable without a service, and replaceable by implementing
`memory.base.Memory`. It also supports `forget`: a memory that turns out to be
wrong has to be removable.

`MemoryManager.context_block` returns an **empty string** when nothing is known.
An empty "Known context" heading costs tokens and teaches the model nothing.

What gets *written* to long-term memory is deliberately narrow: a confirmed root
cause, an incident conclusion the log agent rated high-confidence. Writing every
observation to durable memory turns retrieval into noise within a week.

---

## 6. Tools

A tool takes its collaborators through its constructor and nothing else — a
`Workspace`, a `JiraClient`, a callable that runs a query. That is what makes
each one testable on its own, which the specification asked for and which the
test suite relies on.

`Tool.run` never raises for a tool-level failure; it returns
`ToolResult(ok=False)`. The agent loop turns that into a `tool_result` block
with `is_error` set. Failures are data.

### The three deterministic boundaries

These never read the prompt, so a prompt-injected instruction cannot argue with
them:

- **`Workspace.resolve`** — resolves symlinks, rejects anything outside the root,
  refuses denied globs. Every model-supplied path goes through it.
- **`assert_read_only`** — strips comments, then requires a single `SELECT` or
  `WITH … SELECT`. Anything else is refused.
- **`ToolGuard`** — a `BeforeToolExecution` hook that inspects the *call*, and
  vetoes writes platform-wide when `allow_writes` is false.

`Workspace.run` splits commands with `shlex` and executes without a shell, so
caller-supplied text cannot chain a second command.

---

## 7. Hooks

Seven events, synchronous dispatch, ordered by priority. Two properties matter:

**Fail-soft.** A handler that raises is logged and skipped. Observability must
never be the reason a workflow dies.

**Vetoable.** A handler can call `context.cancel(reason)`, which stops the chain
and the pending operation. That is how policy is enforced without policy code
being scattered through the call sites.

The audit trail is one JSONL line per event with credentials scrubbed — greppable,
and shippable to Seq without a parser.

---

## 8. MCP

Each of the eight servers is a wrapper that knows three things: its config, the
tools it is *expected* to expose, and how to check its health. Transport is
injected.

Declaring the expected tool set statically is what makes `cli.py mcp health`
useful: a server that is enabled but exposes a different tool set is a
configuration error worth surfacing, not a mystery at call time. It also means a
disabled server is still documented.

`MCPToolProxy` presents an MCP tool through the same `Tool` interface as a native
one, so the tool loop, the policy hooks and the audit trail apply uniformly.
Agents cannot tell the difference — which is the point.

The stdio transport owns a dedicated event loop on its own thread. MCP's Python
SDK is async and its sessions are stateful; opening a session per call would be
both slow and wrong, and colouring the whole codebase async for one integration
would be worse.

Every server is **disabled by default**. An agent should not be offered a tool
that cannot run.

---

## 9. Plugins

Four lifecycle methods — `register`, `validate`, `execute`, `shutdown` — and the
manager enforces the order. A plugin that fails validation is disabled and shut
down rather than left half-wired; a plugin that raises during a stage is recorded
as a failed result. Neither can take a run down.

The review plugins pair a **deterministic pattern scan** with a **model pass**.
The scan runs first and is not skippable: a hardcoded credential should not
depend on a model noticing it. The model pass catches what a regex cannot.

`plugins/migration.py` is the shape to copy for anything risky: it inspects, it
reports, and it never applies. Schema changes are a human decision.

---

## 10. Configuration and DI

`container.py` is the only module that constructs anything. Construction order
follows the dependency arrows:

```
settings -> hooks -> llm -> workspace -> tools -> mcp
         -> memory -> prompts -> skills -> agents -> plugins
         -> workflows -> commands
```

Hooks come first so everything built afterwards can be observed and guarded.
Plugins come after agents because a plugin may add tools and hooks.

The keyword overrides on `build_container` are the test seam: pass an `EchoLLM`
and a temporary workspace and the entire platform runs offline. That is exactly
what `tests/conftest.py` does.

Settings are typed dataclasses layered YAML-then-environment. Nothing reads
`os.environ` at call time except the integration adapters resolving their own
credentials.

---

## 11. What this platform deliberately does not do

- **Merge anything.** Pull requests open as drafts, from a feature branch, never
  from `main`.
- **Apply migrations.** They are inspected and reported.
- **Write to Jira.** Status transitions belong to a human.
- **Force push.** Ever.
- **Treat a dry run as evidence.** The offline stub reports success at every
  step so the happy path is exercisable; it is a wiring check and says so in its
  own module docstring.

---

## 12. Known limits

Worth knowing before this runs against a real repository:

- **Diffs are described, not captured.** `CodeChange` records a path, an action
  and a rationale, because the coding agent writes whole files. A reviewer
  wanting a true diff should read the working tree or wire `git diff` in as a tool.
- **The build and test parsers are MSBuild- and `dotnet test`-shaped.** Other
  runners fall back to the exit code, which is a coarser signal — `parse_test_summary`
  reports `passed: None` rather than guessing.
- **Retrieval quality depends on what was indexed.** Without `scripts/index_codebase.py`
  having been run, `recall` only returns what the platform itself learned.
- **The lexical fallback is genuinely worse than embeddings.** It is there so a
  missing ChromaDB degrades quality rather than stopping a run; `vector.backend`
  always says which one answered.
- **Human-in-the-loop needs a checkpointer to resume.** With
  `checkpoint_backend: none` an interrupted run reports `awaiting_human` and
  ends; with `sqlite` it can be resumed on the same thread id.
