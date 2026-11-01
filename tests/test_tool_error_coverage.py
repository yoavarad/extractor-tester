"""Every ErrorCode is proven by at least one test marked covers_error("<CODE>")."""

from pathlib import Path

import pytest

from extractor_tester.models import ErrorCode

TESTS = Path(__file__).parent


class _Collector:
    """Pytest plugin that records the covers_error codes of every collected test."""

    def __init__(self) -> None:
        self.codes: set[str] = set()

    def pytest_collection_modifyitems(self, items: list[pytest.Item]) -> None:
        for item in items:
            for mark in item.iter_markers("covers_error"):
                self.codes.update(mark.args)


def marked_codes() -> set[str]:
    """Codes named by covers_error markers, from a collect-only run of all tests.

    "-m ''" turns off the default "not slow" filter so slow tests are counted too.
    """
    collector = _Collector()
    args = ["--collect-only", "-q", "-m", "", "-p", "no:cacheprovider", str(TESTS)]
    exit_code = pytest.main(args, plugins=[collector])
    assert exit_code == 0, f"collecting the tests failed (pytest exit code {exit_code})"
    return collector.codes


def test_every_error_code_has_a_marked_test() -> None:
    """Fails with a plain list of unmarked codes and of marker typos."""
    marked = marked_codes()
    known = {code.value for code in ErrorCode}
    missing = sorted(known - marked)
    unknown = sorted(marked - known)
    problems = []
    if missing:
        problems.append("no test marked for: " + ", ".join(missing))
    if unknown:
        problems.append("marker names unknown code: " + ", ".join(unknown))
    assert not problems, "; ".join(problems)
