"""Typed configuration objects for the agent platform.

Configuration is layered, later layers win:

1. dataclass defaults (this module)
2. ``configs/platform.yaml`` (and any file passed to :func:`load_settings`)
3. environment variables / dotenv file

Nothing in this module imports the rest of the platform, so it can be loaded by
scripts and tests without booting the container.
"""

from __future__ import annotations

import os
from dataclasses import dataclass, field, fields, is_dataclass
from pathlib import Path
from typing import Any, Mapping, MutableMapping, TypeVar

import yaml

ROOT_DIR: Path = Path(__file__).resolve().parent.parent
CONFIG_DIR: Path = ROOT_DIR / "configs"
LOG_DIR: Path = ROOT_DIR / "logs"
DOTENV_FILENAME = ".env"

T = TypeVar("T")

# Model ids are exact strings from the Anthropic model table -- never append a
# date suffix to them.
DEFAULT_MODEL = "claude-sonnet-5"
DEFAULT_FAST_MODEL = "claude-haiku-4-5"


class ConfigError(RuntimeError):
    """Raised when configuration is missing or internally inconsistent."""


@dataclass(slots=True)
class LLMSettings:
    """Everything needed to talk to Claude."""

    provider: str = "anthropic"
    model: str = DEFAULT_MODEL
    fast_model: str = DEFAULT_FAST_MODEL
    max_tokens: int = 16000
    #: ``low`` | ``medium`` | ``high`` | ``xhigh`` | ``max`` -- sent as
    #: ``output_config.effort``. Higher effort costs more tokens.
    effort: str = "high"
    #: Adaptive thinking is the only supported on-mode for Claude Sonnet 5.
    thinking: bool = True
    #: ``omitted`` (default) or ``summarized`` -- controls whether reasoning
    #: summaries come back on the wire. Thinking is billed either way.
    thinking_display: str = "omitted"
    timeout_seconds: float = 600.0
    max_retries: int = 2
    #: Maximum tool-calling round trips inside a single agent turn.
    max_tool_iterations: int = 12
    api_key_env: str = "ANTHROPIC_API_KEY"
    #: When true no network call is made; the deterministic echo LLM is used.
    dry_run: bool = False


@dataclass(slots=True)
class MemorySettings:
    short_term_max_messages: int = 40
    long_term_path: str = ".state/long_term.json"
    vector_backend: str = "chroma"  # ``chroma`` | ``memory``
    vector_path: str = ".state/chroma"
    vector_collection: str = "project-knowledge"
    top_k: int = 5


@dataclass(slots=True)
class GraphSettings:
    """Loop control for the LangGraph state machine."""

    max_iterations: int = 3
    checkpoint_backend: str = "memory"  # ``memory`` | ``sqlite`` | ``none``
    checkpoint_path: str = ".state/checkpoints.sqlite"
    #: Node names that pause for a human before continuing.
    interrupt_before: list[str] = field(default_factory=list)
    human_in_the_loop: bool = False


@dataclass(slots=True)
class MCPServerSettings:
    name: str
    transport: str = "stdio"  # ``stdio`` | ``http`` | ``null``
    command: str | None = None
    args: list[str] = field(default_factory=list)
    url: str | None = None
    env: dict[str, str] = field(default_factory=dict)
    enabled: bool = False
    timeout_seconds: float = 30.0


@dataclass(slots=True)
class ObservabilitySettings:
    service_name: str = "agent-platform"
    log_level: str = "INFO"
    log_dir: str = "logs"
    json_logs: bool = False
    otlp_endpoint: str | None = None
    traces_enabled: bool = True
    console_exporter: bool = False


@dataclass(slots=True)
class IntegrationSettings:
    jira_base_url: str | None = None
    jira_email_env: str = "JIRA_EMAIL"
    jira_token_env: str = "JIRA_API_TOKEN"
    jira_project: str | None = None
    github_repo: str | None = None
    github_token_env: str = "GITHUB_TOKEN"
    github_api_url: str = "https://api.github.com"
    seq_base_url: str | None = None
    seq_api_key_env: str = "SEQ_API_KEY"


@dataclass(slots=True)
class WorkspaceSettings:
    """Where the platform is allowed to read and write source code."""

    root: str = "."
    #: Command used by the ``build_solution`` tool.
    build_command: str = "dotnet build"
    #: Command used by the ``run_tests`` tool.
    test_command: str = "dotnet test"
    #: Glob patterns never handed to a tool, whatever the model asks for.
    denied_globs: list[str] = field(
        default_factory=lambda: ["**/.git/**", "**/*.pem", "**/secrets/**", "**/*.env"]
    )
    max_file_bytes: int = 512_000
    #: When false ``write_file`` refuses to touch disk (proposal-only mode).
    allow_writes: bool = True


@dataclass(slots=True)
class PlatformSettings:
    """Root settings object; the single thing the container is built from."""

    environment: str = "development"
    llm: LLMSettings = field(default_factory=LLMSettings)
    memory: MemorySettings = field(default_factory=MemorySettings)
    graph: GraphSettings = field(default_factory=GraphSettings)
    observability: ObservabilitySettings = field(default_factory=ObservabilitySettings)
    integrations: IntegrationSettings = field(default_factory=IntegrationSettings)
    workspace: WorkspaceSettings = field(default_factory=WorkspaceSettings)
    mcps: dict[str, MCPServerSettings] = field(default_factory=dict)
    skills_dir: str = "skills"
    prompts_dir: str = "prompts/templates"
    enabled_plugins: list[str] = field(default_factory=list)

    @property
    def root_dir(self) -> Path:
        return ROOT_DIR

    def resolve(self, relative: str) -> Path:
        """Resolve a config-relative path against the platform root."""
        path = Path(relative)
        return path if path.is_absolute() else ROOT_DIR / path


