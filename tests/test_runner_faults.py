"""Item timeouts and worker crashes in the compare run."""

import json
from pathlib import Path

import pytest

from tests.helpers import write_item
from tests.runner_common import (
    CONFIG,
    folders,
    item_errors,
    item_files,
    read_jsonl,
    run_with_workers,
    three_items,
)


def test_slow_item_times_out_and_others_are_compared(tmp_path: Path) -> None:
    """A 1 s timeout on two different 300,000-char texts yields ITEM_TIMEOUT for that item only."""
    import random

    cfg_path = tmp_path / "c.yaml"
    cfg_path.write_text(
        CONFIG + "item_timeout_seconds: 1\nfull_compare_max_chars: 1000000\n", encoding="utf-8"
    )
    old, new = folders(tmp_path)
    three_items(old, new)
    rng = random.Random(0)
    for d in (old, new):
        files = item_files("slow")
        files[1]["text"] = "".join(rng.choices("abcdefghij \n", k=300_000))
        write_item(d, "slow", files)
    out = run_with_workers(tmp_path, cfg_path, old, new, 2)
    rows = {r["item_id"]: r for r in read_jsonl(out / "items.jsonl")}
    assert item_errors(out) == {"a": [], "b": [], "c": [], "slow": ["ITEM_TIMEOUT"]}
    assert all(rows[i]["status"] == "compared" for i in ("a", "b", "c"))
    assert rows["slow"]["status"] == "error"
    report = json.loads((out / "report.json").read_text(encoding="utf-8"))
    assert report["run_stats"]["workers_replaced"] == 1


def test_crashed_worker_marks_only_its_item(
    tmp_path: Path, cfg: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """A worker that exits mid-item yields WORKER_CRASHED for that item; the run completes."""
    from extractor_tester.pool import CRASH_ITEM_ENV

    monkeypatch.setenv(CRASH_ITEM_ENV, "b")
    old, new = folders(tmp_path)
    three_items(old, new)
    out = run_with_workers(tmp_path, cfg, old, new, 2)
    assert item_errors(out) == {"a": [], "b": ["WORKER_CRASHED"], "c": []}
    report = json.loads((out / "report.json").read_text(encoding="utf-8"))
    assert report["run_stats"]["workers_replaced"] == 1
