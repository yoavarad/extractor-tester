"""Deterministic benchmark corpus: OLD/NEW item folders with ~1% word edits in NEW."""

import base64
import json
import random
from pathlib import Path

CONFIG = "name_field: name\nmime_primary_field: mp\nmime_secondary_field: ms\n"
MIN_CHARS, MAX_CHARS = 1_000, 31_000  # uniform, so the median is ~16,000 (<= 20,000)
EDIT_RATE = 0.01
_VOCAB_SIZE = 5_000


def _vocab(rng: random.Random) -> list[str]:
    letters = "abcdefghijklmnopqrstuvwxyz"
    return ["".join(rng.choices(letters, k=rng.randint(3, 9))) for _ in range(_VOCAB_SIZE)]


def make_text(rng: random.Random, vocab: list[str], chars: int) -> list[str]:
    """Return the words of a random text of about chars characters."""
    return rng.choices(vocab, k=max(1, chars // 7))


def edit(rng: random.Random, vocab: list[str], words: list[str]) -> list[str]:
    """Replace about EDIT_RATE of the words."""
    return [rng.choice(vocab) if rng.random() < EDIT_RATE else w for w in words]


def _write(folder: Path, item_id: str, words: list[str]) -> int:
    text = " ".join(words)
    root = {
        "fileId": item_id,
        "parentId": item_id,
        "rootId": item_id,
        "name": f"{item_id}.txt",
        "mp": "text",
        "ms": "plain",
        "text": base64.b64encode(text.encode()).decode("ascii"),
    }
    (folder / f"{item_id}.json").write_text(
        json.dumps({"itemId": item_id, "files": [root]}), encoding="utf-8"
    )
    return len(text)


def generate(root: Path, pairs: int, seed: int = 0) -> tuple[Path, Path, Path, list[int]]:
    """Write pairs item pairs under root; return (old, new, config path, decoded lengths)."""
    rng = random.Random(seed)
    vocab = _vocab(rng)
    old, new = root / "old", root / "new"
    old.mkdir(parents=True)
    new.mkdir()
    lengths = []
    for i in range(pairs):
        words = make_text(rng, vocab, rng.randint(MIN_CHARS, MAX_CHARS))
        item_id = f"item{i:06d}"
        lengths.append(_write(old, item_id, words))
        _write(new, item_id, edit(rng, vocab, words))
    config = root / "config.yaml"
    config.write_text(CONFIG, encoding="utf-8")
    return old, new, config, lengths
