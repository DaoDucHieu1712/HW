import { ChatAnthropic } from "@langchain/anthropic";
import type { LoopConfig, Role } from "./config.js";
import type { Usage } from "./state.js";

/**
 * Builds the chat model for one role.
 *
 * Three settings here are deliberate rather than defaults:
 *
 *  - **Adaptive thinking.** Claude decides how much to think per turn. The older fixed
 *    `budget_tokens` form is rejected outright by Opus 5, and a fixed budget was always the wrong
 *    shape for this workload anyway: reading a file needs no reasoning, reconciling a failing test
 *    against a layering rule needs a lot.
 *  - **Effort per role.** This is the cost dial that does not change the model. The scout skims and
 *    summarises, so it runs lower; the coder and fixer do the reasoning that decides whether the run
 *    succeeds, so they run at `xhigh`.
 *  - **A large max_tokens.** The coder returns whole files. Truncating one mid-file produces a
 *    write that compiles to nothing and costs a whole repair cycle to discover.
 */
export function chatModel(role: Role, config: LoopConfig): ChatAnthropic {
  return new ChatAnthropic({
    model: config.model.models[role],
    maxTokens: config.model.maxTokens,
    thinking: { type: "adaptive" },
    outputConfig: { effort: config.model.effort[role] },
    streaming: true,
  });
}

/**
 * Pulls token counts out of whatever LangChain hands back.
 *
 * Defensive on purpose: `usage_metadata` is the normalised shape, but provider-specific responses
 * carry the cache counters in `response_metadata` instead, and the cache numbers are the ones worth
 * watching — a run whose cache reads are zero across iterations is re-paying full price for the same
 * conversation prefix every turn.
 */
export function readUsage(message: unknown): Usage {
  const source = message as {
    usage_metadata?: {
      input_tokens?: number;
      output_tokens?: number;
      input_token_details?: { cache_read?: number; cache_creation?: number };
    };
    response_metadata?: {
      usage?: {
        input_tokens?: number;
        output_tokens?: number;
        cache_read_input_tokens?: number;
        cache_creation_input_tokens?: number;
      };
    };
  };

  const normalised = source?.usage_metadata;
  const raw = source?.response_metadata?.usage;

  return {
    inputTokens: normalised?.input_tokens ?? raw?.input_tokens ?? 0,
    outputTokens: normalised?.output_tokens ?? raw?.output_tokens ?? 0,
    cacheReadTokens: normalised?.input_token_details?.cache_read ?? raw?.cache_read_input_tokens ?? 0,
    cacheCreationTokens:
      normalised?.input_token_details?.cache_creation ?? raw?.cache_creation_input_tokens ?? 0,
  };
}

/** Per-million-token list prices, for the run cost estimate the CLI prints. */
const PRICING: Record<string, { input: number; output: number; cacheRead: number }> = {
  "claude-opus-5": { input: 5, output: 25, cacheRead: 0.5 },
  "claude-sonnet-5": { input: 2, output: 10, cacheRead: 0.2 },
  "claude-haiku-4-5": { input: 1, output: 5, cacheRead: 0.1 },
};

/**
 * Estimates what a run cost, in dollars.
 *
 * An estimate, not a bill: it prices every token at the roles' shared rate rather than tracking
 * which model produced which token, so a mixed-model configuration is approximate. It exists so the
 * number is visible at all — an agentic loop with no cost readout is one nobody notices is expensive.
 */
export function estimateCost(usage: Usage, model: string): number {
  const price = PRICING[model] ?? PRICING["claude-opus-5"];
  return (
    (usage.inputTokens / 1e6) * price.input +
    (usage.outputTokens / 1e6) * price.output +
    (usage.cacheReadTokens / 1e6) * price.cacheRead +
    (usage.cacheCreationTokens / 1e6) * price.input * 1.25
  );
}
