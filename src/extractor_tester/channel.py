"""Worker message channel: one reader thread per worker pipe, so the main loop never blocks."""

from __future__ import annotations

import threading
import time
from collections import deque
from dataclasses import dataclass, field
from threading import Event
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from multiprocessing.connection import _ConnectionBase as Connection  # Pipe end, any OS
    from multiprocessing.process import BaseProcess

__all__ = ["SEND_GRACE_SECONDS", "Event", "Slot", "clock", "open_slot"]

# A message being received for longer than this is a stalled sender: judge the worker anyway.
SEND_GRACE_SECONDS = 5.0
clock = time.monotonic


@dataclass
class Slot:
    """One worker process, its pipes and what the pool knows about it."""

    proc: BaseProcess
    inbox: Connection  # parent -> worker items
    conn: Connection  # worker -> parent messages; only the reader thread uses and closes it
    started: float
    startup_failures: int = 0
    ready: bool = False
    taken: bool = False  # the worker acked the item it holds
    proven: bool = False  # the worker has taken at least one item
    broken: bool = False  # the message channel failed (EOF, corrupt payload)
    busy_since: float | None = None  # a message is being received since then
    dead_since: float | None = None  # first seen dead while its reader still ran
    msgs: deque = field(default_factory=deque)
    thread: threading.Thread | None = None

    def close(self) -> None:
        """Close the item pipe; the reader closes the message pipe when it reaches EOF."""
        self.inbox.close()


def recv(conn: Connection) -> object:
    return conn.recv()


def deliver(s: Slot, msg: object, wake: Event) -> None:
    s.msgs.append(msg)
    wake.set()


def _reader(s: Slot, wake: Event) -> None:
    """Move messages from the worker's pipe to s.msgs; on EOF or failure mark s broken."""
    try:
        while s.conn.poll(None):
            s.busy_since = clock()
            deliver(s, recv(s.conn), wake)
            s.busy_since = None
    except Exception:  # EOF, broken pipe or any corrupt payload
        pass
    s.busy_since = None  # nothing more is arriving
    s.conn.close()
    s.broken = True
    wake.set()


def open_slot(
    proc: BaseProcess, inbox: Connection, conn: Connection, startup_failures: int, wake: Event
) -> Slot:
    """Build the slot for a started worker and start its reader thread."""
    s = Slot(proc, inbox, conn, clock(), startup_failures)
    s.thread = threading.Thread(target=_reader, args=(s, wake), daemon=True)
    s.thread.start()
    return s
