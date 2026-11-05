"""Regenerate aggregate_cases.json from the Python aggregator.

Run from the repository root:

    uv run python dotnet/tests/ExtractorTester.Tests/Fixtures/Aggregate/gen_aggregate_fixtures.py

Scenarios reuse the inputs of tests/test_aggregate.py. Each scenario stores the config kwargs,
the PairFacts / ItemResult inputs as pydantic JSON, and the Report JSON of Aggregator.finish().
Stats, Lowest and Slowest cases cover tie order and even-length medians.
"""

import importlib
import json
import random
import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[5]
sys.path[:0] = [str(ROOT), str(ROOT / "src")]

agg_mod = importlib.import_module("extractor_tester.aggregate")
util = importlib.import_module("extractor_tester.aggregate_util")
models = importlib.import_module("extractor_tester.models")
prof = importlib.import_module("extractor_tester.profiling")
ta = importlib.import_module("tests.test_aggregate")

Example = models.Example
ErrorCode, ErrorRecord, FieldDiff = models.ErrorCode, models.ErrorRecord, models.FieldDiff


def scenario(cfg_kw: dict[str, Any], pairs: list[Any], items: list[Any]) -> dict[str, Any]:
    """Run the aggregator and record inputs plus the report JSON."""
    agg = agg_mod.Aggregator(ta._cfg(**cfg_kw))
    for p in pairs:
        agg.add_pair(p)
    for i in items:
        agg.add_item(i)
    return {
        "config": cfg_kw,
        "pairs": [p.model_dump_json() for p in pairs],
        "items": [i.model_dump_json() for i in items],
        "report": agg.finish().model_dump_json(),
    }


def build_scenarios() -> dict[str, Any]:
    err = ErrorRecord(code=ErrorCode.MISSING_FIELD, scope="file", severity="error", message="m")
    warn = ErrorRecord(code=ErrorCode.ORPHAN_FILE, scope="file", severity="warning", message="w")
    d = FieldDiff(path="t", group="item", status="changed", old_value=1, new_value=2)
    out: dict[str, Any] = {}
    out["basic"] = scenario(
        {},
        [ta._pair(f"i{i}", changed=i == 0) for i in range(4)],
        [ta._item(f"i{i}") for i in range(4)],
    )
    out["capped_cut"] = scenario(
        {"examples_per_result": 2, "snippet_chars": 20},
        [ta._pair(f"i{i}", changed=True, mime_equal=False, mime_old="x" * 50) for i in range(5)],
        [],
    )
    out["errors"] = scenario(
        {},
        [ta._pair("i0", errors=[err, err])],
        [ta._item("i1", [err, warn])],
    )
    out["one_sided"] = scenario(
        {},
        [ta._pair("i0", kind="only-in-old", path_new=None, identical=False)],
        [],
    )
    out["decode_errors"] = scenario(
        {},
        [ta._pair("i0", decode_errors_old=2, decode_errors_new=1), ta._pair("i1")],
        [],
    )
    out["item_metadata"] = scenario(
        {},
        [ta._pair("i0", errors=[warn])],
        [
            ta._item("i0").model_copy(update={"metadata_diffs": [d]}),
            ta._item("i1").model_copy(update={"status": "only-in-old", "metadata_diffs": [d]}),
        ],
    )
    sims = [0.1, 0.5, 0.9, 0.3, 0.7, 1.0, 0.2, 0.8, 0.6, 0.4]
    out["percentiles"] = scenario({}, [ta._tpair(f"i{i}", s) for i, s in enumerate(sims)], [])
    out["lowest_ties"] = scenario(
        {"examples_per_result": 3},
        [ta._tpair(i, s) for i, s in [("b", 0.5), ("a", 0.5), ("c", 0.9), ("d", 0.5), ("e", 0.1)]],
        [],
    )
    out["cut_warn"] = scenario({"cut_lines_warn_pct": 20}, [ta._tpair("a", 0.5, lines_cut=3)], [])
    out["cut_ok_hints"] = scenario(
        {"cut_lines_warn_pct": 20},
        [ta._tpair("a", 0.5, lines_cut=1), ta._pair("b", hints=["new-more-text"])],
        [],
    )
    odd = ta._pair(
        "t1",
        field_diffs=[
            FieldDiff(path="n", group="generic", status="changed", old_value=1, new_value="é" * 30),
            FieldDiff(
                path="l",
                group="generic",
                status="changed",
                old_value=list(range(40)),
                new_value=1.5,
            ),
            FieldDiff(path="z", group="generic", status="changed", old_value=None, new_value=True),
        ],
        identical=False,
    )
    out["odd_values"] = scenario({"snippet_chars": 20}, [odd], [])
    return out


def build_util() -> dict[str, Any]:
    rng = random.Random(136)
    stats_cases = [[], [0.4], [0.1, 0.2], [0.3, 0.1, 0.2], [0.5] * 4]
    for n in (5, 9, 10, 11, 12, 19, 20, 21, 33, 100):
        stats_cases.append([round(rng.random(), 3) for _ in range(n)])
    stats = [
        {"values": v, "stats": json.dumps(util.stats(v), separators=(",", ":"))}
        for v in stats_cases
    ]

    lowest = []
    for cap in (0, 1, 2, 3):
        offers = [
            ("a", 0.9, "p2", None),
            ("b", 0.2, None, "p1"),
            ("b", 0.2, "p0", None),
            ("a", 0.2, "z", None),
            ("a", 0.2, "z", None),
            ("c", 0.5, None, None),
            ("a", 0.2, "y", "x"),
        ]
        low = util.Lowest(cap)
        for i, (item, sim, po, pn) in enumerate(offers):
            low.add(sim, Example(item_id=item, path_old=po, path_new=pn, note=str(i)))
        lowest.append(
            {
                "cap": cap,
                "offers": [list(o) for o in offers],
                "kept_notes": [e.note for e in low.examples()],
            }
        )

    slow = prof.Slowest(3)
    offers_s = [("a", 1.0), ("b", 2.0), ("c", 2.0), ("d", 2.0), ("e", 0.5), ("f", 3.0)]
    for item, sec in offers_s:
        slow.add(item, sec, "compared")
    return {
        "stats": stats,
        "lowest": lowest,
        "slowest": {
            "n": 3,
            "offers": offers_s,
            "items": json.dumps(slow.items(), separators=(",", ":")),
        },
    }


def main() -> None:
    data = {"scenarios": build_scenarios(), "util": build_util()}
    out = Path(__file__).with_name("aggregate_cases.json")
    out.write_text(json.dumps(data, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
