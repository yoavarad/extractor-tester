"""Top-level compare run: config, discovery, per-item compare, writers, report.json."""

from __future__ import annotations

import os
import shutil
import sys
import time
import traceback
from datetime import UTC, datetime
from importlib.metadata import PackageNotFoundError, version
from typing import TYPE_CHECKING

from extractor_tester.aggregate import Aggregator
from extractor_tester.config import ConfigError, load_config
from extractor_tester.loader import LoaderError, discover
from extractor_tester.models import ErrorCode, RunStats
from extractor_tester.output import (
    OutputError,
    items_writer,
    pairs_writer,
    prepare_out,
    write_report,
)
from extractor_tester.pool import WorkerStats, compare_all
from extractor_tester.render import render

if TYPE_CHECKING:
    from pathlib import Path


def _tool_version() -> str:
    try:
        return version("extractor-tester")
    except PackageNotFoundError:
        return "0"


def _execute(
    old: Path,
    new: Path,
    config_path: Path,
    out: Path,
    overwrite: bool,
    workers: int,
    profile: bool = False,
) -> None:
    t0 = time.perf_counter()
    started_at = datetime.now(UTC)
    config = load_config(config_path)
    discovery = discover(old, new)
    prepare_out(out, overwrite)
    if profile:
        shutil.rmtree(out / "profile", ignore_errors=True)
    agg = Aggregator(config)
    worker_stats = WorkerStats()
    with pairs_writer(out) as pw, items_writer(out) as iw:
        for result, facts, _pid in compare_all(
            discovery.items,
            config,
            workers,
            worker_stats,
            out / "profile" if profile else None,
        ):
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
            workers=workers,
            stage_seconds={},
            text_seconds_by_family={},
            slowest_items=[],
            workers_replaced=worker_stats.replaced,
            profile_dir="profile" if profile else None,
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
    """Execute a compare run in worker processes and return the exit code.

    workers defaults to the CPU count; profile writes out/profile/worker-<n>.prof per worker.
    """
    try:
        _execute(old, new, config_path, out, overwrite, workers or os.cpu_count() or 1, profile)
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
