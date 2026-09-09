import type { LoopConfig, Role } from "../config.js";
import { coderPrompt, fixerPrompt, plannerPrompt, reviewerPrompt, scoutPrompt } from "./prompts.js";

/**
 * One agent: a role, the instructions that give it that role, and the tools it may use.
 *
 * Definitions are data rather than classes, deliberately mirroring `AgentDefinition` on the C# side.
 * A new specialist is a registration, not a type — and the tool list stays visible in one place,
 * which is what makes it reviewable.
 */
export interface AgentDefinition {
  role: Role;
  description: string;
  systemPrompt: (config: LoopConfig) => string;
  /**
   * Tool names this agent may call, from the registry in `../tools/index.ts`.
   *
   * An empty list means no tools — an agent that reasons over what it was handed rather than going
   * to look. The narrow surface is the main lever on whether an agent stays on task, so each list is
   * what that role needs and nothing more.
   */
  tools: string[];
  /** Model round-trips one turn may spend on tools before it must answer. */
  maxIterations: number;
}

export const AGENTS: Record<Role, AgentDefinition> = {
  scout: {
    role: "scout",
    description: "Reads the codebase and reports how the relevant part of it currently works. Read-only.",
    systemPrompt: (config) => scoutPrompt(config.repo),
    tools: ["list_files", "read_file", "search_code"],
    maxIterations: 14,
  },

  planner: {
    role: "planner",
    description: "Turns a request plus the scout's brief into the file-level plan a human approves.",
    systemPrompt: (config) => plannerPrompt(config.repo),

    // Read-only, and narrower than the scout's: the planner works from the brief. Given the whole
    // tree it re-explores instead of planning, and arrives at the same brief for twice the cost.
    tools: ["read_file", "search_code"],
    maxIterations: 10,
  },

  coder: {
    role: "coder",
    description: "Implements the approved plan in the isolated worktree, compiling as it goes.",
    systemPrompt: (config) => coderPrompt(config.repo),
    tools: ["list_files", "read_file", "search_code", "write_file", "run_build"],
    maxIterations: 30,
  },

  fixer: {
    role: "fixer",
    description: "Diagnoses a failing build or test from its output and repairs the cause.",
    systemPrompt: (config) => fixerPrompt(config.repo),

    // The only role with the live-diagnostics tools. A failure that is about runtime behaviour
    // rather than compilation is exactly where the C# side's log and SQL buffers earn their place.
    tools: ["list_files", "read_file", "search_code", "write_file", "run_build", "run_tests", "trace_log", "trace_sql"],
    maxIterations: 24,
  },

  reviewer: {
    role: "reviewer",
    description: "Judges a green change against the plan's acceptance criteria and the layering rules.",
    systemPrompt: (config) => reviewerPrompt(config.repo),

    // Read-only by construction. A reviewer that can write stops reviewing and starts fixing, and
    // then nothing independent has looked at the change.
    tools: ["list_files", "read_file", "search_code"],
    maxIterations: 16,
  },
};
