import { Annotation } from "@langchain/langgraph";
import { z } from "zod";

/**
 * The plan the loop commits to before it writes any code.
 *
 * Structured rather than prose because it is the thing the human gate approves, and an approval has
 * to be about something specific. "Add caching to the vocab endpoint" is not reviewable; a named
 * list of files, each with a stated change and a reason, is.
 */
export const PlanSchema = z.object({
  summary: z.string().describe("One paragraph: what will change and why, in the reviewer's language."),
  files: z
    .array(
      z.object({
        path: z.string().describe("Path relative to the repository root."),
        action: z.enum(["create", "modify", "delete"]),
        change: z.string().describe("What specifically changes in this file, and why it belongs here."),
        layer: z
          .enum(["Domain", "Application", "Infrastructure", "Api", "Tests", "Other"])
          .describe("Which architectural layer this file lives in. Used to check the dependency rule."),
      }),
    )
    .min(1)
    .describe("Every file the change touches. Missing one here means the coder discovers it late."),
  acceptanceCriteria: z
    .array(z.string())
    .min(1)
    .describe("Observable conditions that make this done. Each must be checkable, not aspirational."),
  risks: z.array(z.string()).describe("What could go wrong, including anything the plan is unsure about."),
  openQuestions: z
    .array(z.string())
    .describe("Anything the request left genuinely ambiguous. Empty when the request was clear enough."),
});

export type Plan = z.infer<typeof PlanSchema>;

/** The reviewer's verdict on a change that already compiles and passes its tests. */
export const ReviewSchema = z.object({
  verdict: z.enum(["approve", "request_changes"]),
  summary: z.string().describe("The judgement in two or three sentences."),
  findings: z
    .array(
      z.object({
        severity: z.enum(["blocking", "major", "minor"]),
        path: z.string().describe("File the finding is in."),
        detail: z.string().describe("What is wrong and what it would take to fix it."),
      }),
    )
    .describe("Specific problems. Empty on a clean approval — do not invent findings to look thorough."),
  unmetCriteria: z
    .array(z.string())
    .describe("Acceptance criteria from the plan that the change does not actually satisfy."),
});

export type Review = z.infer<typeof ReviewSchema>;

export interface CommandOutcome {
  ok: boolean;
  exitCode: number;
  output: string;
  elapsedMs: number;
  timedOut: boolean;
}

export interface Usage {
  inputTokens: number;
  outputTokens: number;
  cacheReadTokens: number;
  cacheCreationTokens: number;
}

export const ZERO_USAGE: Usage = {
  inputTokens: 0,
  outputTokens: 0,
  cacheReadTokens: 0,
  cacheCreationTokens: 0,
};

export interface LoopEvent {
  at: string;
  node: string;
  message: string;
}

/**
 * Where a run currently is. Everything except `running` is a state a human needs to see.
 *
 * `awaiting_plan` and `awaiting_patch` are the two interrupt points. `blocked` is the honest outcome
 * when the repair budget ran out — distinct from `failed`, which means the machinery broke.
 */
export type LoopStatus =
  | "running"
  | "awaiting_plan"
  | "awaiting_patch"
  | "applied"
  | "proposed"
  | "blocked"
  | "rejected"
  | "failed";

/**
 * The graph's state.
 *
 * Two rules shaped this. First, anything a human needs in order to decide at a gate has to be in
 * here, because a resumed run rebuilds itself from the checkpoint and nothing else. Second, the
 * verification fields — `build`, `test`, `changedPaths`, `diff` — are written only by deterministic
 * nodes that actually ran a command, never by a model. An agent can claim its tests pass; it cannot
 * write that claim into this state.
 */
export const LoopState = Annotation.Root({
  /** What the user asked for, verbatim. */
  request: Annotation<string>(),

  /** Absolute path to the isolated worktree the loop develops in. */
  workRoot: Annotation<string>({ reducer: (_, next) => next, default: () => "" }),
  branch: Annotation<string>({ reducer: (_, next) => next, default: () => "" }),
  baseCommit: Annotation<string>({ reducer: (_, next) => next, default: () => "" }),

  /** The scout's orientation brief: how the relevant part of the codebase currently works. */
  brief: Annotation<string>({ reducer: (_, next) => next, default: () => "" }),

  plan: Annotation<Plan | null>({ reducer: (_, next) => next, default: () => null }),

  /**
   * Redirections a human gave at the plan gate, oldest first.
   *
   * Accumulated rather than replaced so a second revision still sees the first. A planner that
   * forgets the previous correction tends to reintroduce what it was just told to drop.
   */
  planFeedback: Annotation<string[]>({
    reducer: (current, next) => [...current, ...next],
    default: () => [],
  }),

  /** Notes the coder and fixer leave for each other and for the reviewer. */
  notes: Annotation<string[]>({
    reducer: (current, next) => [...current, ...next],
    default: () => [],
  }),

  build: Annotation<CommandOutcome | null>({ reducer: (_, next) => next, default: () => null }),
  test: Annotation<CommandOutcome | null>({ reducer: (_, next) => next, default: () => null }),

  /** How many repair cycles have been spent. Bounds the build/test/fix loop. */
  attempt: Annotation<number>({ reducer: (_, next) => next, default: () => 0 }),

  review: Annotation<Review | null>({ reducer: (_, next) => next, default: () => null }),

  /** Paths changed in the worktree, from git rather than from what an agent said it wrote. */
  changedPaths: Annotation<string[]>({ reducer: (_, next) => next, default: () => [] }),
  diff: Annotation<string>({ reducer: (_, next) => next, default: () => "" }),

  /** Id of the proposal in the C# patch queue, when HW.Api took it. */
  patchId: Annotation<string | null>({ reducer: (_, next) => next, default: () => null }),

  status: Annotation<LoopStatus>({ reducer: (_, next) => next, default: () => "running" }),

  /**
   * What the human decided at the gate that just ran.
   *
   * Routing reads this rather than inferring the decision from status or from how many feedback
   * entries accumulated. Inference works until the day two paths produce the same status, and then
   * it fails silently by sending the run down the wrong edge.
   */
  gateDecision: Annotation<"approve" | "revise" | "reject" | null>({
    reducer: (_, next) => next,
    default: () => null,
  }),

  /** Why the run ended where it did. Shown at every terminal state, including the good ones. */
  outcome: Annotation<string>({ reducer: (_, next) => next, default: () => "" }),

  usage: Annotation<Usage>({
    reducer: (current, next) => ({
      inputTokens: current.inputTokens + next.inputTokens,
      outputTokens: current.outputTokens + next.outputTokens,
      cacheReadTokens: current.cacheReadTokens + next.cacheReadTokens,
      cacheCreationTokens: current.cacheCreationTokens + next.cacheCreationTokens,
    }),
    default: () => ZERO_USAGE,
  }),

  events: Annotation<LoopEvent[]>({
    reducer: (current, next) => [...current, ...next],
    default: () => [],
  }),
});

export type LoopStateType = typeof LoopState.State;

export function event(node: string, message: string): LoopEvent {
  return { at: new Date().toISOString(), node, message };
}
