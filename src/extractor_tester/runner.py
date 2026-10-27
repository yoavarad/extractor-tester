"""Top-level compare run: config, discovery, per-item compare, writers, report.json."""

from __future__ import annotations

import contextlib
import multiprocessing as mp
import os
import sys
import time
import traceback
from dataclasses import dataclass
from datetime import UTC, datetime
from importlib.metadata import PackageNotFoundError, version
from multiprocessing.connection import wait
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
    from multiprocessing.connection import _ConnectionBase as Connection  # Pipe end, any OS
    from multiprocessing.process import BaseProcess
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
# A worker that has not signalled ready this long after spawn is killed and replaced.
STARTUP_TIMEOUT_SECONDS = 120.0
# Consecutive startup failures on one slot before the run gives up (a broken environment).
_MAX_STARTUP_FAILURES = 3
# Test hooks: a worker given the item with this id exits abruptly (crash) or sends a
# corrupt payload and exits (killed mid-write); the worker that first creates the file at
# the hang path hangs during startup.
CRASH_ITEM_ENV = "EXTRACTOR_TESTER_TEST_CRASH_ITEM"
GARBAGE_ITEM_ENV = "EXTRACTOR_TESTER_TEST_GARBAGE_ITEM"
HANG_STARTUP_ENV = "EXTRACTOR_TESTER_TEST_HANG_STARTUP"

_Result = tuple[ItemResult, list[PairFacts], int]


@dataclass
class WorkerStats:
    """Counters compare_all fills in while it runs."""

    replaced: int = 0


def _startup_hook() -> None:
    path = os.environ.get(HANG_STARTUP_ENV)
    if path is None:
        return
    try:
        os.close(os.open(path, os.O_CREAT | os.O_EXCL))
    except FileExistsError:
        return
    while True:
        time.sleep(60)


def _worker_main(config: CompareConfig, conn: Connection) -> None:
    """Worker loop on its own pipe: send ready (index -1), then take one (index, item) at a
    time until None and send back (index, result, facts, pid)."""
    _startup_hook()
    pid = os.getpid()
    crash_item = os.environ.get(CRASH_ITEM_ENV)
    garbage_item = os.environ.get(GARBAGE_ITEM_ENV)
    conn.send((-1, None, None, pid))
    while (msg := conn.recv()) is not None:
        index, item = msg
        if item.item_id == crash_item:
            os._exit(1)
        if item.item_id == garbage_item:
            conn.send_bytes(b"\x80\x05not a pickle")
            os._exit(1)
        result, facts = compare_one(item, config)
        conn.send((index, result, facts, pid))


def _error_result(item_id: str, code: ErrorCode, message: str, seconds: float) -> ItemResult:
    err = ErrorRecord(code=code, scope="item", severity="error", item_id=item_id, message=message)
    return _one_sided(item_id, "error").model_copy(update={"errors": [err], "seconds": seconds})


@dataclass
class _Slot:
    proc: BaseProcess
    conn: Connection
    started: float
    ready: bool = False
    startup_failures: int = 0


def compare_all(
    items: Sequence[DiscoveredItem],
    config: CompareConfig,
    workers: int,
    stats: WorkerStats | None = None,
) -> Iterator[_Result]:
    """Compare items in worker processes; yield (result, facts, worker pid) in input order.

    Each worker has its own pipe and holds at most one item. Dispatch runs at most
    workers x WINDOW_PER_WORKER items ahead of the oldest unfinished item, which bounds the
    buffered results. A worker that dies, sends a corrupt message or holds an item past
    item_timeout_seconds is replaced with a fresh pipe; only its item gets WORKER_CRASHED
    or ITEM_TIMEOUT. A result already readable from the pipe always wins over a timeout.
    A worker not ready within STARTUP_TIMEOUT_SECONDS is replaced. Replacements are
    counted in stats.
    """
    if workers < 1:
        raise ValueError(f"workers must be >= 1, got {workers}")
    stats = stats if stats is not None else WorkerStats()
    ctx = mp.get_context("spawn")
    slots: list[_Slot] = []

    def start(i: int, startup_failures: int = 0) -> None:
        parent, child = ctx.Pipe()
        proc = ctx.Process(target=_worker_main, args=(config, child), daemon=True)
        proc.start()
        child.close()
        slot = _Slot(proc, parent, time.monotonic(), startup_failures=startup_failures)
        if i < len(slots):
            slots[i] = slot
        else:
            slots.append(slot)

    def replace(i: int, startup_failures: int = 0) -> None:
        old = slots[i]
        if old.proc.is_alive():
            old.proc.terminate()
        old.proc.join()
        old.conn.close()
        stats.replaced += 1
        start(i, startup_failures)

    held: dict[int, tuple[int, float]] = {}  # worker slot -> (item index, since)
    buffer: dict[int, _Result] = {}
    window = workers * WINDOW_PER_WORKER
    timeout = config.item_timeout_seconds

    def read(i: int) -> bool:
        """Read every message waiting on slot i; False if the pipe is broken or corrupt."""
        s = slots[i]
        try:
            while s.conn.poll():
                index, result, facts, pid = s.conn.recv()
                if index == -1:
                    s.ready = True
                    s.startup_failures = 0
                elif held.get(i, (None,))[0] == index:
                    del held[i]
                    buffer[index] = (result, facts, pid)
        except Exception:  # broken pipe or any corrupt payload: the channel is unusable
            return False
        return True

    next_dispatch = next_write = 0
    try:
        for i in range(workers):
            start(i)
        while next_write < len(items):
            limit = min(len(items), next_write + window)
            for i, s in enumerate(slots):
                if not s.ready or i in held or next_dispatch >= limit:
                    continue
                try:
                    s.conn.send((next_dispatch, items[next_dispatch]))
                except OSError:  # worker died while idle; the item stays undispatched
                    replace(i)
                    continue
                held[i] = (next_dispatch, time.monotonic())
                next_dispatch += 1
            wait([s.conn for s in slots], timeout=_POLL_SECONDS)
            # Per slot: snapshot liveness and clock, then read everything already sent, so a
            # result written before the snapshot always wins over a crash or timeout verdict.
            for i, s in enumerate(slots):
                alive = s.proc.is_alive()
                now = time.monotonic()
                alive = read(i) and alive
                if i not in held:
                    late = now - s.started > STARTUP_TIMEOUT_SECONDS
                    if s.ready and not alive:
                        replace(i)
                    elif not s.ready and (not alive or late):
                        if s.startup_failures + 1 >= _MAX_STARTUP_FAILURES:
                            raise RuntimeError("worker processes keep failing during startup")
                        replace(i, s.startup_failures + 1)
                    continue
                index, since = held[i]
                item_id = items[index].item_id
                if not alive:
                    code = ErrorCode.WORKER_CRASHED
                    message = f"item {item_id}: worker process crashed"
                elif now - since > timeout:
                    code = ErrorCode.ITEM_TIMEOUT
                    message = f"item {item_id}: not finished after {timeout} s"
                else:
                    continue
                result = _error_result(item_id, code, message, now - since)
                buffer[index] = (result, [], s.proc.pid or 0)
                del held[i]
                replace(i)
            while next_write in buffer:
                yield buffer.pop(next_write)
                next_write += 1
    finally:
        for s in slots:
            with contextlib.suppress(OSError):
                s.conn.send(None)
        deadline = time.monotonic() + 5
        for s in slots:
            if s.proc.pid is not None:
                s.proc.join(timeout=max(0.0, deadline - time.monotonic()))
            if s.proc.is_alive():
                s.proc.terminate()
                s.proc.join()
            s.conn.close()


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
