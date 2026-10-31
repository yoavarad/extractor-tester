"""Build the FileRecord tree of one item from its raw JSON."""

from collections import Counter
from dataclasses import dataclass
from typing import Any

from extractor_tester.config import CompareConfig
from extractor_tester.decode import InvalidBase64Error, decode_text, normalize, text_hash
from extractor_tester.families import family, mime
from extractor_tester.fields import flatten
from extractor_tester.models import ErrorCode, ErrorRecord, FileRecord, Item, Side
from extractor_tester.profiling import add, clock


class TreeError(Exception):
    """Item-level tool-error raised while building the tree."""

    def __init__(self, code: ErrorCode, message: str) -> None:
        """Store code and message."""
        self.code = code
        self.message = message
        super().__init__(message)


@dataclass(frozen=True)
class BuildResult:
    """Built item plus file-level warnings and errors."""

    item: Item
    errors: list[ErrorRecord]


def _find_root(entries: list[dict[str, Any]], item_id: str) -> int:
    for e in entries:
        for k in ("fileId", "parentId"):
            if not isinstance(e.get(k), str):
                raise TreeError(
                    ErrorCode.INVALID_JSON, f"item {item_id}: {k} missing or not a string"
                )
    roots = [
        i
        for i, e in enumerate(entries)
        if e["fileId"] == item_id and e["parentId"] == item_id and e.get("rootId") == item_id
    ]
    if not roots:
        raise TreeError(ErrorCode.ROOT_NOT_FOUND, f"item {item_id}: no root file")
    if len(roots) > 1:
        raise TreeError(ErrorCode.MULTIPLE_ROOTS, f"item {item_id}: {len(roots)} root files")
    seen: set[Any] = set()
    for e in entries:
        fid = e["fileId"]
        if fid in seen:
            raise TreeError(
                ErrorCode.DUPLICATE_FILE_ID, f"item {item_id}: repeated file id {fid!r}"
            )
        seen.add(fid)
    return roots[0]


def _check_unreachable(
    entries: list[dict[str, Any]], reached: set[int], by_id: dict[Any, int], item_id: str
) -> list[int]:
    """Raise PARENT_CYCLE on a loop; return indexes of orphan files (parent names no file)."""
    orphans: list[int] = []
    state: dict[int, bool] = dict.fromkeys(reached, True)  # True = no loop above
    for start in range(len(entries)):
        if start in state:
            continue
        walk: list[int] = []
        on_walk: set[int] = set()
        cur: int | None = start
        while cur is not None and cur not in state:
            if cur in on_walk:
                raise TreeError(
                    ErrorCode.PARENT_CYCLE,
                    f"item {item_id}: parent loop at file {entries[cur]['fileId']!r}",
                )
            walk.append(cur)
            on_walk.add(cur)
            cur = by_id.get(entries[cur]["parentId"])
        if cur is None:
            orphans.append(walk[-1])
        for w in walk:
            state[w] = True
    return orphans


def build(
    data: dict[str, Any], config: CompareConfig, side: Side, source_path: str = ""
) -> BuildResult:
    """Build an Item with a FileRecord tree; raises TreeError on item-level errors."""
    item_id: str = data["itemId"]
    entries: list[dict[str, Any]] = data["files"]
    root_idx = _find_root(entries, item_id)
    by_id = {e["fileId"]: i for i, e in enumerate(entries)}
    kids: dict[int, list[int]] = {}
    for i, e in enumerate(entries):
        if i != root_idx and e["parentId"] in by_id:
            kids.setdefault(by_id[e["parentId"]], []).append(i)

    order = [root_idx]  # BFS, parents before children
    paths = {root_idx: _esc(_name(entries[root_idx], config))}
    for idx in order:
        names = {c: _name(entries[c], config) for c in kids.get(idx, [])}
        counts = Counter(names.values())
        seen: Counter[str] = Counter()
        for c in kids.get(idx, []):
            n = _esc(names[c])
            seen[n] += 1
            label = f"{n}[{seen[n]}]" if counts[n] > 1 else n
            paths[c] = f"{paths[idx]}/{label}"
            order.append(c)
    reached = set(order)
    errors: list[ErrorRecord] = [
        ErrorRecord(
            code=ErrorCode.ORPHAN_FILE,
            scope="file",
            severity="warning",
            item_id=item_id,
            side=side,
            message=(
                f"file {entries[o]['fileId']!r} parent {entries[o]['parentId']!r} "
                "names no file; file and subtree dropped"
            ),
        )
        for o in _check_unreachable(entries, reached, by_id, item_id)
    ]

    records: dict[int, FileRecord] = {}
    for idx in reversed(order):
        children = [records[c] for c in kids.get(idx, [])]
        records[idx] = _record(
            entries[idx], idx, paths[idx], children, config, item_id, side, errors
        )
    meta = {k: v for k, v in data.items() if k not in ("itemId", "files")}
    item = Item(
        item_id=item_id,
        side=side,
        metadata=meta,
        files=[records[root_idx]],
        source_path=source_path,
    )
    return BuildResult(item, errors)


def _esc(name: str) -> str:
    """Escape path syntax so occurrence marks and separators cannot collide with names."""
    return name.replace("\\", "\\\\").replace("/", "\\/").replace("[", "\\[")


def _name(entry: dict[str, Any], config: CompareConfig) -> str:
    value = flatten(entry).get(config.name_field)
    return value if isinstance(value, str) else ""


def _record(
    entry: dict[str, Any],
    index: int,
    path: str,
    children: list[FileRecord],
    config: CompareConfig,
    item_id: str,
    side: Side,
    errors: list[ErrorRecord],
) -> FileRecord:
    flat = flatten(entry)
    raw_text = flat.pop(config.text_field, None)
    missing = [
        f
        for f in (
            config.name_field,
            config.mime_primary_field,
            config.mime_secondary_field,
            config.text_field,
        )
        if (raw_text if f == config.text_field else flat.get(f)) is None
    ]
    shown = path
    errors.extend(
        ErrorRecord(
            code=ErrorCode.MISSING_FIELD,
            scope="file",
            severity="warning",
            item_id=item_id,
            side=side,
            file_path=shown,
            message=f"field {f!r} absent or null",
        )
        for f in missing
    )
    t_dec = clock()
    try:
        text, bad = decode_text(raw_text)
    except InvalidBase64Error as e:
        text, bad = "", 0
        errors.append(
            ErrorRecord(
                code=ErrorCode.INVALID_BASE64,
                scope="file",
                severity="error",
                item_id=item_id,
                side=side,
                file_path=shown,
                message=str(e),
            )
        )
    add("decode", clock() - t_dec)
    m = mime(_str(flat.get(config.mime_primary_field)), _str(flat.get(config.mime_secondary_field)))
    return FileRecord(
        file_id=entry["fileId"],
        parent_id=entry["parentId"],
        index=index,
        name=_str(flat.get(config.name_field)) or "",
        mime=m or "",
        family=family(m, config),
        path=path,
        fields=flat,
        text=text,
        text_hash=None if bad else text_hash(normalize(text), config.text_hash_chars),
        decode_errors=bad,
        children=children,
        missing_fields=missing,
    )


def _str(v: object) -> str | None:
    return v if isinstance(v, str) else None
