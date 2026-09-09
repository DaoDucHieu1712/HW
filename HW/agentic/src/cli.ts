import { randomUUID } from "node:crypto";
import { mkdirSync } from "node:fs";
import { Command } from "@langchain/langgraph";
import { Command as Cli } from "commander";
import "dotenv/config";
import pc from "picocolors";
import { loadConfig } from "./config.js";
import { createCheckpointer } from "./checkpoint.js";
import { buildGraph, type GateDecision, type GatePayload } from "./graph.js";
import { estimateCost } from "./models.js";
import type { LoopStateType } from "./state.js";
import { DotnetApi } from "./tools/dotnetApi.js";

const program = new Cli()
  .name("loop")
  .description("A LangGraph feature-development loop: plan, code, build, test, self-repair, review.")
  .version("0.1.0");

function heading(text: string): void {
  console.log(`\n${pc.bold(pc.cyan(`── ${text} ${"─".repeat(Math.max(0, 68 - text.length))}`))}`);
}

function requireApiKey(): void {
  if (process.env.ANTHROPIC_API_KEY) return;
  console.error(
    pc.red("ANTHROPIC_API_KEY is not set.") +
      "\nSet it in agentic/.env (see .env.example) or export it in your shell.",
  );
  process.exit(1);
}

/**
 * Streams a run until it either finishes or pauses at a gate.
 *
 * The interrupt is not an error condition: `__interrupt__` on a stream chunk is how LangGraph says
 * a human is needed. Everything up to that point is already checkpointed, so this function can
 * simply return and let the process exit — the run is resumable from `loop resume`.
 */
async function drive(
  graph: ReturnType<typeof buildGraph>,
  input: Parameters<ReturnType<typeof buildGraph>["stream"]>[0],
  threadId: string,
  modelForCost: string,
): Promise<void> {
  const config = { configurable: { thread_id: threadId }, recursionLimit: 200 };
  const stream = await graph.stream(input, { ...config, streamMode: "updates" as const });

  for await (const chunk of stream) {
    for (const [node, update] of Object.entries(chunk as Record<string, unknown>)) {
      if (node === "__interrupt__") continue;
      const events = (update as { events?: { node: string; message: string }[] })?.events ?? [];
      for (const entry of events) {
        console.log(`${pc.dim(new Date().toLocaleTimeString())} ${pc.green(entry.node.padEnd(11))} ${entry.message}`);
      }
    }
  }

  const snapshot = await graph.getState(config);
  const state = snapshot.values as LoopStateType;
  const pending = snapshot.tasks.flatMap((task) => task.interrupts ?? []);

  if (pending.length > 0) {
    const payload = pending[0].value as GatePayload;
    renderGate(payload, threadId);
    return;
  }

  heading(`Run finished — ${state.status}`);
  console.log(state.outcome || "(no outcome recorded)");
  reportCost(state, modelForCost);
}

function renderGate(payload: GatePayload, threadId: string): void {
  if (payload.gate === "plan") {
    heading("PLAN — awaiting your decision");
    console.log(payload.summary);
  } else {
    heading("CHANGE — awaiting your decision");
    console.log(payload.summary);
    console.log(`\n${pc.dim("Files changed:")}`);
    for (const path of payload.changedPaths ?? []) console.log(`  ${path}`);
    console.log(`\n${pc.dim("Run")} loop diff ${threadId} ${pc.dim("to read the full diff.")}`);
  }

  console.log(`\n${pc.bold("Resume with one of:")}`);
  console.log(`  npm run loop -- resume ${threadId} --approve`);
  console.log(`  npm run loop -- resume ${threadId} --revise "what to change"`);
  console.log(`  npm run loop -- resume ${threadId} --reject "why"`);
}

function reportCost(state: LoopStateType, model: string): void {
  const { usage } = state;
  const cost = estimateCost(usage, model);
  console.log(
    pc.dim(
      `\ntokens  in ${usage.inputTokens.toLocaleString()}  out ${usage.outputTokens.toLocaleString()}  ` +
        `cache-read ${usage.cacheReadTokens.toLocaleString()}  ≈ $${cost.toFixed(2)}`,
    ),
  );
}

program
  .command("run")
  .description("Start a new feature-development run.")
  .argument("<request>", "What to build, in plain language.")
  .option("--repo <path>", "Repository to develop in. Defaults to the parent of agentic/.")
  .option("--thread <id>", "Thread id to use. Defaults to a new uuid.")
  .action(async (request: string, options: { repo?: string; thread?: string }) => {
    requireApiKey();
    const config = loadConfig({ repoRoot: options.repo });
    mkdirSync(config.worktreeDir, { recursive: true });

    const threadId = options.thread ?? randomUUID();
    const graph = buildGraph(config, createCheckpointer(config.stateDir));

    heading(`Run ${threadId}`);
    console.log(`${pc.dim("repo   ")} ${config.repo.root}`);
    console.log(`${pc.dim("build  ")} ${config.repo.buildCommand}`);
    console.log(`${pc.dim("test   ")} ${config.repo.testCommand}`);
    console.log(`${pc.dim("request")} ${request}\n`);

    await drive(graph, { request }, threadId, config.model.models.coder);
  });

program
  .command("resume")
  .description("Resume a run that is paused at a gate.")
  .argument("<threadId>")
  .option("--approve", "Let it through.")
  .option("--revise <feedback>", "Send it back with a correction.")
  .option("--reject <reason>", "Stop the run.")
  .action(async (threadId: string, options: { approve?: boolean; revise?: string; reject?: string }) => {
    requireApiKey();
    const config = loadConfig();
    const graph = buildGraph(config, createCheckpointer(config.stateDir));

    const chosen = [options.approve && "approve", options.revise && "revise", options.reject && "reject"].filter(
      Boolean,
    );
    if (chosen.length !== 1) {
      console.error(pc.red("Pass exactly one of --approve, --revise <feedback>, or --reject <reason>."));
      process.exit(1);
    }

    const decision: GateDecision = options.approve
      ? { action: "approve" }
      : options.revise
        ? { action: "revise", feedback: options.revise }
        : { action: "reject", feedback: options.reject };

    heading(`Resuming ${threadId} — ${decision.action}`);
    await drive(graph, new Command({ resume: decision }), threadId, config.model.models.coder);
  });

