"""Top-level compare run: config, discovery, per-item compare, writers, report.json."""

from __future__ import annotations

import multiprocessing as mp
import os
import queue
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
    from collections.abc import Iterator, Sequence
    from multiprocessing.process import BaseProcess
    from multiprocessing.queues import Queue
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


WINDOW_PER_WORKER = 32
_POLL_SECONDS = 0.5

_Result = tuple[ItemResult, list[PairFacts], int]


def _worker_main(slot: int, config: CompareConfig, inbox: Queue, outbox: Queue) -> None:
    """Worker loop: take one (index, item) at a time until None, return results with the pid."""
    pid = os.getpid()
    while (msg := inbox.get()) is not None:
        index, item = msg
        result, facts = compare_one(item, config)
        outbox.put((slot, index, result, facts, pid))


def compare_all(
    items: Sequence[DiscoveredItem], config: CompareConfig, workers: int
) -> Iterator[_Result]:
    """Compare items in worker processes; yield (result, facts, worker pid) in input order.

    Each worker holds at most one item. Dispatch runs at most workers x WINDOW_PER_WORKER
    items ahead of the oldest unfinished item, which bounds the buffered results.
    """
    if workers < 1:
        raise ValueError(f"workers must be >= 1, got {workers}")
    ctx = mp.get_context("spawn")
    outbox: Queue = ctx.Queue()
    inboxes: list[Queue] = [ctx.Queue() for _ in range(workers)]
    procs: list[BaseProcess] = [
        ctx.Process(target=_worker_main, args=(i, config, inboxes[i], outbox), daemon=True)
        for i in range(workers)
    ]
    held: dict[int, tuple[int, float]] = {}  # worker slot -> (item index, since)
    buffer: dict[int, _Result] = {}
    window = workers * WINDOW_PER_WORKER
    next_dispatch = next_write = 0
    try:
        for p in procs:
            p.start()
        while next_write < len(items):
            for slot in range(workers):
                if slot in held or next_dispatch >= min(len(items), next_write + window):
                    continue
                inboxes[slot].put((next_dispatch, items[next_dispatch]))
                held[slot] = (next_dispatch, time.monotonic())
                next_dispatch += 1
            try:
                slot, index, result, facts, pid = outbox.get(timeout=_POLL_SECONDS)
            except queue.Empty:
                dead = [s for s in held if not procs[s].is_alive()]
                if dead:
                    lost = items[held[dead[0]][0]].item_id
                    raise RuntimeError(f"worker process exited while holding item {lost}") from None
                continue
            del held[slot]
            buffer[index] = (result, facts, pid)
            while next_write in buffer:
                yield buffer.pop(next_write)
                next_write += 1
    finally:
        for inbox in inboxes:
            inbox.put(None)
        deadline = time.monotonic() + 5
        for p in procs:
            if p.pid is not None:
                p.join(timeout=max(0.0, deadline - time.monotonic()))
            if p.is_alive():
                p.terminate()
                p.join()


def _execute(
    old: Path, new: Path, config_path: Path, out: Path, overwrite: bool, workers: int
) -> None:
    t0 = time.perf_counter()
    started_at = datetime.now(UTC)
    config = load_config(config_path)
    discovery = discover(old, new)
    prepare_out(out, overwrite)
    agg = Aggregator(config)
    with pairs_writer(out) as pw, items_writer(out) as iw:
        for result, facts, _pid in compare_all(discovery.items, config, workers):
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
    """Execute a compare run in worker processes and return the exit code.

    workers defaults to the CPU count; profile is accepted for the CLI contract only.
    """
    del profile
    try:
        _execute(old, new, config_path, out, overwrite, workers or os.cpu_count() or 1)
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
