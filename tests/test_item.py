"""Integration tests for compare_item through hand-built item files."""

from pathlib import Path
from typing import Any

import pytest

from extractor_tester.compare.item import compare_item
from extractor_tester.config import CompareConfig
from extractor_tester.models import ErrorCode
from tests.helpers import write_item

CFG = CompareConfig.model_validate(
    {"name_field": "name", "mime_primary_field": "mp", "mime_secondary_field": "ms"}
)


def f(fid: str, parent: str, name: str, ms: str = "pdf", **extra: object) -> dict[str, object]:
    """Build one file entry; the root has parentId equal to its own id."""
    return {
        "fileId": fid,
        "parentId": parent,
        "rootId": "I",
        "name": name,
        "mp": "application",
        "ms": ms,
        "text": "hello",
        **extra,
    }


def sides(
    tmp_path: Path,
    old_files: list[dict[str, object]],
    new_files: list[dict[str, object]],
    old_meta: dict[str, Any] | None = None,
    new_meta: dict[str, Any] | None = None,
) -> tuple[Path, Path]:
    """Write OLD and NEW item files and return their paths."""
    (tmp_path / "old").mkdir()
    (tmp_path / "new").mkdir()
    o = write_item(tmp_path / "old", "I", old_files, **(old_meta or {}))
    n = write_item(tmp_path / "new", "I", new_files, **(new_meta or {}))
    return o, n


def test_one_fact_per_paired_and_one_sided_file(tmp_path: Path) -> None:
    """One fact per paired and one sided file."""
    root = f("I", "I", "root")
    o, n = sides(
        tmp_path,
        [root, f("a", "I", "a.pdf"), f("gone", "I", "gone.pdf")],
        [root, f("a", "I", "a.pdf"), f("new", "I", "new.pdf", ms="docx")],
    )
    result, facts = compare_item(o, n, CFG)
    assert result.status == "compared"
    assert [(p.kind, p.family) for p in facts] == [
        ("paired", "application/pdf"),
        ("paired", "application/pdf"),
        ("only-in-old", "application/pdf"),
        ("only-in-new", "application/docx"),
    ]
    assert (result.files_identical, result.files_changed) == (1, 1)
    assert (result.files_only_old, result.files_only_new) == (1, 1)
    root_facts = facts[0]
    assert (root_facts.children_old, root_facts.children_new) == (2, 2)
    assert (root_facts.children_only_old, root_facts.children_only_new) == (1, 1)
    assert not root_facts.identical
    assert facts[1].tier == "name"
    assert facts[1].tie_break == "none"
    assert facts[1].mime_equal is True
    assert facts[1].family_equal is True
    assert facts[1].text is not None


def test_metadata_change_is_one_changed_diff_on_right_pair(tmp_path: Path) -> None:
    """Metadata change is one changed diff on right pair."""
    root = f("I", "I", "root")
    o, n = sides(
        tmp_path,
        [root, f("a", "I", "a.pdf", author="x"), f("b", "I", "b.pdf", author="y")],
        [root, f("a", "I", "a.pdf", author="x"), f("b", "I", "b.pdf", author="z")],
    )
    result, facts = compare_item(o, n, CFG)
    by_path = {p.path_old: p for p in facts}
    changed = [d for d in by_path["root/b.pdf"].field_diffs if d.status == "changed"]
    assert [(d.path, d.old_value, d.new_value) for d in changed] == [("author", "y", "z")]
    assert by_path["root/a.pdf"].field_diffs == []
    assert by_path["root/a.pdf"].fields_same == ["author"]
    assert by_path["root/a.pdf"].identical
    assert not by_path["root/b.pdf"].identical
    assert (result.files_identical, result.files_changed) == (2, 1)


def test_item_metadata_diffs_exclude_id_and_files(tmp_path: Path) -> None:
    """Item metadata diffs exclude id and files."""
    root = f("I", "I", "root")
    o, n = sides(tmp_path, [root], [root], {"lang": "en", "k": 1}, {"lang": "fr", "k": 1})
    result, _ = compare_item(o, n, CFG)
    assert {d.path: d.status for d in result.metadata_diffs} == {"k": "same", "lang": "changed"}
    assert all(d.group == "item" for d in result.metadata_diffs)


def test_mime_change_marks_pair(tmp_path: Path) -> None:
    """Mime change marks pair."""
    o, n = sides(
        tmp_path,
        [f("I", "I", "root"), f("a", "I", "a.x", ms="pdf")],
        [f("I", "I", "root"), f("a", "I", "a.x", ms="docx")],
    )
    _, facts = compare_item(o, n, CFG)
    assert facts[1].mime_equal is False
    assert facts[1].family_equal is False
    assert not facts[1].identical


