import { END, START, StateGraph, interrupt } from "@langchain/langgraph";
import type { BaseCheckpointSaver } from "@langchain/langgraph";
import { runAgent, runStructured } from "./agents/runner.js";
import type { LoopConfig } from "./config.js";
import { LoopState, PlanSchema, ReviewSchema, event, type CommandOutcome, type LoopStateType, type Plan } from "./state.js";
import { DotnetApi } from "./tools/dotnetApi.js";
import { createTools } from "./tools/index.js";
import { changedFiles, createWorktree, diffAgainst, run } from "./tools/shell.js";
import { Workspace } from "./tools/workspace.js";

/**
 * What a human sends back through a gate.
 *
 * The same shape serves both gates because the decisions have the same structure: let it through,
 * send it back with a correction, or stop. `revise` carries the correction; the other two do not
 * need one.
 */
export interface GateDecision {
  action: "approve" | "revise" | "reject";
  feedback?: string;
}

/** What the CLI is shown when the graph pauses. Rendered for a person, not parsed. */
export interface GatePayload {
  gate: "plan" | "patch";
  summary: string;
  plan?: Plan;
  diff?: string;
  changedPaths?: string[];
  review?: unknown;
}

function outcomeOf(result: Awaited<ReturnType<typeof run>>): CommandOutcome {
  const output = `${result.stdout}\n${result.stderr}`.trim();
  return {
    ok: result.exitCode === 0,
    exitCode: result.exitCode,
    output,
    elapsedMs: result.elapsedMs,
    timedOut: result.timedOut,
  };
}

/** Everything a node needs that is not in the graph state, assembled once per run. */
interface RunContext {
  config: LoopConfig;
  api: DotnetApi;
  /** Tool registries are per-worktree, so they are built lazily once the worktree exists. */
  toolsFor: (workRoot: string) => ReturnType<typeof createTools>;
}

function describePlan(plan: Plan): string {
  const files = plan.files
    .map((file) => `  ${file.action.padEnd(6)} ${file.path}  [${file.layer}]\n           ${file.change}`)
    .join("\n");

  const criteria = plan.acceptanceCriteria.map((item) => `  - ${item}`).join("\n");
  const risks = plan.risks.length > 0 ? plan.risks.map((item) => `  - ${item}`).join("\n") : "  (none stated)";
  const questions =
    plan.openQuestions.length > 0
      ? plan.openQuestions.map((item) => `  ? ${item}`).join("\n")
      : "  (none)";

  return [
    plan.summary,
    "",
    "FILES",
    files,
    "",
    "ACCEPTANCE CRITERIA",
    criteria,
    "",
    "RISKS",
    risks,
    "",
    "OPEN QUESTIONS",
    questions,
  ].join("\n");
}

/**
 * Builds the feature-development loop.
 *
 * The shape is:
 *
 *   prepare -> scout -> plan -> [HUMAN GATE] -> code -> build -> test -> review -> propose
 *                        ^                        ^        |       |        |         |
 *                        |                        |        v       v        v         v
 *                        +----- revise -----------+      repair  repair  repair  [HUMAN GATE] -> apply
 *
 * Two properties are worth stating because everything else follows from them.
 *
 * **The verification nodes are not agents.** `build` and `test` run the configured commands and
 * write the real exit code into state. A model can claim its change works; it has no way to make
 * this graph believe it. Every route out of the loop that ends in `applied` passed through a real
 * process exiting zero.
 *
 * **Every cycle is bounded.** The repair loop stops at `maxRepairAttempts` and reports `blocked`
 * rather than spinning. A loop whose only exit is success does not terminate on the tasks that
 * matter most.
 */
