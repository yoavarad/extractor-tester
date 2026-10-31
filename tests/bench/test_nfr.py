"""Slow NFR benchmarks; run with `pytest -m slow`. BENCH_PAIRS scales the corpus down."""

import contextlib
import os
import random
import threading
import time
from dataclasses import dataclass
from pathlib import Path

import psutil
import pytest

from extractor_tester.compare.text import compare_text
from extractor_tester.config import CompareConfig
from extractor_tester.runner import run
from tests.bench.generator import generate

pytestmark = pytest.mark.slow

PAIRS = int(os.environ.get("BENCH_PAIRS", "100000"))
WORKERS = 8
MAX_RUN_SECONDS = 30 * 60
MAX_RSS_BYTES = 2 * 1024**3
MAX_HTML_BYTES = 20 * 1024**2
MAX_FULL_COMPARE_SECONDS = 0.3


@dataclass
class BenchRun:
    """Measurements of the one big compare run."""

    seconds: float
    peak_rss: int
    html: Path


def _tree_rss(proc: psutil.Process) -> int:
    total = 0
    for p in [proc, *proc.children(recursive=True)]:
        with contextlib.suppress(psutil.Error):
            total += p.memory_info().rss
    return total


@pytest.fixture(scope="module")
def bench_run(tmp_path_factory: pytest.TempPathFactory) -> BenchRun:
    """Generate the corpus and run one compare, sampling summed RSS of main plus workers."""
    root = tmp_path_factory.mktemp("bench")
    old, new, config, _ = generate(root / "corpus", PAIRS, seed=0)
    out = root / "out"
    peak = 0
    stop = threading.Event()
    me = psutil.Process()

    def sample() -> None:
        nonlocal peak
        while not stop.is_set():
            peak = max(peak, _tree_rss(me))
            stop.wait(0.2)

    sampler = threading.Thread(target=sample, daemon=True)
    sampler.start()
    t0 = time.perf_counter()
    try:
        code = run(old, new, config, out, workers=WORKERS)
    finally:
        stop.set()
        sampler.join()
    seconds = time.perf_counter() - t0
    assert code == 0
    return BenchRun(seconds, peak, out / "report.html")


def test_run_time_100k(bench_run: BenchRun) -> None:
    """Run time <= 30 min at 8 workers."""
    assert bench_run.seconds <= MAX_RUN_SECONDS


def test_peak_memory(bench_run: BenchRun) -> None:
    """Peak RSS summed over main and workers <= 2 GB."""
    assert bench_run.peak_rss <= MAX_RSS_BYTES


def test_html_size(bench_run: BenchRun) -> None:
    """report.html <= 20 MB."""
    assert bench_run.html.stat().st_size <= MAX_HTML_BYTES


def test_full_compare_pair() -> None:
    """One full comparison of two 100,000-char texts with 1% edits takes <= 0.3 s."""
    rng = random.Random(0)
    old = "".join(rng.choices("abcdefghijklmnopqrstuvwxyz ", k=100_000))
    chars = list(old)
    for i in rng.sample(range(len(chars)), 1_000):
        chars[i] = rng.choice("0123456789")
    new = "".join(chars)
    cfg = CompareConfig(name_field="n", mime_primary_field="a", mime_secondary_field="b")
    compare_text(old[:1000], new[:1000], cfg)  # warm up imports and caches
    facts = compare_text(old, new, cfg)
    assert facts.method == "full"
    assert facts.seconds <= MAX_FULL_COMPARE_SECONDS
