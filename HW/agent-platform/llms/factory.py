"""Builds the configured :class:`llms.base.LLMClient`.

Keeping construction in one place means agents never branch on provider, and
switching the whole platform into offline mode is a single config flag.
"""

from __future__ import annotations

import logging

from configs.settings import LLMSettings
from hooks.manager import HookManager

from .base import LLMClient
from .claude import ClaudeLLM
from .fake import EchoLLM

logger = logging.getLogger(__name__)


def build_llm(
    settings: LLMSettings,
    *,
    hooks: HookManager | None = None,
    model: str | None = None,
) -> LLMClient:
    """Return the LLM client for ``settings``.

    ``dry_run`` short-circuits to :class:`EchoLLM` so workflows can be wired up,
    demoed and tested without an API key or spend.
    """
    if settings.dry_run:
        logger.warning("LLM dry-run mode: no API calls will be made")
        return EchoLLM(model=f"dry-run:{model or settings.model}")

    provider = settings.provider.lower()
    if provider in {"anthropic", "claude"}:
        return ClaudeLLM(settings, hooks=hooks, model=model)
    if provider == "echo":
        return EchoLLM(model=model or settings.model)
    raise ValueError(
        f"unsupported LLM provider {settings.provider!r}; expected anthropic or echo"
    )


__all__ = ["build_llm"]
