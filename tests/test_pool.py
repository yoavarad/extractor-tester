"""Worker pool: startup deadline, result-vs-timeout ordering, corrupt worker channel."""

from pathlib import Path

import pytest

from tests.test_runner import CONFIG, _three_items, folders


def _compare_all(tmp_path: Path, extra_config: str = "", workers: int = 2) -> tuple[list, int]:
    from extractor_tester.config import load_config
    from extractor_tester.loader import discover
    from extractor_tester.pool import WorkerStats, compare_all

    old, new = folders(tmp_path)
    _three_items(old, new)
    cfg_path = tmp_path / "c.yaml"
    cfg_path.write_text(CONFIG + extra_config, encoding="utf-8")
    stats = WorkerStats()
    results = list(compare_all(discover(old, new).items, load_config(cfg_path), workers, stats))
    return results, stats.replaced


def test_worker_hung_at_startup_is_replaced(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """A worker that never signals ready is killed after the startup deadline and replaced."""
    from extractor_tester import pool

    monkeypatch.setattr(pool, "STARTUP_TIMEOUT_SECONDS", 15.0)
    monkeypatch.setenv(pool.HANG_STARTUP_ENV, str(tmp_path / "hang.flag"))
    results, replaced = _compare_all(tmp_path, workers=1)  # one worker: nothing runs until replaced
    assert [(r.item_id, r.status) for r, _, _ in results] == [
        ("a", "compared"),
        ("b", "compared"),
        ("c", "compared"),
    ]
    assert replaced == 1


def test_result_already_received_beats_timeout(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """A result received before the timeout check is kept, not ITEM_TIMEOUT.

    No wall-clock race: queueing a result and jumping the pool's clock 1000 s happen under
    one lock that every clock read also takes, so a check sees either the old clock or the
    queued result with the item long expired. One worker keeps a single item in flight.
    """
    import threading

    from extractor_tester import channel

    real_clock, real_deliver = channel.clock, channel.deliver
    lock = threading.Lock()
    jumps = [0]

    def deliver(s: channel.Slot, msg: tuple, wake: channel.Event) -> None:
        with lock:
            real_deliver(s, msg, wake)
            if msg[0] >= 0 and msg[1] is not None:  # a result, now queued
                jumps[0] += 1

    def clock() -> float:
        with lock:
            return real_clock() + 1000 * jumps[0]

    monkeypatch.setattr(channel, "clock", clock)
    monkeypatch.setattr(channel, "deliver", deliver)
    results, replaced = _compare_all(tmp_path, "item_timeout_seconds: 1\n", workers=1)
    assert [(r.item_id, r.status, r.errors) for r, _, _ in results] == [
        ("a", "compared", []),
        ("b", "compared", []),
        ("c", "compared", []),
    ]
    assert jumps[0] == 3
    assert replaced == 0


def test_worker_killed_mid_write_marks_only_its_item(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """A worker that leaves a corrupt message on its channel and dies yields WORKER_CRASHED
    for its item only; the other workers' results still arrive."""
    from extractor_tester.pool import GARBAGE_ITEM_ENV

    monkeypatch.setenv(GARBAGE_ITEM_ENV, "b")
    results, replaced = _compare_all(tmp_path)
    codes = {r.item_id: [e.code.value for e in r.errors] for r, _, _ in results}
    assert codes == {"a": [], "b": ["WORKER_CRASHED"], "c": []}
    assert replaced == 1


def test_stalled_sender_does_not_block_other_workers(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """A worker whose result never finishes arriving cannot block the main loop: the other
    items are compared and the stalled item ends as ITEM_TIMEOUT."""
    import threading
    from multiprocessing.connection import Connection

    from extractor_tester import channel

    release = threading.Event()
    real_recv = channel.recv

    def recv(conn: Connection) -> object:
        msg = real_recv(conn)
        if isinstance(msg, tuple) and msg[1] is not None and msg[1].item_id == "b":
            release.wait()  # stuck mid-receive, as with a sender that stalls mid-write
        return msg

    monkeypatch.setattr(channel, "recv", recv)
    monkeypatch.setattr(channel, "SEND_GRACE_SECONDS", 0.5)
    try:
        results, replaced = _compare_all(tmp_path, "item_timeout_seconds: 3\n")
    finally:
        release.set()
    codes = {r.item_id: [e.code.value for e in r.errors] for r, _, _ in results}
    assert codes == {"a": [], "b": ["ITEM_TIMEOUT"], "c": []}
    assert replaced == 1


def test_workers_dying_before_taking_items_end_the_run(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """Workers that die right after startup are respawned only a bounded number of times;
    the run then fails with INTERNAL_FAILURE instead of looping forever."""
    from extractor_tester.pool import DIE_BEFORE_TAKE_ENV
    from tests.test_runner import invoke

    monkeypatch.setenv(DIE_BEFORE_TAKE_ENV, "1")
    old, new = folders(tmp_path)
    _three_items(old, new)
    cfg_path = tmp_path / "c.yaml"
    cfg_path.write_text(CONFIG, encoding="utf-8")
    out = tmp_path / "out"
    code, output = invoke("compare", old, new, "--config", cfg_path, "--out", out, "--workers", 1)
    assert code == 1
    assert "INTERNAL_FAILURE" in output
    assert "worker processes keep failing before taking any item" in output
