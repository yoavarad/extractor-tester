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


_PROGRESS_SECONDS = 0.1


def _progress(done: int, total: int, t0: float) -> None:
    """Rewrite the single progress line on stderr."""
    elapsed = time.perf_counter() - t0
    sys.stderr.write(f"\rcompared {done}/{total} items, {elapsed:.1f}s elapsed")
    sys.stderr.flush()


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
    total = len(discovery.items)
    last_shown = 0.0
    _progress(0, total, t0)
    with pairs_writer(out) as pw, items_writer(out) as iw:
        for done, (result, facts, _pid) in enumerate(
            compare_all(
                discovery.items,
                config,
                workers,
                worker_stats,
                out / "profile" if profile else None,
            ),
            start=1,
        ):
            t_agg = time.perf_counter()
            for pf in facts:
                pw.write(pf)
                agg.add_pair(pf)
            iw.write(result)
            agg.add_item(result)
            agg.add_stage("aggregate", time.perf_counter() - t_agg)
            if done == total or time.monotonic() - last_shown >= _PROGRESS_SECONDS:
                _progress(done, total, t0)
                last_shown = time.monotonic()
    if total:
        sys.stderr.write("\n")
    t_fin = time.perf_counter()
    stage_seconds, text_by_family, slowest = agg.timings()
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
            stage_seconds=stage_seconds,
            text_seconds_by_family=text_by_family,
            slowest_items=slowest,
            workers_replaced=worker_stats.replaced,
            profile_dir="profile" if profile else None,
        ),
    )
    report.warnings.extend(f"{w.code.value}: {w.message}" for w in discovery.warnings)
    if discovery.ignored_count:
        report.warnings.append(
            f"{discovery.ignored_count} folder entries ignored (not top-level *.json files)"
        )
    agg.add_stage("aggregate", time.perf_counter() - t_fin)
    # Render time is only known after rendering: render once to measure it, then write the
    # final report.json (with the render stage) and render again so the HTML shows it too.
    t_render = time.perf_counter()
    render(write_report(out, report), out / "report.html")
    agg.add_stage("render", time.perf_counter() - t_render)
    stage_seconds = agg.timings()[0]
    report.run_stats.stage_seconds = stage_seconds
    report.run_stats.wall_seconds = time.perf_counter() - t0
    render(write_report(out, report), out / "report.html")


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
    except KeyboardInterrupt:
        print("\nInterrupted", file=sys.stderr)
        return 130
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
