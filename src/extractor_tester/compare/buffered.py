"""Buffered text comparison for long texts: block alignment, then local character diffs."""

from __future__ import annotations

from difflib import SequenceMatcher
from typing import TYPE_CHECKING, Any

from rapidfuzz.distance import Indel

from extractor_tester.decode import normalize

if TYPE_CHECKING:
    from extractor_tester.config import CompareConfig

MAX_SNIPPETS = 3


def split_blocks(text: str, max_block_chars: int) -> tuple[list[str], int, int]:
    """Split ``text`` into normalized line blocks; cut long lines into several blocks.

    Returns (blocks, non-empty line count, cut line count).
    """
    blocks: list[str] = []
    total = cut = 0
    for line in text.splitlines():
        norm = normalize(line)
        if not norm:
            continue
        total += 1
        if len(norm) > max_block_chars:
            cut += 1
        blocks.extend(norm[i : i + max_block_chars] for i in range(0, len(norm), max_block_chars))
    return blocks, total, cut


def compare_buffered(old: str, new: str, config: CompareConfig) -> dict[str, Any]:
    """Align line blocks of two raw texts and measure how much text they share."""
    a, total_a, cut_a = split_blocks(old, config.max_block_chars)
    b, total_b, cut_b = split_blocks(new, config.max_block_chars)
    ids: dict[str, int] = {}
    ha = [ids.setdefault(x, len(ids)) for x in a]
    hb = [ids.setdefault(x, len(ids)) for x in b]
    la, lb = sum(map(len, a)), sum(map(len, b))
    matched = oversized = 0
    snippets: list[dict[str, str]] = []
    n = config.snippet_chars
    for tag, i1, i2, j1, j2 in SequenceMatcher(None, ha, hb, autojunk=False).get_opcodes():
        if tag == "equal":
            matched += sum(map(len, a[i1:i2]))
            continue
        if len(snippets) < MAX_SNIPPETS:
            snippets.append({"old": " ".join(a[i1:i2])[:n], "new": " ".join(b[j1:j2])[:n]})
        if tag != "replace":
            continue
        run_a, run_b = a[i1:i2], b[j1:j2]
        if max(sum(map(len, run_a)), sum(map(len, run_b))) <= config.full_compare_max_chars:
            matched += _lcs("".join(run_a), "".join(run_b))
        else:
            oversized += 1
            matched += sum(_lcs(x, y) for x, y in zip(run_a, run_b, strict=False))
    total, cut = total_a + total_b, cut_a + cut_b
    return {
        "edit_similarity": 2 * matched / (la + lb) if la + lb else 1.0,
        "old_found_in_new": matched / la if la else 1.0,
        "new_found_in_old": matched / lb if lb else 1.0,
        "snippets": snippets,
        "lines_total": total,
        "lines_cut": cut,
        "lines_cut_pct": 100 * cut / total if total else 0.0,
        "oversized_runs": oversized,
    }


def _lcs(x: str, y: str) -> int:
    """Length of the longest common subsequence of two strings."""
    return (len(x) + len(y) - Indel.distance(x, y)) // 2
