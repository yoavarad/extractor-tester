"""Tests for the streaming Aggregator."""

from typing import Any

from extractor_tester.aggregate import Aggregator
from extractor_tester.config import CompareConfig
from extractor_tester.models import ErrorCode, ErrorRecord, FieldDiff, ItemResult, PairFacts


def _cfg(**kw: Any) -> CompareConfig:  # noqa: ANN401
    return CompareConfig(name_field="name", mime_primary_field="a", mime_secondary_field="b", **kw)


def _pair(item: str, changed: bool = False, **kw: Any) -> PairFacts:  # noqa: ANN401
    diff = FieldDiff(
        path="author",
        group="generic",
        status="changed" if changed else "same",
        old_value="a",
        new_value="b" if changed else "a",
    )
    base: dict[str, Any] = {
        "item_id": item,
        "kind": "paired",
        "path_old": "r",
        "path_new": "r",
        "family": "pdf",
        "tier": "root",
        "tie_break": "none",
        "mime_equal": True,
        "family_equal": True,
        "field_diffs": [diff],
        "fields_same": [],
        "hints": [],
        "identical": not changed,
        "errors": [],
    }
    base.update(kw)
    return PairFacts(**base)


def _item(item: str, errors: list[ErrorRecord] | None = None) -> ItemResult:
    return ItemResult(
        item_id=item,
        status="compared",
        metadata_diffs=[],
        files_identical=0,
        files_changed=0,
        files_only_old=0,
        files_only_new=0,
        errors=errors or [],
        seconds=0.0,
        stage_seconds={},
    )


def _run(cfg: CompareConfig) -> str:
    agg = Aggregator(cfg)
    for i in range(4):
        agg.add_pair(_pair(f"i{i}", changed=i == 0))
        agg.add_item(_item(f"i{i}"))
    return agg.finish().model_dump_json()


def test_deterministic() -> None:
    """Same facts in the same order give identical JSON."""
    assert _run(_cfg()) == _run(_cfg())


def test_field_rate_one_of_four() -> None:
    """One changed field in four pairs gives rate 0.25 and one example."""
    agg = Aggregator(_cfg())
    for i in range(4):
        agg.add_pair(_pair(f"i{i}", changed=i == 0))
    fam = agg.finish().families[0]
    m = next(f for f in fam.fields if f.key == "author:changed")
    assert (m.count, m.denominator, m.rate, len(m.examples)) == (1, 4, 0.25, 1)
    assert fam.pairs == 4
    assert fam.identical.count == 3
    assert fam.tiers[0].key == "root"


def test_examples_capped_and_cut() -> None:
    """Examples stop at the cap and values are cut to snippet_chars."""
    agg = Aggregator(_cfg(examples_per_result=2, snippet_chars=20))
    for i in range(5):
        agg.add_pair(_pair(f"i{i}", changed=True, mime_equal=False, mime_old="x" * 50))
    fam = agg.finish().families[0]
    assert fam.mime_changed.count == 5
    assert [e.item_id for e in fam.mime_changed.examples] == ["i0", "i1"]
    assert fam.mime_changed.examples[0].old_value == "x" * 20


def test_errors_by_code() -> None:
    """Errors are counted by code with examples; warnings are summarised."""
    err = ErrorRecord(code=ErrorCode.MISSING_FIELD, scope="file", severity="error", message="m")
    warn = ErrorRecord(code=ErrorCode.ORPHAN_FILE, scope="file", severity="warning", message="w")
    agg = Aggregator(_cfg())
    agg.add_pair(_pair("i0", errors=[err, err]))
    agg.add_item(_item("i1", [err, warn]))
    rep = agg.finish()
    assert [(e.key, e.count, len(e.examples)) for e in rep.errors] == [("MISSING_FIELD", 3, 3)]
    assert rep.warnings == ["ORPHAN_FILE: 1 occurrence(s)"]
    assert rep.families[0].file_errors[0].count == 2


def test_one_sided() -> None:
    """One-sided pairs count separately from paired ones."""
    agg = Aggregator(_cfg())
    agg.add_pair(_pair("i0", kind="only-in-old", path_new=None, identical=False))
    fam = agg.finish().families[0]
    assert fam.only_in_old.count == 1
    assert fam.pairs == 0


