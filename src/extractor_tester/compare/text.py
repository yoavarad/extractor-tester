"""Text comparison for one file pair: fast path, full and buffered methods (see metrics.md)."""

from __future__ import annotations

import time
from typing import TYPE_CHECKING, Any

from rapidfuzz.distance import Indel, Opcode

from extractor_tester.compare.buffered import compare_buffered
from extractor_tester.decode import normalize
from extractor_tester.models import TextFacts

if TYPE_CHECKING:
    from extractor_tester.config import CompareConfig

MAX_SNIPPETS = 3


def compare_text(old: str, new: str, config: CompareConfig) -> TextFacts:
    """Pick the comparison method and compute TextFacts for one pair."""
    start = time.perf_counter()
    na, nb = normalize(old), normalize(new)
    base: dict[str, Any] = {
        "raw_equal": old == new,
        "normalized_equal": na == nb,
        "old_chars": len(old),
        "new_chars": len(new),
        "length_delta": len(new) - len(old),
        "old_empty": not na,
        "new_empty": not nb,
        "lines_total": 0,
        "lines_cut": 0,
        "lines_cut_pct": 0.0,
        "oversized_runs": 0,
    }
    if na == nb:
        facts: dict[str, Any] = {
            "method": "fast-path",
            "relation": "equal",
            "edit_similarity": 1.0,
            "jaccard": 1.0,
            "snippets": [],
        }
    elif max(len(old), len(new)) <= config.full_compare_max_chars:
        facts = _full(na, nb, config)
    else:
        facts = _buffered(old, new, na, nb, config)
    return TextFacts(**{**base, **facts}, seconds=time.perf_counter() - start)


def _buffered(old: str, new: str, na: str, nb: str, config: CompareConfig) -> dict[str, Any]:
    """Compare two long, unequal texts by aligned line blocks."""
    facts = compare_buffered(old, new, config)
    words_a, words_b = set(na.split()), set(nb.split())
    shared = len(words_a & words_b)
    facts["method"] = "buffered"
    facts["jaccard"] = shared / len(words_a | words_b)
    facts["relation"] = _relation(
        na,
        nb,
        facts["old_found_in_new"],
        facts["new_found_in_old"],
        shared,
        config.containment_pct / 100,
    )
    return facts


def _full(a: str, b: str, config: CompareConfig) -> dict[str, Any]:
    """Compare two normalized, unequal texts character by character."""
    la, lb = len(a), len(b)
    lcs = (la + lb - Indel.distance(a, b)) // 2
    old_found = lcs / la if la else 1.0
    new_found = lcs / lb if lb else 1.0
    words_a, words_b = set(a.split()), set(b.split())
    shared = len(words_a & words_b)
    return {
        "method": "full",
        "edit_similarity": 2 * lcs / (la + lb),
        "jaccard": shared / len(words_a | words_b),
        "old_found_in_new": old_found,
        "new_found_in_old": new_found,
        "relation": _relation(a, b, old_found, new_found, shared, config.containment_pct / 100),
        "snippets": _snippets(a, b, config.snippet_chars),
    }


def _relation(
    a: str, b: str, old_found: float, new_found: float, shared_words: int, threshold: float
) -> str:
    """Label how two unequal texts relate, from containment of the shorter in the longer."""
    if not a:
        return "old-within-new"
    if not b:
        return "new-within-old"
    if len(b) > len(a) and old_found >= threshold:
        return "old-within-new"
    if len(a) > len(b) and new_found >= threshold:
        return "new-within-old"
    return "overlapping" if shared_words else "disjoint"


def _snippets(a: str, b: str, n: int) -> list[dict[str, str]]:
    """Return up to three first differing regions, each side cut to ``n`` chars.

    Aligns a small window after each common run; the window grows only while it
    holds no shared character, so cost is small except for near-disjoint texts.
    """
    out: list[dict[str, str]] = []
    window = 4 * n
    i = j = 0
    while len(out) < MAX_SNIPPETS:
        k = _common_prefix(a, b, i, j)
        i, j = i + k, j + k
        if i >= len(a) and j >= len(b):
            break
        equal = _first_equal(a, b, i, j, window)
        if equal is None:
            out.append({"old": a[i : i + n], "new": b[j : j + n]})
            break
        out.append({"old": a[i : i + equal.src_start][:n], "new": b[j : j + equal.dest_start][:n]})
        i, j = i + equal.src_start, j + equal.dest_start
    return out


def _first_equal(a: str, b: str, i: int, j: int, window: int) -> Opcode | None:
    """First equal opcode aligning ``a[i:]`` with ``b[j:]``, growing the window as needed.

    Returns None only when the rest of both texts shares nothing.
    """
    while True:
        ops = Indel.opcodes(a[i : i + window], b[j : j + window])
        equal = next((op for op in ops if op.tag == "equal"), None)
        if equal is not None or (i + window >= len(a) and j + window >= len(b)):
            return equal
        window *= 2


def _common_prefix(a: str, b: str, i: int, j: int) -> int:
    """Length of the common prefix of ``a[i:]`` and ``b[j:]``."""
    k = 0
    step = 1024
    while step:
        while (
            i + k + step <= len(a)
            and j + k + step <= len(b)
            and a[i + k : i + k + step] == b[j + k : j + k + step]
        ):
            k += step
        step //= 2
    return k
