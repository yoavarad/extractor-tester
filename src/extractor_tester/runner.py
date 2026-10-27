"""Top-level compare run: config, discovery, per-item compare, writers, report.json."""

from __future__ import annotations

import multiprocessing as mp
import os
import queue
import sys
import time
import traceback
from dataclasses import dataclass
from datetime import UTC, datetime
from importlib.metadata import PackageNotFoundError, version
from typing import TYPE_CHECKING

from extractor_tester.aggregate import Aggregator
from extractor_tester.compare.item import compare_item
from extractor_tester.config import ConfigError, load_config
from extractor_tester.loader import LoaderError, discover
from extractor_tester.models import ErrorCode, ErrorRecord, ItemResult, PairFacts, RunStats
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
# Test hook: a worker given the item with this id exits abruptly, simulating a crash.
CRASH_ITEM_ENV = "EXTRACTOR_TESTER_TEST_CRASH_ITEM"

_Result = tuple[ItemResult, list[PairFacts], int]


@dataclass
class WorkerStats:
    """Counters compare_all fills in while it runs."""

    replaced: int = 0


def _worker_main(slot: int, config: CompareConfig, inbox: Queue, outbox: Queue) -> None:
    """Worker loop: announce readiness (index -1), then take one (index, item) at a time until
    None and return results with the pid."""
    pid = os.getpid()
    crash_item = os.environ.get(CRASH_ITEM_ENV)
    outbox.put((slot, -1, None, None, pid))
    while (msg := inbox.get()) is not None:
        index, item = msg
        if crash_item is not None and item.item_id == crash_item:
            os._exit(1)
        result, facts = compare_one(item, config)
        outbox.put((slot, index, result, facts, pid))


def _error_result(item_id: str, code: ErrorCode, message: str, seconds: float) -> ItemResult:
    err = ErrorRecord(code=code, scope="item", severity="error", item_id=item_id, message=message)
    return _one_sided(item_id, "error").model_copy(update={"errors": [err], "seconds": seconds})


def compare_all(
    items: Sequence[DiscoveredItem],
    config: CompareConfig,
    workers: int,
    stats: WorkerStats | None = None,
) -> Iterator[_Result]:
    """Compare items in worker processes; yield (result, facts, worker pid) in input order.

    Each worker holds at most one item. Dispatch runs at most workers x WINDOW_PER_WORKER
    items ahead of the oldest unfinished item, which bounds the buffered results. A worker
    that dies or holds an item past item_timeout_seconds is replaced; only its item gets
    WORKER_CRASHED or ITEM_TIMEOUT. Replacements are counted in stats.
    """
    if workers < 1:
        raise ValueError(f"workers must be >= 1, got {workers}")
    stats = stats if stats is not None else WorkerStats()
    ctx = mp.get_context("spawn")
    outbox: Queue = ctx.Queue()
    inboxes: list[Queue] = []
    procs: list[BaseProcess] = []

    def start(slot: int) -> None:
        inbox: Queue = ctx.Queue()
        proc = ctx.Process(target=_worker_main, args=(slot, config, inbox, outbox), daemon=True)
        proc.start()
        if slot < len(procs):
            inboxes[slot], procs[slot] = inbox, proc
        else:
            inboxes.append(inbox)
            procs.append(proc)

    ready: set[int] = set()  # slots whose current worker has started up
    held: dict[int, tuple[int, float]] = {}  # worker slot -> (item index, since)
    buffer: dict[int, _Result] = {}
    window = workers * WINDOW_PER_WORKER
    timeout = config.item_timeout_seconds
    next_dispatch = next_write = 0
    try:
        for slot in range(workers):
            start(slot)
        while next_write < len(items):
            limit = min(len(items), next_write + window)
            for slot in range(workers):
                if slot not in ready or slot in held or next_dispatch >= limit:
                    continue
                inboxes[slot].put((next_dispatch, items[next_dispatch]))
                held[slot] = (next_dispatch, time.monotonic())
                next_dispatch += 1
            # Drain every queued message before the timeout pass, so a finished item is
            # never timed out just because its result waited behind others.
            wait: float | None = _POLL_SECONDS
            while True:
                try:
                    msg = outbox.get(timeout=wait) if wait else outbox.get_nowait()
                except queue.Empty:
                    break
                wait = None
                slot, index, result, facts, pid = msg
                if index == -1:
                    if procs[slot].pid == pid:
                        ready.add(slot)
                # A result from a worker already given up on (timed out) is stale: drop it.
                elif held.get(slot, (None,))[0] == index:
                    del held[slot]
                    buffer[index] = (result, facts, pid)
            now = time.monotonic()
            for slot in range(workers):
                if slot not in held and not procs[slot].is_alive():
                    procs[slot].join()
                    if slot not in ready:  # startup failure would repeat on every respawn
                        raise RuntimeError("worker process exited during startup")
                    ready.discard(slot)
                    stats.replaced += 1
                    start(slot)
            for slot, (index, since) in list(held.items()):
                proc = procs[slot]
                item_id = items[index].item_id
                if not proc.is_alive():
                    proc.join()
                    code = ErrorCode.WORKER_CRASHED
                    message = f"item {item_id}: worker process crashed"
                elif now - since > timeout:
                    proc.terminate()
                    proc.join()
                    code = ErrorCode.ITEM_TIMEOUT
                    message = f"item {item_id}: not finished after {timeout} s"
                else:
                    continue
                result = _error_result(item_id, code, message, now - since)
                buffer[index] = (result, [], proc.pid or 0)
                del held[slot]
                ready.discard(slot)
                stats.replaced += 1
                start(slot)
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
    worker_stats = WorkerStats()
    with pairs_writer(out) as pw, items_writer(out) as iw:
        for result, facts, _pid in compare_all(discovery.items, config, workers, worker_stats):
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
