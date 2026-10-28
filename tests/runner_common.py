"""Shared helpers for the end-to-end compare-run tests."""

import json
from pathlib import Path
from typing import Any

from typer.testing import CliRunner

from extractor_tester.cli import app
from tests.helpers import write_item

CONFIG = "name_field: name\nmime_primary_field: mp\nmime_secondary_field: ms\n"


def f(fid: str, parent: str, name: str, **extra: object) -> dict[str, object]:
    """Build one file entry; the root has parentId equal to its own id."""
    base = {"fileId": fid, "parentId": parent, "name": name, "mp": "application", "ms": "pdf"}
    return {**base, "text": "hello", **extra}


def item_files(item_id: str, author: str = "x") -> list[dict[str, object]]:
    """Build a root with one child."""
    return [
        f(item_id, item_id, "root", rootId=item_id),
        f(f"{item_id}-a", item_id, "a.pdf", rootId=item_id, author=author),
    ]


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


def three_items(old: Path, new: Path) -> None:
    """Write items a, b, c on both sides; NEW differs in author."""
    for i in ("a", "b", "c"):
        write_item(old, i, item_files(i))
        write_item(new, i, item_files(i, author="y"))


def item_errors(out: Path) -> dict[str, list[str]]:
    """Map item id to its error codes."""
    return {r["item_id"]: [e["code"] for e in r["errors"]] for r in read_jsonl(out / "items.jsonl")}


def run_with_workers(tmp_path: Path, cfg: Path, old: Path, new: Path, workers: int) -> Path:
    """Run compare with N workers and return the output folder."""
    out = tmp_path / f"out{workers}"
    code, output = invoke("compare", old, new, "--config", cfg, "--out", out, "--workers", workers)
    assert code == 0, output
    return out
