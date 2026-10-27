"""Streaming aggregator: counters plus capped example lists, folded into a Report."""

from __future__ import annotations

from collections import Counter
from datetime import UTC, datetime
from typing import TYPE_CHECKING, Any

from extractor_tester.models import (
    ErrorRecord,
    Example,
    FamilyAggregate,
    FieldDiff,
    ItemResult,
    MetricResult,
    PairFacts,
    Report,
    RunStats,
)

if TYPE_CHECKING:
    from extractor_tester.config import CompareConfig

_EPOCH = datetime(1970, 1, 1, tzinfo=UTC)


class _Counter:
    """A count with the first `cap` examples seen."""

    def __init__(self, cap: int) -> None:
        """Start empty with an example cap."""
        self.cap = cap
        self.count = 0
        self.examples: list[Example] = []

    def add(self, example: Example, amount: int = 1) -> None:
        """Count occurrences; keep the example while under the cap."""
        self.count += amount
        if len(self.examples) < self.cap:
            self.examples.append(example)

    def result(self, key: str, denominator: int) -> MetricResult:
        """Build the MetricResult for this counter."""
        rate = self.count / denominator if denominator else 0.0
        return MetricResult(
            key=key,
            count=self.count,
            denominator=denominator,
            rate=rate,
            examples=list(self.examples),
        )


class _Family:
    """Mutable per-family state."""

    def __init__(self, cap: int) -> None:
        """Create empty counters."""
        self.cap = cap
        self.pairs = 0
        self.only_old = _Counter(cap)
        self.only_new = _Counter(cap)
        self.identical = _Counter(cap)
        self.mime_changed = _Counter(cap)
        self.family_changed = _Counter(cap)
        self.decode_errors = _Counter(cap)
        self.fields: dict[str, _Counter] = {}
        self.tiers: dict[str, _Counter] = {}
        self.file_errors: dict[str, _Counter] = {}

    def bump(self, table: dict[str, _Counter], key: str, example: Example) -> None:
        """Add an occurrence to the keyed counter, creating it on first use."""
        table.setdefault(key, _Counter(self.cap)).add(example)


