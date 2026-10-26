import base64
import importlib
import json
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
    for token in ["OLD", "NEW", "--config", "--out", "--overwrite", "--workers", "--profile"]:
        assert token in result.output


def test_compare_stub_exits_zero(tmp_path: Path) -> None:
    """compare stub exits 0 with a placeholder message."""
    cfg = tmp_path / "c.yaml"
    cfg.write_text("x: 1", encoding="utf-8")
    args = ["compare", str(tmp_path), str(tmp_path), "--config", str(cfg)]
    result = CliRunner().invoke(app, args)
    assert result.exit_code == 0
    assert "not implemented yet" in result.output


def test_write_item_round_trips_text(tmp_path: Path) -> None:
    """write_item text decodes back to the original string."""
    path = write_item(tmp_path, "i1", [{"fileId": "1", "text": "héllo"}], extra="x")
    data = json.loads(path.read_text(encoding="utf-8"))
    assert data["itemId"] == "i1"
    assert data["extra"] == "x"
    assert base64.b64decode(data["files"][0]["text"]).decode("utf-8") == "héllo"
