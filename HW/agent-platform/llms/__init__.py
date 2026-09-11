"""LLM abstraction plus the Claude adapter."""

from .base import (
    LLMClient,
    LLMMessage,
    LLMResponse,
    ToolCall,
    parse_json_payload,
    system_blocks,
)
from .claude import ClaudeLLM
from .factory import build_llm
from .fake import EchoLLM, json_response, stub_from_schema, tool_call_response

__all__ = [
    "ClaudeLLM",
    "EchoLLM",
    "LLMClient",
    "LLMMessage",
    "LLMResponse",
    "ToolCall",
    "build_llm",
    "json_response",
    "parse_json_payload",
    "stub_from_schema",
    "system_blocks",
    "tool_call_response",
]