export function buildGraph(config: LoopConfig, checkpointer: BaseCheckpointSaver) {
  const api = new DotnetApi(config.dotnetApiBaseUrl);

  const registries = new Map<string, ReturnType<typeof createTools>>();
  const ctx: RunContext = {
    config,
    api,
    toolsFor: (workRoot) => {
      let registry = registries.get(workRoot);
      if (!registry) {
        registry = createTools({
          workspace: new Workspace({ root: workRoot, excludeDirs: config.repo.excludeDirs }),
          api,
          config,
        });
        registries.set(workRoot, registry);
      }
      return registry;
    },
  };

  /** Cuts the isolated branch this run develops on. Nothing before this point can write anything. */
  const prepare = async (state: LoopStateType) => {
    const branch = `${config.branchPrefix}${Date.now().toString(36)}`;
    const { path, baseCommit } = await createWorktree(config.repo.root, config.worktreeDir, branch);

    return {
      workRoot: path,
      branch,
      baseCommit,
      status: "running" as const,
      events: [event("prepare", `Worktree ${branch} at ${path}, based on ${baseCommit.slice(0, 8)}.`)],
    };
  };

  const scout = async (state: LoopStateType) => {
    const result = await runAgent(
      "scout",
      `A feature has been requested:\n\n${state.request}\n\nOrient the loop: report how this part of the codebase works today, which files are involved, and the pattern the change should follow.`,
      config,
      ctx.toolsFor(state.workRoot),
    );

    return {
      brief: result.text,
      usage: result.usage,
      events: [event("scout", `Brief written after ${result.toolCalls} tool calls.`)],
    };
  };

  const plan = async (state: LoopStateType) => {
    const revision =
      state.planFeedback.length > 0
        ? `\n\nA human reviewed your previous plan and sent it back. Their feedback, oldest first — address all of it, and do not reintroduce anything an earlier round told you to drop:\n${state.planFeedback.map((item, index) => `  ${index + 1}. ${item}`).join("\n")}\n\nYour previous plan was:\n${state.plan ? describePlan(state.plan) : "(none)"}`
        : "";

    const { value, usage } = await runStructured(
      "planner",
      `Feature request:\n\n${state.request}\n\nThe scout reports:\n\n${state.brief}${revision}\n\nProduce the plan.`,
      PlanSchema,
      config,
    );

    return {
      plan: value,
      usage,
      status: "awaiting_plan" as const,
      events: [event("plan", `Plan covers ${value.files.length} files.`)],
    };
  };

  /**
   * The first human gate.
   *
   * `interrupt` suspends the graph and persists everything to the checkpoint. The process can exit
   * here — resuming later replays from the checkpoint, which is why this is a gate a person can
   * genuinely take their time at rather than a prompt they have to answer before their terminal
   * times out.
   */
  const planGate = async (state: LoopStateType) => {
    const decision = interrupt<GatePayload, GateDecision>({
      gate: "plan",
      summary: state.plan ? describePlan(state.plan) : "No plan was produced.",
      plan: state.plan ?? undefined,
    });

    if (decision.action === "approve") {
      return {
        status: "running" as const,
        gateDecision: "approve" as const,
        events: [event("plan-gate", "Plan approved.")],
      };
    }
    if (decision.action === "reject") {
      return {
        status: "rejected" as const,
        gateDecision: "reject" as const,
        outcome: decision.feedback ?? "Plan rejected by the reviewer.",
        events: [event("plan-gate", "Plan rejected; run ended.")],
      };
    }
    return {
      planFeedback: [decision.feedback ?? "Revise the plan."],
      status: "running" as const,
      gateDecision: "revise" as const,
      events: [event("plan-gate", "Plan sent back for revision.")],
    };
  };

  const code = async (state: LoopStateType) => {
    const result = await runAgent(
      "coder",
      `Implement this approved plan.\n\nORIGINAL REQUEST\n${state.request}\n\nPLAN\n${state.plan ? describePlan(state.plan) : ""}\n\nCONTEXT FROM THE SCOUT\n${state.brief}`,
      config,
      ctx.toolsFor(state.workRoot),
    );

    return {
      notes: [`coder: ${result.text}`],
      usage: result.usage,
      events: [event("code", `Coder finished after ${result.toolCalls} tool calls.`)],
    };
  };

  /** Deterministic. The compiler's exit code, not an agent's opinion of it. */
  const build = async (state: LoopStateType) => {
    const result = await run(config.repo.buildCommand, {
      cwd: state.workRoot,
      timeoutSeconds: config.commandTimeoutSeconds,
      maxOutputChars: config.maxToolResultChars * 2,
    });
    const outcome = outcomeOf(result);

    return {
      build: outcome,
      events: [event("build", outcome.ok ? `Build succeeded in ${outcome.elapsedMs}ms.` : `Build failed (exit ${outcome.exitCode}).`)],
    };
  };

  /** Deterministic, for the same reason as `build`. */
  const test = async (state: LoopStateType) => {
    const result = await run(config.repo.testCommand, {
      cwd: state.workRoot,
      timeoutSeconds: config.commandTimeoutSeconds,
      maxOutputChars: config.maxToolResultChars * 2,
    });
    const outcome = outcomeOf(result);

    return {
      test: outcome,
      events: [event("test", outcome.ok ? `Tests passed in ${outcome.elapsedMs}ms.` : `Tests failed (exit ${outcome.exitCode}).`)],
    };
  };

  /**
   * The repair node serves three different callers — a failed build, a failed test, and a reviewer
   * or human who rejected a change that is already green — so it works out which one it is rather
   * than assuming. Handing the fixer a "TEST OUTPUT" section full of passing tests, because a human
   * asked for a design change, is the kind of misleading context that produces a confident non-fix.
   */
  const repair = async (state: LoopStateType) => {
    const attempt = state.attempt + 1;

    let problem: string;
    if (state.build && !state.build.ok) {
      problem = `THE BUILD IS FAILING. Compiler output:\n\n${state.build.output || "(no output captured)"}`;
    } else if (state.test && !state.test.ok) {
      problem = `THE BUILD SUCCEEDS BUT TESTS ARE FAILING. Test output:\n\n${state.test.output || "(no output captured)"}`;
    } else {
      problem =
        "THE CHANGE COMPILES AND ITS TESTS PASS, but it was rejected on review. Nothing is broken " +
        "mechanically — what follows is a judgement about the change itself, so do not go looking " +
        "for a compiler error. Address the substance of it.";
    }

    const reviewFindings =
      state.review && state.review.verdict === "request_changes"
        ? `\n\nREVIEWER\n${state.review.summary}\n${state.review.findings.map((finding) => `  [${finding.severity}] ${finding.path}: ${finding.detail}`).join("\n")}${state.review.unmetCriteria.length > 0 ? `\nUnmet acceptance criteria:\n${state.review.unmetCriteria.map((item) => `  - ${item}`).join("\n")}` : ""}`
        : "";

    const result = await runAgent(
      "fixer",
      `Repair attempt ${attempt} of ${config.maxRepairAttempts}.\n\nORIGINAL REQUEST\n${state.request}\n\nPLAN\n${state.plan ? describePlan(state.plan) : ""}\n\n${problem}${reviewFindings}\n\nWhat the loop has done so far:\n${state.notes.slice(-3).join("\n\n")}\n\nFix the cause.`,
      config,
      ctx.toolsFor(state.workRoot),
    );

    return {
      attempt,
      notes: [`fixer (attempt ${attempt}): ${result.text}`],
      usage: result.usage,
      // Cleared so the reviewer's verdict cannot be mistaken for current after a repair.
      review: null,
      events: [event("repair", `Attempt ${attempt} finished after ${result.toolCalls} tool calls.`)],
    };
  };

  const review = async (state: LoopStateType) => {
    const paths = await changedFiles(state.workRoot, state.baseCommit);
    const diff = await diffAgainst(state.workRoot, state.baseCommit);

    const { value, usage } = await runStructured(
      "reviewer",
      `Review this change. It already compiles and its tests pass, so judge what that does not cover.\n\nORIGINAL REQUEST\n${state.request}\n\nPLAN AND ACCEPTANCE CRITERIA\n${state.plan ? describePlan(state.plan) : ""}\n\nFILES CHANGED\n${paths.join("\n")}\n\nDIFF\n${diff.slice(0, 120_000)}`,
      ReviewSchema,
      config,
    );

    return {
      review: value,
      changedPaths: paths,
      diff,
      usage,
      events: [
        event("review", `Reviewer said ${value.verdict} with ${value.findings.length} findings.`),
      ],
    };
  };

  /**
   * Hands the finished change to the C# patch queue, where the existing approval machinery takes it.
   *
   * If HW.Api is not running the change is not lost — it stays on its branch, and the gate below
   * still works. The queue is the preferred path because it gives both systems one place where
   * pending edits live; it is not a required one.
   */
  const propose = async (state: LoopStateType) => {
    if (!(await api.isReachable())) {
      return {
        events: [event("propose", "HW.Api unreachable; the change stays on its branch for local review.")],
      };
    }

    const workspace = new Workspace({ root: state.workRoot, excludeDirs: config.repo.excludeDirs });
    const files = [];
    for (const path of state.changedPaths) {
      if (!workspace.exists(path)) continue;
      files.push({ path, newContent: await workspace.readRaw(path) });
    }

    if (files.length === 0) {
      return { events: [event("propose", "Nothing to propose — no files changed.")] };
    }

    try {
      const proposal = await api.proposePatch({
        agent: "loop-agentic",
        title: state.plan?.summary.split("\n")[0].slice(0, 120) ?? state.request.slice(0, 120),
        rationale: `${state.plan?.summary ?? state.request}\n\nReviewer: ${state.review?.summary ?? "n/a"}`,
        files,
      });

      return {
        patchId: proposal?.id ?? null,
        events: [event("propose", proposal ? `Queued as patch ${proposal.id}.` : "HW.Api accepted no proposal.")],
      };
    } catch (error) {
      return {
        events: [
          event("propose", `Could not queue the patch: ${error instanceof Error ? error.message : String(error)}. The change stays on its branch.`),
        ],
      };
    }
  };

  /**
   * The second human gate: the only point at which anything can reach the user's checkout.
   *
   * Approving here does not itself write. It records the decision and lets `apply` merge the branch,
   * or the CLI approve the queued patch — both of which are ordinary, reversible git operations
   * against a change that has already compiled, passed, and been reviewed.
   */
  const patchGate = async (state: LoopStateType) => {
    const decision = interrupt<GatePayload, GateDecision>({
      gate: "patch",
      summary: `${state.changedPaths.length} files changed on ${state.branch}.\n\n${state.review?.summary ?? ""}`,
      diff: state.diff,
      changedPaths: state.changedPaths,
      review: state.review ?? undefined,
    });

    if (decision.action === "approve") {
      return {
        status: "applied" as const,
        gateDecision: "approve" as const,
        outcome: `Approved. Branch ${state.branch} is ready to merge${state.patchId ? `, patch ${state.patchId} queued in HW.Api` : ""}.`,
        events: [event("patch-gate", "Change approved.")],
      };
    }
    if (decision.action === "reject") {
      return {
        status: "rejected" as const,
        gateDecision: "reject" as const,
        outcome: decision.feedback ?? "Change rejected at the patch gate.",
        events: [event("patch-gate", "Change rejected.")],
      };
    }
    return {
      notes: [`patch gate: ${decision.feedback ?? "Reviewer asked for changes."}`],
      status: "running" as const,
      gateDecision: "revise" as const,
      events: [event("patch-gate", "Sent back to the fixer.")],
    };
  };

  // ---- routing -------------------------------------------------------------

  const afterPlanGate = (state: LoopStateType) => {
    if (state.gateDecision === "reject") return END;
    return state.gateDecision === "revise" ? "make_plan" : "code";
  };

  const afterBuild = (state: LoopStateType) => {
    if (state.build?.ok) return "verify_tests";
    return state.attempt >= config.maxRepairAttempts ? "exhausted" : "repair";
  };

  const afterTest = (state: LoopStateType) => {
    if (state.test?.ok) return "review_change";
    return state.attempt >= config.maxRepairAttempts ? "exhausted" : "repair";
  };

  const afterReview = (state: LoopStateType) => {
    if (state.review?.verdict === "approve") return "propose";
    return state.attempt >= config.maxRepairAttempts ? "exhausted" : "repair";
  };

  const afterPatchGate = (state: LoopStateType) => (state.gateDecision === "revise" ? "repair" : END);

  /** The honest terminal state when the repair budget ran out with the change still not green. */
  const exhausted = async (state: LoopStateType) => {
    const reason = !state.build?.ok
      ? `the build still fails: ${state.build?.output.slice(-1500) ?? ""}`
      : !state.test?.ok
        ? `tests still fail: ${state.test?.output.slice(-1500) ?? ""}`
        : `the reviewer would not approve it: ${state.review?.summary ?? ""}`;

    return {
      status: "blocked" as const,
      outcome: `Gave up after ${state.attempt} repair attempts — ${reason}\n\nThe work is on branch ${state.branch} at ${state.workRoot}; inspect it there.`,
      events: [event("exhausted", `Budget of ${config.maxRepairAttempts} repair attempts spent.`)],
    };
  };

  // Node names deliberately differ from the state channels they write (`make_plan` writes `plan`,
  // `verify_build` writes `build`). LangGraph rejects a node whose name collides with a channel, and
  // the verb-first names read better in a trace besides.
  return new StateGraph(LoopState)
    .addNode("prepare", prepare)
    .addNode("scout", scout)
    .addNode("make_plan", plan)
    .addNode("plan_gate", planGate)
    .addNode("code", code)
    .addNode("verify_build", build)
    .addNode("verify_tests", test)
    .addNode("repair", repair)
    .addNode("review_change", review)
    .addNode("propose", propose)
    .addNode("patch_gate", patchGate)
    .addNode("exhausted", exhausted)
    .addEdge(START, "prepare")
    .addEdge("prepare", "scout")
    .addEdge("scout", "make_plan")
    .addEdge("make_plan", "plan_gate")
    .addConditionalEdges("plan_gate", afterPlanGate, {
      make_plan: "make_plan",
      code: "code",
      [END]: END,
    })
    .addEdge("code", "verify_build")
    .addConditionalEdges("verify_build", afterBuild, {
      verify_tests: "verify_tests",
      repair: "repair",
      exhausted: "exhausted",
    })
    .addConditionalEdges("verify_tests", afterTest, {
      review_change: "review_change",
      repair: "repair",
      exhausted: "exhausted",
    })
    // The repair cycle: every fix goes back through a real build, never straight to review.
    .addEdge("repair", "verify_build")
    .addConditionalEdges("review_change", afterReview, {
      propose: "propose",
      repair: "repair",
      exhausted: "exhausted",
    })
    .addEdge("propose", "patch_gate")
    .addConditionalEdges("patch_gate", afterPatchGate, { repair: "repair", [END]: END })
    .addEdge("exhausted", END)
    .compile({ checkpointer });
}
