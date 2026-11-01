"""Golden corpus: exact reproduction, example presence and metric keys.

Regenerate the expected files with `uv run python -m tests.golden.corpus`.
"""

import copy
import json
from pathlib import Path

import pytest

from tests.golden import checks
from tests.golden.checks import (
    METRICS_MD,
    field_names,
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
    md = METRICS_MD.read_text(encoding="utf-8")
    bad = undefined_keys(_report(), md)
    assert bad == [], undefined_keys_message(bad, md, field_names(_report()))


def test_metrics_key_check_fails_on_undefined_key() -> None:
    """Adding an undefined key to a copy of the report is caught."""
    report = copy.deepcopy(_report())
    report["families"][0]["hints"].append(
        {"key": "made-up-key", "count": 1, "denominator": 1, "rate": 1.0, "examples": []}
    )
    assert undefined_keys(report, METRICS_MD.read_text(encoding="utf-8")) == ["made-up-key"]


def _result(key: str) -> dict:
    return {"key": key, "count": 0, "denominator": 1, "rate": 0.0, "examples": []}


def _keys_report(*keys: str, fields: tuple[str, ...] = ()) -> dict:
    """A tiny report: `keys` are plain results; `fields` are per-field results (the field names)."""
    return {"results": [_result(k) for k in keys], "fields": [_result(f) for f in fields]}


def test_field_placeholder_matches_field_names_in_report() -> None:
    """<field> is a field name the report lists, so it may contain ':' (e.g. 'dc:title')."""
    md = METRICS_MD.read_text(encoding="utf-8")
    fields = ("author:changed", "dc:title:changed")
    good = ["author:changed", "dc:title:changed", "dc:title:only-in-new", "author:only-in-old"]
    bad = [":changed", "a:bogus", "nosuch:changed", "dc:changed", "author:changed:changed"]
    assert undefined_keys(_keys_report(*good, fields=fields), md) == []
    assert undefined_keys(_keys_report(*bad, fields=fields), md) == sorted(bad)


def test_field_with_colon_and_extra_suffix_is_a_field_name_only_if_listed() -> None:
    """'a:changed:changed' is accepted only when the report lists a field called 'a:changed'."""
    md = METRICS_MD.read_text(encoding="utf-8")
    assert (
        undefined_keys(_keys_report("a:changed:changed", fields=("a:changed:changed",)), md) == []
    )
    assert undefined_keys(_keys_report("a:changed:changed", fields=("a:changed",)), md) == [
        "a:changed:changed"
    ]


def test_empty_tool_error_folder_is_a_clear_error(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    """No manifests would make '<error code>' match an empty key, so fail naming the folder."""
    monkeypatch.setattr(checks, "TOOL_ERRORS", tmp_path)
    md = METRICS_MD.read_text(encoding="utf-8")
    with pytest.raises(ValueError, match="no tool-error manifests found in .*" + tmp_path.name):
        undefined_keys(_keys_report("x"), md)


def test_undefined_key_message_names_key_and_expected_forms() -> None:
    """The failure text names the bad key and the metrics.md forms it should look like."""
    md = METRICS_MD.read_text(encoding="utf-8")
    text = undefined_keys_message(["nosuch:changed"], md, {"author"})
    assert "'nosuch:changed'" in text
    assert "<field>:changed" in text
    assert "field part 'nosuch' is not a field name in this report" in text
    assert "field part" not in undefined_keys_message(["made-up-key"], md, {"author"})


def test_error_code_placeholder_matches_only_known_codes() -> None:
    """A real tool-error code passes as a key; an invented one is rejected."""
    md = METRICS_MD.read_text(encoding="utf-8")
    code = tool_error_codes()[0]
    assert undefined_keys(_keys_report(code, "not-a-code"), md) == ["not-a-code"]
