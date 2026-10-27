"""Tests for the field differ."""

from extractor_tester.compare.fields import diff, is_empty
from extractor_tester.config import CompareConfig


def cfg(**kw: object) -> CompareConfig:
    """Build a config."""
    base: dict = {
        "name_field": "n",
        "mime_primary_field": "mp",
        "mime_secondary_field": "ms",
        "generic_fields": ["meta"],
        "ignore_fields": ["skip"],
    }
    base.update(kw)
    return CompareConfig(**base)


def by_path(old: dict, new: dict, **kw: object) -> dict:
    """Diff and index by path."""
    return {d.path: d for d in diff(old, new, cfg(**kw))}


def test_number_equality() -> None:
    """1 equals 1.0; true differs from 1."""
    r = by_path({"x": 1, "y": True, "z": 0}, {"x": 1.0, "y": 1, "z": False})
    assert r["x"].status == "same"
    assert r["y"].status == "changed"
    assert r["z"].status == "changed"


def test_null_is_value() -> None:
    """Null vs absent is one-sided; null vs null same."""
    r = by_path({"a": None, "b": None}, {"c": None, "b": None})
    assert r["a"].status == "only-in-old"
    assert r["c"].status == "only-in-new"
    assert r["b"].status == "same"


def test_type_change_records_types() -> None:
    """Number to string is changed with both types."""
    d = by_path({"a": 1}, {"a": "1"})["a"]
    assert (d.status, d.old_type, d.new_type) == ("changed", "number", "string")


def test_lists_whole_and_nested_numbers() -> None:
    """Lists compare whole; nested bool vs int differs."""
    r = by_path({"l": [1, 2], "m": [True], "o": {}}, {"l": [1.0, 2], "m": [1], "o": {}})
    assert r["l"].status == "same"
    assert r["m"].status == "changed"
    assert r["o"].status == "same"


def test_ignored_and_structural_skipped_sorted_groups() -> None:
    """Ignored and structural fields vanish; output sorted; groups set."""
    out = diff(
        {"skip": 1, "n": "x", "mp": "m", "ms": "t", "text": "t", "z": 1, "meta.k": 1},
        {"skip": 2, "n": "y", "mp": "m", "ms": "t", "text": "u", "z": 1, "meta.k": 2},
        cfg(),
    )
    assert [d.path for d in out] == ["meta.k", "z"]
    assert [d.group for d in out] == ["generic", "specific"]


def test_truncates_strings() -> None:
    """Long strings are cut to snippet_chars."""
    d = by_path({"a": "x" * 500}, {"a": "y"}, snippet_chars=20)["a"]
    assert d.old_value == "x" * 20
    assert d.new_value == "y"


def test_is_empty() -> None:
    """Empty means null, empty string, list or dict."""
    assert all(is_empty(v) for v in (None, "", [], {}))
    assert not any(is_empty(v) for v in (0, False, "a", [0], {"a": 1}))


def test_nan_same() -> None:
    """NaN equals NaN so identical metadata is not a diff."""
    assert by_path({"a": float("nan")}, {"a": float("nan")})["a"].status == "same"
