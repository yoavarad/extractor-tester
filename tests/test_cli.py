"""CLI exit codes for the compare command."""

from pathlib import Path

from typer.testing import CliRunner

from extractor_tester.cli import app

CONFIG = "name_field: name\nmime_primary_field: mp\nmime_secondary_field: ms\n"


def setup(tmp_path: Path) -> tuple[Path, Path, Path]:
    """Create OLD and NEW folders and a minimal config."""
    old, new = tmp_path / "old", tmp_path / "new"
    old.mkdir()
    new.mkdir()
    cfg = tmp_path / "c.yaml"
    cfg.write_text(CONFIG, encoding="utf-8")
    return old, new, cfg


def test_missing_input_exits_1(tmp_path: Path) -> None:
    """Missing input path exits 1 with INPUT_NOT_FOUND."""
    old, _, cfg = setup(tmp_path)
    args = ["compare", str(old), str(tmp_path / "nope"), "--config", str(cfg)]
    result = CliRunner().invoke(app, [*args, "--out", str(tmp_path / "o")])
    assert result.exit_code == 1
    assert "INPUT_NOT_FOUND" in result.output


def test_unknown_option_exits_2(tmp_path: Path) -> None:
    """An unknown option is a usage error."""
    old, new, cfg = setup(tmp_path)
    args = ["compare", str(old), str(new), "--config", str(cfg), "--bogus"]
    assert CliRunner().invoke(app, args).exit_code == 2
