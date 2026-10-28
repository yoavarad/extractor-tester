"""Worker pool: compare items in worker processes with per-item timeout and crash recovery."""

from __future__ import annotations

import contextlib
import cProfile
import multiprocessing as mp
import os
import signal
import time
from dataclasses import dataclass
from typing import TYPE_CHECKING

from extractor_tester import channel
from extractor_tester.compare.item import compare_item
from extractor_tester.models import ErrorCode, ErrorRecord, ItemResult, PairFacts

if TYPE_CHECKING:
    from collections.abc import Iterator, Sequence
    from multiprocessing.connection import _ConnectionBase as Connection  # Pipe end, any OS
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
# Consecutive workers on one slot that die or hang before taking any item; at this count
# the run gives up (a broken environment) instead of respawning forever.
_MAX_STARTUP_FAILURES = 3
# Test hooks: a worker given the item with this id exits abruptly (crash) or sends a
# corrupt payload and exits (killed mid-write); the worker that first creates the file at
# the hang path hangs during startup; with DIE_BEFORE_TAKE set, every worker exits on
# receiving its first item, before taking it.
CRASH_ITEM_ENV = "EXTRACTOR_TESTER_TEST_CRASH_ITEM"
GARBAGE_ITEM_ENV = "EXTRACTOR_TESTER_TEST_GARBAGE_ITEM"
HANG_STARTUP_ENV = "EXTRACTOR_TESTER_TEST_HANG_STARTUP"
DIE_BEFORE_TAKE_ENV = "EXTRACTOR_TESTER_TEST_DIE_BEFORE_TAKE"

_Result = tuple[ItemResult, list[PairFacts], int]
_Pair = tuple["Connection", "Connection"]


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
    slot: int, config: CompareConfig, conns: _Pair, profile_dir: Path | None = None
) -> None:
    """Worker loop on its own (inbox, outbox) pipes: send ready (index -1), then take one
    (index, item) at a time until None, ack it (index, None, None, pid) and send back
    (index, result, facts, pid).

    With profile_dir set, the loop runs under cProfile and dumps worker-<slot>.prof on exit.
    """
    _startup_hook()
    # Ctrl-C reaches the whole console group; the main process alone stops the workers.
    for name in ("SIGINT", "SIGBREAK"):
        if hasattr(signal, name):
            signal.signal(getattr(signal, name), signal.SIG_IGN)
    pid = os.getpid()
    crash_item = os.environ.get(CRASH_ITEM_ENV)
    garbage_item = os.environ.get(GARBAGE_ITEM_ENV)
    prof = cProfile.Profile() if profile_dir is not None else None
    if prof:
        prof.enable()
    inbox, conn = conns
    conn.send((-1, None, None, pid))
    try:
        while (msg := inbox.recv()) is not None:
            index, item = msg
            if os.environ.get(DIE_BEFORE_TAKE_ENV):
                os._exit(1)
            conn.send((index, None, None, pid))
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


