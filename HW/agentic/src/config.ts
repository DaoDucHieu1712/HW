import { existsSync, readFileSync } from "node:fs";
import { dirname, isAbsolute, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

/** Repo root of this package (agentic/), independent of where the CLI was invoked from. */
export const PACKAGE_ROOT = resolve(dirname(fileURLToPath(import.meta.url)), "..");

/**
 * The roles the loop runs. Each maps to one agent definition and one model choice, so a role can be
 * moved to a cheaper model without touching the graph.
 */
export type Role = "scout" | "planner" | "coder" | "fixer" | "reviewer";

export const ROLES: Role[] = ["scout", "planner", "coder", "fixer", "reviewer"];

/**
 * What the loop needs to know about the codebase it is developing in.
 *
 * Everything here is data rather than code: pointing the loop at a different repository is a config
 * change, not a fork. `conventions` is injected verbatim into the planner and coder prompts — it is
 * the single place where "this is a DDD/CQRS .NET solution" is stated.
 */
export interface RepoConfig {
  /** Absolute path to the repository the loop develops in. */
  root: string;
  /** Shell command that must exit 0 before a change is considered to compile. */
  buildCommand: string;
  /** Shell command that must exit 0 before a change is considered correct. */
  testCommand: string;
  /** Glob prefixes the loop may read. Everything else is invisible to it. */
  includeGlobs: string[];
  /** Directories never read and never written, whatever an agent asks for. */
  excludeDirs: string[];
  /** Free text describing the architecture, layering rules and house style. Goes into prompts. */
  conventions: string;
}

export interface ModelConfig {
  /** Anthropic model id per role. */
  models: Record<Role, string>;
  /**
   * Reasoning depth per role. `xhigh` is the sweet spot for coding and long-horizon agentic work;
   * `low` is right for a role that only reads and summarises.
   */
  effort: Record<Role, "low" | "medium" | "high" | "xhigh" | "max">;
  maxTokens: number;
}

export interface LoopConfig {
  repo: RepoConfig;
  model: ModelConfig;
  /** How many model round-trips one agent turn may spend on tools before it must answer. */
  maxIterations: number;
  /** How many build/test repair cycles before the loop gives up and reports. */
  maxRepairAttempts: number;
  /** Ceiling on a single tool result, in characters. Keeps one `dotnet build` dump from eating context. */
  maxToolResultChars: number;
  /** Where checkpoints and proposals live. */
  stateDir: string;
  /** Where the loop's git worktrees are created. */
  worktreeDir: string;
  branchPrefix: string;
  /** Base URL of HW.Api, for the .NET-side tools and the shared patch queue. Empty disables them. */
  dotnetApiBaseUrl: string;
  /** Seconds a build or test command may run before it is killed. */
  commandTimeoutSeconds: number;
}

const DEFAULT_CONVENTIONS = `This is a .NET 8 solution laid out in DDD / Clean Architecture layers. The
dependency rule is strict and non-negotiable — a violation is a rejected change, not a style note:

  HW.Domain          entities, value objects, domain events, domain exceptions. References nothing.
  HW.Application     CQRS over MediatR (Commands/Queries + Handlers), FluentValidation validators,
                     pipeline behaviors, DTOs as records, abstractions (interfaces) for anything
                     outside the process. References Domain only.
  HW.Infrastructure  EF Core (Pomelo/MariaDB), Dapper, Redis cache, RabbitMQ/Kafka messaging, the
                     outbox, and the LLM provider adapters. Implements Application's abstractions.
  HW.Api             controllers, middleware, DI composition. References Application + Infrastructure.

House style, taken from the existing code:
  - Features are vertical slices: HW.Application/Features/<Area>/{Commands,Queries,Dtos}.
  - A query or command is a record implementing IRequest<T>, with a sibling Handler class.
  - DTOs are records. Mapping uses Mapster.
  - Controllers stay thin: build the request, ISender.Send, wrap in ApiResponseFactory.
  - Interfaces live in HW.Application/Abstractions/<Area>/, implementations in HW.Infrastructure/<Area>/.
  - XML doc comments explain WHY a design choice was made, not what the code literally does.
    Match that voice — the existing comments are prose, not restatements of the signature.
  - Nullable reference types are enabled. Do not introduce warnings.`;

function defaults(): LoopConfig {
  const repoRoot = resolve(PACKAGE_ROOT, "..");

  return {
    repo: {
      root: repoRoot,
      buildCommand: "dotnet build HW.slnx --nologo -v quiet",
      testCommand: "dotnet test HW.slnx --nologo -v quiet",
      includeGlobs: ["**/*.cs", "**/*.csproj", "**/*.slnx", "**/*.json", "**/*.md"],
      excludeDirs: ["bin", "obj", ".git", ".vs", "node_modules", "agentic", ".state"],
      conventions: DEFAULT_CONVENTIONS,
    },
    model: {
      // Claude Opus 5 everywhere by default. Moving a role to claude-sonnet-5 or claude-haiku-4-5
      // is a deliberate cost decision — make it here, per role, and measure the result.
      models: {
        scout: "claude-opus-5",
        planner: "claude-opus-5",
        coder: "claude-opus-5",
        fixer: "claude-opus-5",
        reviewer: "claude-opus-5",
      },
      effort: {
        scout: "medium",
        planner: "high",
        coder: "xhigh",
        fixer: "xhigh",
        reviewer: "high",
      },
      maxTokens: 32_000,
    },
    maxIterations: 24,
    maxRepairAttempts: 4,
    maxToolResultChars: 12_000,
    stateDir: join(PACKAGE_ROOT, ".state"),
    worktreeDir: join(PACKAGE_ROOT, ".state", "worktrees"),
    branchPrefix: "agentic/",
    dotnetApiBaseUrl: "",
    commandTimeoutSeconds: 900,
  };
}

/** Deep-merges a partial config file over the defaults. Arrays and scalars replace; objects merge. */
function merge<T>(base: T, override: unknown): T {
  if (override === undefined || override === null) return base;
  if (Array.isArray(base) || typeof base !== "object") return override as T;

  const out: Record<string, unknown> = { ...(base as Record<string, unknown>) };
  for (const [key, value] of Object.entries(override as Record<string, unknown>)) {
    out[key] = key in out ? merge((base as Record<string, unknown>)[key], value) : value;
  }
  return out as T;
}

/**
 * Loads `loop.config.json` from the package root if present, then applies environment overrides.
 *
 * The environment wins over the file so a one-off run can be pointed at another repository
 * (`LOOP_REPO_ROOT=... npm run loop -- run "..."`) without editing anything.
 */
export function loadConfig(overrides: Partial<{ repoRoot: string; apiBaseUrl: string }> = {}): LoopConfig {
  let config = defaults();

  const file = join(PACKAGE_ROOT, "loop.config.json");
  if (existsSync(file)) {
    config = merge(config, JSON.parse(readFileSync(file, "utf8")));
  }

  const repoRoot = overrides.repoRoot ?? process.env.LOOP_REPO_ROOT;
  if (repoRoot) config.repo.root = repoRoot;
  if (process.env.LOOP_BUILD_COMMAND) config.repo.buildCommand = process.env.LOOP_BUILD_COMMAND;
  if (process.env.LOOP_TEST_COMMAND) config.repo.testCommand = process.env.LOOP_TEST_COMMAND;

  const apiBaseUrl = overrides.apiBaseUrl ?? process.env.HW_API_BASE_URL;
  if (apiBaseUrl) config.dotnetApiBaseUrl = apiBaseUrl.replace(/\/+$/, "");

  if (process.env.LOOP_MAX_REPAIR_ATTEMPTS) {
    config.maxRepairAttempts = Number(process.env.LOOP_MAX_REPAIR_ATTEMPTS);
  }

  config.repo.root = isAbsolute(config.repo.root)
    ? resolve(config.repo.root)
    : resolve(PACKAGE_ROOT, config.repo.root);

  return config;
}
