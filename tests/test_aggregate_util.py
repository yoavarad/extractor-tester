"""Tests for aggregator helpers."""

from extractor_tester.aggregate_util import Lowest, stats
from extractor_tester.models import Example


def test_stats_empty_and_single() -> None:
    """No data gives empty dict; one value is all three stats."""
    assert stats([]) == {}
    assert stats([0.4]) == {"median": 0.4, "p10": 0.4, "min": 0.4}


def test_lowest_keeps_cap_lowest() -> None:
    """Only the lowest-similarity examples are kept, in order."""
    low = Lowest(2)
    for item, sim in [("a", 0.9), ("b", 0.2), ("c", 0.5)]:
        low.add(sim, Example(item_id=item))
    assert [e.item_id for e in low.examples()] == ["b", "c"]
