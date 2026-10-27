"""Tests for extractor_tester.decode."""

import base64

import pytest

from extractor_tester.decode import InvalidBase64Error, decode_text, normalize, text_hash


def _b64(data: bytes) -> str:
    return base64.b64encode(data).decode("ascii")


def test_round_trip_ascii_and_unicode() -> None:
    """Round trip ascii and unicode."""
    assert decode_text(_b64(b"hello")) == ("hello", 0)
    assert decode_text(_b64("héllo 世界 😀".encode())) == ("héllo 世界 😀", 0)


def test_none_returns_empty() -> None:
    """None returns empty."""
    assert decode_text(None) == ("", 0)


def test_invalid_utf8_counts_sequences() -> None:
    """Invalid utf8 counts sequences."""
    text, count = decode_text(_b64(b"a\xff\xfeb"))
    assert count == 2
    assert text == "a��b"


def test_legit_replacement_char_not_counted() -> None:
    """Legit replacement char not counted."""
    assert decode_text(_b64("a�b".encode())) == ("a�b", 0)


def test_counter_resets_between_calls() -> None:
    """Counter resets between calls."""
    assert decode_text(_b64(b"\xff"))[1] == 1
    assert decode_text(_b64(b"\xff"))[1] == 1
    assert decode_text(_b64(b"ok"))[1] == 0


@pytest.mark.parametrize("value", [123, ["a"], b"aGk=", 1.5])
def test_non_string_raises(value: object) -> None:
    """Non string raises."""
    with pytest.raises(InvalidBase64Error):
        decode_text(value)


@pytest.mark.parametrize("value", ["not base64!", "aGk", "a$Gk="])
def test_malformed_base64_raises(value: str) -> None:
    """Malformed base64 raises."""
    with pytest.raises(InvalidBase64Error):
        decode_text(value)


def test_normalize_collapses_and_trims() -> None:
    """Normalize collapses and trims."""
    assert normalize("  a \t\n b   c  ") == "a b c"
    assert normalize("a\u00a0\u2003b") == "a b"
    assert normalize("   ") == ""


def test_text_hash_empty_is_none() -> None:
    """Text hash empty is none."""
    assert text_hash("", 10) is None


def test_text_hash_nonpositive_limit_is_none() -> None:
    """Text hash nonpositive limit is none."""
    assert text_hash("abc", 0) is None
    assert text_hash("abc", -1) is None


def test_text_hash_whitespace_differences_equal() -> None:
    """Text hash whitespace differences equal."""
    a = normalize("foo   bar\n baz")
    b = normalize(" foo bar baz ")
    assert text_hash(a, 100) == text_hash(b, 100)


def test_text_hash_limit_truncates() -> None:
    """Text hash limit truncates."""
    assert text_hash("abcdXXX", 4) == text_hash("abcdYYY", 4)
    assert text_hash("abcdXXX", 5) != text_hash("abcdYYY", 5)


def test_normalize_unicode_whitespace() -> None:
    """Normalize unicode whitespace."""
    assert normalize("a\u3000b\xa0\u2003c") == "a b c"


def test_text_hash_is_sha256() -> None:
    """Text hash is sha256."""
    expected = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
    assert text_hash("abc", 10) == expected
