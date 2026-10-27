from pathlib import Path

import pytest
import yaml
from pydantic import ValidationError

from extractor_tester import models
from extractor_tester.models import (
    ErrorCode,
    ErrorRecord,
    FieldDiff,
    FilePair,
    FileRecord,
    ItemResult,
)

ROOT = Path(__file__).resolve().parent.parent
COMPONENTS = ROOT / ".ydk" / "components"
NAMES = [
    "Item", "FileRecord", "FilePair", "FieldDiff",
    "TextFacts", "PairFacts", "ItemResult", "ErrorRecord",
]  # fmt: skip


@pytest.mark.parametrize("name", NAMES)
def test_fields_match_manifest(name: str) -> None:
    """Field names and order equal manifest."""
    manifest = yaml.safe_load((COMPONENTS / "entity" / "model" / f"{name}.yaml").read_text("utf-8"))
    assert list(getattr(models, name).model_fields) == list(manifest["fields"])


def test_error_codes_match_manifests() -> None:
    """Enum equals the 20 manifest codes."""
    codes = {
        yaml.safe_load(p.read_text("utf-8"))["code"]
        for p in (COMPONENTS / "tool-error" / "errors").glob("*.yaml")
    }
    assert len(codes) == 20
    assert {c.value for c in ErrorCode} == codes


def _sample() -> tuple[FileRecord, ErrorRecord, FieldDiff]:
    rec = FileRecord(
        file_id="a", parent_id="a", index=0, name="n", mime="a/b", family="f", path="n",
        fields={"x": 1}, text="t", decode_errors=0, children=[], missing_fields=[],
    )  # fmt: skip
    err = ErrorRecord(code=ErrorCode.ORPHAN_FILE, scope="file", severity="error", message="m")
    diff = FieldDiff(path="p", group="item", status="changed", old_value=1, new_value="a")
    return rec, err, diff


def test_dump_stable_and_round_trip() -> None:
    """Dump stable; round-trips."""
    rec, err, diff = _sample()
    res = ItemResult(
        item_id="i", status="compared", metadata_diffs=[diff], files_identical=0, files_changed=1,
        files_only_old=0, files_only_new=0, errors=[err], seconds=0.1, stage_seconds={"load": 0.1},
    )  # fmt: skip
    assert res.model_dump_json() == res.model_dump_json()
    assert ItemResult.model_validate_json(res.model_dump_json()) == res
    pair = FilePair(kind="paired", old=rec, new=rec, tier="root")
    assert pair.tie_break == "none"
    assert list(pair.model_dump()) == list(FilePair.model_fields)
    assert FilePair.model_validate_json(pair.model_dump_json()) == pair


def test_frozen() -> None:
    """Models are immutable."""
    _, err, _ = _sample()
    with pytest.raises(ValidationError):
        err.message = "x"  # ty: ignore[invalid-assignment]
