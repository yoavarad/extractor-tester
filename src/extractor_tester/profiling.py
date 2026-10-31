"""Lightweight stage timers and the slowest-items list behind RunStats."""

from __future__ import annotations

import heapq
import time

# Stages reported in RunStats.stage_seconds; every one is present, 0.0 when never run.
STAGES = (
    "load",
    "decode",
    "tree",
    "match",
    "field_compare",
    "text_fast",
    "text_full",
    "text_buffered",
    "aggregate",
    "render",
)
TEXT_STAGE = {"fast-path": "text_fast", "full": "text_full", "buffered": "text_buffered"}
SLOWEST_N = 10
_Entry = tuple[float, int, dict[str, object]]

# Per-process accumulators for stages timed deep inside a call; items run one at a time.
_acc: dict[str, float] = {}


def clock() -> float:
    """One clock read; the only timer primitive used by stages."""
    return time.perf_counter()


def add(stage: str, seconds: float) -> None:
    """Accumulate seconds for a stage timed inside the current item."""
    _acc[stage] = _acc.get(stage, 0.0) + seconds


def take(stage: str) -> float:
    """Return and reset a stage's accumulated seconds."""
    return _acc.pop(stage, 0.0)


class Slowest:
    """The N slowest items seen, kept in bounded memory."""

    def __init__(self, n: int = SLOWEST_N) -> None:
        """Start empty."""
        self.n = n
        self._heap: list[_Entry] = []
        self._seq = 0

    def add(self, item_id: str, seconds: float, status: str) -> None:
        """Offer one item; only the n slowest survive."""
        self._seq += 1
        entry: _Entry = (
            seconds,
            -self._seq,
            {"item_id": item_id, "seconds": seconds, "status": status},
        )
        if len(self._heap) < self.n:
            heapq.heappush(self._heap, entry)
        elif entry[:2] > self._heap[0][:2]:
            heapq.heapreplace(self._heap, entry)

    def items(self) -> list[dict[str, object]]:
        """Slowest first."""
        return [e[2] for e in sorted(self._heap, key=lambda e: e[:2], reverse=True)]
