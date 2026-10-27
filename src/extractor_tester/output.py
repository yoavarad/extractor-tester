"""Report writer: output folder rules, streaming JSONL writers, atomic report.json."""

from __future__ import annotations

import contextlib
import os
import tempfile
from typing import IO, TYPE_CHECKING

from extractor_tester.models import ErrorCode, Report

if TYPE_CHECKING:
    from pathlib import Path
    from types import TracebackType

    from pydantic import BaseModel

PAIRS_FILE = "pairs.jsonl"
ITEMS_FILE = "items.jsonl"
REPORT_FILE = "report.json"


class OutputError(Exception):
    """Run-fatal output problem carrying a tool-error code."""

    def __init__(self, code: ErrorCode, message: str) -> None:
        """Store the code and message."""
        super().__init__(message)
        self.code = code
        self.message = message


def _write_failed(path: Path, e: OSError) -> OutputError:
    """Build an OUTPUT_WRITE_FAILED error for a path."""
    return OutputError(ErrorCode.OUTPUT_WRITE_FAILED, f"cannot write {path}: {e.strerror or e}")


def prepare_out(out: Path, overwrite: bool) -> Path:
    """Create the output folder; refuse a non-empty one unless overwrite is set."""
    try:
        if out.exists() and not out.is_dir():
            raise OutputError(ErrorCode.OUTPUT_WRITE_FAILED, f"{out} is not a directory")
        if out.is_dir() and any(out.iterdir()) and not overwrite:
            raise OutputError(
                ErrorCode.OUTPUT_EXISTS,
                f"output folder {out} is not empty; use --overwrite to reuse it",
            )
        out.mkdir(parents=True, exist_ok=True)
    except OSError as e:
        raise _write_failed(out, e) from e
    return out


class JsonlWriter:
    """Append one model per line as utf-8 JSON; truncates any existing file on open."""

    def __init__(self, path: Path) -> None:
        """Open the file for writing."""
        self.path = path
        try:
            self._fh: IO[str] = path.open("w", encoding="utf-8", newline="\n")
        except OSError as e:
            raise _write_failed(path, e) from e

    def write(self, model: BaseModel) -> None:
        """Write one model as a single JSON line."""
        try:
            self._fh.write(model.model_dump_json() + "\n")
        except OSError as e:
            raise _write_failed(self.path, e) from e

    def close(self) -> None:
        """Flush and close the file."""
        try:
            self._fh.close()
        except OSError as e:
            raise _write_failed(self.path, e) from e

    def __enter__(self) -> JsonlWriter:
        """Return self."""
        return self

    def __exit__(
        self,
        exc_type: type[BaseException] | None,
        exc: BaseException | None,
        tb: TracebackType | None,
    ) -> None:
        """Close the file."""
        self.close()


def pairs_writer(out: Path) -> JsonlWriter:
    """Open pairs.jsonl in the output folder."""
    return JsonlWriter(out / PAIRS_FILE)


def items_writer(out: Path) -> JsonlWriter:
    """Open items.jsonl in the output folder."""
    return JsonlWriter(out / ITEMS_FILE)


def write_report(out: Path, report: Report) -> Path:
    """Write report.json atomically: temp file in the same folder, then rename."""
    target = out / REPORT_FILE
    tmp_name: str | None = None
    try:
        fd, tmp_name = tempfile.mkstemp(dir=out, prefix=".report-", suffix=".tmp")
        with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as fh:
            fh.write(report.model_dump_json(indent=2))
            fh.flush()
            os.fsync(fh.fileno())
        os.replace(tmp_name, target)
    except BaseException as e:
        if tmp_name:
            with contextlib.suppress(OSError):
                os.unlink(tmp_name)
        if isinstance(e, OSError):
            raise _write_failed(target, e) from e
        raise
    return target
