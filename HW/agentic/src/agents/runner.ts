import { AIMessage, HumanMessage, type BaseMessage } from "@langchain/core/messages";
import type { StructuredToolInterface } from "@langchain/core/tools";
import { createReactAgent } from "@langchain/langgraph/prebuilt";
import type { z } from "zod";
import type { LoopConfig, Role } from "../config.js";
import { chatModel, readUsage } from "../models.js";
import { ZERO_USAGE, type Usage } from "../state.js";
import { AGENTS } from "./definitions.js";

export interface AgentResult {
  /** The agent's final answer. */
  text: string;
  usage: Usage;
  /** True when the agent hit its iteration cap instead of finishing on its own terms. */
  budgetExhausted: boolean;
  toolCalls: number;
}

function sumUsage(messages: BaseMessage[]): Usage {
  return messages.reduce<Usage>((total, message) => {
    if (!(message instanceof AIMessage)) return total;
    const usage = readUsage(message);
    return {
      inputTokens: total.inputTokens + usage.inputTokens,
      outputTokens: total.outputTokens + usage.outputTokens,
      cacheReadTokens: total.cacheReadTokens + usage.cacheReadTokens,
      cacheCreationTokens: total.cacheCreationTokens + usage.cacheCreationTokens,
    };
  }, ZERO_USAGE);
}

function finalText(messages: BaseMessage[]): string {
  for (let index = messages.length - 1; index >= 0; index -= 1) {
    const message = messages[index];
    if (!(message instanceof AIMessage)) continue;
    const content = message.content;
    if (typeof content === "string" && content.trim().length > 0) return content;
    if (Array.isArray(content)) {
      const text = content
        .filter((block): block is { type: "text"; text: string } => {
          const candidate = block as { type?: string; text?: string };
          return candidate.type === "text" && typeof candidate.text === "string";
        })
        .map((block) => block.text)
        .join("\n")
        .trim();
      if (text.length > 0) return text;
    }
  }
  return "";
}

/**
 * Runs one agent to completion: ask, execute the tools it asked for, hand back the results, repeat.
 *
 * The tool-calling loop itself comes from LangGraph's prebuilt ReAct agent rather than being written
 * out again here — it is the same loop as `EngineerLoop` on the C# side, and there is nothing to be
 * gained by maintaining a second copy of it. What this function adds is the part that is specific to
 * this system: the per-role tool allowlist, the iteration budget, and usage accounting.
 *
 * The budget is enforced through LangGraph's recursion limit, which surfaces as a thrown error when
 * exceeded. That is caught rather than propagated: an agent that ran out of turns still gathered
 * work, and its partial output is more useful to the next node than a dead run. The caller sees
 * `budgetExhausted` and decides.
 */
export async function runAgent(
  role: Role,
  task: string,
  config: LoopConfig,
  registry: Record<string, StructuredToolInterface>,
): Promise<AgentResult> {
  const definition = AGENTS[role];

  const tools = definition.tools
    .map((name) => {
      const found = registry[name];
      if (!found) throw new Error(`Agent '${role}' lists tool '${name}', which is not registered.`);
      return found;
    })
    .filter(Boolean);

  const agent = createReactAgent({
    llm: chatModel(role, config),
    tools,
    prompt: definition.systemPrompt(config),
  });

  const maxIterations = Math.min(definition.maxIterations, config.maxIterations * 2);

  try {
    const result = await agent.invoke(
      { messages: [new HumanMessage(task)] },
      // Each ReAct iteration costs two graph steps (model, then tools), plus one to finish.
      { recursionLimit: maxIterations * 2 + 1 },
    );

    const messages = result.messages as BaseMessage[];
    return {
      text: finalText(messages),
      usage: sumUsage(messages),
      budgetExhausted: false,
      toolCalls: messages.filter((message) => message.getType() === "tool").length,
    };
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    if (!/recursion|GraphRecursionError/i.test(message)) throw error;

    return {
      text:
        `[${role} ran out of its ${maxIterations}-iteration budget before finishing. ` +
        "Its work so far is in the worktree, but it did not report a conclusion.]",
      usage: ZERO_USAGE,
      budgetExhausted: true,
      toolCalls: maxIterations,
    };
  }
}

/**
 * Runs a model call that must return a specific shape, with no tools.
 *
 * Used for the plan and the review — the two outputs the graph branches on. Free text would have to
 * be parsed, and a parser over model prose is a source of failures that only appear in production.
 * Structured output moves that guarantee to the API.
 */
export async function runStructured<T extends z.ZodType>(
  role: Role,
  task: string,
  schema: T,
  config: LoopConfig,
): Promise<{ value: z.infer<T>; usage: Usage }> {
  const model = chatModel(role, config).withStructuredOutput(schema, { includeRaw: true });
  const response = (await model.invoke([
    new HumanMessage(`${AGENTS[role].systemPrompt(config)}\n\n---\n\n${task}`),
  ])) as { parsed: z.infer<T>; raw: BaseMessage };

  return { value: response.parsed, usage: readUsage(response.raw) };
}
