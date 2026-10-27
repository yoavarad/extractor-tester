"""Top-level compare run: config, discovery, per-item compare, writers, report.json."""

from __future__ import annotations

import sys
import time
import traceback
from datetime import UTC, datetime
from importlib.metadata import PackageNotFoundError, version
from typing import TYPE_CHECKING

from extractor_tester.aggregate import Aggregator
from extractor_tester.compare.item import compare_item
from extractor_tester.config import ConfigError, load_config
from extractor_tester.loader import LoaderError, discover
from extractor_tester.models import ErrorCode, ItemResult, PairFacts, RunStats
from extractor_tester.output import (
    OutputError,
    items_writer,
    pairs_writer,
    prepare_out,
    write_report,
)
from extractor_tester.render import render

if TYPE_CHECKING:
    from pathlib import Path

    from extractor_tester.config import CompareConfig
    from extractor_tester.loader import DiscoveredItem


def _tool_version() -> str:
    try:
        return version("extractor-tester")
    except PackageNotFoundError:
        return "0"


def _one_sided(item_id: str, status: str) -> ItemResult:
    return ItemResult.model_validate(
        {
            "item_id": item_id,
            "status": status,
            "metadata_diffs": [],
            "files_identical": 0,
            "files_changed": 0,
            "files_only_old": 0,
            "files_only_new": 0,
            "errors": [],
            "seconds": 0.0,
            "stage_seconds": {},
        }
    )


def compare_one(item: DiscoveredItem, config: CompareConfig) -> tuple[ItemResult, list[PairFacts]]:
    """Compare one discovered item; the single per-item call a parallel runner can swap out."""
    if item.new_path is None:
        return _one_sided(item.item_id, "only-in-old"), []
    if item.old_path is None:
        return _one_sided(item.item_id, "only-in-new"), []
    return compare_item(item.old_path, item.new_path, config)


def _execute(old: Path, new: Path, config_path: Path, out: Path, overwrite: bool) -> None:
    t0 = time.perf_counter()
    started_at = datetime.now(UTC)
    config = load_config(config_path)
    discovery = discover(old, new)
    prepare_out(out, overwrite)
    agg = Aggregator(config)
    with pairs_writer(out) as pw, items_writer(out) as iw:
        for item in discovery.items:
            result, facts = compare_one(item, config)
            for pf in facts:
                pw.write(pf)
                agg.add_pair(pf)
            iw.write(result)
            agg.add_item(result)
    report = agg.finish(
        tool_version=_tool_version(),
        started_at=started_at,
        inputs={
            "old": str(old),
            "new": str(new),
            "mode": "file" if old.is_file() else "folder",
            "config": str(config_path),
        },
        run_stats=RunStats(
            wall_seconds=time.perf_counter() - t0,
            workers=1,
            stage_seconds={},
            text_seconds_by_family={},
            slowest_items=[],
            workers_replaced=0,
        ),
    )
    report.warnings.extend(f"{w.code.value}: {w.message}" for w in discovery.warnings)
    if discovery.ignored_count:
        report.warnings.append(
            f"{discovery.ignored_count} folder entries ignored (not top-level *.json files)"
        )
    report_json = write_report(out, report)
    render(report_json, out / "report.html")


def run(
    old: Path,
    new: Path,
    config_path: Path,
    out: Path,
    overwrite: bool = False,
    workers: int | None = None,
    profile: bool = False,
) -> int:
    """Execute a compare run sequentially and return the exit code.

    workers and profile are accepted for the CLI contract; this slice runs in-process.
    """
    del workers, profile
    try:
        _execute(old, new, config_path, out, overwrite)
    except ConfigError as e:
        print(f"{ErrorCode.INVALID_CONFIG.value}: {e}", file=sys.stderr)
        return 1
    except (LoaderError, OutputError) as e:
        print(f"{e.code.value}: {e.message}", file=sys.stderr)
        return 1
    except Exception as e:
        traceback.print_exc(file=sys.stderr)
        print(
            f"{ErrorCode.INTERNAL_FAILURE.value}: Internal failure: {type(e).__name__}: {e}",
            file=sys.stderr,
        )
        return 1
    return 0
