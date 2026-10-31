"""Stage timers: slowest-items list and aggregator folding."""

from extractor_tester.aggregate import Aggregator
from extractor_tester.config import CompareConfig
from extractor_tester.models import ItemResult
from extractor_tester.profiling import STAGES, Slowest, add, take


def test_slowest_keeps_top_n_descending() -> None:
    """Only the n slowest survive, slowest first."""
    s = Slowest(3)
    for i, sec in enumerate([1.0, 5.0, 3.0, 2.0, 4.0]):
        s.add(f"i{i}", sec, "compared")
    assert [x["seconds"] for x in s.items()] == [5.0, 4.0, 3.0]


def test_decode_accumulates_and_resets() -> None:
    """take_decode returns the sum then clears it."""
    take("decode")
    add("decode", 0.5)
    add("decode", 0.25)
    assert take("decode") == 0.75
    assert take("decode") == 0.0


def test_aggregator_sums_item_stages_with_every_stage_present() -> None:
    """Item stage seconds add up; unseen stages stay at zero."""
    agg = Aggregator(
        CompareConfig(name_field="n", mime_primary_field="a", mime_secondary_field="b")
    )
    for i in range(2):
        agg.add_item(
            ItemResult(
                item_id=f"i{i}",
                status="compared",
                metadata_diffs=[],
                files_identical=0,
                files_changed=0,
                files_only_old=0,
                files_only_new=0,
                errors=[],
                seconds=1.0 + i,
                stage_seconds={"load": 0.5},
            )
        )
    agg.add_stage("render", 0.1)
    stages, _, slowest = agg.timings()
    assert set(stages) == set(STAGES)
    assert stages["load"] == 1.0
    assert stages["render"] == 0.1
    assert [x["item_id"] for x in slowest] == ["i1", "i0"]
