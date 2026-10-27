from pathlib import Path

import yaml

from extractor_tester import models
from extractor_tester.models import (
    Example,
    FamilyAggregate,
    MetricResult,
    Report,
    RunStats,
)

MANIFESTS = Path(__file__).parents[1] / ".ydk/components/entity/model"


def test_fields_match_manifests() -> None:
    """Model field sets equal manifest field sets."""
    for name in ["Example", "MetricResult", "FamilyAggregate", "RunStats", "Report"]:
        manifest = yaml.safe_load((MANIFESTS / f"{name}.yaml").read_text())
        assert set(getattr(models, name).model_fields) == set(manifest["fields"]), name


def _mr(key: str = "k") -> MetricResult:
    """Build a sample MetricResult."""
    ex = Example(item_id="i", old_value={"a": 1}, new_value=None, note="n")
    return MetricResult(key=key, count=1, denominator=2, rate=0.5, examples=[ex])


def test_report_json_round_trip() -> None:
    """Report survives a JSON round trip."""
    fam = FamilyAggregate(
        family="text",
        pairs=2,
        only_in_old=_mr(),
        only_in_new=_mr(),
        identical=_mr(),
        mime_changed=_mr(),
        family_changed=_mr(),
        fields=[_mr()],
        edit_similarity={"median": 0.9},
        jaccard={"median": 0.8},
        relations=[_mr()],
        methods={"fast": 1},
        lines_cut_pct=0.0,
        cut_lines_warning=False,
        tiers=[_mr()],
        hints=[_mr()],
        decode_errors=_mr(),
        file_errors=[_mr()],
    )
    rep = Report(
        schema_version=1,
        tool_version="0.1.0",
        started_at="2026-01-01T00:00:00Z",
        inputs={"old": "a", "new": "b", "mode": "folder"},
        config={"x": 1},
        totals={"pairs": 2},
        items_only_old=_mr(),
        items_only_new=_mr(),
        item_fields=[_mr()],
        families=[fam],
        errors=[_mr()],
        warnings=["w"],
        run_stats=RunStats(
            wall_seconds=1.0,
            workers=2,
            stage_seconds={"load": 0.1},
            text_seconds_by_family={"text": 0.2},
            slowest_items=[{"item_id": "i", "seconds": 0.3}],
            workers_replaced=0,
        ),
    )
    assert Report.model_validate_json(rep.model_dump_json()) == rep
