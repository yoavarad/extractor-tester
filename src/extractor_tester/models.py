"""Report-side pydantic models. Report is loadable from report.json alone."""

from typing import Any

from pydantic import AwareDatetime, BaseModel


class Example(BaseModel):
    """A concrete instance backing a reported result."""

    item_id: str
    path_old: str | None = None
    path_new: str | None = None
    old_value: Any = None
    new_value: Any = None
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
