#!/usr/bin/env python3
"""Verification plugin: no Python file under src/ exceeds 300 lines.

Project copy of the bundled python-file-length plugin, which only scans app/.
"""

import json
import sys
import time
from pathlib import Path

LIMIT = 300


def main() -> None:
    """Run the src-file-length verification check."""
    context = json.loads(sys.stdin.read())
    project_root = context["project_root"]
    start = time.time()

    src_dir = Path(project_root) / "src"
    messages: list[str] = []

    if src_dir.is_dir():
        for py_file in sorted(src_dir.rglob("*.py")):
            if "__pycache__" in py_file.parts:
                continue
            try:
                lines = len(py_file.read_text(encoding="utf-8").splitlines())
            except OSError:
                continue
            if lines > LIMIT:
                rel = py_file.relative_to(project_root).as_posix()
                messages.append(f"FAIL: {rel} -- {lines} lines (max {LIMIT})")

    passed = not messages
    output = f"PASS: all src/ Python files <= {LIMIT} lines" if passed else "\n".join(messages)

    result = {
        "name": "src-file-length",
        "passed": passed,
        "output": output,
        "duration_seconds": round(time.time() - start, 1),
        "detail": {"failures": len(messages)},
    }
    json.dump(result, sys.stdout)
    sys.exit(0 if passed else 1)


if __name__ == "__main__":
    main()