def test_exception_becomes_item_processing_failed(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Exception becomes item processing failed."""
    root = f("I", "I", "root")
    o, n = sides(tmp_path, [root], [root])

    def boom(*_a: object, **_k: object) -> None:
        raise RuntimeError("kaput")

    monkeypatch.setattr("extractor_tester.compare.item.match_item", boom)
    result, facts = compare_item(o, n, CFG)
    assert facts == []
    assert result.status == "error"
    (err,) = result.errors
    assert err.code == ErrorCode.ITEM_PROCESSING_FAILED
    assert err.scope == "item"
    assert err.item_id == "I"
    assert "RuntimeError" in err.message
    assert "kaput" in err.message


def test_loader_error_keeps_its_code(tmp_path: Path) -> None:
    """Loader error keeps its code."""
    o, n = sides(tmp_path, [f("I", "I", "root")], [f("I", "I", "root")])
    n.write_text("not json", encoding="utf-8")
    result, facts = compare_item(o, n, CFG)
    assert facts == []
    assert result.status == "error"
    assert result.errors[0].code == ErrorCode.INVALID_JSON


def test_facts_never_contain_full_text(tmp_path: Path) -> None:
    """Facts never contain full text."""
    big = "secret-body " * 500
    root = f("I", "I", "root", text=big)
    o, n = sides(tmp_path, [root], [root])
    _, facts = compare_item(o, n, CFG)
    assert "secret-body" not in facts[0].model_dump_json()


def test_file_level_error_attached_to_its_pair(tmp_path: Path) -> None:
    """File level error attached to its pair."""
    root = f("I", "I", "root")
    bad = f("a", "I", "a.pdf")
    del bad["name"]
    o, n = sides(tmp_path, [root, bad], [root, f("a", "I", "a.pdf")])
    _, facts = compare_item(o, n, CFG)
    assert any(e.code == ErrorCode.MISSING_FIELD for e in facts[1].errors) or any(
        e.code == ErrorCode.MISSING_FIELD for p in facts for e in p.errors
    )


def test_unattached_build_warnings_reach_item_result(tmp_path: Path) -> None:
    """Unattached build warnings reach item result."""
    root = f("I", "I", "root")
    o, n = sides(tmp_path, [root, f("orph", "nowhere", "o.pdf")], [root])
    result, facts = compare_item(o, n, CFG)
    assert [e.code for e in result.errors] == [ErrorCode.ORPHAN_FILE]
    assert len(facts) == 1


def test_paired_text_facts_attached(tmp_path: Path) -> None:
    """Paired files carry text facts; one-sided files do not."""
    o, n = sides(
        tmp_path,
        [f("I", "I", "root"), f("a", "I", "a.pdf", text="alpha beta")],
        [
            f("I", "I", "root"),
            f("a", "I", "a.pdf", text="alpha gamma"),
            f("b", "I", "b.docx", ms="docx"),
        ],
    )
    _, facts = compare_item(o, n, CFG)
    paired = [p for p in facts if p.kind == "paired"]
    assert all(p.text is not None for p in paired)
    assert next(p for p in facts if p.kind == "only-in-new").text is None


def test_invalid_base64_skips_text_but_keeps_field_diffs(tmp_path: Path) -> None:
    """Invalid base64 gives no text facts, a file error and still field diffs."""
    o, n = sides(
        tmp_path,
        [f("I", "I", "root"), f("a", "I", "a.pdf", title="x")],
        [f("I", "I", "root"), f("a", "I", "a.pdf", title="y")],
    )
    data = n.read_text(encoding="utf-8").replace("aGVsbG8=", "!!notbase64")
    n.write_text(data, encoding="utf-8")
    _, facts = compare_item(o, n, CFG)
    pair = next(
        p for p in facts if p.path_old == p.path_new and (p.path_old or "").endswith("a.pdf")
    )
    assert pair.text is None
    assert any(e.code == ErrorCode.INVALID_BASE64 for e in pair.errors)
    assert [d.path for d in pair.field_diffs] == ["title"]
    assert not pair.identical


def test_whitespace_only_change_is_identical(tmp_path: Path) -> None:
    """Whitespace-only change leaves the pair identical."""
    o, n = sides(
        tmp_path,
        [f("I", "I", "root"), f("a", "I", "a.pdf", text="a b")],
        [f("I", "I", "root"), f("a", "I", "a.pdf", text="a \n  b ")],
    )
    result, facts = compare_item(o, n, CFG)
    assert all(p.identical for p in facts)
    assert result.files_identical == 2


def test_text_change_is_not_identical_and_hinted(tmp_path: Path) -> None:
    """New text where old had none raises new-more-text; reverse raises new-less-text."""
    o, n = sides(
        tmp_path,
        [f("I", "I", "root"), f("a", "I", "a.pdf", text="")],
        [f("I", "I", "root"), f("a", "I", "a.pdf", text="something new")],
    )
    _, facts = compare_item(o, n, CFG)
    pair = next(p for p in facts if p.path_old and p.path_old.endswith("a.pdf"))
    assert "new-more-text" in pair.hints
    assert not pair.identical
    _, back = compare_item(n, o, CFG)
    pair = next(p for p in back if p.path_old and p.path_old.endswith("a.pdf"))
    assert "new-less-text" in pair.hints


def test_field_filled_and_emptied_hints(tmp_path: Path) -> None:
    """A field present only in NEW is filled; only in OLD is emptied."""
    o, n = sides(
        tmp_path,
        [f("I", "I", "root"), f("a", "I", "a.pdf", author="bob")],
        [f("I", "I", "root"), f("a", "I", "a.pdf", title="t")],
    )
    _, facts = compare_item(o, n, CFG)
    pair = next(p for p in facts if p.path_old and p.path_old.endswith("a.pdf"))
    assert {"new-filled-field", "new-emptied-field"} <= set(pair.hints)


def test_subfile_count_hints(tmp_path: Path) -> None:
    """An extra child in NEW raises new-more-subfiles; a missing one new-fewer-subfiles."""
    base = [f("I", "I", "root"), f("a", "I", "a.pdf")]
    extra = [*base, f("c", "a", "c.pdf")]
    o, n = sides(tmp_path, base, extra)
    _, facts = compare_item(o, n, CFG)
    pair = next(p for p in facts if p.path_old and p.path_old.endswith("a.pdf"))
    assert "new-more-subfiles" in pair.hints
    assert not pair.identical
    _, back = compare_item(n, o, CFG)
    pair = next(p for p in back if p.path_old and p.path_old.endswith("a.pdf"))
    assert "new-fewer-subfiles" in pair.hints
