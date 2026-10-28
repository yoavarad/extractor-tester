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
    """Results sitting in the pipe when the timeout check runs are kept, not ITEM_TIMEOUT.

    The parent's wait is slowed to 2.5 s, past the 1 s item timeout, so every timeout check sees an
    expired clock while the finished result is already waiting to be read.
    """
    import time
    from multiprocessing.connection import wait as real_wait

    from extractor_tester import pool

    def slow_wait(conns: list, timeout: float | None = None) -> list:
        time.sleep(2.5)
        return real_wait(conns, timeout=0)

    monkeypatch.setattr(pool, "wait", slow_wait)
    results, replaced = _compare_all(tmp_path, "item_timeout_seconds: 1\n")
    assert [(r.item_id, r.status, r.errors) for r, _, _ in results] == [
        ("a", "compared", []),
        ("b", "compared", []),
        ("c", "compared", []),
    ]
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
