"""Tests for the config loader."""

from pathlib import Path

import pytest

from extractor_tester.config import ConfigError, load_config

MIN = "name_field: name\nmime_primary_field: mime.a\nmime_secondary_field: mime.b\n"


def write(p: Path, text: str) -> Path:
    """Write a config file under tmp_path."""
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(text, encoding="utf-8")
    return p


def test_minimal_defaults(tmp_path: Path) -> None:
    """Minimal defaults."""
    c = load_config(write(tmp_path / "c.yaml", MIN))
    assert c.text_field == "text"
    assert c.generic_fields == []
    assert c.type_families_file is None
    assert c.text_hash_chars == 1_000_000
    assert c.full_compare_max_chars == 100_000
    assert c.max_block_chars == 4000
    assert c.cut_lines_warn_pct == 20
    assert c.containment_pct == 98
    assert c.examples_per_result == 5
    assert c.snippet_chars == 300
    assert c.max_item_bytes == 52_428_800
    assert c.item_timeout_seconds == 600
    assert c.family_map == {}


def test_structural_and_text_always_ignored(tmp_path: Path) -> None:
    """Structural and text always ignored."""
    c = load_config(write(tmp_path / "c.yaml", MIN + "text_field: body\nignore_fields: [x]\n"))
    assert {"x", "fileId", "parentId", "rootId", "body"} <= set(c.ignore_fields)


def test_missing_file(tmp_path: Path) -> None:
    """Missing file."""
    with pytest.raises(ConfigError, match=r"nope.yaml") as e:
        load_config(tmp_path / "nope.yaml")
    assert e.value.code == "INVALID_CONFIG"


def test_bad_yaml(tmp_path: Path) -> None:
    """Bad yaml."""
    with pytest.raises(ConfigError, match=r"c.yaml"):
        load_config(write(tmp_path / "c.yaml", "a: [unclosed\n"))


def test_unknown_key(tmp_path: Path) -> None:
    """Unknown key."""
    with pytest.raises(ConfigError, match=r"c.yaml.*bogus"):
        load_config(write(tmp_path / "c.yaml", MIN + "bogus: 1\n"))


def test_wrong_type(tmp_path: Path) -> None:
    """Wrong type."""
    with pytest.raises(ConfigError, match=r"c.yaml.*text_hash_chars"):
        load_config(write(tmp_path / "c.yaml", MIN + "text_hash_chars: abc\n"))


def test_out_of_range(tmp_path: Path) -> None:
    """Out of range."""
    with pytest.raises(ConfigError, match=r"c.yaml.*max_block_chars"):
        load_config(write(tmp_path / "c.yaml", MIN + "max_block_chars: 99\n"))


def test_missing_required(tmp_path: Path) -> None:
    """Missing required."""
    with pytest.raises(ConfigError, match=r"name_field"):
        load_config(write(tmp_path / "c.yaml", "mime_primary_field: a\nmime_secondary_field: b\n"))


def test_family_relative_to_config_not_cwd(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    """Family relative to config not cwd."""
    write(tmp_path / "cfg" / "fam.yaml", "families:\n  office: [Application/PDF, text/plain]\n")
    p = write(tmp_path / "cfg" / "c.yaml", MIN + "type_families_file: fam.yaml\n")
    monkeypatch.chdir(tmp_path)
    c = load_config(p)
    assert c.family_map == {"application/pdf": "office", "text/plain": "office"}


def test_mime_in_two_families(tmp_path: Path) -> None:
    """Mime in two families."""
    write(tmp_path / "fam.yaml", "families:\n  a: [x/y]\n  b: [X/Y]\n")
    p = write(tmp_path / "c.yaml", MIN + "type_families_file: fam.yaml\n")
    with pytest.raises(ConfigError, match=r"fam.yaml.*x/y"):
        load_config(p)


def test_family_map_not_an_input_key(tmp_path: Path) -> None:
    """Family map not an input key."""
    with pytest.raises(ConfigError, match=r"c.yaml.*family_map"):
        load_config(write(tmp_path / "c.yaml", MIN + "family_map: {a/b: x}\n"))
