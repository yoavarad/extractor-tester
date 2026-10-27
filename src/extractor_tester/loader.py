"""Input discovery and item JSON parsing."""

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from extractor_tester.config import CompareConfig
from extractor_tester.models import ErrorCode, ErrorRecord, Side


class LoaderError(Exception):
    """Tool-error raised by discovery or item loading."""

    def __init__(self, code: ErrorCode, message: str) -> None:
        """Store code and message."""
        self.code = code
        self.message = message
        super().__init__(message)


@dataclass(frozen=True)
class DiscoveredItem:
    """One item id with its OLD and NEW file paths (None when absent)."""

    item_id: str
    old_path: Path | None
    new_path: Path | None


@dataclass(frozen=True)
class Discovery:
    """Result of input discovery."""

    items: list[DiscoveredItem]
    ignored_count: int
    warnings: list[ErrorRecord]


def _scan(folder: Path) -> tuple[dict[str, Path], int]:
    found: dict[str, Path] = {}
    ignored = 0
    for entry in folder.iterdir():
        if entry.name.endswith(".json") and entry.is_file():
            found[entry.stem] = entry
        else:
            ignored += 1
    return found, ignored


def discover(old: Path, new: Path) -> Discovery:
    """Classify OLD/NEW as file pair or folder pair and list items sorted by item id."""
    for p in (old, new):
        if not p.exists():
            raise LoaderError(ErrorCode.INPUT_NOT_FOUND, f"Input not found: {p}")
    if old.is_file() != new.is_file():
        kinds = ("a file", "a folder") if old.is_file() else ("a folder", "a file")
        raise LoaderError(
            ErrorCode.MIXED_INPUT_KINDS,
            f"OLD is {kinds[0]} but NEW is {kinds[1]}; both must be files or both folders",
        )
    if old.is_file():
        return Discovery([DiscoveredItem(old.stem, old, new)], 0, [])
    old_found, old_ignored = _scan(old)
    new_found, new_ignored = _scan(new)
    items = [
        DiscoveredItem(i, old_found.get(i), new_found.get(i))
        for i in sorted(old_found.keys() | new_found.keys())
    ]
    warnings = []
    if not items:
        warnings.append(
            ErrorRecord(
                code=ErrorCode.EMPTY_INPUT,
                scope="run",
                severity="warning",
                message=f"No *.json items found in {old} or {new}",
            )
        )
    return Discovery(items, old_ignored + new_ignored, warnings)


def _invalid(prefix: str, path: Path, reason: str) -> LoaderError:
    return LoaderError(ErrorCode.INVALID_JSON, f"{prefix}file {path} is not a valid item: {reason}")


def load_item(
    path: Path, expected_item_id: str | None, config: CompareConfig, side: Side | None = None
) -> dict[str, Any]:
    """Size-check, parse and validate one item JSON; return the raw dict.

    Folder mode callers pass the file stem as expected_item_id; file mode callers load
    OLD with None, then NEW with expected_item_id=old["itemId"].
    """
    prefix = f"{side} " if side else ""
    item_id = expected_item_id or path.stem
    try:
        size = path.stat().st_size
    except OSError as e:
        raise _invalid(prefix, path, f"cannot read file ({e.strerror or e})") from e
    if size > config.max_item_bytes:
        raise LoaderError(
            ErrorCode.ITEM_TOO_LARGE,
            f"{prefix}item {item_id} is {size} bytes (limit {config.max_item_bytes})",
        )
    try:
        with path.open("rb") as fh:
            raw = fh.read(config.max_item_bytes + 1)
    except OSError as e:
        raise _invalid(prefix, path, f"cannot read file ({e.strerror or e})") from e
    if len(raw) > config.max_item_bytes:
        raise LoaderError(
            ErrorCode.ITEM_TOO_LARGE,
            f"{prefix}item {item_id} grew past the limit ({config.max_item_bytes})",
        )
    try:
        data = json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as e:
        raise _invalid(prefix, path, f"cannot parse JSON ({e})") from e
    if not isinstance(data, dict):
        raise _invalid(prefix, path, "top level must be an object")
    found = data.get("itemId")
    if not isinstance(found, str):
        raise _invalid(prefix, path, "itemId missing or not a string")
    files = data.get("files")
    if not isinstance(files, list):
        raise _invalid(prefix, path, "files missing or not a list")
    for entry in files:
        if not isinstance(entry, dict):
            raise _invalid(prefix, path, "files entry is not an object")
        if "fileId" not in entry or "parentId" not in entry:
            raise _invalid(prefix, path, "files entry missing fileId or parentId")
    if expected_item_id is not None and found != expected_item_id:
        raise LoaderError(
            ErrorCode.ITEM_ID_MISMATCH,
            f"{prefix}file {path} has itemId {found}, expected {expected_item_id}",
        )
    return data
