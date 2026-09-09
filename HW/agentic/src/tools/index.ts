import { tool } from "@langchain/core/tools";
import type { StructuredToolInterface } from "@langchain/core/tools";
import { z } from "zod";
import type { LoopConfig } from "../config.js";
import { DotnetApi } from "./dotnetApi.js";
import { run } from "./shell.js";
import { Workspace } from "./workspace.js";

export interface ToolContext {
  workspace: Workspace;
  api: DotnetApi;
  config: LoopConfig;
}

/** Results go back to the model as text, so they are formatted to be read rather than parsed. */
function present(value: unknown, maxChars: number): string {
  const text = typeof value === "string" ? value : JSON.stringify(value, null, 2);
  return text.length <= maxChars
    ? text
    : `${text.slice(0, maxChars)}\n\n[truncated at ${maxChars} characters — narrow the query to see the rest]`;
}

/**
 * Builds the tool surface for one run, bound to that run's worktree.
 *
 * Tools are created per run rather than once per process because the workspace they write through
 * is the run's isolated worktree. A shared, long-lived tool instance would be a tool pointing at the
 * wrong tree the moment a second run started.
 *
 * Returned as a name-keyed map so an agent definition can name the subset it may use — the same
 * narrow-the-tool-surface discipline the C# `AgentCatalog` applies, and for the same reason: a
 * reviewer handed a write tool eventually writes, and a planner handed a build tool stops planning.
 */
