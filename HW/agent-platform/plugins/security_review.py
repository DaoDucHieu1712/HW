"""Security review plugin.

Combines two passes: a deterministic pattern scan that never misses the obvious
things, and a model pass for the reasoning-dependent ones. The pattern scan runs
first and is not skippable -- a hardcoded credential should not depend on a
model noticing it.
"""

from __future__ import annotations

import re
from typing import Any, Pattern

from graph.state import AgentState

from .base import LLMReviewPlugin, PluginContext, PluginResult

#: ``(pattern, severity, message)``. Deliberately conservative: these fire on
#: shapes that are almost always wrong, not on anything that merely looks risky.
PATTERNS: list[tuple[Pattern[str], str, str]] = [
    (
        re.compile(r"""(password|pwd|secret|api[_-]?key|token)\s*=\s*["'][^"'\s]{8,}["']""", re.I),
        "blocker",
        "Hardcoded credential. Move it to configuration or a secret store.",
    ),
    (
        # A password inside a connection string literal. The value must not start
        # with a placeholder character, so templated strings do not fire.
        re.compile(r"""["'][^"']*\b(?:password|pwd)\s*=\s*[^;"'\s$%{<][^;"'\s]{3,}""", re.I),
        "blocker",
        "Hardcoded credential in a connection string. Move it to configuration "
        "or a secret store.",
    ),
    (
        re.compile(r"""\b(SELECT|INSERT|UPDATE|DELETE)\b[^;\n]*?\+\s*\w+""", re.I),
        "blocker",
        "SQL built by string concatenation. Use a parameterised query.",
    ),
    (
        re.compile(r"""\$@?"[^"]*\b(SELECT|INSERT|UPDATE|DELETE)\b[^"]*\{""", re.I),
        "blocker",
        "SQL built by string interpolation. Use a parameterised query.",
    ),
    (
        re.compile(r"\bcatch\s*\([^)]*\)\s*\{\s*\}", re.I),
        "major",
        "Empty catch block: the failure is swallowed and will surface somewhere unrelated.",
    ),
    (
        re.compile(r"ServerCertificateCustomValidationCallback\s*=\s*.*true", re.I),
        "blocker",
        "TLS certificate validation is disabled.",
    ),
    (
        re.compile(r"\[AllowAnonymous\]", re.I),
        "major",
        "Endpoint is anonymous. Confirm that is intended for this route.",
    ),
    (
        re.compile(r"\bMD5\b|\bSHA1\b|new\s+Random\s*\(", re.I),
        "major",
        "Weak hash or non-cryptographic RNG used where a secure primitive is expected.",
    ),
]


class SecurityReviewPlugin(LLMReviewPlugin):
    """Finds injection, secret-handling and authorisation defects."""

    name = "security_review"
    version = "1.0"
    description = "Scans changed files for security defects, by pattern and by model."
    stages = ("review",)
    focus = (
        "security. Look for injection (SQL, command, path), authentication and "
        "authorisation gaps, secrets in source, unsafe deserialisation, missing "
        "input validation at trust boundaries, and information disclosure in error "
        "responses."
    )

    @property
    def category(self) -> str:
        return "security"

    def register(self, context: PluginContext) -> None:
        super().register(context)

    def execute(self, state: AgentState) -> PluginResult:
        pattern_findings = self._scan(state)
        model_result = super().execute(state)
        return PluginResult(
            plugin=self.name,
            findings=pattern_findings + model_result.findings,
            artifacts={
                **model_result.artifacts,
                "pattern_findings": len(pattern_findings),
            },
            ok=model_result.ok,
            error=model_result.error,
        )

    def _scan(self, state: AgentState) -> list[Any]:
        """Deterministic pass. Runs even when the model pass fails."""
        findings: list[Any] = []
        for path in self.changed_files(state)[: self.max_files]:
            content = self.read(path)
            if not content:
                continue
            for number, line in enumerate(content.splitlines(), start=1):
                for pattern, severity, message in PATTERNS:
                    if pattern.search(line):
                        findings.append(
                            self.finding(
                                message,
                                severity=severity,
                                category="security",
                                path=path,
                                line=number,
                            )
                        )
                        break  # one finding per line is enough to act on
        return findings


__all__ = ["PATTERNS", "SecurityReviewPlugin"]
