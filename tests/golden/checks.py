"""Report checks: example presence, result keys defined in metrics.md, tool-error codes."""

import re
from collections.abc import Iterator
from pathlib import Path
from typing import Any

import yaml

ROOT = Path(__file__).resolve().parents[2]
METRICS_MD = ROOT / "docs" / "specs" / "metrics.md"
TOOL_ERRORS = ROOT / ".ydk" / "components" / "tool-error" / "errors"
_PLACEHOLDER = re.compile(r"<([^>]+)>")


def tool_error_codes() -> list[str]:
    """Codes of every tool-error manifest, sorted."""
    paths = TOOL_ERRORS.glob("*.yaml")
    return sorted(yaml.safe_load(p.read_text(encoding="utf-8"))["code"] for p in paths)


def metric_results(value: object, where: str = "report") -> Iterator[tuple[str, dict[str, Any]]]:
    """Yield (location, MetricResult dict) for every MetricResult in a report."""
    if isinstance(value, dict):
        if {"key", "count", "denominator", "rate", "examples"} <= value.keys():
            yield where, value
        for k, v in value.items():
            yield from metric_results(v, f"{where}.{k}")
    elif isinstance(value, list):
        for i, v in enumerate(value):
            yield from metric_results(v, f"{where}[{i}]")


def missing_examples(report: dict[str, Any]) -> list[str]:
    """Locations of non-zero MetricResults that carry no examples."""
    return [w for w, m in metric_results(report) if m["count"] and not m["examples"]]


def _key_patterns(metrics_md: str) -> list[re.Pattern[str]]:
    """Full-match patterns for every backticked key in the 'Result keys' section's list."""
    section = metrics_md.split("## Result keys", 1)[1].split("\n## ", 1)[0]
    body = "\n".join(line for line in section.splitlines() if line.startswith("- "))
    holes = {"field": ".+", "error code": "|".join(map(re.escape, tool_error_codes()))}
    patterns = []
    for token in re.findall(r"`([^`]+)`", body):
        parts = _PLACEHOLDER.split(token)  # literal, name, literal, name, ...
        rx = "".join(re.escape(p) if i % 2 == 0 else f"(?:{holes[p]})" for i, p in enumerate(parts))
        patterns.append(re.compile(rx))
    return patterns


def undefined_keys(report: dict[str, Any], metrics_md: str) -> list[str]:
    """Result keys of the report that no key in metrics.md defines."""
    patterns = _key_patterns(metrics_md)
    keys = sorted({m["key"] for _, m in metric_results(report)})
    return [k for k in keys if not any(p.fullmatch(k) for p in patterns)]
