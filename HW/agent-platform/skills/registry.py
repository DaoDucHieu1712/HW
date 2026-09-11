"""Skill registry.

A *skill* is reusable domain knowledge an agent can load into its prompt: the
conventions of CQRS, how this codebase does EF Core, what a good xUnit test
looks like. Skills are data, not code -- a team adds one by dropping a folder
in, without touching the platform.

Layout on disk::

    skills/<name>/
        skill.yaml      metadata: name, description, tags, applies_to
        prompt.md       the guidance injected into the system prompt
        examples/       optional worked examples, loaded on demand
"""

from __future__ import annotations

import logging
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterator, Sequence

import yaml

logger = logging.getLogger(__name__)

SKILL_MANIFEST = "skill.yaml"
SKILL_PROMPT = "prompt.md"
SKILL_EXAMPLES = "examples"


@dataclass(slots=True)
class Skill:
    """One packaged unit of domain knowledge."""

    name: str
    description: str
    prompt: str
    tags: tuple[str, ...] = ()
    #: Agent names this skill is relevant to; empty means "any".
    applies_to: tuple[str, ...] = ()
    version: str = "1.0"
    path: Path | None = None
    _examples: dict[str, str] | None = field(default=None, repr=False)

    @property
    def examples(self) -> dict[str, str]:
        """Worked examples, read lazily -- they are large and rarely all needed."""
        if self._examples is None:
            self._examples = {}
            if self.path is not None:
                directory = self.path / SKILL_EXAMPLES
                if directory.is_dir():
                    for file in sorted(directory.iterdir()):
                        if file.is_file():
                            self._examples[file.name] = file.read_text(
                                encoding="utf-8", errors="replace"
                            )
        return self._examples

    def render(self, *, include_examples: bool = False, max_examples: int = 1) -> str:
        """Render the skill as a prompt section."""
        block = f"### Skill: {self.name}\n{self.prompt.strip()}"
        if include_examples and self.examples:
            chosen = list(self.examples.items())[:max_examples]
            rendered = "\n\n".join(
                f"#### Example: {name}\n```\n{body.strip()}\n```" for name, body in chosen
            )
            block = f"{block}\n\n{rendered}"
        return block

    def to_dict(self) -> dict[str, Any]:
        return {
            "name": self.name,
            "description": self.description,
            "tags": list(self.tags),
            "applies_to": list(self.applies_to),
            "version": self.version,
            "examples": sorted(self.examples),
        }


class SkillRegistry:
    """Loads skills from disk and hands them to agents by name or tag."""

    def __init__(self) -> None:
        self._skills: dict[str, Skill] = {}

    # -- loading ---------------------------------------------------------

    @classmethod
    def from_directory(cls, directory: str | Path) -> "SkillRegistry":
        registry = cls()
        registry.load_directory(directory)
        return registry

    def load_directory(self, directory: str | Path) -> int:
        """Load every skill folder under ``directory``. Returns the count added."""
        root = Path(directory)
        if not root.is_dir():
            logger.warning("skills directory not found: %s", root)
            return 0

        loaded = 0
        for child in sorted(root.iterdir()):
            if not child.is_dir() or child.name.startswith((".", "_")):
                continue
            skill = self._load_one(child)
            if skill is not None:
                self.register(skill, replace=True)
                loaded += 1
        logger.info("loaded %d skills from %s", loaded, root)
        return loaded

    @staticmethod
    def _load_one(path: Path) -> Skill | None:
        manifest_path = path / SKILL_MANIFEST
        prompt_path = path / SKILL_PROMPT
        if not manifest_path.exists() or not prompt_path.exists():
            logger.warning("skipping %s: missing %s or %s", path.name, SKILL_MANIFEST, SKILL_PROMPT)
            return None
        try:
            manifest = yaml.safe_load(manifest_path.read_text(encoding="utf-8")) or {}
        except yaml.YAMLError as exc:
            logger.error("invalid %s in %s: %s", SKILL_MANIFEST, path.name, exc)
            return None

        return Skill(
            name=str(manifest.get("name") or path.name),
            description=str(manifest.get("description", "")),
            prompt=prompt_path.read_text(encoding="utf-8"),
            tags=tuple(manifest.get("tags") or ()),
            applies_to=tuple(manifest.get("applies_to") or ()),
            version=str(manifest.get("version", "1.0")),
            path=path,
        )

    # -- access ----------------------------------------------------------

    def register(self, skill: Skill, *, replace: bool = False) -> Skill:
        if skill.name in self._skills and not replace:
            raise ValueError(f"skill {skill.name!r} is already registered")
        self._skills[skill.name] = skill
        return skill

    def get(self, name: str) -> Skill:
        try:
            return self._skills[name]
        except KeyError as exc:
            raise KeyError(
                f"unknown skill {name!r}; available: {', '.join(sorted(self._skills))}"
            ) from exc

    def has(self, name: str) -> bool:
        return name in self._skills

    def names(self) -> list[str]:
        return sorted(self._skills)

    def by_tag(self, tag: str) -> list[Skill]:
        return [skill for skill in self._skills.values() if tag in skill.tags]

    def for_agent(self, agent: str) -> list[Skill]:
        """Skills that declare themselves relevant to ``agent``."""
        return [
            skill
            for skill in self._skills.values()
            if not skill.applies_to or agent in skill.applies_to
        ]

    def select(self, names: Sequence[str]) -> list[Skill]:
        """Resolve requested skills, warning about (not failing on) unknown ones."""
        selected: list[Skill] = []
        for name in names:
            if self.has(name):
                selected.append(self.get(name))
            else:
                logger.warning("agent requested unknown skill %r; ignoring", name)
        return selected

    def render(self, names: Sequence[str], *, include_examples: bool = False) -> str:
        """Render the selected skills as one prompt block."""
        skills = self.select(names)
        if not skills:
            return ""
        body = "\n\n".join(skill.render(include_examples=include_examples) for skill in skills)
        return "## Applicable skills\n\n" + body

    def __iter__(self) -> Iterator[Skill]:
        return iter(self._skills.values())

    def __len__(self) -> int:
        return len(self._skills)


__all__ = ["Skill", "SkillRegistry"]
