"""Worker-count behaviour, determinism and profiling of the compare run."""

import json
from pathlib import Path

from tests.helpers import write_item
from tests.runner_common import (
    CONFIG,
    folders,
    invoke,
    item_files,
    run_with_workers,
)

_TIMING_KEYS = {"seconds", "stage_seconds"}


def _untimed(value: object) -> object:
    """Drop per-run timing fields, which differ between any two runs."""
    if isinstance(value, dict):
        return {k: _untimed(v) for k, v in value.items() if k not in _TIMING_KEYS}
    if isinstance(value, list):
        return [_untimed(v) for v in value]
    return value


def _jsonl_untimed(path: Path) -> list[str]:
    return [json.dumps(_untimed(json.loads(line))) for line in path.read_bytes().splitlines()]


def test_outputs_identical_for_one_and_four_workers(tmp_path: Path, cfg: Path) -> None:
    """pairs, items and report are identical for 1 and 4 workers, apart from timings.

    Per rules/deterministic-output, report.json excludes started_at and run_stats; per-item
    and per-pair timing fields vary run to run regardless of worker count.
    """
    old, new = folders(tmp_path)
    for n in range(12):
        i = f"item{n:02d}"
        write_item(old, i, item_files(i))
        write_item(new, i, item_files(i, author="y" if n % 3 == 0 else "x"))
    (new / "item05.json").write_text("{not json", encoding="utf-8")
    write_item(old, "gone", item_files("gone"))
    o1 = run_with_workers(tmp_path, cfg, old, new, 1)
    o4 = run_with_workers(tmp_path, cfg, old, new, 4)
    for name in ("pairs.jsonl", "items.jsonl"):
        assert _jsonl_untimed(o1 / name) == _jsonl_untimed(o4 / name)

    def stable(out: Path) -> bytes:
        report = json.loads((out / "report.json").read_text(encoding="utf-8"))
        del report["started_at"], report["run_stats"]
        return json.dumps(report).encode()

    assert stable(o1) == stable(o4)
    r4 = json.loads((o4 / "report.json").read_text(encoding="utf-8"))
    assert r4["run_stats"]["workers"] == 4


def test_single_worker_runs_in_separate_process(tmp_path: Path) -> None:
    """With one worker the comparison runs in a child process, results in input order."""
    import os

    from extractor_tester.config import load_config
    from extractor_tester.loader import discover
    from extractor_tester.pool import compare_all

    old, new = folders(tmp_path)
    for i in ("b", "a"):
        write_item(old, i, item_files(i))
        write_item(new, i, item_files(i))
    cfg_path = tmp_path / "c.yaml"
    cfg_path.write_text(CONFIG, encoding="utf-8")
    results = list(compare_all(discover(old, new).items, load_config(cfg_path), 1))
    assert [r.item_id for r, _, _ in results] == ["a", "b"]
    assert all(pid != os.getpid() for _, _, pid in results)


def test_workers_zero_exits_2(tmp_path: Path, cfg: Path) -> None:
    """--workers 0 is a usage error."""
    old, new = folders(tmp_path)
    code, _ = invoke("compare", old, new, "--config", cfg, "--out", tmp_path / "o", "--workers", 0)
    assert code == 2


def test_profile_writes_one_prof_per_worker(tmp_path: Path, cfg: Path) -> None:
    """--profile writes loadable profile/worker-<n>.prof for each worker and records profile_dir."""
    import pstats

    old, new = folders(tmp_path)
    for i in ("a", "b"):
        write_item(old, i, item_files(i))
        write_item(new, i, item_files(i))
    out = tmp_path / "out"
    code, output = invoke(
        "compare", old, new, "--config", cfg, "--out", out, "--workers", 2, "--profile"
    )
    assert code == 0, output
    profs = sorted(p.name for p in (out / "profile").glob("worker-*.prof"))
    assert profs == ["worker-0.prof", "worker-1.prof"]
    assert pstats.Stats(str(out / "profile" / "worker-0.prof")).get_stats_profile().func_profiles
    report = json.loads((out / "report.json").read_text(encoding="utf-8"))
    assert report["run_stats"]["profile_dir"] == "profile"


def test_no_profile_dir_by_default(tmp_path: Path, cfg: Path) -> None:
    """Without --profile no profile folder is written."""
    old, new = folders(tmp_path)
    write_item(old, "a", item_files("a"))
    write_item(new, "a", item_files("a"))
    out = tmp_path / "out"
    code, output = invoke("compare", old, new, "--config", cfg, "--out", out)
    assert code == 0, output
    assert not (out / "profile").exists()
