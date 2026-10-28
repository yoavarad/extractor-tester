"""Worker pool: compare items in worker processes with per-item timeout and crash recovery."""

from __future__ import annotations

import contextlib
import cProfile
import multiprocessing as mp
import os
import time
from dataclasses import dataclass
from multiprocessing.connection import wait
from typing import TYPE_CHECKING

from extractor_tester.compare.item import compare_item
from extractor_tester.models import ErrorCode, ErrorRecord, ItemResult, PairFacts

if TYPE_CHECKING:
    from collections.abc import Iterator, Sequence
    from multiprocessing.connection import _ConnectionBase as Connection  # Pipe end, any OS
    from multiprocessing.process import BaseProcess
    from pathlib import Path

    from extractor_tester.config import CompareConfig
    from extractor_tester.loader import DiscoveredItem


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


def _worker_main(
    slot: int, config: CompareConfig, conn: Connection, profile_dir: Path | None = None
) -> None:
    """Worker loop on its own pipe: send ready (index -1), then take one (index, item) at a
    time until None and send back (index, result, facts, pid).

    With profile_dir set, the loop runs under cProfile and dumps worker-<slot>.prof on exit.
    """
    _startup_hook()
    pid = os.getpid()
    crash_item = os.environ.get(CRASH_ITEM_ENV)
    garbage_item = os.environ.get(GARBAGE_ITEM_ENV)
    prof = cProfile.Profile() if profile_dir is not None else None
    if prof:
        prof.enable()
    conn.send((-1, None, None, pid))
    try:
        while (msg := conn.recv()) is not None:
            index, item = msg
            if item.item_id == crash_item:
                os._exit(1)
            if item.item_id == garbage_item:
                conn.send_bytes(b"\x80\x05not a pickle")
                os._exit(1)
            result, facts = compare_one(item, config)
            conn.send((index, result, facts, pid))
    finally:
        if prof and profile_dir is not None:
            prof.disable()
            profile_dir.mkdir(parents=True, exist_ok=True)
            target = profile_dir / f"worker-{slot}.prof"
            tmp = target.with_suffix(".tmp")
            prof.dump_stats(str(tmp))
            tmp.replace(target)


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
    profile_dir: Path | None = None,
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
        proc = ctx.Process(target=_worker_main, args=(i, config, child, profile_dir), daemon=True)
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
