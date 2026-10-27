"""Tests for flatten and classify."""

import pytest

from extractor_tester.config import CompareConfig
from extractor_tester.fields import classify, flatten


def cfg() -> CompareConfig:
    """Build a config with generic and ignored fields."""
    return CompareConfig(
        name_field="n",
        mime_primary_field="a",
        mime_secondary_field="b",
        generic_fields=["meta"],
        ignore_fields=["skip"],
    )


def test_flatten_deep() -> None:
    """2000-level nesting does not recurse."""
    obj: dict = {"leaf": 1}
    for _ in range(1999):
        obj = {"k": obj}
    out = flatten(obj)
    assert out == {".".join(["k"] * 1999 + ["leaf"]): 1}


def test_flatten_basic() -> None:
    """Lists are single values; empty dicts kept."""
    assert flatten({"a": {"b": [1, 2], "c": {}}, "d": 3}) == {"a.b": [1, 2], "a.c": {}, "d": 3}
    assert flatten({}) == {}


def test_classify() -> None:
    """Classification rules."""
    c = cfg()
    assert classify("meta.author.name", c) == "generic"
    assert classify("meta", c) == "generic"
    assert classify("metadata", c) == "specific"
    assert classify("skip.x", c) == "ignored"
    for p in ("fileId", "parentId", "rootId", "text"):
        assert classify(p, c) == "ignored"
    assert classify("other", c) == "specific"


def test_flatten_collision_raises() -> None:
    """Literal dotted key colliding with nested path raises, naming the path."""
    with pytest.raises(ValueError, match=r"a.b"):
        flatten({"a": {"b": 1}, "a.b": 2})


def test_classify_ignore_beats_generic() -> None:
    """A path covered by both ignore and generic is ignored."""
    c = cfg()
    c.generic_fields.append("skip")
    assert classify("skip.x", c) == "ignored"
