"""End-to-end compare runs through the CLI on tmp_path folders."""

import json
from pathlib import Path
from typing import Any

import pytest
from typer.testing import CliRunner

from extractor_tester.cli import app
from tests.helpers import write_item

CONFIG = "name_field: name\nmime_primary_field: mp\nmime_secondary_field: ms\n"


def f(fid: str, parent: str, name: str, **extra: object) -> dict[str, object]:
    """Build one file entry; the root has parentId equal to its own id."""
    base = {"fileId": fid, "parentId": parent, "name": name, "mp": "application", "ms": "pdf"}
    return {**base, "text": "hello", **extra}


def item_files(item_id: str, author: str = "x") -> list[dict[str, object]]:
    """A root with one child."""
    return [
        f(item_id, item_id, "root", rootId=item_id),
        f(f"{item_id}-a", item_id, "a.pdf", rootId=item_id, author=author),
    ]


@pytest.fixture
def cfg(tmp_path: Path) -> Path:
    """Write a minimal compare-config."""
    p = tmp_path / "c.yaml"
    p.write_text(CONFIG, encoding="utf-8")
    return p


def folders(tmp_path: Path) -> tuple[Path, Path]:
    """Create empty OLD and NEW folders."""
    old, new = tmp_path / "old", tmp_path / "new"
    old.mkdir()
    new.mkdir()
    return old, new


def invoke(*args: object) -> tuple[int, str]:
    """Run the CLI and return exit code plus combined output."""
    result = CliRunner().invoke(app, [str(a) for a in args])
    return result.exit_code, result.output


def read_jsonl(path: Path) -> list[dict[str, Any]]:
    """Parse a JSONL file."""
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines()]


def test_folders_of_three_items_write_all_outputs(tmp_path: Path, cfg: Path) -> None:
    """Folders of three items write pairs, items and report and exit 0."""
    old, new = folders(tmp_path)
    for i in ("c", "a", "b"):
        write_item(old, i, item_files(i))
        write_item(new, i, item_files(i, author="y" if i == "b" else "x"))
    out = tmp_path / "out"
    code, output = invoke("compare", old, new, "--config", cfg, "--out", out)
    assert code == 0, output
    items = read_jsonl(out / "items.jsonl")
    assert [i["item_id"] for i in items] == ["a", "b", "c"]
    assert all(i["status"] == "compared" for i in items)
    pairs = read_jsonl(out / "pairs.jsonl")
    assert len(pairs) == 6
    report = json.loads((out / "report.json").read_text(encoding="utf-8"))
    assert report["totals"]["items"] == 3
    assert report["totals"]["compared"] == 3
    assert report["totals"]["pairs"] == 6
    assert report["inputs"]["old"] == str(old)


def test_malformed_item_is_reported_and_others_compared(tmp_path: Path, cfg: Path) -> None:
    """A malformed item gets INVALID_JSON while the good ones are compared."""
    old, new = folders(tmp_path)
    for i in ("a", "b", "c"):
        write_item(old, i, item_files(i))
        write_item(new, i, item_files(i))
    (new / "b.json").write_text("{not json", encoding="utf-8")
    out = tmp_path / "out"
    code, output = invoke("compare", old, new, "--config", cfg, "--out", out)
    assert code == 0, output
    items = {i["item_id"]: i for i in read_jsonl(out / "items.jsonl")}
    assert items["b"]["status"] == "error"
    assert items["b"]["errors"][0]["code"] == "INVALID_JSON"
    assert items["a"]["status"] == items["c"]["status"] == "compared"
    report = json.loads((out / "report.json").read_text(encoding="utf-8"))
    assert [e["key"] for e in report["errors"]] == ["INVALID_JSON"]


def test_one_sided_items_and_ignored_entries(tmp_path: Path, cfg: Path) -> None:
    """One-sided items become item results; ignored entries become a warning."""
    old, new = folders(tmp_path)
    write_item(old, "a", item_files("a"))
    write_item(old, "gone", item_files("gone"))
    write_item(new, "a", item_files("a"))
    write_item(new, "fresh", item_files("fresh"))
    (old / "notes.txt").write_text("x", encoding="utf-8")
    (new / "sub").mkdir()
    out = tmp_path / "out"
    code, output = invoke("compare", old, new, "--config", cfg, "--out", out)
    assert code == 0, output
    status = {i["item_id"]: i["status"] for i in read_jsonl(out / "items.jsonl")}
    assert status == {"a": "compared", "fresh": "only-in-new", "gone": "only-in-old"}
    report = json.loads((out / "report.json").read_text(encoding="utf-8"))
    assert report["totals"]["only_in_old"] == 1
    assert report["totals"]["only_in_new"] == 1
    assert any("2" in w and "ignored" in w for w in report["warnings"])


def test_empty_folders_are_a_valid_run(tmp_path: Path, cfg: Path) -> None:
    """Empty folders exit 0 with an EMPTY_INPUT warning."""
    old, new = folders(tmp_path)
    out = tmp_path / "out"
    code, output = invoke("compare", old, new, "--config", cfg, "--out", out)
    assert code == 0, output
    report = json.loads((out / "report.json").read_text(encoding="utf-8"))
    assert any("EMPTY_INPUT" in w for w in report["warnings"])


