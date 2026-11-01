"""Golden corpus: exact reproduction, example presence and metric keys.

Regenerate the expected files with `uv run python -m tests.golden.corpus`.
"""

import copy
import json
from pathlib import Path

import pytest

from tests.golden.checks import METRICS_MD, metric_results, missing_examples, undefined_keys
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


def test_golden_outputs_match_from_long_temp_path(tmp_path: Path) -> None:
    """A corpus under a folder path of 200+ characters gives the same outputs.

    Error notes are cut at snippet_chars, so they must not contain the input folder.
    The path stays a little under the classic 260-char Windows limit.
    """
    base = tmp_path / ("d" * max(1, 205 - len(str(tmp_path)) - 1))
    assert len(str(base)) >= 200, "temp root too long to build a 200-char test path"
    base.mkdir()
    actual = run(base, 1)
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
    assert undefined_keys(_report(), METRICS_MD.read_text(encoding="utf-8")) == []


def test_metrics_key_check_fails_on_undefined_key() -> None:
    """Adding an undefined key to a copy of the report is caught."""
    report = copy.deepcopy(_report())
    report["families"][0]["hints"].append(
        {"key": "made-up-key", "count": 1, "denominator": 1, "rate": 1.0, "examples": []}
    )
    assert undefined_keys(report, METRICS_MD.read_text(encoding="utf-8")) == ["made-up-key"]
