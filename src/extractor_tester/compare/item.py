"""Compare one item end to end: load, build trees, match, record per-pair facts."""

import time
from pathlib import Path
from typing import Any

from extractor_tester.compare.fields import diff
from extractor_tester.config import CompareConfig
from extractor_tester.fields import flatten
from extractor_tester.loader import LoaderError, load_item
from extractor_tester.matching.matcher import match_item
from extractor_tester.models import (
    ErrorCode,
    ErrorRecord,
    FieldDiff,
    FilePair,
    Item,
    ItemResult,
    PairFacts,
    Side,
)
from extractor_tester.tree import BuildResult, TreeError, build


def compare_item(
    old_path: Path, new_path: Path, config: CompareConfig
) -> tuple[ItemResult, list[PairFacts]]:
    """Compare one item's OLD and NEW files; never raises."""
    t0 = time.perf_counter()
    item_id = old_path.stem
    stages: dict[str, float] = {}
    try:
        old_data = load_item(old_path, None, config, "old")
        item_id = old_data["itemId"]
        new_data = load_item(new_path, item_id, config, "new")
        old_build = build(old_data, config, "old", str(old_path))
        new_build = build(new_data, config, "new", str(new_path))
        stages["load"] = time.perf_counter() - t0
        t1 = time.perf_counter()
        pairs = match_item(old_build.item, new_build.item)
        stages["match"] = time.perf_counter() - t1
        t2 = time.perf_counter()
        facts = [_facts(item_id, p, pairs, old_build, new_build, config) for p in pairs]
        stages["compare"] = time.perf_counter() - t2
        attached = {id(e) for f in facts for e in f.errors}
        unattached = [e for e in old_build.errors + new_build.errors if id(e) not in attached]
        meta = _metadata_diffs(old_build.item, new_build.item, config)
    except (LoaderError, TreeError) as e:
        return _failed(item_id, e.code, e.message, t0), []
    except Exception as e:  # any unexpected failure is an item-level tool-error
        return _failed(
            item_id, ErrorCode.ITEM_PROCESSING_FAILED, f"{type(e).__name__}: {e}", t0
        ), []
    result = ItemResult(
        item_id=item_id,
        status="compared",
        metadata_diffs=meta,
        files_identical=sum(f.identical for f in facts),
        files_changed=sum(f.kind == "paired" and not f.identical for f in facts),
        files_only_old=sum(f.kind == "only-in-old" for f in facts),
        files_only_new=sum(f.kind == "only-in-new" for f in facts),
        errors=unattached,
        seconds=time.perf_counter() - t0,
        stage_seconds=stages,
    )
    return result, facts


def _failed(item_id: str, code: ErrorCode, message: str, t0: float) -> ItemResult:
    err = ErrorRecord(code=code, scope="item", severity="error", item_id=item_id, message=message)
    return ItemResult(
        item_id=item_id,
        status="error",
        metadata_diffs=[],
        files_identical=0,
        files_changed=0,
        files_only_old=0,
        files_only_new=0,
        errors=[err],
        seconds=time.perf_counter() - t0,
        stage_seconds={},
    )


def _metadata_diffs(old: Item, new: Item, config: CompareConfig) -> list[FieldDiff]:
    diffs = diff(flatten(old.metadata), flatten(new.metadata), config)
    return [d.model_copy(update={"group": "item"}) for d in diffs]


def _file_errors(build_result: BuildResult, side: Side, path: str | None) -> list[ErrorRecord]:
    if path is None:
        return []
    return [e for e in build_result.errors if e.side == side and e.file_path == path]


def _facts(
    item_id: str,
    pair: FilePair,
    all_pairs: list[FilePair],
    old_build: BuildResult,
    new_build: BuildResult,
    config: CompareConfig,
) -> PairFacts:
    old, new = pair.old, pair.new
    path_old = old.path if old else None
    path_new = new.path if new else None
    shown = old if old is not None else new
    assert shown is not None
    common: dict[str, Any] = {
        "item_id": item_id,
        "kind": pair.kind,
        "path_old": path_old,
        "path_new": path_new,
        "family": shown.family,
        "tier": pair.tier,
        "tie_break": pair.tie_break,
        "mime_old": old.mime if old else None,
        "mime_new": new.mime if new else None,
        "hints": [],
        "errors": _file_errors(old_build, "old", path_old)
        + _file_errors(new_build, "new", path_new),
        "decode_errors_old": old.decode_errors if old else None,
        "decode_errors_new": new.decode_errors if new else None,
    }
    if old is None or new is None:
        return PairFacts(
            **common,
            field_diffs=[],
            fields_same=[],
            children_old=len(old.children) if old else None,
            children_new=len(new.children) if new else None,
            identical=False,
        )
    ids_old = {c.file_id for c in old.children}
    ids_new = {c.file_id for c in new.children}
    only_old = sum(
        p.kind == "only-in-old" and p.old is not None and p.old.file_id in ids_old
        for p in all_pairs
    )
    only_new = sum(
        p.kind == "only-in-new" and p.new is not None and p.new.file_id in ids_new
        for p in all_pairs
    )
    diffs = diff(old.fields, new.fields, config)
    changed = [d for d in diffs if d.status != "same"]
    mime_equal = old.mime == new.mime
    family_equal = old.family == new.family
    return PairFacts(
        **common,
        mime_equal=mime_equal,
        family_equal=family_equal,
        field_diffs=changed,
        fields_same=[d.path for d in diffs if d.status == "same"],
        children_old=len(old.children),
        children_new=len(new.children),
        children_only_old=only_old,
        children_only_new=only_new,
        identical=mime_equal and not changed and only_old == 0 and only_new == 0,
    )
