"""Regenerate the C# model/JSON fixtures from the Python models.

Run from the repository root:

    uv run python dotnet/tests/ExtractorTester.Tests/Fixtures/Models/generate_fixtures.py

Writes, next to this script:
- schema.json: field names in declaration order per model, plus the ErrorCode values.
- sample-<Model>.json / sample-<Model>.indent.json: edge-case instances, compact and indent=2.
- golden-pairs.jsonl, golden-items.jsonl, golden-report.json: a real run over the golden
  corpus (input paths replaced by "<tmp>"), re-dumped by pydantic.
"""

import datetime as dt
import importlib
import json
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[5]
sys.path[:0] = [str(ROOT), str(ROOT / "src")]

m = importlib.import_module("extractor_tester.models")
corpus = importlib.import_module("tests.golden.corpus")
runner_common = importlib.import_module("tests.runner_common")

OUT = Path(__file__).parent
MODELS = [
    m.Item,
    m.FileRecord,
    m.FilePair,
    m.FieldDiff,
    m.TextFacts,
    m.PairFacts,
    m.ItemResult,
    m.ErrorRecord,
    m.Example,
    m.MetricResult,
    m.FamilyAggregate,
    m.RunStats,
    m.Report,
]
FLOATS = [1.0, 1e-05, 0.1, 1 / 3, 123456789.0, 1e16, 1e-06, 1.5e300, 5e-324, -0.0, 1e15, 2.5e-07]
TRICKY = 'q"b\\s/\n\r\t\b\f\x00\x1f\x7f \u00e9 \u2713 \U0001f600 \u2028 <&> \u0085'


def _write(path: Path, text: str) -> None:
    path.write_bytes(text.encode("utf-8"))


def _file(fid: str, parent: str, children: list) -> object:
    return m.FileRecord(
        file_id=fid,
        parent_id=parent,
        index=0,
        name=TRICKY,
        mime="application/pdf",
        family="pdf",
        path=f"root/{fid}",
        fields={
            "pages": 3,
            "ratio": 0.5,
            "big": 2**70,
            "nested": {"a": [1, 2.0, None, True]},
            "empty": {},
            "list": [],
        },
        text=TRICKY,
        text_hash=None,
        decode_errors=2,
        children=children,
        missing_fields=["x"],
    )