def _coerce(target_type: Any, value: Any) -> Any:
    """Best-effort conversion of YAML scalars into the annotated type."""
    if value is None:
        return None
    if isinstance(target_type, str):
        # ``from __future__ import annotations`` turns annotations into strings.
        target_type = {"int": int, "float": float, "bool": bool, "str": str}.get(target_type, Any)
    origin = getattr(target_type, "__origin__", None)
    if origin in (list, dict) or target_type in (list, dict, Any):
        return value
    if target_type is bool and isinstance(value, str):
        return value.strip().lower() in {"1", "true", "yes", "on"}
    if target_type in (int, float, str) and not isinstance(value, target_type):
        try:
            return target_type(value)
        except (TypeError, ValueError):
            return value
    return value


def _apply(instance: T, values: Mapping[str, Any]) -> T:
    """Overlay a mapping onto a dataclass instance, recursing into nested ones."""
    if not is_dataclass(instance):  # pragma: no cover - defensive
        raise ConfigError(f"{instance!r} is not a dataclass")
    known = {f.name: f for f in fields(instance)}
    for key, value in values.items():
        target = known.get(key)
        if target is None:
            continue
        current = getattr(instance, key)
        if is_dataclass(current) and isinstance(value, Mapping):
            _apply(current, value)
        else:
            setattr(instance, key, _coerce(target.type, value))
    return instance


def _load_yaml(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    with path.open("r", encoding="utf-8") as handle:
        data = yaml.safe_load(handle) or {}
    if not isinstance(data, dict):
        raise ConfigError(f"{path} must contain a YAML mapping at the top level")
    return data


def _load_dotenv(path: Path) -> None:
    """Minimal dotenv loader -- existing environment variables always win."""
    if not path.exists():
        return
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, _, value = line.partition("=")
        os.environ.setdefault(key.strip(), value.strip().strip('"').strip("'"))


#: Environment variable -> dotted settings path.
ENV_OVERRIDES: dict[str, str] = {
    "AGENT_ENV": "environment",
    "AGENT_MODEL": "llm.model",
    "AGENT_FAST_MODEL": "llm.fast_model",
    "AGENT_EFFORT": "llm.effort",
    "AGENT_MAX_TOKENS": "llm.max_tokens",
    "AGENT_DRY_RUN": "llm.dry_run",
    "AGENT_MAX_ITERATIONS": "graph.max_iterations",
    "AGENT_HUMAN_IN_THE_LOOP": "graph.human_in_the_loop",
    "AGENT_LOG_LEVEL": "observability.log_level",
    "AGENT_OTLP_ENDPOINT": "observability.otlp_endpoint",
    "AGENT_WORKSPACE_ROOT": "workspace.root",
    "AGENT_BUILD_COMMAND": "workspace.build_command",
    "AGENT_TEST_COMMAND": "workspace.test_command",
    "AGENT_ALLOW_WRITES": "workspace.allow_writes",
    "JIRA_BASE_URL": "integrations.jira_base_url",
    "JIRA_PROJECT": "integrations.jira_project",
    "GITHUB_REPO": "integrations.github_repo",
    "SEQ_BASE_URL": "integrations.seq_base_url",
}


def _nest(dotted: str, value: Any) -> dict[str, Any]:
    head, _, tail = dotted.partition(".")
    return {head: _nest(tail, value)} if tail else {head: value}


def _deep_merge(
    base: MutableMapping[str, Any], overlay: Mapping[str, Any]
) -> MutableMapping[str, Any]:
    for key, value in overlay.items():
        if isinstance(value, Mapping) and isinstance(base.get(key), MutableMapping):
            _deep_merge(base[key], value)
        else:
            base[key] = value
    return base


def load_settings(
    config_path: str | Path | None = None,
    *,
    env: Mapping[str, str] | None = None,
) -> PlatformSettings:
    """Build :class:`PlatformSettings` from YAML plus environment overrides."""
    _load_dotenv(ROOT_DIR / DOTENV_FILENAME)
    environ = dict(os.environ if env is None else env)

    path = Path(config_path) if config_path else CONFIG_DIR / "platform.yaml"
    if not path.is_absolute():
        path = ROOT_DIR / path
    raw = _load_yaml(path)

    # MCP servers live in their own file so ops can rotate them independently.
    mcp_raw = _load_yaml(CONFIG_DIR / "mcps.yaml").get("mcps", {})

    overrides: dict[str, Any] = {}
    for env_key, dotted in ENV_OVERRIDES.items():
        if environ.get(env_key) not in (None, ""):
            _deep_merge(overrides, _nest(dotted, environ[env_key]))
    _deep_merge(raw, overrides)

    settings = _apply(PlatformSettings(), raw)
    settings.mcps = {
        name: _apply(MCPServerSettings(name=name), body or {})
        for name, body in (mcp_raw or {}).items()
    }
    return settings


__all__ = [
    "CONFIG_DIR",
    "ConfigError",
    "GraphSettings",
    "IntegrationSettings",
    "LLMSettings",
    "LOG_DIR",
    "MCPServerSettings",
    "MemorySettings",
    "ObservabilitySettings",
    "PlatformSettings",
    "ROOT_DIR",
    "WorkspaceSettings",
    "load_settings",
]
