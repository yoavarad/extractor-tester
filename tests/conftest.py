"""Shared fixtures."""

from pathlib import Path

import pytest

from tests.runner_common import CONFIG


@pytest.fixture
def cfg(tmp_path: Path) -> Path:
    """Write a minimal compare-config."""
    p = tmp_path / "c.yaml"
    p.write_text(CONFIG, encoding="utf-8")
    return p
