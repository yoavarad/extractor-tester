"""Tests for the tree builder."""

import base64
from typing import Any

import pytest

from extractor_tester.config import CompareConfig
from extractor_tester.decode import text_hash
from extractor_tester.matching.matcher import match_item
from extractor_tester.models import ErrorCode, FileRecord
from extractor_tester.tree import TreeError, build

CFG = CompareConfig(name_field="name", mime_primary_field="mt.a", mime_secondary_field="mt.b")


def f(fid: str, parent: str, name: str | None = "n", text: str | None = "hi") -> dict:
    """Build one raw file entry rooted at item 'I'."""
    e: dict[str, Any] = {
        "fileId": fid,
        "parentId": parent,
        "rootId": "I",
        "mt": {"a": "text", "b": "plain"},
    }
    if name is not None:
        e["name"] = name
    if text is not None:
        e["text"] = base64.b64encode(text.encode()).decode()
    return e


def item(*files: dict) -> dict:
    """Build raw item data."""
    return {"itemId": "I", "files": list(files)}


def walk(rec: FileRecord) -> list[FileRecord]:
    """Flatten a tree iteratively."""
    out, stack = [], [rec]
    while stack:
        r = stack.pop()
        out.append(r)
        stack.extend(r.children)
    return out


def test_tree_and_order() -> None:
    """Tree and order."""
    res = build(item(f("I", "I", "root"), f("c", "I", "b"), f("d", "I", "a")), CFG, "old")
    root = res.item.files[0]
    assert [c.name for c in root.children] == ["b", "a"]
    assert [c.path for c in root.children] == ["root/b", "root/a"]
    assert root.text == "hi"
    assert root.mime == "text/plain"
    assert root.fields["mt.a"] == "text"
    assert res.errors == []


def test_sibling_occurrence_paths() -> None:
    """Sibling occurrence paths."""
    res = build(
        item(f("I", "I", "r"), f("1", "I", "a.png"), f("2", "I", "a.png"), f("3", "I", "b")),
        CFG,
        "new",
    )
    assert [c.path for c in res.item.files[0].children] == ["r/a.png[1]", "r/a.png[2]", "r/b"]


def test_root_not_found() -> None:
    """Root not found."""
    with pytest.raises(TreeError) as e:
        build(item(f("x", "I")), CFG, "old")
    assert e.value.code == ErrorCode.ROOT_NOT_FOUND


def test_multiple_roots() -> None:
    """Multiple roots."""
    with pytest.raises(TreeError) as e:
        build(item(f("I", "I"), f("I", "I")), CFG, "old")
    assert e.value.code == ErrorCode.MULTIPLE_ROOTS


def test_duplicate_file_id() -> None:
    """Duplicate file id."""
    with pytest.raises(TreeError) as e:
        build(item(f("I", "I"), f("a", "I"), f("a", "I")), CFG, "old")
    assert e.value.code == ErrorCode.DUPLICATE_FILE_ID


def test_parent_cycle() -> None:
    """Parent cycle."""
    with pytest.raises(TreeError) as e:
        build(item(f("I", "I"), f("a", "b"), f("b", "a")), CFG, "old")
    assert e.value.code == ErrorCode.PARENT_CYCLE


def test_orphan_keeps_rest() -> None:
    """Orphan keeps rest."""
    res = build(item(f("I", "I", "r"), f("a", "I", "ok"), f("o", "zz"), f("p", "o")), CFG, "old")
    assert sorted(r.name for r in walk(res.item.files[0])) == ["ok", "r"]
    assert [e.code for e in res.errors] == [ErrorCode.ORPHAN_FILE]


def test_missing_field_warning() -> None:
    """Missing field warning."""
    res = build(item(f("I", "I", "r"), f("a", "I", name=None, text=None)), CFG, "old")
    child = res.item.files[0].children[0]
    assert child.name == ""
    assert child.text == ""
    assert set(child.missing_fields) == {"name", "text"}
    assert {e.code for e in res.errors} == {ErrorCode.MISSING_FIELD}
    assert all(e.severity == "warning" for e in res.errors)


def test_deep_chain() -> None:
    """Deep chain."""
    files = [f("I", "I", "r")] + [
        f(str(i), "I" if i == 0 else str(i - 1), "d") for i in range(5000)
    ]
    res = build(item(*files), CFG, "old")
    assert len(walk(res.item.files[0])) == 5001


def test_paths_unique_despite_suffix_and_slash_names() -> None:
    """Paths unique despite suffix and slash names."""
    d = item(
        f("I", "I", "r"), f("1", "I", "a"), f("2", "I", "a"), f("3", "I", "a[2]"),
        f("4", "1", "b"), f("5", "I", "a/b"),
    )  # fmt: skip
    paths = [r.path for r in walk(build(d, CFG, "old").item.files[0])]
    assert len(paths) == len(set(paths)) == 6


def test_non_string_ids_are_tree_errors() -> None:
    """Non string ids are tree errors."""
    with pytest.raises(TreeError):
        build(item(f("I", "I"), {**f("x", "I"), "fileId": ["x"]}), CFG, "old")


def test_text_hash_set_and_empty_none() -> None:
    """Every record carries text_hash; empty text gives None."""
    res = build(item(f("I", "I", "r", "hi"), f("c", "I", "e", "")), CFG, "old")
    root = res.item.files[0]
    assert root.text_hash == text_hash("hi", CFG.text_hash_chars)
    assert root.text_hash is not None
    assert root.children[0].text_hash is None


def test_text_hash_ignores_whitespace_differences() -> None:
    """Texts differing only in whitespace hash equal."""
    a = build(item(f("I", "I", "r", "a  b\n c")), CFG, "old").item.files[0]
    b = build(item(f("I", "I", "r", " a b c ")), CFG, "new").item.files[0]
    assert a.text_hash == b.text_hash is not None


def test_matcher_pairs_renamed_file_by_text_hash() -> None:
    """Renamed file with same text and family pairs via the text-hash tier."""
    old = build(item(f("I", "I", "r"), f("c", "I", "old.txt", "same body")), CFG, "old").item
    new = build(item(f("I", "I", "r"), f("c", "I", "new.txt", "same  body")), CFG, "new").item
    pairs = match_item(old, new)
    assert [(p.kind, p.tier) for p in pairs[1:]] == [("paired", "text-hash")]
