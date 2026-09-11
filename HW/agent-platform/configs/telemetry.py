"""OpenTelemetry and logging bootstrap.

OpenTelemetry is an optional dependency. When it is missing (or tracing is
disabled) every helper here degrades to a no-op so the platform still runs --
observability must never be the reason a workflow cannot start.
"""

from __future__ import annotations

import logging
import logging.config
import os
from contextlib import contextmanager
from pathlib import Path
from typing import Any, Iterator

import yaml

from .settings import ObservabilitySettings, ROOT_DIR

logger = logging.getLogger(__name__)

_TRACER: Any | None = None
_CONFIGURED = False


def configure_logging(settings: ObservabilitySettings) -> None:
    """Apply ``configs/logging.yaml``, falling back to basicConfig."""
    global _CONFIGURED
    if _CONFIGURED:
        return
    log_dir = ROOT_DIR / settings.log_dir
    log_dir.mkdir(parents=True, exist_ok=True)

    config_path = ROOT_DIR / "configs" / "logging.yaml"
    if config_path.exists():
        with config_path.open("r", encoding="utf-8") as handle:
            config = yaml.safe_load(handle)
        # Rewrite relative filenames so the platform can be started from anywhere.
        for handler in (config.get("handlers") or {}).values():
            filename = handler.get("filename")
            if filename and not Path(filename).is_absolute():
                handler["filename"] = str(ROOT_DIR / filename)
        config.setdefault("root", {})["level"] = settings.log_level
        logging.config.dictConfig(config)
    else:  # pragma: no cover - only when the repo is partially checked out
        logging.basicConfig(
            level=settings.log_level,
            format="%(asctime)s %(levelname)-7s %(name)-28s %(message)s",
        )
    _CONFIGURED = True


def configure_tracing(settings: ObservabilitySettings) -> Any | None:
    """Initialise a tracer provider; returns a tracer or ``None``."""
    global _TRACER
    if _TRACER is not None:
        return _TRACER
    if not settings.traces_enabled:
        return None
    try:
        from opentelemetry import trace
        from opentelemetry.sdk.resources import Resource
        from opentelemetry.sdk.trace import TracerProvider
        from opentelemetry.sdk.trace.export import BatchSpanProcessor, ConsoleSpanExporter
    except ImportError:
        logger.debug("opentelemetry not installed; tracing disabled")
        return None

    resource = Resource.create(
        {
            "service.name": settings.service_name,
            "deployment.environment": os.getenv("AGENT_ENV", "development"),
        }
    )
    provider = TracerProvider(resource=resource)

    endpoint = settings.otlp_endpoint
    if endpoint:
        try:
            from opentelemetry.exporter.otlp.proto.http.trace_exporter import (
                OTLPSpanExporter,
            )

            provider.add_span_processor(
                BatchSpanProcessor(OTLPSpanExporter(endpoint=f"{endpoint.rstrip('/')}/v1/traces"))
            )
        except ImportError:  # pragma: no cover - exporter extra not installed
            logger.warning("OTLP endpoint configured but the exporter package is missing")
    if settings.console_exporter:
        provider.add_span_processor(BatchSpanProcessor(ConsoleSpanExporter()))

    trace.set_tracer_provider(provider)
    _TRACER = trace.get_tracer(settings.service_name)
    logger.info("tracing enabled (endpoint=%s)", endpoint or "console/none")
    return _TRACER


def get_tracer() -> Any | None:
    return _TRACER


@contextmanager
def span(name: str, **attributes: Any) -> Iterator[Any | None]:
    """Start a span when tracing is on; otherwise a zero-cost no-op."""
    tracer = get_tracer()
    if tracer is None:
        yield None
        return
    with tracer.start_as_current_span(name) as current:
        for key, value in attributes.items():
            if value is not None:
                current.set_attribute(key, value if isinstance(value, (str, int, float, bool)) else str(value))
        yield current


def bootstrap_observability(settings: ObservabilitySettings) -> None:
    configure_logging(settings)
    configure_tracing(settings)


__all__ = [
    "bootstrap_observability",
    "configure_logging",
    "configure_tracing",
    "get_tracer",
    "span",
]
