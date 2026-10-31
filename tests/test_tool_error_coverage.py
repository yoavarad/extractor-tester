"""Every tool-error manifest code is asserted by at least one test."""

import re
from pathlib import Path

from tests.golden.checks import tool_error_codes

TESTS = Path(__file__).parent


def untested_codes() -> list[str]:
    """Manifest codes that no other test file mentions on an assert line."""
    own = Path(__file__).resolve()
    text = "\n".join(
        p.read_text(encoding="utf-8") for p in TESTS.rglob("*.py") if p.resolve() != own
    )
    asserted = "\n".join(line for line in text.splitlines() if "assert" in line)
    return [c for c in tool_error_codes() if not re.search(rf"\b{c}\b", asserted)]


def test_manifests_found() -> None:
    """The manifest folder is read; an empty list would make the check vacuous."""
    assert len(tool_error_codes()) >= 20


def test_every_tool_error_code_is_asserted() -> None:
    """Lists the untested codes; there must be none."""
    assert untested_codes() == []
