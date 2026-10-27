#!/usr/bin/env python3
"""Verification plugin: every staged src/ module has a tests/**/test_<name>.py.

Project copy of the bundled python-tdd-guard plugin. The bundled one reads
config.staged_files, which ydk never populates, so it always passes. This copy
asks git for the staged files directly.
"""

import json
import subprocess
import sys
import time
from pathlib import Path

SKIP = ("__init__", "conftest", "/tests/")


def _staged(root: Path) -> list[str]:
    result = subprocess.run(
        ["git", "diff", "--cached", "--name-only", "--diff-filter=ACMR"],
        cwd=root,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )
    return result.stdout.splitlines() if result.returncode == 0 else []


def main() -> None:
    """Run the src-tdd-guard verification check."""
    context = json.loads(sys.stdin.read())
    root = Path(context["project_root"])
    start = time.time()

    tests_dir = root / "tests"
    messages: list[str] = []
    for filepath in _staged(root):
        if not filepath.startswith("src/") or not filepath.endswith(".py"):
            continue
        if any(pat in filepath for pat in SKIP):
            continue
        test_name = f"test_{Path(filepath).name}"
        if not (tests_dir.is_dir() and any(tests_dir.rglob(test_name))):
            messages.append(f"FAIL: {filepath} -- {test_name} not found in tests/")

    passed = not messages
    if passed:
        output = "PASS: all staged source files have test files"
    else:
        output = "\n".join(messages) + f"\nTDD: {len(messages)} file(s) missing tests."

    result = {
        "name": "src-tdd-guard",
        "passed": passed,
        "output": output,
        "duration_seconds": round(time.time() - start, 1),
        "detail": {"failures": len(messages)},
    }
    json.dump(result, sys.stdout)
    sys.exit(0 if passed else 1)


if __name__ == "__main__":
    main()
