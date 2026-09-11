"""Architecture review plugin.

Checks the dependency rule: that no inner layer references an outer one. This is
the kind of erosion that never shows up in a single review but compounds over a
year, so it is worth a deterministic check rather than an opinion.
"""

from __future__ import annotations

import re
from typing import Any

from graph.state import AgentState

from .base import LLMReviewPlugin, PluginResult

#: layer -> namespaces it must never reference.
FORBIDDEN_REFERENCES: dict[str, tuple[str, ...]] = {
    "domain": (
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "System.Data",
        "Dapper",
        "Newtonsoft.Json",
        ".Infrastructure",
        ".Api",
    ),
    "application": (
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore.Mvc",
        "System.Data.SqlClient",
        ".Infrastructure",
        ".Api",
    ),
    "infrastructure": (".Api",),
}

_USING = re.compile(r"^\s*using\s+(?:static\s+)?([\w.]+)\s*;", re.MULTILINE)


def layer_of(path: str) -> str | None:
    """Infer the architectural layer from a path, or ``None`` if it is not clear."""
    lowered = path.replace("\\", "/").lower()
    for layer in ("domain", "application", "infrastructure", "api"):
        if f".{layer}/" in lowered or f"/{layer}/" in lowered:
            return layer
    return None


class ArchitectureReviewPlugin(LLMReviewPlugin):
    """Enforces layer boundaries and flags structural drift."""

    name = "architecture_review"
    version = "1.0"
    description = "Checks the dependency rule and reviews structural fit."
    stages = ("review",)
    focus = (
        "architecture. Check that dependencies point inward, that business rules "
        "sit on the domain rather than in handlers, that abstractions are owned by "
        "the layer that needs them, and that the change fits the shape of the "
        "codebase rather than introducing a second way of doing the same thing."
    )

    @property
    def category(self) -> str:
        return "architecture"

    def execute(self, state: AgentState) -> PluginResult:
        violations = self._check_dependency_rule(state)
        model_result = super().execute(state)
        return PluginResult(
            plugin=self.name,
            findings=violations + model_result.findings,
            artifacts={**model_result.artifacts, "dependency_violations": len(violations)},
            ok=model_result.ok,
            error=model_result.error,
        )

    def _check_dependency_rule(self, state: AgentState) -> list[Any]:
        findings: list[Any] = []
        for path in self.changed_files(state)[: self.max_files]:
            layer = layer_of(path)
            forbidden = FORBIDDEN_REFERENCES.get(layer or "", ())
            if not forbidden:
                continue
            content = self.read(path)
            if not content:
                continue
            for number, line in enumerate(content.splitlines(), start=1):
                match = _USING.match(line)
                if not match:
                    continue
                namespace = match.group(1)
                for banned in forbidden:
                    if banned in namespace:
                        findings.append(
                            self.finding(
                                f"The {layer} layer references {namespace}. Dependencies "
                                f"point inward: {layer} must not know about {banned}.",
                                severity="major",
                                category="architecture",
                                path=path,
                                line=number,
                                suggestion=(
                                    "Define the abstraction in this layer and implement "
                                    "it further out."
                                ),
                            )
                        )
                        break
        return findings


__all__ = ["FORBIDDEN_REFERENCES", "ArchitectureReviewPlugin", "layer_of"]
