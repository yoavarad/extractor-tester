"""Stderr progress line and Ctrl-C handling of the compare run."""

import re
import signal
import subprocess
import sys
import time
from pathlib import Path

import pytest

from tests.helpers import write_item
from tests.runner_common import CONFIG, folders, item_files


@pytest.fixture
def cfg(tmp_path: Path) -> Path:
    """Write a minimal compare-config."""
    p = tmp_path / "c.yaml"
    p.write_text(CONFIG, encoding="utf-8")
    return p


def test_progress_line_on_stderr(tmp_path: Path, cfg: Path) -> None:
    """A finished run leaves a done/total progress line on stderr."""
    old, new = folders(tmp_path)
    for i in range(3):
        for d in (old, new):
            write_item(d, f"i{i}", item_files(f"i{i}"))
    proc = subprocess.run(
        [
            sys.executable,
            "-c",
            "from extractor_tester.cli import app; app()",
            "compare",
            str(old),
            str(new),
            "--config",
            str(cfg),
            "--out",
            str(tmp_path / "out"),
            "--workers",
            "1",
        ],
        capture_output=True,
        text=True,
        timeout=120,
    )
    assert proc.returncode == 0, proc.stderr
    assert "3/3 items" in proc.stderr


def test_ctrl_c_exits_130_without_report(tmp_path: Path, cfg: Path) -> None:
    """Interrupting mid-run exits 130 and writes no report."""
    old, new = folders(tmp_path)
    big = "word " * 100_000
    for i in range(60):
        for d, text in ((old, big + "a"), (new, big + "b")):
            files = item_files(f"i{i}")
            files[1]["text"] = text
            write_item(d, f"i{i}", files)
    out = tmp_path / "out"
    windows = sys.platform == "win32"
    # Windows: a new process group makes CTRL_BREAK deliverable; map it to KeyboardInterrupt.
    boot = (
        "import signal, sys; "
        + ("signal.signal(signal.SIGBREAK, signal.default_int_handler); " if windows else "")
        + "from extractor_tester.cli import app; app()"
    )
    proc = subprocess.Popen(
        [
            sys.executable,
            "-c",
            boot,
            "compare",
            str(old),
            str(new),
            "--config",
            str(cfg),
            "--out",
            str(out),
            "--workers",
            "1",
        ],
        stderr=subprocess.PIPE,
        text=True,
        creationflags=subprocess.CREATE_NEW_PROCESS_GROUP if windows else 0,
    )
    seen = ""
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline and not re.search(r"compared [1-9]\d*/", seen):
        assert proc.stderr is not None
        seen += proc.stderr.read(1)
    assert proc.poll() is None, "run finished before interrupt"
    proc.send_signal(signal.CTRL_BREAK_EVENT if windows else signal.SIGINT)
    _, err = proc.communicate(timeout=60)
    assert proc.returncode == 130, err
    assert not (out / "report.json").exists()
    assert not (out / "report.html").exists()
    assert "Traceback" not in err
