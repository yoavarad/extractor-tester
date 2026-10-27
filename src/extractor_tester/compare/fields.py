"""Field-level diff of flattened metadata."""

import math
from typing import Any

from extractor_tester.config import CompareConfig
from extractor_tester.fields import classify
from extractor_tester.models import FieldDiff

Json = Any  # arbitrary JSON value


def is_empty(value: Json) -> bool:
    """Empty means null, empty string, empty list or empty object."""
    return value is None or (isinstance(value, str | list | dict) and not value)


def _json_type(value: Json) -> str:
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "boolean"
    if isinstance(value, int | float):
        return "number"
    if isinstance(value, str):
        return "string"
    if isinstance(value, list):
        return "array"
    return "object"


def _equal(a: Json, b: Json) -> bool:
    """JSON equality: numbers numerically, bool never equals a number."""
    ta, tb = _json_type(a), _json_type(b)
    if ta != tb:
        return False
    if ta == "array":
        return len(a) == len(b) and all(_equal(x, y) for x, y in zip(a, b, strict=True))
    if ta == "object":
        return a.keys() == b.keys() and all(_equal(a[k], b[k]) for k in a)
    if ta == "number" and math.isnan(a) and math.isnan(b):
        return True
    return bool(a == b)


def _snip(value: Json, limit: int) -> Json:
    return value[:limit] if isinstance(value, str) else value


def diff(old: dict[str, Any], new: dict[str, Any], config: CompareConfig) -> list[FieldDiff]:
    """Diff two flattened field maps, sorted by path, skipping ignored and structural paths."""
    structural = {
        config.name_field,
        config.mime_primary_field,
        config.mime_secondary_field,
        config.text_field,
    }
    out: list[FieldDiff] = []
    for path in sorted(old.keys() | new.keys()):
        group = classify(path, config)
        if group == "ignored" or path in structural:
            continue
        in_old, in_new = path in old, path in new
        if in_old and in_new:
            status = "same" if _equal(old[path], new[path]) else "changed"
        else:
            status = "only-in-old" if in_old else "only-in-new"
        n = config.snippet_chars
        out.append(
            FieldDiff(
                path=path,
                group=group,
                status=status,
                old_value=_snip(old.get(path), n),
                new_value=_snip(new.get(path), n),
                old_type=_json_type(old[path]) if in_old else None,
                new_type=_json_type(new[path]) if in_new else None,
            )
        )
    return out