def test_invalid_config_exits_1(tmp_path: Path) -> None:
    """A broken config exits 1 with INVALID_CONFIG."""
    old, new = folders(tmp_path)
    bad = tmp_path / "bad.yaml"
    bad.write_text("x: 1\n", encoding="utf-8")
    code, output = invoke("compare", old, new, "--config", bad, "--out", tmp_path / "o")
    assert code == 1
    assert "INVALID_CONFIG" in output


def test_non_empty_out_exits_1(tmp_path: Path, cfg: Path) -> None:
    """A non-empty output folder without --overwrite exits 1 with OUTPUT_EXISTS."""
    old, new = folders(tmp_path)
    out = tmp_path / "out"
    out.mkdir()
    (out / "keep").write_text("x", encoding="utf-8")
    code, output = invoke("compare", old, new, "--config", cfg, "--out", out)
    assert code == 1
    assert "OUTPUT_EXISTS" in output


def test_file_mode_reports_one_item(tmp_path: Path, cfg: Path) -> None:
    """Two item files compare as one item."""
    old, new = folders(tmp_path)
    o = write_item(old, "I", item_files("I"))
    n = write_item(new, "I", item_files("I"))
    out = tmp_path / "out"
    code, output = invoke("compare", o, n, "--config", cfg, "--out", out)
    assert code == 0, output
    items = read_jsonl(out / "items.jsonl")
    assert [(i["item_id"], i["status"]) for i in items] == [("I", "compared")]
    report = json.loads((out / "report.json").read_text(encoding="utf-8"))
    assert report["totals"]["items"] == 1


def test_unexpected_failure_is_internal_failure(
    tmp_path: Path, cfg: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """An exception in the main process exits 1 with INTERNAL_FAILURE and no report."""
    old, new = folders(tmp_path)
    write_item(old, "a", item_files("a"))
    write_item(new, "a", item_files("a"))

    def boom(*_a: object, **_k: object) -> None:
        raise RuntimeError("kaput")

    monkeypatch.setattr("extractor_tester.runner.Aggregator.finish", boom)
    out = tmp_path / "out"
    code, output = invoke("compare", old, new, "--config", cfg, "--out", out)
    assert code == 1
    assert "INTERNAL_FAILURE" in output
    assert "kaput" in output
    assert not (out / "report.json").exists()
    assert len(read_jsonl(out / "items.jsonl")) == 1


def _run_with_workers(tmp_path: Path, cfg: Path, old: Path, new: Path, workers: int) -> Path:
    out = tmp_path / f"out{workers}"
    code, output = invoke("compare", old, new, "--config", cfg, "--out", out, "--workers", workers)
    assert code == 0, output
    return out


def _html_run(tmp_path: Path, cfg: Path, item_id: str, name: str, text: str) -> Path:
    old, new = folders(tmp_path)
    for d in (old, new):
        files = item_files("a")
        files[1]["name"] = name
        files[1]["text"] = text
        write_item(d, item_id, files)
    out = tmp_path / "out"
    code, output = invoke("compare", old, new, "--config", cfg, "--out", out)
    assert code == 0, output
    return out


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
    o1 = _run_with_workers(tmp_path, cfg, old, new, 1)
    o4 = _run_with_workers(tmp_path, cfg, old, new, 4)
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
    from extractor_tester.runner import compare_all

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


def test_report_html_written_and_rerender_is_byte_identical(tmp_path: Path, cfg: Path) -> None:
    """report.html sits next to report.json and re-rendering reproduces it."""
    from extractor_tester.render import render

    out = _html_run(tmp_path, cfg, "a", "a.pdf", "hello")
    html = (out / "report.html").read_bytes()
    (out / "report.html").unlink()
    render(out / "report.json", out / "report.html")
    assert (out / "report.html").read_bytes() == html


def test_script_tags_in_input_are_escaped(tmp_path: Path, cfg: Path) -> None:
    """Script tags from names and text never appear as markup."""
    old, new = folders(tmp_path)
    evil = "<script>alert(1)</script>"
    for d, author in ((old, evil), (new, evil + "2")):
        files = item_files("a", author=author)
        files[1]["name"] = evil
        files[1]["text"] = evil
        write_item(d, "a", files)
    out = tmp_path / "out"
    code, output = invoke("compare", old, new, "--config", cfg, "--out", out)
    assert code == 0, output
    html = (out / "report.html").read_text(encoding="utf-8")
    assert evil not in html
    assert "&lt;script&gt;alert(1)&lt;/script&gt;" in html


def test_path_shaped_item_id_creates_nothing_outside_out(tmp_path: Path, cfg: Path) -> None:
    """An item id like ../x writes no file outside --out."""
    box = tmp_path / "box"
    box.mkdir()
    old, new = folders(box)
    for d in (old, new):
        write_item(d, "a", item_files("../x"))
    before = {p for p in tmp_path.rglob("*")}
    out = box / "out"
    code, _ = invoke("compare", old, new, "--config", cfg, "--out", out)
    assert code in (0, 1)
    created = {p for p in tmp_path.rglob("*")} - before
    assert all(out in p.parents or p == out for p in created)
    assert not (box / "x").exists()


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
    assert pstats.Stats(str(out / "profile" / "worker-0.prof")).total_calls > 0
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
