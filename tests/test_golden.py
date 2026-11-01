"""Golden corpus: exact reproduction, example presence and metric keys.

Regenerate the expected files with `uv run python -m tests.golden.corpus`.
"""

import copy
import json
from pathlib import Path

import pytest

from tests.golden.checks import (
    METRICS_MD,
    metric_results,
    missing_examples,
    tool_error_codes,
    undefined_keys,
    undefined_keys_message,
)
from tests.golden.corpus import EXPECTED, OUTPUT_FILES, run


@pytest.fixture(scope="module")
def corpus_dir(tmp_path_factory: pytest.TempPathFactory) -> Path:
    """One corpus folder shared by the worker-count runs."""
    return tmp_path_factory.mktemp("golden")


@pytest.mark.parametrize("workers", [1, 4])
def test_golden_outputs_match(corpus_dir: Path, workers: int) -> None:
    """pairs.jsonl, items.jsonl and report.json match the checked-in files, timings excluded."""
    actual = run(corpus_dir, workers)
    for name in OUTPUT_FILES:
        assert actual[name] == (EXPECTED / name).read_text(encoding="utf-8"), name


def _report() -> dict:
    return json.loads((EXPECTED / "report.json").read_text(encoding="utf-8"))


def test_golden_report_has_examples_on_every_nonzero_result() -> None:
    """rules/examples-on-every-result: each non-zero MetricResult carries examples."""
    report = _report()
    assert any(m["count"] for _, m in metric_results(report))
    assert missing_examples(report) == []


def test_example_presence_check_fails_on_emptied_list() -> None:
    """Emptying one example list in a copy of the report is caught."""
    report = copy.deepcopy(_report())
    where, result = next((w, m) for w, m in metric_results(report) if m["count"])
    result["examples"] = []
    assert missing_examples(report) == [where]


def test_golden_report_keys_defined_in_metrics_md() -> None:
    """Every result key of the golden report is defined in metrics.md."""
    assert len(list(metric_results(_report()))) >= 30
    md = METRICS_MD.read_text(encoding="utf-8")
    bad = undefined_keys(_report(), md)
    assert bad == [], undefined_keys_message(bad, md)


def test_metrics_key_check_fails_on_undefined_key() -> None:
    """Adding an undefined key to a copy of the report is caught."""
    report = copy.deepcopy(_report())
    report["families"][0]["hints"].append(
        {"key": "made-up-key", "count": 1, "denominator": 1, "rate": 1.0, "examples": []}
    )
    assert undefined_keys(report, METRICS_MD.read_text(encoding="utf-8")) == ["made-up-key"]


def _keys_report(*keys: str) -> dict:
    return {
        "results": [
            {"key": k, "count": 0, "denominator": 1, "rate": 0.0, "examples": []} for k in keys
        ]
    }


def test_field_placeholder_is_strict() -> None:
    """<field> keys need a non-empty field with no ':'; other shapes are rejected."""
    md = METRICS_MD.read_text(encoding="utf-8")
    good = ["author:changed", "pages:only-in-new", "a.b.c:only-in-old"]
    bad = [":changed", "a:changed:changed", "a:bogus", "a:b:changed"]
    assert undefined_keys(_keys_report(*good), md) == []
    assert undefined_keys(_keys_report(*bad), md) == sorted(bad)


def test_undefined_key_message_names_key_and_expected_forms() -> None:
    """The failure text names the bad key and the metrics.md forms it should look like."""
    md = METRICS_MD.read_text(encoding="utf-8")
    text = undefined_keys_message(["a:changed:changed"], md)
    assert "'a:changed:changed'" in text
    assert "<field>:changed" in text


def test_error_code_placeholder_matches_only_known_codes() -> None:
    """A real tool-error code passes as a key; an invented one is rejected."""
    md = METRICS_MD.read_text(encoding="utf-8")
    code = tool_error_codes()[0]
    assert undefined_keys(_keys_report(code, "not-a-code"), md) == ["not-a-code"]
