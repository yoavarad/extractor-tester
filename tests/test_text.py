"""Tests for extractor_tester.compare.text."""

import pytest

from extractor_tester.compare.text import compare_text
from extractor_tester.config import CompareConfig

OLD = "The quick brown fox jumps over the lazy dog near the river bank today."


def _cfg(**kw: object) -> CompareConfig:
    base = {"name_field": "name", "mime_primary_field": "a", "mime_secondary_field": "b"}
    return CompareConfig.model_validate({**base, **kw})


def test_identical_fast_path() -> None:
    """Identical texts take the fast path with exact flag true."""
    f = compare_text(OLD, OLD, _cfg())
    assert f.method == "fast-path"
    assert f.raw_equal
    assert f.normalized_equal
    assert f.relation == "equal"
    assert f.edit_similarity == 1
    assert f.jaccard == 1
    assert f.snippets == []


def test_whitespace_only_change_is_equal() -> None:
    """Whitespace-only change: relation equal, exact flag false, similarities 1."""
    new = "  The quick\n brown\tfox jumps over the lazy dog near the river bank today."
    f = compare_text(OLD, new, _cfg())
    assert f.method == "fast-path"
    assert f.raw_equal is False
    assert f.normalized_equal is True
    assert f.relation == "equal"
    assert f.edit_similarity == 1
    assert f.jaccard == 1


def test_both_empty_is_equal() -> None:
    """Two empty texts are equal."""
    f = compare_text("", "  ", _cfg())
    assert f.relation == "equal"
    assert f.old_empty
    assert f.new_empty


def test_appended_text_is_old_within_new() -> None:
    """Old text plus appended text: old-within-new."""
    f = compare_text(OLD, OLD + " And then it slept.", _cfg())
    assert f.method == "full"
    assert f.relation == "old-within-new"
    assert f.old_found_in_new == 1
    assert f.new_found_in_old is not None
    assert f.new_found_in_old < 1
    assert 0 < (f.edit_similarity or 0) < 1
    assert f.length_delta == len(" And then it slept.")


def test_truncated_text_is_new_within_old() -> None:
    """Truncated text: new-within-old."""
    f = compare_text(OLD, OLD[:40], _cfg())
    assert f.relation == "new-within-old"
    assert f.new_found_in_old == 1
    assert f.length_delta < 0


def test_containment_threshold_respected() -> None:
    """A within label needs containment_pct of the shorter text."""
    new = OLD.replace("lazy", "sleepy") + " More words here."
    assert compare_text(OLD, new, _cfg()).relation == "overlapping"
    assert compare_text(OLD, new, _cfg(containment_pct=80)).relation == "old-within-new"


def test_partially_shared_is_overlapping() -> None:
    """Partially shared texts: overlapping."""
    f = compare_text(OLD, "A quick red fox ran under the bridge yesterday evening.", _cfg())
    assert f.relation == "overlapping"
    assert f.jaccard is not None
    assert 0 < f.jaccard < 1


def test_no_shared_content_is_disjoint() -> None:
    """No shared content: disjoint."""
    f = compare_text("alpha beta gamma", "xyz qrs tuv", _cfg())
    assert f.relation == "disjoint"
    assert f.jaccard == 0


def test_empty_old_is_old_within_new() -> None:
    """Empty old, non-empty new: old-within-new."""
    f = compare_text("", OLD, _cfg())
    assert f.relation == "old-within-new"
    assert f.old_empty
    assert not f.new_empty
    assert f.edit_similarity == 0
    assert f.jaccard == 0


def test_empty_new_is_new_within_old() -> None:
    """Non-empty old, empty new: new-within-old."""
    f = compare_text(OLD, "", _cfg())
    assert f.relation == "new-within-old"
    assert f.new_empty


def test_snippets_capped_at_three_and_snippet_chars() -> None:
    """Up to three snippets, each side at most snippet_chars."""
    old = " ".join(f"block{i} " + "x" * 50 for i in range(10))
    new = " ".join(f"other{i} " + "y" * 50 for i in range(10))
    f = compare_text(old, new, _cfg(snippet_chars=20))
    assert 1 <= len(f.snippets) <= 3
    for s in f.snippets:
        assert len(s["old"]) <= 20
        assert len(s["new"]) <= 20


def test_snippets_show_first_differences_in_order() -> None:
    """Snippets hold the first three differing regions, in order."""
    old = "one two three four five six seven eight"
    new = "one TWO three four FIVE six seven EIGHT"
    f = compare_text(old, new, _cfg())
    assert f.snippets == [
        {"old": "two", "new": "TWO"},
        {"old": "five", "new": "FIVE"},
        {"old": "eight", "new": "EIGHT"},
    ]


def test_long_snippet_region_is_cut() -> None:
    """A large differing region is cut to snippet_chars on each side."""
    f = compare_text("a" * 500, "b" * 500, _cfg(snippet_chars=30))
    assert f.snippets == [{"old": "a" * 30, "new": "b" * 30}]


def test_region_longer_than_window_is_not_mispaired() -> None:
    """A deletion longer than the alignment window leaves the new side empty."""
    f = compare_text("Z" * 2000 + " TAIL", "TAIL", _cfg())
    assert f.snippets == [{"old": "Z" * 300, "new": ""}]


def test_over_threshold_raises_not_implemented() -> None:
    """Texts above full_compare_max_chars need the buffered method (not yet built)."""
    with pytest.raises(NotImplementedError):
        compare_text("a" * 101, "b" * 50, _cfg(full_compare_max_chars=100))


def test_over_threshold_equal_still_fast_path() -> None:
    """Normalized-equal long texts take the fast path regardless of size."""
    f = compare_text("a" * 101, "a" * 101, _cfg(full_compare_max_chars=100))
    assert f.method == "fast-path"


def test_counts_and_timing_recorded() -> None:
    """Char counts, buffered-only counters and seconds are recorded."""
    f = compare_text("abc", "abcd", _cfg())
    assert (f.old_chars, f.new_chars, f.length_delta) == (3, 4, 1)
    assert f.lines_total == f.lines_cut == f.oversized_runs == 0
    assert f.seconds >= 0
