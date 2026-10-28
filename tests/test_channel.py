"""Worker message channel: reader thread queues messages and marks the slot broken on EOF."""

import multiprocessing as mp
from typing import Any

from extractor_tester import channel


def _slot() -> tuple[channel.Slot, Any, channel.Event]:
    msg_r, msg_w = mp.Pipe(duplex=False)
    item_r, item_w = mp.Pipe(duplex=False)
    item_r.close()
    wake = channel.Event()
    proc = mp.get_context("spawn").Process(target=print)
    return channel.open_slot(proc, item_w, msg_r, 0, wake), msg_w, wake


def test_reader_queues_messages_then_marks_broken_on_eof() -> None:
    """Messages arrive in order in s.msgs; closing the sender marks the slot broken."""
    s, w, wake = _slot()
    w.send((-1, None, None, 1))
    w.send((0, None, None, 1))
    w.close()
    assert s.thread is not None
    s.thread.join(5)
    assert list(s.msgs) == [(-1, None, None, 1), (0, None, None, 1)]
    assert s.broken
    assert wake.is_set()
    assert s.conn.closed
    s.close()


def test_corrupt_payload_marks_broken() -> None:
    """Bytes that do not unpickle end the reader with the slot broken."""
    s, w, _ = _slot()
    w.send_bytes(b"\x80\x05not a pickle")
    assert s.thread is not None
    s.thread.join(5)
    assert s.broken
    assert not s.msgs
    w.close()
    s.close()
