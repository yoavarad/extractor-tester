"""Golden corpus: 12 items from testing-strategy.md and the normalizer for their outputs.

Regenerate the expected files after an intended output change:

    uv run python -m tests.golden.corpus

This builds the corpus in a temporary folder, runs `compare` with one worker and writes
normalized pairs.jsonl, items.jsonl and report.json into tests/golden/expected/.
"""

import json
import tempfile
from pathlib import Path

from tests.helpers import write_item
from tests.runner_common import CONFIG, f, invoke

EXPECTED = Path(__file__).parent / "expected"
OUTPUT_FILES = ("pairs.jsonl", "items.jsonl", "report.json")
# Fields that differ between any two runs: timings, the run start and the input paths.
_VOLATILE = {"seconds", "stage_seconds", "started_at", "run_stats", "inputs"}

_OLD_TEXT = "The quarterly report covers revenue, costs and the outlook for next year."


def _root(item_id: str) -> dict[str, object]:
    """The item's root file: its id is the item id, it is its own parent and root."""
    return f(item_id, item_id, "root.eml", ms="eml", text="mail body", rootId=item_id)


def _item(
    old: Path, new: Path, item_id: str, old_child: dict, new_child: dict, *new_extra: dict
) -> None:
    """Write a root plus one child on both sides; NEW may get extra children."""
    for side, child in ((old, old_child), (new, new_child)):
        files = [_root(item_id), {**f(f"{item_id}-a", item_id, "a.pdf"), **child}]
        write_item(side, item_id, files + (list(new_extra) if side == new else []))


def build(old: Path, new: Path) -> None:
    """Write the 12 golden items into the OLD and NEW folders."""
    base = {"text": _OLD_TEXT, "author": "Jane", "pages": 3}
    _item(old, new, "g01-identical", base, base)
    _item(old, new, "g02-metadata", base, {**base, "author": "John"})
    _item(old, new, "g03-type-change", base, {**base, "pages": "3"})
    _item(old, new, "g04-text-extension", base, {**base, "text": _OLD_TEXT + " Appendix A."})
    _item(old, new, "g05-text-loss", base, {**base, "text": _OLD_TEXT[:30]})
    _item(old, new, "g06-rewritten", base, {**base, "text": "Completely different words here."})
    _item(old, new, "g07-renamed", base, {**base, "name": "report-final.pdf"})
    sib = "g08-same-name-siblings"
    for side in (old, new):
        imgs = [f(f"{sib}-{n}", sib, "image001.png", ms="png", text=f"img {n}") for n in (1, 2)]
        write_item(side, sib, [_root(sib), *imgs])
    sub = "g09-one-sided-subfile"
    _item(old, new, sub, base, base, f(f"{sub}-b", sub, "b.pdf", text="extra subfile"))
    _item(old, new, "g10-invalid-base64", base, base)
    path = new / "g10-invalid-base64.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    data["files"][1]["text"] = "!!not base64!!"
    path.write_text(json.dumps(data), encoding="utf-8")
    _item(old, new, "g11-malformed-json", base, base)
    (new / "g11-malformed-json.json").write_text("{not json", encoding="utf-8")
    write_item(old, "g12-one-sided-item", [_root("g12-one-sided-item")])


def normalize(value: object, base: str) -> object:
    """Drop the run-dependent fields and make paths under base OS-neutral, recursively."""
    if isinstance(value, dict):
        return {k: normalize(v, base) for k, v in value.items() if k not in _VOLATILE}
    if isinstance(value, list):
        return [normalize(v, base) for v in value]
    if isinstance(value, str) and base in value:
        return value.replace(base, "<tmp>").replace("\\", "/")
    return value


def normalized_outputs(out: Path, base: Path) -> dict[str, str]:
    """Return each output file normalized and serialized in a stable text form."""
    result = {}
    for name in OUTPUT_FILES:
        text = (out / name).read_text(encoding="utf-8")
        if name.endswith(".jsonl"):
            rows = [normalize(json.loads(line), str(base)) for line in text.splitlines()]
            result[name] = "".join(json.dumps(r) + "\n" for r in rows)
        else:
            result[name] = json.dumps(normalize(json.loads(text), str(base)), indent=1) + "\n"
    return result


def run(tmp: Path, workers: int) -> dict[str, str]:
    """Build the corpus under tmp, run compare with N workers, return normalized outputs."""
    old, new, out = tmp / "old", tmp / "new", tmp / f"out{workers}"
    if not old.exists():
        old.mkdir()
        new.mkdir()
        build(old, new)
    cfg = tmp / "c.yaml"
    cfg.write_text(CONFIG, encoding="utf-8")
    code, output = invoke("compare", old, new, "--config", cfg, "--out", out, "--workers", workers)
    assert code == 0, output
    return normalized_outputs(out, tmp)


def regenerate() -> None:
    """Rewrite the expected files from a one-worker run."""
    EXPECTED.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        for name, text in run(Path(tmp), 1).items():
            (EXPECTED / name).write_text(text, encoding="utf-8", newline="\n")


if __name__ == "__main__":
    regenerate()
