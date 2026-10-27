"""Metadata flattening and field classification."""

from typing import Any, Literal

from extractor_tester.config import CompareConfig


def flatten(obj: dict[str, Any]) -> dict[str, Any]:
    """Flatten nested dicts to dotted paths; lists, scalars, empty dicts are values."""
    out: dict[str, Any] = {}
    stack: list[tuple[str, dict[str, Any]]] = [("", obj)]
    while stack:
        prefix, current = stack.pop()
        for key, value in current.items():
            path = f"{prefix}.{key}" if prefix else key
            if isinstance(value, dict) and value:
                stack.append((path, value))
            else:
                out[path] = value
    return out


def _covers(entries: list[str], path: str) -> bool:
    return any(path == e or path.startswith(e + ".") for e in entries)


def classify(path: str, config: CompareConfig) -> Literal["generic", "specific", "ignored"]:
    """Classify a dotted path as ignored, generic or specific (ignore wins)."""
    if _covers(config.ignore_fields, path):
        return "ignored"
    if _covers(config.generic_fields, path):
        return "generic"
    return "specific"
