import { mkdirSync } from "node:fs";
import { join } from "node:path";
import { SqliteSaver } from "@langchain/langgraph-checkpoint-sqlite";

/**
 * Durable state for the loop.
 *
 * SQLite on disk rather than the in-memory saver, because the two human gates make durability a
 * functional requirement rather than an operational nicety. A run pauses at the plan gate and the
 * process exits; the reviewer looks at it the next morning and resumes. With an in-memory saver that
 * run is simply gone, and the gates become "answer now or lose the work" — which is not a review.
 *
 * It also buys the thing that makes an agentic loop debuggable: every superstep is a checkpoint, so
 * a run that went wrong can be read back node by node instead of guessed at from logs.
 */
export function createCheckpointer(stateDir: string): SqliteSaver {
  mkdirSync(stateDir, { recursive: true });
  return SqliteSaver.fromConnString(join(stateDir, "loop.sqlite"));
}
