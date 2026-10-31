"""Fast checks that the benchmark generator is deterministic and meets the corpus shape."""

import statistics
from pathlib import Path

from tests.bench.generator import generate


def _snapshot(folder: Path) -> dict[str, bytes]:
    return {p.name: p.read_bytes() for p in sorted(folder.glob("*.json"))}


def test_same_seed_same_corpus(tmp_path: Path) -> None:
    """Two runs with one seed give byte-identical files; another seed differs."""
    a = generate(tmp_path / "a", 5, seed=7)
    b = generate(tmp_path / "b", 5, seed=7)
    c = generate(tmp_path / "c", 5, seed=8)
    assert _snapshot(a[0]) == _snapshot(b[0])
    assert _snapshot(a[1]) == _snapshot(b[1])
    assert _snapshot(a[0]) != _snapshot(c[0])


def test_median_length_within_target(tmp_path: Path) -> None:
    """Median decoded text length is at most 20,000 chars."""
    _, _, _, lengths = generate(tmp_path, 200, seed=1)
    assert statistics.median(lengths) <= 20_000