class Aggregator:
    """Fold PairFacts and ItemResults into a Report with bounded memory."""

    def __init__(self, config: CompareConfig) -> None:
        """Remember the config; examples are capped and cut per its settings."""
        self.config = config
        self._cap = config.examples_per_result
        self._families: dict[str, _Family] = {}
        self._items = Counter[str]()
        self._items_only_old = _Counter(self._cap)
        self._items_only_new = _Counter(self._cap)
        self._item_fields: dict[str, _Counter] = {}
        self._errors: dict[str, _Counter] = {}
        self._warnings = Counter[str]()
        self._error_total = 0

    def _cut(self, value: object) -> object:
        n = self.config.snippet_chars
        if isinstance(value, str):
            return value[:n]
        text = repr(value)
        return value if len(text) <= n else text[:n]

    def _record_error(self, err: ErrorRecord, example: Example) -> None:
        if err.severity == "warning":
            self._warnings[err.code.value] += 1
            return
        self._error_total += 1
        self._errors.setdefault(err.code.value, _Counter(self._cap)).add(example)

    def _diff_example(self, base: Example, d: FieldDiff) -> Example:
        note = f"{d.old_type} -> {d.new_type}" if d.old_type != d.new_type else None
        return base.model_copy(
            update={
                "old_value": self._cut(d.old_value),
                "new_value": self._cut(d.new_value),
                "note": note,
            }
        )

    def add_pair(self, pf: PairFacts) -> None:
        """Fold one file pair into its family's counters."""
        fam = self._families.setdefault(pf.family, _Family(self._cap))
        ex = Example(item_id=pf.item_id, path_old=pf.path_old, path_new=pf.path_new)
        if pf.kind == "only-in-old":
            fam.only_old.add(ex)
        elif pf.kind == "only-in-new":
            fam.only_new.add(ex)
        else:
            fam.pairs += 1
            if pf.identical:
                fam.identical.add(ex)
            if pf.mime_equal is False:
                fam.mime_changed.add(
                    ex.model_copy(
                        update={
                            "old_value": self._cut(pf.mime_old),
                            "new_value": self._cut(pf.mime_new),
                        }
                    )
                )
            if pf.family_equal is False:
                fam.family_changed.add(ex)
            if pf.tier is not None:
                fam.bump(fam.tiers, pf.tier, ex)
            for d in pf.field_diffs:
                if d.status != "same":
                    fam.bump(fam.fields, f"{d.path}:{d.status}", self._diff_example(ex, d))
        n_dec = (pf.decode_errors_old or 0) + (pf.decode_errors_new or 0)
        if n_dec:
            fam.decode_errors.add(ex, n_dec)
        for err in pf.errors:
            if err.severity != "warning":
                fam.bump(fam.file_errors, err.code.value, ex)
            self._record_error(err, ex.model_copy(update={"note": self._cut(err.message)}))

    def add_item(self, item: ItemResult) -> None:
        """Fold one item result into the item-level counters."""
        self._items[item.status] += 1
        ex = Example(item_id=item.item_id)
        if item.status == "only-in-old":
            self._items_only_old.add(ex)
        elif item.status == "only-in-new":
            self._items_only_new.add(ex)
        for d in item.metadata_diffs if item.status == "compared" else []:
            if d.status != "same":
                key = f"{d.path}:{d.status}"
                self._item_fields.setdefault(key, _Counter(self._cap)).add(
                    self._diff_example(ex, d)
                )
        for err in item.errors:
            self._record_error(err, ex.model_copy(update={"note": self._cut(err.message)}))

    def _family(self, name: str, f: _Family) -> FamilyAggregate:
        n = f.pairs
        total = n + f.only_old.count + f.only_new.count
        return FamilyAggregate(
            family=name,
            pairs=n,
            only_in_old=f.only_old.result("only-in-old", total),
            only_in_new=f.only_new.result("only-in-new", total),
            identical=f.identical.result("identical", n),
            mime_changed=f.mime_changed.result("mime-changed", n),
            family_changed=f.family_changed.result("family-changed", n),
            fields=[f.fields[k].result(k, n) for k in sorted(f.fields)],
            edit_similarity={},
            jaccard={},
            relations=[],
            methods={},
            lines_cut_pct=0.0,
            cut_lines_warning=False,
            tiers=[f.tiers[k].result(k, n) for k in sorted(f.tiers)],
            hints=[],
            decode_errors=f.decode_errors.result("decode-errors", n),
            file_errors=[f.file_errors[k].result(k, n) for k in sorted(f.file_errors)],
        )

    def finish(
        self,
        tool_version: str = "0",
        started_at: datetime = _EPOCH,
        inputs: dict[str, Any] | None = None,
        run_stats: RunStats | None = None,
    ) -> Report:
        """Build the Report from everything added so far."""
        n_items = sum(self._items.values())
        compared = self._items["compared"]
        return Report(
            schema_version=1,
            tool_version=tool_version,
            started_at=started_at,
            inputs=inputs or {},
            config=self.config.model_dump(),
            totals={
                "items": n_items,
                "compared": compared,
                "only_in_old": self._items["only-in-old"],
                "only_in_new": self._items["only-in-new"],
                "error": self._items["error"],
                "pairs": sum(f.pairs for f in self._families.values()),
            },
            items_only_old=self._items_only_old.result("items-only-in-old", n_items),
            items_only_new=self._items_only_new.result("items-only-in-new", n_items),
            item_fields=[
                self._item_fields[k].result(k, compared) for k in sorted(self._item_fields)
            ],
            families=[self._family(k, self._families[k]) for k in sorted(self._families)],
            errors=[self._errors[k].result(k, self._error_total) for k in sorted(self._errors)],
            warnings=[f"{k}: {v} occurrence(s)" for k, v in sorted(self._warnings.items())],
            run_stats=run_stats
            or RunStats(
                wall_seconds=0.0,
                workers=1,
                stage_seconds={},
                text_seconds_by_family={},
                slowest_items=[],
                workers_replaced=0,
            ),
        )
