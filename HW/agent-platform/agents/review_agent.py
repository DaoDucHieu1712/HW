"""Review agent -- the quality gate the graph routes on."""

from __future__ import annotations

from typing import Any

from graph.state import AgentState, Status, blocking_comments, verification_passed

from .base import AgentConfig, AgentResult, BaseAgent


class ReviewAgent(BaseAgent):
    """Reviews the change for correctness and risk.

    Read-only by design. A reviewer that can edit stops being a reviewer: it
    fixes what it finds and the finding never gets recorded.
    """

    name = "review"
    description = (
        "Reviews the implemented change for correctness, security and design, and "
        "decides whether the run is finished or has to go back to the planner."
    )
    default_config = AgentConfig(
        tools=["read_file", "search_code"],
        skills=["clean_architecture", "event_driven_architecture"],
        max_tokens=12000,
        max_tool_iterations=10,
        effort="high",
    )
    output_schema = {
        "type": "object",
        "properties": {
            "summary": {"type": "string", "description": "The verdict in one paragraph."},
            "approved": {
                "type": "boolean",
                "description": "True only when nothing blocking remains.",
            },
            "comments": {
                "type": "array",
                "description": "Findings, most severe first. Empty is a valid answer.",
                "items": {
                    "type": "object",
                    "properties": {
                        "path": {"type": "string"},
                        "line": {"type": "integer"},
                        "severity": {
                            "type": "string",
                            "enum": ["blocker", "major", "minor", "nit"],
                        },
                        "category": {
                            "type": "string",
                            "enum": [
                                "correctness",
                                "security",
                                "performance",
                                "architecture",
                                "testing",
                                "style",
                            ],
                        },
                        "message": {"type": "string"},
                        "suggestion": {"type": "string"},
                    },
                    "required": ["severity", "category", "message"],
                },
            },
            "risk": {"type": "string", "enum": ["low", "medium", "high"]},
        },
        "required": ["summary", "approved", "comments"],
        "additionalProperties": True,
    }

    def build_prompt(self, state: AgentState) -> str:
        changes = state.get("code_changes") or []
        sections = [
            f"# Objective\n{state.get('objective') or state.get('task')}",
            "# Changes to review\n"
            + (
                "\n".join(
                    f"- {change.get('action', 'modified')} {change.get('path', '')}: "
                    f"{change.get('rationale', '')}"
                    for change in changes
                )
                or "(none recorded)"
            ),
            "# Verification evidence\n" + _evidence(state),
        ]

        artifacts = state.get("artifacts") or {}
        if artifacts.get("test_warning"):
            sections.append(f"# Noted by the test agent\n{artifacts['test_warning']}")
        if artifacts.get("plugin_findings"):
            sections.append(
                "# Findings from review plugins\n"
                + "\n".join(f"- {finding}" for finding in artifacts["plugin_findings"][:20])
            )

        sections.append(
            "Read the changed files before judging them. Report only defects you can "
            "point at -- a file, a line, and the input that makes it go wrong. Rank a "
            "finding 'blocker' only if shipping it would break correctness or security; "
            "style preferences are 'nit' and never block. If the change is sound, say so "
            "and approve it: manufacturing findings to look thorough wastes an iteration."
        )
        return "\n\n".join(sections)

    def apply(self, state: AgentState, result: AgentResult) -> dict[str, Any]:
        if not result.ok:
            return {
                "status": Status.NEEDS_REVISION.value,
                "errors": [f"review: {result.error}"],
                "history": [result.to_history()],
            }

        payload = result.payload
        comments = [
            {
                "path": comment.get("path", ""),
                "line": int(comment.get("line") or 0),
                "severity": str(comment.get("severity", "minor")).lower(),
                "category": str(comment.get("category", "correctness")).lower(),
                "message": comment.get("message", ""),
                "suggestion": comment.get("suggestion", ""),
                "source": self.name,
            }
            for comment in (payload.get("comments") or [])
        ]

        patch: dict[str, Any] = {
            "review_comments": comments,
            "history": [result.to_history()],
            "artifacts": {
                "review_summary": payload.get("summary", ""),
                "risk": payload.get("risk", "medium"),
            },
        }

        # Approval alone is not enough: where the run was supposed to build and
        # test, the evidence must be green. The reviewer's opinion cannot
        # override it. A read-only review workflow produces no such evidence and
        # says so by setting `verification_required` to false.
        blocking = [c for c in comments if c["severity"] in {"blocker", "major"}]
        needs_evidence = bool((state.get("context") or {}).get("verification_required", True))
        verified = verification_passed(state) if needs_evidence else True
        approved = bool(payload.get("approved")) and not blocking

        if approved and verified:
            patch["status"] = Status.SUCCESS.value
        else:
            patch["status"] = Status.NEEDS_REVISION.value
            if not verified:
                patch["errors"] = ["review: verification has not passed"]
        return patch


def _evidence(state: AgentState) -> str:
    lines: list[str] = []
    for result in state.get("test_results") or []:
        verdict = "PASS" if result.get("success") else "FAIL"
        detail = ""
        if result.get("kind") == "unit" and result.get("passed") is not None:
            detail = (
                f" (passed={result.get('passed')} failed={result.get('failed')} "
                f"skipped={result.get('skipped')})"
            )
        lines.append(f"- {result.get('kind', 'run')}: {verdict}{detail}")
    if not lines:
        lines.append("- none recorded; treat the change as unverified")
    existing = blocking_comments(state)
    if existing:
        lines.append(f"- {len(existing)} blocking finding(s) from a previous review")
    return "\n".join(lines)


__all__ = ["ReviewAgent"]
