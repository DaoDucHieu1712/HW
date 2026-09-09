import { spawn } from "node:child_process";

export interface CommandResult {
  command: string;
  cwd: string;
  exitCode: number;
  stdout: string;
  stderr: string;
  timedOut: boolean;
  elapsedMs: number;
}

export interface RunOptions {
  cwd: string;
  timeoutSeconds?: number;
  /** Ceiling on captured output per stream. A failing build can emit megabytes; nobody reads them. */
  maxOutputChars?: number;
  env?: NodeJS.ProcessEnv;
}

/**
 * Runs a command and captures its result rather than throwing on failure.
 *
 * A non-zero exit is not an exception here: a failing build is the loop's normal input, the thing
 * the fixer reads and works from. Throwing would turn the most common state into an error path.
 *
 * The command runs through the platform shell, because the configured build and test commands are
 * written the way a person would type them (`dotnet build HW.slnx --nologo -v quiet`) rather than as
 * argv arrays. That makes the command string trusted input: it comes from config, never from a
 * model. No tool exposed to an agent reaches this function with model-supplied text.
 */
export function run(command: string, options: RunOptions): Promise<CommandResult> {
  const timeoutSeconds = options.timeoutSeconds ?? 900;
  const maxOutputChars = options.maxOutputChars ?? 60_000;
  const started = Date.now();

  return new Promise((resolvePromise) => {
    const child = spawn(command, {
      cwd: options.cwd,
      shell: true,
      env: { ...process.env, ...options.env },
      windowsHide: true,
    });

    let stdout = "";
    let stderr = "";
    let timedOut = false;

    child.stdout?.on("data", (chunk: Buffer) => {
      if (stdout.length < maxOutputChars) stdout += chunk.toString();
    });
    child.stderr?.on("data", (chunk: Buffer) => {
      if (stderr.length < maxOutputChars) stderr += chunk.toString();
    });

    const timer = setTimeout(() => {
      timedOut = true;
      child.kill("SIGKILL");
    }, timeoutSeconds * 1000);

    const finish = (exitCode: number) => {
      clearTimeout(timer);
      resolvePromise({
        command,
        cwd: options.cwd,
        exitCode,
        stdout: truncate(stdout, maxOutputChars),
        stderr: truncate(stderr, maxOutputChars),
        timedOut,
        elapsedMs: Date.now() - started,
      });
    };

    child.on("error", (error) => {
      stderr += `\n${error.message}`;
      finish(-1);
    });

    child.on("close", (code) => finish(timedOut ? 124 : (code ?? -1)));
  });
}

function truncate(value: string, max: number): string {
  return value.length <= max
    ? value
    : `${value.slice(0, max)}\n\n[output truncated at ${max} characters]`;
}

/** Runs a git command and returns stdout, throwing on failure — git failing here is a real error. */
export async function git(args: string, cwd: string): Promise<string> {
  const result = await run(`git ${args}`, { cwd, timeoutSeconds: 120 });
  if (result.exitCode !== 0) {
    throw new Error(`git ${args} failed (${result.exitCode}): ${result.stderr || result.stdout}`);
  }
  return result.stdout.trim();
}

/**
 * Creates an isolated worktree for one loop run.
 *
 * This is the containment boundary, and the reason the loop can be allowed to write files at all.
 * It writes, builds and tests inside the worktree, so a run that goes wrong — a fixer that deletes
 * the wrong file, a build that leaves artefacts, a run abandoned halfway — never touches the tree
 * the user is working in. Nothing reaches the main tree except through the patch gate at the end.
 *
 * The branch is cut from HEAD rather than from the working tree, so uncommitted local changes are
 * deliberately excluded: the loop develops against committed code, which is what makes the diff it
 * produces reviewable on its own terms.
 */
export async function createWorktree(
  repoRoot: string,
  worktreeDir: string,
  branch: string,
): Promise<{ path: string; baseCommit: string }> {
  const baseCommit = await git("rev-parse HEAD", repoRoot);
  const path = `${worktreeDir}/${branch.replace(/[/\\]/g, "-")}`;

  await git(`worktree add --detach "${path}" ${baseCommit}`, repoRoot);
  await git(`switch --create ${branch}`, path);

  return { path, baseCommit };
}

export async function removeWorktree(repoRoot: string, worktreePath: string): Promise<void> {
  await run(`git worktree remove --force "${worktreePath}"`, { cwd: repoRoot, timeoutSeconds: 120 });
}

/** Files changed in the worktree relative to the commit it branched from, staged or not. */
export async function changedFiles(worktreePath: string, baseCommit: string): Promise<string[]> {
  await run("git add -A", { cwd: worktreePath, timeoutSeconds: 120 });
  const output = await git(`diff --name-only ${baseCommit}`, worktreePath);
  return output.split("\n").map((line) => line.trim()).filter(Boolean);
}

export async function diffAgainst(worktreePath: string, baseCommit: string): Promise<string> {
  await run("git add -A", { cwd: worktreePath, timeoutSeconds: 120 });
  return git(`diff ${baseCommit}`, worktreePath);
}
