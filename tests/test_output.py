"""Tests for the report writer."""

from datetime import UTC, datetime
from pathlib import Path

import pytest

from extractor_tester import output
from extractor_tester.models import (
    ErrorCode,
    ItemResult,
    MetricResult,
    PairFacts,
    Report,
    RunStats,
)
from extractor_tester.output import OutputError, prepare_out


def _pair(item_id: str = "i1") -> PairFacts:
    """Build a minimal PairFacts."""
    return PairFacts(
        item_id=item_id,
        kind="paired",
        family="text",
        tie_break="none",
        field_diffs=[],
        fields_same=[],
        hints=[],
        identical=True,
        errors=[],
    )


def _mr() -> MetricResult:
    """Build an empty MetricResult."""
    return MetricResult(key="k", count=0, denominator=0, rate=0.0, examples=[])


def _report() -> Report:
    """Build a minimal Report."""
    return Report(
        schema_version=1,
        tool_version="0",
        started_at=datetime(2026, 1, 1, tzinfo=UTC),
        inputs={},
        config={},
        totals={},
        items_only_old=_mr(),
        items_only_new=_mr(),
        item_fields=[],
        families=[],
        errors=[],
        warnings=[],
        run_stats=RunStats(
            wall_seconds=0.0,
            workers=1,
            stage_seconds={},
            text_seconds_by_family={},
            slowest_items=[],
            workers_replaced=0,
        ),
    )


def test_prepare_creates_missing(tmp_path: Path) -> None:
    """A missing folder is created."""
    out = tmp_path / "a" / "b"
    assert prepare_out(out, False) == out
    assert out.is_dir()


def test_prepare_empty_ok(tmp_path: Path) -> None:
    """An empty existing folder is accepted."""
    prepare_out(tmp_path, False)


def test_prepare_non_empty_refused(tmp_path: Path) -> None:
    """Non-empty folder without overwrite raises OUTPUT_EXISTS."""
    (tmp_path / "x").write_text("1")
    with pytest.raises(OutputError) as ei:
        prepare_out(tmp_path, False)
    assert ei.value.code == ErrorCode.OUTPUT_EXISTS


def test_prepare_non_empty_overwrite(tmp_path: Path) -> None:
    """Non-empty folder with overwrite proceeds and keeps other files."""
    (tmp_path / "x").write_text("1")
    prepare_out(tmp_path, True)
    assert (tmp_path / "x").exists()


def test_prepare_unwritable(tmp_path: Path) -> None:
    """A path that cannot be a folder raises OUTPUT_WRITE_FAILED."""
    f = tmp_path / "file"
    f.write_text("1")
    for bad in (f, f / "sub"):
        with pytest.raises(OutputError) as ei:
            prepare_out(bad, True)
        assert ei.value.code == ErrorCode.OUTPUT_WRITE_FAILED


def test_writer_open_failure(tmp_path: Path) -> None:
    """Opening a writer in a missing folder raises OUTPUT_WRITE_FAILED."""
    with pytest.raises(OutputError) as ei:
        output.pairs_writer(tmp_path / "missing")
    assert ei.value.code == ErrorCode.OUTPUT_WRITE_FAILED


def test_pairs_jsonl_one_per_line(tmp_path: Path) -> None:
    """Each line parses back as PairFacts, utf-8."""
    with output.pairs_writer(tmp_path) as w:
        w.write(_pair("é1"))
        w.write(_pair("i2"))
    lines = (tmp_path / "pairs.jsonl").read_text(encoding="utf-8").splitlines()
    assert [PairFacts.model_validate_json(x).item_id for x in lines] == ["é1", "i2"]


def test_items_jsonl(tmp_path: Path) -> None:
    """Items file holds ItemResult lines."""
    r = ItemResult(
        item_id="i",
        status="compared",
        metadata_diffs=[],
        files_identical=1,
        files_changed=0,
        files_only_old=0,
        files_only_new=0,
        errors=[],
        seconds=0.1,
        stage_seconds={},
    )
    with output.items_writer(tmp_path) as w:
        w.write(r)
    text = (tmp_path / "items.jsonl").read_text(encoding="utf-8")
    assert ItemResult.model_validate_json(text) == r


def test_evil_item_id_stays_inside(tmp_path: Path) -> None:
    """An item id with path parts creates no file outside the out folder."""
    out = tmp_path / "out"
    prepare_out(out, False)
    with output.pairs_writer(out) as w:
        w.write(_pair("../../evil"))
    output.write_report(out, _report())
    assert {p.name for p in tmp_path.iterdir()} == {"out"}
    assert {p.name for p in out.iterdir()} == {"pairs.jsonl", "report.json"}


def test_write_report_atomic(tmp_path: Path) -> None:
    """Report round-trips and leaves no temp files."""
    rep = _report()
    path = output.write_report(tmp_path, rep)
    assert Report.model_validate_json(path.read_text(encoding="utf-8")) == rep
    assert [p.name for p in tmp_path.iterdir()] == ["report.json"]


def test_write_report_failure_keeps_old(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    """A failed rename raises OUTPUT_WRITE_FAILED, keeps old report, removes temp."""
    output.write_report(tmp_path, _report())
    before = (tmp_path / "report.json").read_text(encoding="utf-8")

    def boom(*_a: object) -> None:
        """Fail like a full disk."""
        raise PermissionError(13, "denied")

    monkeypatch.setattr(output.os, "replace", boom)
    with pytest.raises(OutputError) as ei:
        output.write_report(tmp_path, _report())
    assert ei.value.code == ErrorCode.OUTPUT_WRITE_FAILED
    assert (tmp_path / "report.json").read_text(encoding="utf-8") == before
    assert [p.name for p in tmp_path.iterdir()] == ["report.json"]
