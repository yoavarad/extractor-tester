"""Helpers for the aggregator: bounded lowest-similarity examples and percentile stats."""

from __future__ import annotations

import heapq
import statistics
from typing import TYPE_CHECKING, Any

if TYPE_CHECKING:
    from extractor_tester.models import Example


class Lowest:
    """The `cap` lowest-similarity examples, ordered by (similarity, item id, path)."""

    def __init__(self, cap: int) -> None:
        """Start empty with an example cap."""
        self.cap = cap
        self._heap: list[tuple[float, _Rev, _Rev, int, Example]] = []
        self._seq = 0

    def add(self, sim: float, example: Example) -> None:
        """Offer an example; the heap keeps only the lowest `cap`."""
        self._seq += 1
        path = example.path_new or example.path_old or ""
        # Negate for a max-heap on (sim, item, path) so the worst kept entry pops first.
        key = (-sim, _Rev(example.item_id), _Rev(path), self._seq, example)
        if len(self._heap) < self.cap:
            heapq.heappush(self._heap, key)
        elif self.cap and key[:4] > self._heap[0][:4]:
            heapq.heapreplace(self._heap, key)

    def examples(self) -> list[Example]:
        """Return kept examples, lowest similarity first."""
        ordered = sorted(self._heap, key=lambda t: (-t[0], t[1].s, t[2].s, t[3]))
        return [t[4] for t in ordered]


class _Rev:
    """String wrapper with reversed ordering (for max-heap tie-breaking)."""

    __slots__ = ("s",)

    def __init__(self, s: str) -> None:
        self.s = s

    def __lt__(self, other: _Rev) -> bool:
        return self.s > other.s

    def __gt__(self, other: _Rev) -> bool:
        return self.s < other.s

    def __eq__(self, other: object) -> bool:
        return isinstance(other, _Rev) and self.s == other.s

    def __hash__(self) -> int:
        return hash(self.s)


def stats(values: list[float]) -> dict[str, Any]:
    """Median, 10th percentile and minimum; empty when no data."""
    if not values:
        return {}
    p10 = statistics.quantiles(values, n=10)[0] if len(values) > 1 else values[0]
    return {"median": statistics.median(values), "p10": p10, "min": min(values)}