program
  .command("status")
  .description("Show where a run is.")
  .argument("<threadId>")
  .action(async (threadId: string) => {
    const config = loadConfig();
    const graph = buildGraph(config, createCheckpointer(config.stateDir));
    const snapshot = await graph.getState({ configurable: { thread_id: threadId } });
    const state = snapshot.values as LoopStateType;

    if (!state.request) {
      console.error(pc.red(`No run with thread id ${threadId}.`));
      process.exit(1);
    }

    heading(`Run ${threadId} — ${state.status}`);
    console.log(`${pc.dim("request ")} ${state.request}`);
    console.log(`${pc.dim("branch  ")} ${state.branch}`);
    console.log(`${pc.dim("worktree")} ${state.workRoot}`);
    console.log(`${pc.dim("attempts")} ${state.attempt} of ${config.maxRepairAttempts}`);
    console.log(`${pc.dim("build   ")} ${state.build ? (state.build.ok ? "passing" : `failing (${state.build.exitCode})`) : "not run"}`);
    console.log(`${pc.dim("tests   ")} ${state.test ? (state.test.ok ? "passing" : `failing (${state.test.exitCode})`) : "not run"}`);
    console.log(`${pc.dim("next    ")} ${snapshot.next.join(", ") || "(done)"}`);
    if (state.outcome) console.log(`\n${state.outcome}`);

    heading("Timeline");
    for (const entry of state.events) {
      console.log(`${pc.dim(entry.at.slice(11, 19))} ${pc.green(entry.node.padEnd(11))} ${entry.message}`);
    }

    reportCost(state, config.model.models.coder);
  });

program
  .command("diff")
  .description("Print the full diff a run produced.")
  .argument("<threadId>")
  .action(async (threadId: string) => {
    const config = loadConfig();
    const graph = buildGraph(config, createCheckpointer(config.stateDir));
    const snapshot = await graph.getState({ configurable: { thread_id: threadId } });
    const state = snapshot.values as LoopStateType;
    console.log(state.diff || "(no diff recorded yet)");
  });

program
  .command("doctor")
  .description("Check that everything the loop depends on is actually working.")
  .action(async () => {
    const config = loadConfig();
    heading("Doctor");

    /**
     * A check reports its own verdict rather than merely returning text, and `warn` is a distinct
     * outcome from `fail`. The distinction is load-bearing here: an unconfigured HW.Api costs the
     * loop its runtime-diagnostics tools but nothing else, while a missing API key stops it dead.
     * Printing both as "ok" is how a doctor command becomes something people stop reading.
     */
    type Verdict = { level: "ok" | "warn" | "fail"; detail: string };

    const checks: [string, () => Promise<Verdict>][] = [
      [
        "ANTHROPIC_API_KEY",
        async () =>
          process.env.ANTHROPIC_API_KEY
            ? { level: "ok", detail: "set" }
            : { level: "fail", detail: "missing — the loop cannot call a model" },
      ],
      [
        "repository",
        async () => {
          const { git } = await import("./tools/shell.js");
          const head = await git("rev-parse --short HEAD", config.repo.root);
          return { level: "ok", detail: `${config.repo.root} @ ${head}` };
        },
      ],
      [
        "dotnet",
        async () => {
          const { run } = await import("./tools/shell.js");
          const result = await run("dotnet --version", { cwd: config.repo.root, timeoutSeconds: 60 });
          return result.exitCode === 0
            ? { level: "ok", detail: `dotnet ${result.stdout.trim()}` }
            : { level: "fail", detail: "not found on PATH — build and test nodes cannot run" };
        },
      ],
      [
        "HW.Api",
        async () => {
          if (!config.dotnetApiBaseUrl) {
            return { level: "warn", detail: "not configured — trace_log and trace_sql unavailable" };
          }
          const api = new DotnetApi(config.dotnetApiBaseUrl);
          return (await api.isReachable())
            ? { level: "ok", detail: `reachable at ${config.dotnetApiBaseUrl}` }
            : { level: "warn", detail: `configured but not reachable at ${config.dotnetApiBaseUrl}` };
        },
      ],
      [
        "checkpointer",
        async () => {
          createCheckpointer(config.stateDir);
          return { level: "ok", detail: `sqlite at ${config.stateDir}` };
        },
      ],
    ];

    const label = { ok: pc.green("ok  "), warn: pc.yellow("warn"), fail: pc.red("fail") };
    let failed = false;

    for (const [name, check] of checks) {
      try {
        const verdict = await check();
        if (verdict.level === "fail") failed = true;
        console.log(`${label[verdict.level]} ${name.padEnd(16)} ${verdict.detail}`);
      } catch (error) {
        failed = true;
        console.log(`${label.fail} ${name.padEnd(16)} ${error instanceof Error ? error.message : String(error)}`);
      }
    }

    heading("Models");
    for (const [role, model] of Object.entries(config.model.models)) {
      console.log(`  ${role.padEnd(9)} ${model.padEnd(18)} effort ${config.model.effort[role as keyof typeof config.model.effort]}`);
    }

    if (failed) {
      console.log(`\n${pc.red("Not ready to run.")} Fix the failures above.`);
      process.exitCode = 1;
    }
  });

await program.parseAsync();
