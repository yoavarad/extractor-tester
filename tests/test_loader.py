"""Tests for the input loader."""

import json
from pathlib import Path

import pytest

from extractor_tester.config import CompareConfig
from extractor_tester.loader import LoaderError, discover, load_item
from extractor_tester.models import ErrorCode
from tests.helpers import write_item


def cfg(max_item_bytes: int = 52_428_800) -> CompareConfig:
    """Build a minimal config."""
    return CompareConfig(
        name_field="name",
        mime_primary_field="a",
        mime_secondary_field="b",
        max_item_bytes=max_item_bytes,
    )


def dirs(tmp_path: Path) -> tuple[Path, Path]:
    """Create OLD and NEW folders."""
    old, new = tmp_path / "old", tmp_path / "new"
    old.mkdir()
    new.mkdir()
    return old, new


def test_sorted_and_paired(tmp_path: Path) -> None:
    """Items paired by stem and sorted."""
    old, new = dirs(tmp_path)
    for d in (old, new):
        for item_id in ("c", "a", "d", "b"):
            write_item(d, item_id, [])
    d = discover(old, new)
    assert [i.item_id for i in d.items] == ["a", "b", "c", "d"]
    assert d.items[0].old_path == old / "a.json"
    assert d.items[0].new_path == new / "a.json"
    assert d.ignored_count == 0
    assert d.warnings == []


def test_one_sided(tmp_path: Path) -> None:
    """One-sided items have the other path None."""
    old, new = dirs(tmp_path)
    write_item(old, "x", [])
    write_item(new, "y", [])
    x, y = discover(old, new).items
    assert (x.item_id, x.new_path) == ("x", None)
    assert (y.item_id, y.old_path) == ("y", None)


def test_ignored_counted(tmp_path: Path) -> None:
    """Subfolders and non-json files are counted across both sides."""
    old, new = dirs(tmp_path)
    write_item(old, "a", [])
    (old / "sub").mkdir()
    (old / "n.txt").write_text("x")
    (new / "sub.json").mkdir()
    d = discover(old, new)
    assert [i.item_id for i in d.items] == ["a"]
    assert d.ignored_count == 3


def test_file_mode(tmp_path: Path) -> None:
    """File mode yields one item keyed by the OLD stem."""
    old, new = dirs(tmp_path)
    o = write_item(old, "a", [])
    n = write_item(new, "b", [])
    d = discover(o, n)
    assert len(d.items) == 1
    assert (d.items[0].item_id, d.items[0].old_path, d.items[0].new_path) == ("a", o, n)
    assert d.ignored_count == 0


@pytest.mark.covers_error("INPUT_NOT_FOUND")
def test_input_not_found(tmp_path: Path) -> None:
    """Missing path raises INPUT_NOT_FOUND, OLD checked first."""
    with pytest.raises(LoaderError) as e:
        discover(tmp_path / "nope", tmp_path / "nope2")
    assert e.value.code == ErrorCode.INPUT_NOT_FOUND
    assert str(e.value) == f"Input not found: {tmp_path / 'nope'}"


@pytest.mark.covers_error("MIXED_INPUT_KINDS")
def test_mixed_kinds(tmp_path: Path) -> None:
    """File vs folder raises MIXED_INPUT_KINDS."""
    old, new = dirs(tmp_path)
    f = write_item(old, "a", [])
    with pytest.raises(LoaderError) as e:
        discover(f, new)
    assert e.value.code == ErrorCode.MIXED_INPUT_KINDS
    assert str(e.value) == "OLD is a file but NEW is a folder; both must be files or both folders"


@pytest.mark.covers_error("EMPTY_INPUT")
def test_empty_input_warning(tmp_path: Path) -> None:
    """No items yields EMPTY_INPUT warning, no raise."""
    old, new = dirs(tmp_path)
    d = discover(old, new)
    assert d.items == []
    (w,) = d.warnings
    assert (w.code, w.scope, w.severity) == (ErrorCode.EMPTY_INPUT, "run", "warning")
    assert w.message == f"No *.json items found in {old} or {new}"


def test_load_ok(tmp_path: Path) -> None:
    """Valid item loads."""
    p = write_item(tmp_path, "a", [{"fileId": "1", "parentId": ""}])
    assert load_item(p, "a", cfg())["itemId"] == "a"
    assert load_item(p, None, cfg())["itemId"] == "a"


@pytest.mark.parametrize(
    "content",
    [
        b"{not json",
        b"\xff\xfe",
        b"[]",
        b'{"files": []}',
        b'{"itemId": 1, "files": []}',
        b'{"itemId": "a"}',
        b'{"itemId": "a", "files": {}}',
        b'{"itemId": "a", "files": [1]}',
        b'{"itemId": "a", "files": [{"parentId": ""}]}',
        b'{"itemId": "a", "files": [{"fileId": "1"}]}',
    ],
)
@pytest.mark.covers_error("INVALID_JSON")
def test_invalid_json(tmp_path: Path, content: bytes) -> None:
    """Unparseable or malformed items raise INVALID_JSON."""
    p = tmp_path / "a.json"
    p.write_bytes(content)
    with pytest.raises(LoaderError) as e:
        load_item(p, "a", cfg(), "old")
    assert e.value.code == ErrorCode.INVALID_JSON


def test_unreadable_is_invalid_json(tmp_path: Path) -> None:
    """Missing file raises INVALID_JSON."""
    with pytest.raises(LoaderError) as e:
        load_item(tmp_path / "gone.json", None, cfg())
    assert e.value.code == ErrorCode.INVALID_JSON


@pytest.mark.covers_error("ITEM_TOO_LARGE")
def test_too_large_not_parsed(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    """Size check precedes parsing."""
    p = tmp_path / "a.json"
    p.write_bytes(b"{not json" + b" " * 100)

    def boom(*_a: object, **_k: object) -> None:
        raise AssertionError("parsed")

    monkeypatch.setattr(json, "loads", boom)
    with pytest.raises(LoaderError) as e:
        load_item(p, "a", cfg(max_item_bytes=10), "new")
    assert e.value.code == ErrorCode.ITEM_TOO_LARGE
    assert str(e.value) == "new item a is 109 bytes (limit 10)"


@pytest.mark.covers_error("ITEM_ID_MISMATCH")
def test_id_mismatch_folder_mode(tmp_path: Path) -> None:
    """itemId differing from the stem raises ITEM_ID_MISMATCH."""
    p = write_item(tmp_path, "a", [])
    with pytest.raises(LoaderError) as e:
        load_item(p, "b", cfg(), "old")
    assert e.value.code == ErrorCode.ITEM_ID_MISMATCH
    assert str(e.value) == f"old file {p.name} has itemId a, expected b"


@pytest.mark.covers_error("ITEM_ID_MISMATCH")
def test_id_mismatch_file_mode(tmp_path: Path) -> None:
    """NEW checked against OLD itemId in file mode."""
    old, new = dirs(tmp_path)
    o = write_item(old, "a", [])
    n = write_item(new, "b", [])
    expected = load_item(o, None, cfg(), "old")["itemId"]
    with pytest.raises(LoaderError) as e:
        load_item(n, expected, cfg(), "new")
    assert e.value.code == ErrorCode.ITEM_ID_MISMATCH


def test_utf8_bom_tolerated(tmp_path: Path) -> None:
    """A UTF-8 BOM prefix does not cause INVALID_JSON."""
    p = tmp_path / "a.json"
    p.write_bytes(b'\xef\xbb\xbf{"itemId": "a", "files": []}')
    item = load_item(p, "a", cfg(), "old")
    assert item["itemId"] == "a"
