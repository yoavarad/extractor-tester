"""Pydantic data models for the comparison pipeline and report. No logic."""

from __future__ import annotations

from enum import StrEnum
from typing import Any, Literal

from pydantic import AwareDatetime, BaseModel, ConfigDict


class Frozen(BaseModel):
    """Base for immutable models."""

    model_config = ConfigDict(frozen=True)


Side = Literal["old", "new"]
PairKind = Literal["paired", "only-in-old", "only-in-new"]
Hint = Literal[
    "new-more-text",
    "new-less-text",
    "new-filled-field",
    "new-emptied-field",
    "new-more-subfiles",
    "new-fewer-subfiles",
]


class ErrorCode(StrEnum):
    """Tool-error codes."""

    DUPLICATE_FILE_ID = "DUPLICATE_FILE_ID"
    EMPTY_INPUT = "EMPTY_INPUT"
    INPUT_NOT_FOUND = "INPUT_NOT_FOUND"
    INTERNAL_FAILURE = "INTERNAL_FAILURE"
    INVALID_BASE64 = "INVALID_BASE64"
    INVALID_CONFIG = "INVALID_CONFIG"
    INVALID_JSON = "INVALID_JSON"
    ITEM_ID_MISMATCH = "ITEM_ID_MISMATCH"
    ITEM_PROCESSING_FAILED = "ITEM_PROCESSING_FAILED"
    ITEM_TIMEOUT = "ITEM_TIMEOUT"
    ITEM_TOO_LARGE = "ITEM_TOO_LARGE"
    MISSING_FIELD = "MISSING_FIELD"
    MIXED_INPUT_KINDS = "MIXED_INPUT_KINDS"
    MULTIPLE_ROOTS = "MULTIPLE_ROOTS"
    ORPHAN_FILE = "ORPHAN_FILE"
    OUTPUT_EXISTS = "OUTPUT_EXISTS"
    OUTPUT_WRITE_FAILED = "OUTPUT_WRITE_FAILED"
    PARENT_CYCLE = "PARENT_CYCLE"
    ROOT_NOT_FOUND = "ROOT_NOT_FOUND"
    WORKER_CRASHED = "WORKER_CRASHED"


class ErrorRecord(Frozen):
    """One occurrence of a tool-error."""

    code: ErrorCode
    scope: Literal["run", "item", "file"]
    severity: Literal["fatal", "error", "warning"]
    item_id: str | None = None
    side: Side | None = None
    file_path: str | None = None
    message: str


class FileRecord(Frozen):
    """One decoded file in an item tree."""

    file_id: str
    parent_id: str
    index: int
    name: str
    mime: str
    family: str
    path: str
    fields: dict[str, Any]
    text: str
    text_hash: str | None = None
    decode_errors: int
    children: list[FileRecord]
    missing_fields: list[str]


class Item(Frozen):
    """One pipeline's output for one item."""

    item_id: str
    side: Side
    metadata: dict[str, Any]
    files: list[FileRecord]
    source_path: str


class FilePair(Frozen):
    """Match outcome for one file."""

    kind: PairKind
    old: FileRecord | None = None
    new: FileRecord | None = None
    tier: Literal["root", "name", "text-hash", "type-family"] | None = None
    tie_break: Literal["none", "text-hash", "position"] = "none"
    unmatched_reason: Literal["no-candidate", "parent-unpaired"] | None = None


class FieldDiff(Frozen):
    """Comparison of one dotted-path field."""

    path: str
    group: Literal["item", "generic", "specific"]
    status: Literal["same", "changed", "only-in-old", "only-in-new"]
    old_value: Any = None
    new_value: Any = None
    old_type: str | None = None
    new_type: str | None = None


class TextFacts(Frozen):
    """Text comparison result for one pair."""

    raw_equal: bool
    normalized_equal: bool
    method: Literal["fast-path", "full", "buffered", "skipped"]
    edit_similarity: float | None = None
    jaccard: float | None = None
    old_found_in_new: float | None = None
    new_found_in_old: float | None = None
    relation: (
        Literal["equal", "old-within-new", "new-within-old", "overlapping", "disjoint"] | None
    ) = None
    old_chars: int
    new_chars: int
    length_delta: int
    old_empty: bool
    new_empty: bool
    lines_total: int
    lines_cut: int
    lines_cut_pct: float
    oversized_runs: int
    snippets: list[dict[str, Any]]
    seconds: float


class PairFacts(Frozen):
    """All facts about one file pair."""

    item_id: str
    kind: PairKind
    path_old: str | None = None
    path_new: str | None = None
    family: str
    tier: str | None = None
    tie_break: str
    mime_old: str | None = None
    mime_new: str | None = None
    mime_equal: bool | None = None
    family_equal: bool | None = None
    field_diffs: list[FieldDiff]
    fields_same: list[str]
    text: TextFacts | None = None
    children_old: int | None = None
    children_new: int | None = None
    children_only_old: int | None = None
    children_only_new: int | None = None
    hints: list[Hint]
    identical: bool
    errors: list[ErrorRecord]
    decode_errors_old: int | None = None
    decode_errors_new: int | None = None


class ItemResult(Frozen):
    """Outcome for one item."""

    item_id: str
    status: Literal["compared", "only-in-old", "only-in-new", "error"]
    metadata_diffs: list[FieldDiff]
    files_identical: int
    files_changed: int
    files_only_old: int
    files_only_new: int
    errors: list[ErrorRecord]
    seconds: float
    stage_seconds: dict[str, float]


class Example(BaseModel):
    """A concrete instance backing a reported result."""

    item_id: str
    path_old: str | None = None
    path_new: str | None = None
    old_value: Any = None
    new_value: Any = None
    similarity: float | None = None
    note: str | None = None


class MetricResult(BaseModel):
    """One reported number with its examples."""

    key: str
    count: int
    denominator: int
    rate: float
    examples: list[Example]


class FamilyAggregate(BaseModel):
    """Everything reported for one type family."""

    family: str
    pairs: int
    only_in_old: MetricResult
    only_in_new: MetricResult
    identical: MetricResult
    mime_changed: MetricResult
    family_changed: MetricResult
    fields: list[MetricResult]
    edit_similarity: dict[str, Any]
    jaccard: dict[str, Any]
    relations: list[MetricResult]
    methods: dict[str, int]
    lines_cut_pct: float
    cut_lines_warning: bool
    tiers: list[MetricResult]
    hints: list[MetricResult]
    decode_errors: MetricResult
    file_errors: list[MetricResult]


class RunStats(BaseModel):
    """Timing and profiling numbers of the run."""

    wall_seconds: float
    workers: int
    stage_seconds: dict[str, float]
    text_seconds_by_family: dict[str, float]
    slowest_items: list[dict[str, Any]]
    workers_replaced: int
    profile_dir: str | None = None


class Report(BaseModel):
    """Content of report.json."""

    schema_version: int
    tool_version: str
    started_at: AwareDatetime
    inputs: dict[str, Any]
    config: dict[str, Any]
    totals: dict[str, int]
    items_only_old: MetricResult
    items_only_new: MetricResult
    item_fields: list[MetricResult]
    families: list[FamilyAggregate]
    errors: list[MetricResult]
    warnings: list[str]
    run_stats: RunStats
