"""Tests for the buffered text comparison method."""

import random

from extractor_tester.compare.text import compare_text
from extractor_tester.config import CompareConfig


def _cfg(**kw: object) -> CompareConfig:
    base = {"name_field": "name", "mime_primary_field": "a", "mime_secondary_field": "b"}
    return CompareConfig.model_validate({**base, **kw})


def _lines(n: int, seed: int = 1) -> list[str]:
    rng = random.Random(seed)
    words = ["alpha", "beta", "gamma", "delta", "omega", "sigma", "kappa", "theta"]
    return [" ".join(rng.choice(words) for _ in range(8)) + f" #{i}" for i in range(n)]


def _edit(lines: list[str]) -> list[str]:
    out = list(lines)
    for i in range(0, len(out), 37):
        out[i] = out[i].replace("alpha", "ALPHA-CHANGED")
    del out[100:105]
    out.insert(300, "a freshly inserted line of text")
    return out


def test_buffered_close_to_full_on_line_edits() -> None:
    """Buffered edit similarity is within 0.02 of the full method on 50k chars."""
    old = "\n".join(_lines(900))
    new = "\n".join(_edit(_lines(900)))
    assert len(old) > 40_000
    full = compare_text(old, new, _cfg())
    buf = compare_text(old, new, _cfg(full_compare_max_chars=1000))
    assert full.method == "full"
    assert buf.method == "buffered"
    assert abs((full.edit_similarity or 0) - (buf.edit_similarity or 0)) <= 0.02
    assert buf.relation == full.relation
    assert buf.jaccard == full.jaccard
    assert len(buf.snippets) == 3


def test_no_line_breaks_is_cut_and_counted() -> None:
    """One huge line is cut into blocks and the cut-line count recorded."""
    old = " ".join(f"word{i}" for i in range(2000))
    new = old.replace("word1000", "changed")
    f = compare_text(old, new, _cfg(full_compare_max_chars=1000, max_block_chars=500))
    assert f.method == "buffered"
    assert (f.lines_total, f.lines_cut, f.lines_cut_pct) == (2, 2, 100.0)
    assert 0.9 < (f.edit_similarity or 0) < 1


def test_uncut_lines_not_counted() -> None:
    """Short lines are never counted as cut."""
    old = "\n".join(_lines(200))
    f = compare_text(old, old + "\nextra", _cfg(full_compare_max_chars=1000))
    assert f.lines_total == 401  # both sides counted
    assert f.lines_cut == 0
    assert f.relation == "old-within-new"


def test_oversized_replaced_run_compared_block_by_block() -> None:
    """A replaced run above the full threshold is compared per block and counted."""
    old = "\n".join(f"old line number {i} filler" for i in range(100))
    new = "\n".join(f"new line number {i} filler" for i in range(100))
    f = compare_text(old, new, _cfg(full_compare_max_chars=1000))
    assert f.method == "buffered"
    assert f.oversized_runs == 1
    assert 0.5 < (f.edit_similarity or 0) < 1


def test_small_replaced_run_not_oversized() -> None:
    """Replaced runs within the threshold are compared as one piece."""
    old = "\n".join(_lines(200))
    lines = _lines(200)
    lines[50] = "completely different content here"
    f = compare_text(old, "\n".join(lines), _cfg(full_compare_max_chars=1000))
    assert f.oversized_runs == 0


def test_one_side_without_text() -> None:
    """Empty old side against long new text gives old-within-new."""
    f = compare_text("", "\n".join(_lines(200)), _cfg(full_compare_max_chars=1000))
    assert f.method == "buffered"
    assert f.relation == "old-within-new"
    assert f.edit_similarity == 0


def test_wrapped_vs_unwrapped_ratios_stay_within_one() -> None:
    """Separator spaces never inflate matched characters above either text length."""
    old = "\n".join("x" for _ in range(1000))
    new = " ".join("x" for _ in range(1000)) + " tail"
    f = compare_text(old, new, _cfg(full_compare_max_chars=500))
    for v in (f.edit_similarity, f.old_found_in_new, f.new_found_in_old):
        assert v is not None
        assert 0 <= v <= 1
