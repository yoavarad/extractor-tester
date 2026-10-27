import base64
import importlib
import json
import re
from pathlib import Path

import pytest
from typer.testing import CliRunner

from extractor_tester.cli import app
from tests.helpers import write_item

MODULES = [
    "config",
    "loader",
    "decode",
    "families",
    "fields",
    "tree",
    "matching.matcher",
    "matching.tiers",
    "compare.fields",
    "compare.text",
    "compare.item",
    "aggregate",
    "output",
    "render",
    "runner",
    "profiling",
    "models",
    "cli",
]


@pytest.mark.parametrize("name", MODULES)
def test_module_imports(name: str) -> None:
    """Every scaffolded module imports."""
    importlib.import_module(f"extractor_tester.{name}")


def test_compare_help_lists_arguments() -> None:
    """compare --help lists all arguments and options."""
    args = ["compare", "--help"]
    result = CliRunner().invoke(app, args)
    assert result.exit_code == 0
    # Rich forces ANSI styling on CI (GITHUB_ACTIONS); strip it before matching.
    plain = re.sub(r"\x1b\[[0-9;]*m", "", result.output)
    for token in ["OLD", "NEW", "--config", "--out", "--overwrite", "--workers", "--profile"]:
        assert token in plain


def test_write_item_round_trips_text(tmp_path: Path) -> None:
    """write_item text decodes back to the original string."""
    path = write_item(tmp_path, "i1", [{"fileId": "1", "text": "héllo"}], extra="x")
    data = json.loads(path.read_text(encoding="utf-8"))
    assert data["itemId"] == "i1"
    assert data["extra"] == "x"
    assert base64.b64decode(data["files"][0]["text"]).decode("utf-8") == "héllo"