def test_decode_errors_summed() -> None:
    """Decode errors on both sides are summed per family."""
    agg = Aggregator(_cfg())
    agg.add_pair(_pair("i0", decode_errors_old=2, decode_errors_new=1))
    agg.add_pair(_pair("i1"))
    de = agg.finish().families[0].decode_errors
    assert (de.count, len(de.examples)) == (3, 1)


def test_item_metadata_only_for_compared_and_warning_skipped() -> None:
    """Item metadata diffs count only for compared items; file warnings are not errors."""
    d = FieldDiff(path="t", group="item", status="changed", old_value=1, new_value=2)
    agg = Aggregator(_cfg())
    agg.add_item(_item("i0").model_copy(update={"metadata_diffs": [d]}))
    agg.add_item(_item("i1").model_copy(update={"status": "only-in-old", "metadata_diffs": [d]}))
    warn = ErrorRecord(code=ErrorCode.ORPHAN_FILE, scope="file", severity="warning", message="w")
    agg.add_pair(_pair("i0", errors=[warn]))
    rep = agg.finish()
    assert rep.item_fields[0].count == 1
    assert rep.item_fields[0].rate == 1.0
    assert rep.families[0].file_errors == []


def _tpair(item: str, sim: float | None, **kw: Any) -> PairFacts:  # noqa: ANN401
    text = {
        "raw_equal": False,
        "normalized_equal": False,
        "method": "full",
        "edit_similarity": sim,
        "jaccard": sim,
        "relation": "overlapping",
        "old_chars": 1,
        "new_chars": 1,
        "length_delta": 0,
        "old_empty": False,
        "new_empty": False,
        "lines_total": 10,
        "lines_cut": 0,
        "lines_cut_pct": 0.0,
        "oversized_runs": 0,
        "snippets": [],
        "seconds": 0.0,
    }
    text.update(kw)
    return _pair(item, text=text, path_old=f"p/{item}", path_new=f"p/{item}")


def test_text_percentiles_match_statistics() -> None:
    """Median/p10/min match statistics results."""
    import statistics

    sims = [0.1, 0.5, 0.9, 0.3, 0.7, 1.0, 0.2, 0.8, 0.6, 0.4]
    agg = Aggregator(_cfg())
    for i, s in enumerate(sims):
        agg.add_pair(_tpair(f"i{i}", s))
    fam = agg.finish().families[0]
    assert fam.edit_similarity == {
        "median": statistics.median(sims),
        "p10": statistics.quantiles(sims, n=10)[0],
        "min": 0.1,
    }
    assert fam.methods == {"full": 10}
    assert fam.relations[0].key == "overlapping"
    assert fam.relations[0].count == 10


def test_lowest_similarity_examples_tie_order() -> None:
    """Lowest-similarity examples, ties by item id."""
    agg = Aggregator(_cfg(examples_per_result=3))
    for item, s in [("b", 0.5), ("a", 0.5), ("c", 0.9), ("d", 0.5), ("e", 0.1)]:
        agg.add_pair(_tpair(item, s))
    ex = agg.finish().families[0].relations[0].examples
    assert [e.item_id for e in ex] == ["e", "a", "b"]
    assert [e.similarity for e in ex] == [0.1, 0.5, 0.5]
    assert all(e.old_value is None for e in ex)


def test_cut_line_warning_and_hints() -> None:
    """Cut-line share above threshold warns; hints counted."""
    agg = Aggregator(_cfg(cut_lines_warn_pct=20))
    agg.add_pair(_tpair("a", 0.5, lines_cut=3))
    assert agg.finish().families[0].cut_lines_warning is True
    low = Aggregator(_cfg(cut_lines_warn_pct=20))
    low.add_pair(_tpair("a", 0.5, lines_cut=1))
    assert low.finish().families[0].cut_lines_warning is False
    h = Aggregator(_cfg())
    h.add_pair(_pair("a", hints=["new-more-text"]))
    assert h.finish().families[0].hints[0].key == "new-more-text"
