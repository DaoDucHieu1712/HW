import { existsSync } from "node:fs";
import { mkdir, readFile, readdir, stat, writeFile } from "node:fs/promises";
import { dirname, join, relative, resolve, sep } from "node:path";

export interface WorkspaceFile {
  path: string;
  lines: number;
  bytes: number;
}

export interface CodeMatch {
  path: string;
  line: number;
  text: string;
}

export interface WorkspaceOptions {
  root: string;
  excludeDirs: string[];
  maxFileBytes?: number;
}

/**
 * Read and write access to one directory tree, and nothing outside it.
 *
 * Every path that reaches this class was written by a language model, so the containment check in
 * {@link resolvePath} is the point of the abstraction rather than an incidental detail: `../../..`,
 * an absolute `C:\Windows\...`, and a path that escapes through a symlinked directory are all
 * things a confused model produces, and all of them are refused here rather than deeper down.
 *
 * The instance is bound to the run's git worktree, not to the user's checkout. Writes are therefore
 * safe by construction — see `createWorktree` in `./shell.ts` for why that boundary exists.
 */
export class Workspace {
  readonly root: string;
  private readonly excludeDirs: Set<string>;
  private readonly maxFileBytes: number;

  constructor(options: WorkspaceOptions) {
    this.root = resolve(options.root);
    this.excludeDirs = new Set(options.excludeDirs);
    this.maxFileBytes = options.maxFileBytes ?? 400_000;
  }

  /**
   * Turns a model-supplied relative path into an absolute one inside the root, or throws.
   *
   * The check is done on the resolved path rather than by inspecting the input for `..`, because
   * string inspection misses the cases that matter (`a/../../b`, mixed separators, a path that is
   * already absolute). Comparing resolved prefixes is the only form of this check that holds.
   */
  resolvePath(relativePath: string): string {
    const absolute = resolve(this.root, relativePath);
    const rel = relative(this.root, absolute);

    if (rel.startsWith("..") || resolve(absolute) === resolve(this.root, "..")) {
      throw new Error(`Path '${relativePath}' is outside the workspace root and was refused.`);
    }
    if (rel.split(sep).some((segment) => this.excludeDirs.has(segment))) {
      throw new Error(`Path '${relativePath}' is in an excluded directory and was refused.`);
    }
    return absolute;
  }

  exists(relativePath: string): boolean {
    try {
      return existsSync(this.resolvePath(relativePath));
    } catch {
      return false;
    }
  }

  /** Walks the tree, skipping excluded directories, and returns paths matching an optional glob. */
  async list(globPattern: string | undefined, limit: number): Promise<WorkspaceFile[]> {
    const matcher = globPattern ? globToRegExp(globPattern) : null;
    const found: WorkspaceFile[] = [];

    const walk = async (dir: string): Promise<void> => {
      if (found.length >= limit) return;

      let entries;
      try {
        entries = await readdir(dir, { withFileTypes: true });
      } catch {
        return;
      }

      for (const entry of entries) {
        if (found.length >= limit) return;
        if (entry.isDirectory()) {
          if (this.excludeDirs.has(entry.name)) continue;
          await walk(join(dir, entry.name));
          continue;
        }
        if (!entry.isFile()) continue;

        const absolute = join(dir, entry.name);
        const rel = relative(this.root, absolute).split(sep).join("/");
        if (matcher && !matcher.test(rel)) continue;

        const info = await stat(absolute);
        if (info.size > this.maxFileBytes) continue;

        found.push({
          path: rel,
          bytes: info.size,
          lines: (await readFile(absolute, "utf8")).split("\n").length,
        });
      }
    };

    await walk(this.root);
    return found;
  }

  /**
   * Reads a file with 1-based line numbers prefixed.
   *
   * The numbers are not decoration: an agent that cannot cite a line cannot describe an edit
   * precisely, and a reviewer cannot check a claim about code it was shown without them.
   */
  async read(relativePath: string, fromLine?: number, toLine?: number): Promise<string> {
    const absolute = this.resolvePath(relativePath);
    if (!existsSync(absolute)) {
      throw new Error(`No file at '${relativePath}'. Use list_files or search_code to find it.`);
    }

    const lines = (await readFile(absolute, "utf8")).split("\n");
    const start = Math.max((fromLine ?? 1) - 1, 0);
    const end = Math.min(toLine ?? lines.length, lines.length);
    const width = String(end).length;

    const body = lines
      .slice(start, end)
      .map((line, index) => `${String(start + index + 1).padStart(width, " ")}\t${line}`)
      .join("\n");

    const header = `${relativePath} (lines ${start + 1}-${end} of ${lines.length})`;
    return `${header}\n${body}`;
  }

  /** Raw contents with no line numbers — what a diff and a content hash are computed from. */
  async readRaw(relativePath: string): Promise<string> {
    return readFile(this.resolvePath(relativePath), "utf8");
  }

  /**
   * Writes the complete contents of a file, creating parent directories as needed.
   *
   * Whole-file replacement rather than a patch format, for the same reason the .NET side chose it:
   * an applied diff has to be matched with fuzz, and a model that miscounts context lines produces
   * a patch that applies cleanly in the wrong place. A whole file is either right or obviously wrong.
   */
  async write(relativePath: string, content: string): Promise<void> {
    const absolute = this.resolvePath(relativePath);
    await mkdir(dirname(absolute), { recursive: true });
    await writeFile(absolute, content, "utf8");
  }

  async search(
    pattern: string,
    globPattern: string | undefined,
    isRegex: boolean,
    limit: number,
  ): Promise<CodeMatch[]> {
    const needle = isRegex ? new RegExp(pattern, "i") : null;
    const lowered = pattern.toLowerCase();
    const files = await this.list(globPattern, 5_000);
    const matches: CodeMatch[] = [];

    for (const file of files) {
      if (matches.length >= limit) break;

      const lines = (await readFile(this.resolvePath(file.path), "utf8")).split("\n");
      for (let index = 0; index < lines.length; index += 1) {
        if (matches.length >= limit) break;

        const line = lines[index];
        const hit = needle ? needle.test(line) : line.toLowerCase().includes(lowered);
        if (hit) matches.push({ path: file.path, line: index + 1, text: line.trim().slice(0, 300) });
      }
    }

    return matches;
  }
}

/**
 * Compiles a glob to a regular expression, supporting `**`, `*` and `?`.
 *
 * Written out rather than pulled from a dependency because the surface actually used is this small,
 * and because the ordering subtlety is worth stating: `**` must be replaced before `*`, or the
 * single-segment rule eats the first star of every double.
 */
function globToRegExp(pattern: string): RegExp {
  const escaped = pattern
    .replace(/[.+^${}()|[\]\\]/g, "\\$&")
    .replace(/\*\*\//g, "\u0000SLASH\u0000")
    .replace(/\*\*/g, "\u0000DEEP\u0000")
    .replace(/\*/g, "[^/]*")
    .replace(/\?/g, "[^/]")
    .replace(/\u0000SLASH\u0000/g, "(?:.*/)?")
    .replace(/\u0000DEEP\u0000/g, ".*");

  return new RegExp(`^${escaped}$`, "i");
}
