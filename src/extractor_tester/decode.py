"""Base64 text decoding, whitespace normalization and text hashing."""

import base64
import binascii
import codecs
import hashlib
import threading

_ERROR_HANDLER = "extractor_tester_count_invalid"
_local = threading.local()


class InvalidBase64Error(ValueError):
    """Raised when a value is not a string or not strictly valid base64."""


def _count_handler(exc: UnicodeError) -> tuple[str, int]:
    if not isinstance(exc, UnicodeDecodeError):
        raise exc
    _local.count += 1
    return "�", exc.end


codecs.register_error(_ERROR_HANDLER, _count_handler)


def decode_text(value: object) -> tuple[str, int]:
    """Decode base64 ``value`` to UTF-8 text.

    Returns ``(text, invalid_sequence_count)``. ``None`` yields ``("", 0)``.
    Raises ``InvalidBase64Error`` for non-strings or malformed base64.
    """
    if value is None:
        return "", 0
    if not isinstance(value, str):
        raise InvalidBase64Error(f"expected base64 string, got {type(value).__name__}")
    try:
        raw = base64.b64decode(value, validate=True)
    except (binascii.Error, ValueError) as exc:
        raise InvalidBase64Error(str(exc)) from exc
    previous = getattr(_local, "count", 0)
    _local.count = 0
    try:
        text = raw.decode("utf-8", errors=_ERROR_HANDLER)
        return text, _local.count
    finally:
        _local.count = previous


def normalize(text: str) -> str:
    """Collapse Unicode whitespace runs to single spaces and trim."""
    return " ".join(text.split())


def text_hash(normalized: str, limit: int) -> str | None:
    """Return sha256 hex of the first ``limit`` chars of ``normalized``, or None if empty."""
    if not normalized:
        return None
    return hashlib.sha256(normalized[:limit].encode("utf-8")).hexdigest()