def compare_all(
    items: Sequence[DiscoveredItem],
    config: CompareConfig,
    workers: int,
    stats: WorkerStats | None = None,
    profile_dir: Path | None = None,
) -> Iterator[_Result]:
    """Compare items in worker processes; yield (result, facts, worker pid) in input order.

    Each worker has its own pipes and holds at most one item. Dispatch runs at most
    workers x WINDOW_PER_WORKER items ahead of the oldest unfinished item, which bounds the
    buffered results. A worker that dies, sends a corrupt message or holds an item past
    item_timeout_seconds is replaced with fresh pipes; only its item gets WORKER_CRASHED
    or ITEM_TIMEOUT. A result already received always wins over a timeout. A worker that
    dies before taking its item gives the item back; a worker not ready within
    STARTUP_TIMEOUT_SECONDS is replaced; _MAX_STARTUP_FAILURES such workers in a row on one
    slot end the run with RuntimeError. Replacements are counted in stats.
    """
    if workers < 1:
        raise ValueError(f"workers must be >= 1, got {workers}")
    stats = stats if stats is not None else WorkerStats()
    ctx = mp.get_context("spawn")
    wake = channel.Event()
    slots: list[channel.Slot] = []

    def start(i: int, startup_failures: int = 0) -> None:
        item_r, item_w = ctx.Pipe(duplex=False)
        msg_r, msg_w = ctx.Pipe(duplex=False)
        args = (i, config, (item_r, msg_w), profile_dir)
        proc = ctx.Process(target=_worker_main, args=args, daemon=True)
        try:
            proc.start()
        except BaseException:
            item_w.close()
            msg_r.close()
            raise
        finally:
            item_r.close()
            msg_w.close()
        slot = channel.open_slot(proc, item_w, msg_r, startup_failures, wake)
        if i < len(slots):
            slots[i] = slot
        else:
            slots.append(slot)

    def replace(i: int, startup_failures: int = 0) -> None:
        old = slots[i]
        if old.proc.is_alive():
            old.proc.terminate()
        old.proc.join()  # the pipe now reaches EOF, which ends the old reader thread
        old.close()
        stats.replaced += 1
        start(i, startup_failures)

    held: dict[int, tuple[int, float]] = {}  # worker slot -> (item index, since)
    retry: list[int] = []  # items given back by workers that died before taking them
    buffer: dict[int, _Result] = {}
    window = workers * WINDOW_PER_WORKER
    timeout = config.item_timeout_seconds

    def read(i: int) -> bool:
        """Handle every message received from slot i; False once its channel failed."""
        s = slots[i]
        broken = s.broken  # read first: the reader queues all messages before breaking
        while s.msgs:
            index, result, facts, pid = s.msgs.popleft()
            if index == -1:
                s.ready = True
            elif held.get(i, (None,))[0] != index:
                continue
            elif result is None:
                s.taken = s.proven = True
                s.startup_failures = 0
            else:
                del held[i]
                buffer[index] = (result, facts, pid)
        return not broken

    next_dispatch = next_write = 0
    try:
        for i in range(workers):
            start(i)
        while next_write < len(items):
            limit = min(len(items), next_write + window)
            for i, s in enumerate(slots):
                if not s.ready or i in held or not (retry or next_dispatch < limit):
                    continue
                index = retry[0] if retry else next_dispatch
                try:
                    s.inbox.send((index, items[index]))
                except OSError:  # worker died while idle; judged below
                    continue
                if retry:
                    retry.pop(0)
                else:
                    next_dispatch += 1
                held[i] = (index, channel.clock())
                s.taken = False
            wake.wait(_POLL_SECONDS)
            wake.clear()
            # Per slot: snapshot liveness and clock, then handle everything already received,
            # so a result received before the snapshot always wins over a verdict.
            for i, s in enumerate(slots):
                alive = s.proc.is_alive()
                now = channel.clock()
                if not alive and s.thread is not None and s.thread.is_alive():
                    s.dead_since = s.dead_since or now  # reader still queueing its last words
                    if now - s.dead_since < channel.SEND_GRACE_SECONDS:
                        continue
                busy = s.busy_since
                alive = read(i) and alive
                if alive and busy is not None and now - busy < channel.SEND_GRACE_SECONDS:
                    continue  # a message is arriving; judge on a later pass
                if i in held:
                    index, since = held[i]
                    if alive and now - since <= timeout:
                        continue
                    if alive or s.taken:
                        item_id = items[index].item_id
                        code = ErrorCode.ITEM_TIMEOUT if alive else ErrorCode.WORKER_CRASHED
                        message = (
                            f"item {item_id}: not finished after {timeout} s"
                            if alive
                            else f"item {item_id}: worker process crashed"
                        )
                        result = _error_result(item_id, code, message, now - since)
                        buffer[index] = (result, [], s.proc.pid or 0)
                        del held[i]
                        replace(i)
                        continue
                    retry.append(held.pop(i)[0])  # died before taking it: give it back
                    retry.sort()
                elif alive and (s.ready or now - s.started <= STARTUP_TIMEOUT_SECONDS):
                    continue
                if s.proven:
                    replace(i)
                    continue
                if s.startup_failures + 1 >= _MAX_STARTUP_FAILURES:
                    raise RuntimeError("worker processes keep failing before taking any item")
                replace(i, s.startup_failures + 1)
            while next_write in buffer:
                yield buffer.pop(next_write)
                next_write += 1
    finally:
        for s in slots:
            with contextlib.suppress(OSError):
                s.inbox.send(None)
        deadline = channel.clock() + 5
        for s in slots:
            if s.proc.pid is not None:
                s.proc.join(timeout=max(0.0, deadline - channel.clock()))
            if s.proc.is_alive():
                s.proc.terminate()
                s.proc.join()
            s.close()