export function createTools(ctx: ToolContext): Record<string, StructuredToolInterface> {
  const { workspace, api, config } = ctx;
  const maxChars = config.maxToolResultChars;

  const listFiles = tool(
    async ({ glob, limit }) => {
      const files = await workspace.list(glob, Math.min(limit ?? 50, 200));
      if (files.length === 0) return "No files match that pattern.";
      return present({ root: workspace.root, count: files.length, files }, maxChars);
    },
    {
      name: "list_files",
      description:
        "List source files in the repository, optionally filtered by a glob such as " +
        "HW.Application/**/*.cs. Returns paths with line counts and sizes, not contents. Use it to " +
        "find where something lives before reading it — reading a file you guessed at wastes a turn.",
      schema: z.object({
        glob: z.string().optional().describe("Glob relative to the repository root, e.g. HW.Domain/Entities/*.cs."),
        limit: z.number().int().optional().describe("Maximum paths to return. Defaults to 50."),
      }),
    },
  );

  const readFileTool = tool(
    async ({ path, fromLine, toLine }) => present(await workspace.read(path, fromLine, toLine), maxChars),
    {
      name: "read_file",
      description:
        "Read a source file with 1-based line numbers prefixed. Pass fromLine and toLine to read a " +
        "window of a large file rather than all of it. Always read a file before writing it — " +
        "write_file replaces the whole file, so a write composed from memory silently deletes " +
        "whatever you did not know was there.",
      schema: z.object({
        path: z.string().describe("Path relative to the repository root, e.g. HW.Domain/Entities/Vocab.cs."),
        fromLine: z.number().int().optional().describe("First line to read, 1-based."),
        toLine: z.number().int().optional().describe("Last line to read, inclusive."),
      }),
    },
  );

  const searchCode = tool(
    async ({ pattern, glob, regex, limit }) => {
      const matches = await workspace.search(pattern, glob, regex ?? false, Math.min(limit ?? 40, 200));
      if (matches.length === 0) return "No matches.";
      return present({ count: matches.length, matches }, maxChars);
    },
    {
      name: "search_code",
      description:
        "Search the repository for a string or regular expression and get back matching files with " +
        "line numbers. The fastest way to find where a type, an interface, or a DI registration " +
        "actually lives. Narrow with a glob when a common word would match everywhere.",
      schema: z.object({
        pattern: z.string().describe("Text to find, or a regular expression when regex is true."),
        regex: z.boolean().optional().describe("Treat the pattern as a regular expression. Defaults to false."),
        glob: z.string().optional().describe("Restrict the search, e.g. HW.Infrastructure/**/*.cs."),
        limit: z.number().int().optional().describe("Maximum matches to return. Defaults to 40."),
      }),
    },
  );

  const writeFileTool = tool(
    async ({ path, content }) => {
      if (content.trim().length === 0) {
        throw new Error(
          `Refused to write an empty file to ${path}. Send the complete contents, not a fragment.`,
        );
      }
      const existed = workspace.exists(path);
      await workspace.write(path, content);
      return `${existed ? "Updated" : "Created"} ${path} (${content.split("\n").length} lines).`;
    },
    {
      name: "write_file",
      description:
        "Write the COMPLETE new contents of a file. It replaces the file wholesale — it is not a " +
        "diff and not a fragment, so read the file first and change only what needs changing. " +
        "Writes go to an isolated git worktree, never to the user's checkout, so you can iterate " +
        "freely; nothing reaches their tree until a human approves the final patch.",
      schema: z.object({
        path: z.string().describe("Path relative to the repository root."),
        content: z.string().describe("The complete new contents of the file."),
      }),
    },
  );

  const runBuild = tool(
    async () => {
      const result = await run(config.repo.buildCommand, {
        cwd: workspace.root,
        timeoutSeconds: config.commandTimeoutSeconds,
      });
      const output = `${result.stdout}\n${result.stderr}`.trim();
      return present(
        result.exitCode === 0
          ? `BUILD SUCCEEDED in ${result.elapsedMs}ms.`
          : `BUILD FAILED (exit ${result.exitCode}) in ${result.elapsedMs}ms.\n\n${output}`,
        maxChars,
      );
    },
    {
      name: "run_build",
      description:
        "Compile the solution and get the compiler's output back. Use it to check your own work " +
        "before you finish a turn — a change you have not compiled is a guess. The loop builds again " +
        "independently after your turn, so this is for your benefit, not for the record.",
      schema: z.object({}),
    },
  );

  const runTests = tool(
    async () => {
      const result = await run(config.repo.testCommand, {
        cwd: workspace.root,
        timeoutSeconds: config.commandTimeoutSeconds,
      });
      const output = `${result.stdout}\n${result.stderr}`.trim();
      return present(
        result.exitCode === 0
          ? `TESTS PASSED in ${result.elapsedMs}ms.\n\n${output}`
          : `TESTS FAILED (exit ${result.exitCode}) in ${result.elapsedMs}ms.\n\n${output}`,
        maxChars,
      );
    },
    {
      name: "run_tests",
      description:
        "Run the solution's test suite and get the results back, including the failure output of any " +
        "test that did not pass. Run it after a change you believe is complete.",
      schema: z.object({}),
    },
  );

  const traceLog = tool(
    async (params) => {
      const result = await api.traceLog(params);
      return result === null
        ? "HW.Api is not configured or not running, so no runtime logs are available. Reason from the source instead."
        : present(result, maxChars);
    },
    {
      name: "trace_log",
      description:
        "Read the RUNNING application's recent log entries, with level, category, message, correlation " +
        "id and full exception text. This is live runtime evidence from HW.Api, not something you can " +
        "get by reading source. Take a correlationId from an entry and pass it to trace_sql to see " +
        "what that same request did against the database.",
      schema: z.object({
        correlationId: z.string().optional().describe("Only entries from this request."),
        contains: z.string().optional().describe("Case-insensitive substring of the message or exception."),
        minLevel: z
          .enum(["Trace", "Debug", "Information", "Warning", "Error", "Critical"])
          .optional()
          .describe("Lowest severity to include."),
        limit: z.number().int().optional().describe("How many to return. Defaults to 25."),
      }),
    },
  );

  const traceSql = tool(
    async (params) => {
      const result = await api.traceSql(params);
      return result === null
        ? "HW.Api is not configured or not running, so no SQL trace is available. Reason from the source instead."
        : present(result, maxChars);
    },
    {
      name: "trace_sql",
      description:
        "Read the SQL the RUNNING application actually issued, with timings. Use it to see N+1 " +
        "queries, missing includes and slow commands that source alone will not reveal. Scope it to " +
        "one request with the correlationId from trace_log.",
      schema: z.object({
        correlationId: z.string().optional().describe("Only commands from this request."),
        contains: z.string().optional().describe("Case-insensitive substring of the SQL text."),
        minElapsedMs: z.number().int().optional().describe("Only commands slower than this."),
        limit: z.number().int().optional().describe("How many to return. Defaults to 25."),
      }),
    },
  );

  return {
    list_files: listFiles,
    read_file: readFileTool,
    search_code: searchCode,
    write_file: writeFileTool,
    run_build: runBuild,
    run_tests: runTests,
    trace_log: traceLog,
    trace_sql: traceSql,
  };
}
