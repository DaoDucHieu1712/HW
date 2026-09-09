/**
 * Client for the HW.Api developer-agent endpoints.
 *
 * This is the seam where the TypeScript loop reuses the C# side rather than reimplementing it. Two
 * things live over there and only over there:
 *
 *  - **Runtime diagnostics.** `trace_log` and `trace_sql` read in-memory buffers inside the running
 *    API process — the log entries and the SQL a request actually issued. No amount of reading
 *    source code substitutes for them when a feature compiles, passes tests, and still behaves
 *    wrongly against a real database.
 *  - **The patch queue.** `IPatchProposalStore` already implements proposal, content hashing,
 *    staleness detection and human approval. The loop submits its finished diff there instead of
 *    growing a second, competing approval mechanism.
 *
 * Every method degrades rather than throws when the API is not running. That matters because the
 * loop's core cycle — plan, code, build, test — needs nothing from HW.Api, and a developer running
 * the loop without starting the API should lose the diagnostics, not the loop.
 */

export interface ApiPatchFile {
  path: string;
  newContent: string;
}

/** Builds a query string, dropping anything the caller left undefined. */
function query(params: Record<string, string | number | undefined>): string {
  const pairs = Object.entries(params).filter(
    (entry): entry is [string, string | number] => entry[1] !== undefined,
  );
  return pairs.length === 0
    ? ""
    : `?${pairs.map(([key, value]) => `${key}=${encodeURIComponent(String(value))}`).join("&")}`;
}

export interface ApiPatchProposal {
  id: string;
  agent: string;
  title: string;
  status: string;
  files: { path: string; diff: string }[];
}

export class DotnetApi {
  constructor(
    private readonly baseUrl: string,
    private readonly timeoutMs = 30_000,
  ) {}

  get enabled(): boolean {
    return this.baseUrl.length > 0;
  }

  /** True when HW.Api is actually reachable right now, not merely configured. */
  async isReachable(): Promise<boolean> {
    if (!this.enabled) return false;
    try {
      const response = await this.fetch("/api/dev-agent/providers", { method: "GET" });
      return response !== null;
    } catch {
      return false;
    }
  }

  /** Recent log entries from the running API, newest first. */
  async traceLog(params: {
    correlationId?: string;
    contains?: string;
    minLevel?: string;
    limit?: number;
  }): Promise<unknown> {
    return this.fetch(`/api/diagnostics/logs${query(params)}`, { method: "GET" });
  }

  /** SQL commands the running API issued, optionally scoped to one request's correlation id. */
  async traceSql(params: {
    correlationId?: string;
    contains?: string;
    minElapsedMs?: number;
    limit?: number;
  }): Promise<unknown> {
    return this.fetch(`/api/diagnostics/sql${query(params)}`, { method: "GET" });
  }

  /**
   * Submits the loop's finished change to the C# patch queue for human approval.
   *
   * Whole-file contents, matching `PatchFile.NewContent` on the .NET side: the store hashes each
   * file as it currently stands and refuses the patch at approval time if it moved underneath us.
   */
  async proposePatch(params: {
    agent: string;
    title: string;
    rationale: string;
    files: ApiPatchFile[];
  }): Promise<ApiPatchProposal | null> {
    const result = await this.fetch<{ data: ApiPatchProposal }>("/api/dev-agent/patches", {
      method: "POST",
      body: params,
    });
    return result?.data ?? null;
  }

  async approvePatch(id: string): Promise<unknown> {
    return this.fetch(`/api/dev-agent/patches/${encodeURIComponent(id)}/approve`, { method: "POST" });
  }

  async listPatches(status?: string): Promise<unknown> {
    const query = status ? `?status=${encodeURIComponent(status)}` : "";
    return this.fetch(`/api/dev-agent/patches${query}`, { method: "GET" });
  }

  private async fetch<T = unknown>(
    path: string,
    options: { method: string; body?: unknown },
  ): Promise<T | null> {
    if (!this.enabled) return null;

    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), this.timeoutMs);

    try {
      const response = await fetch(`${this.baseUrl}${path}`, {
        method: options.method,
        headers: options.body ? { "content-type": "application/json" } : undefined,
        body: options.body ? JSON.stringify(options.body) : undefined,
        signal: controller.signal,
      });

      if (!response.ok) {
        throw new Error(`${options.method} ${path} returned ${response.status} ${response.statusText}`);
      }
      return (await response.json()) as T;
    } finally {
      clearTimeout(timer);
    }
  }
}