def samples() -> list:
    """One edge-case instance per model."""
    leaf = _file("c1", "r1", [])
    root = _file("r1", "r1", [leaf])
    err = m.ErrorRecord(
        code=m.ErrorCode.INVALID_BASE64,
        scope="file",
        severity="error",
        item_id="i1",
        side="new",
        file_path="a/b",
        message=TRICKY,
    )
    diff = m.FieldDiff(
        path="a.b",
        group="specific",
        status="changed",
        old_value=FLOATS,
        new_value={"k": TRICKY, "n": -7},
        old_type="list",
        new_type="dict",
    )
    text = m.TextFacts(
        raw_equal=False,
        normalized_equal=False,
        method="full",
        edit_similarity=1 / 3,
        jaccard=1e-05,
        old_found_in_new=1.0,
        new_found_in_old=0.1,
        relation="overlapping",
        old_chars=10,
        new_chars=0,
        length_delta=-10,
        old_empty=False,
        new_empty=True,
        lines_total=3,
        lines_cut=1,
        lines_cut_pct=33.33333333333333,
        oversized_runs=0,
        snippets=[{"old": TRICKY, "new": "", "pos": 4}],
        seconds=123456789.0,
    )
    pair = m.PairFacts(
        item_id="i1",
        kind="paired",
        path_old="a",
        path_new="a",
        family="pdf",
        tier="name",
        tie_break="position",
        mime_old="x",
        mime_new="y",
        mime_equal=False,
        family_equal=True,
        field_diffs=[diff],
        fields_same=["z"],
        text=text,
        children_old=1,
        children_new=2,
        children_only_old=0,
        children_only_new=1,
        hints=["new-more-text"],
        identical=False,
        errors=[err],
        decode_errors_old=0,
        decode_errors_new=None,
    )
    example = m.Example(
        item_id="i1",
        path_old=None,
        path_new="p",
        old_value=1e16,
        new_value=[TRICKY],
        similarity=1e-06,
        note=None,
    )
    metric = m.MetricResult(key="k", count=1, denominator=3, rate=1 / 3, examples=[example])
    empty = m.MetricResult(key="e", count=0, denominator=0, rate=0.0, examples=[])
    family = m.FamilyAggregate(
        family="pdf",
        pairs=3,
        only_in_old=metric,
        only_in_new=empty,
        identical=empty,
        mime_changed=empty,
        family_changed=empty,
        fields=[metric, empty],
        edit_similarity={"mean": 0.1, "buckets": [0, 1, 2]},
        jaccard={},
        relations=[],
        methods={"full": 2, "fast-path": 1},
        lines_cut_pct=0.0,
        cut_lines_warning=False,
        tiers=[metric],
        hints=[],
        decode_errors=empty,
        file_errors=[metric],
    )
    stats = m.RunStats(
        wall_seconds=1.5e-07,
        workers=4,
        stage_seconds={"load": 0.25, "x": 1e16},
        text_seconds_by_family={},
        slowest_items=[{"item_id": "i1", "seconds": 2.0}],
        workers_replaced=0,
        profile_dir=None,
    )
    started = dt.datetime(2026, 1, 2, 3, 4, 5, 123000, tzinfo=dt.timezone(dt.timedelta(hours=2)))
    report = m.Report(
        schema_version=1,
        tool_version="0.1.0",
        started_at=started,
        inputs={"old": TRICKY, "mode": "folder"},
        config={"pct": 20, "f": 0.5, "none": None},
        totals={"compared": 3},
        items_only_old=metric,
        items_only_new=empty,
        item_fields=[],
        families=[family],
        errors=[metric],
        warnings=[TRICKY],
        run_stats=stats,
    )
    return [
        m.Item(
            item_id="i1",
            side="old",
            metadata={"a": TRICKY, "f": FLOATS},
            files=[root],
            source_path="C:\\in\\i1.json",
        ),
        root,
        m.FilePair(kind="paired", old=root, new=leaf, tier="text-hash"),
        diff,
        text,
        pair,
        m.ItemResult(
            item_id="i1",
            status="compared",
            metadata_diffs=[diff],
            files_identical=1,
            files_changed=0,
            files_only_old=0,
            files_only_new=0,
            errors=[err],
            seconds=0.001,
            stage_seconds={"decode": 1e-05, "match": 0.0},
        ),
        err,
        example,
        metric,
        family,
        stats,
        report,
    ]


def _replace(value: object, base: str) -> object:
    if isinstance(value, dict):
        return {k: _replace(v, base) for k, v in value.items()}
    if isinstance(value, list):
        return [_replace(v, base) for v in value]
    if isinstance(value, str):
        return value.replace(base, "<tmp>").replace("\\", "/")
    return value


def golden() -> None:
    """Run compare over the golden corpus and write its outputs re-dumped by pydantic."""
    with tempfile.TemporaryDirectory() as tmp:
        base = Path(tmp)
        old, new, out = base / "old", base / "new", base / "out"
        old.mkdir()
        new.mkdir()
        corpus.build(old, new)
        cfg = base / "c.yaml"
        cfg.write_text(runner_common.CONFIG, encoding="utf-8")
        code, output = runner_common.invoke(
            "compare", old, new, "--config", cfg, "--out", out, "--workers", 1
        )
        assert code == 0, output
        for name, model in (("pairs.jsonl", m.PairFacts), ("items.jsonl", m.ItemResult)):
            lines = (out / name).read_text(encoding="utf-8").splitlines()
            rows = [model.model_validate(_replace(json.loads(x), tmp)) for x in lines]
            _write(OUT / f"golden-{name}", "".join(r.model_dump_json() + "\n" for r in rows))
        data = json.loads((out / "report.json").read_text(encoding="utf-8"))
        report = m.Report.model_validate(_replace(data, tmp))
        _write(OUT / "golden-report.json", report.model_dump_json(indent=2))


def main() -> None:
    schema = {model.__name__: list(model.model_fields) for model in MODELS}
    schema["ErrorCode"] = [c.value for c in m.ErrorCode]
    _write(OUT / "schema.json", json.dumps(schema, indent=2) + "\n")
    for sample in samples():
        name = type(sample).__name__
        _write(OUT / f"sample-{name}.json", sample.model_dump_json())
        _write(OUT / f"sample-{name}.indent.json", sample.model_dump_json(indent=2))
    golden()


if __name__ == "__main__":
    main()
