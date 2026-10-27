"""Tests for MIME and family helpers."""

from extractor_tester.config import CompareConfig
from extractor_tester.families import family, mime


def cfg() -> CompareConfig:
    """Build a config with a family map."""
    c = CompareConfig(name_field="n", mime_primary_field="a", mime_secondary_field="b")
    c.set_family_map({"application/pdf": "docs"})
    return c


def test_mime_join() -> None:
    """Parts are joined and lowercased."""
    assert mime("Application", "PDF") == "application/pdf"


def test_mime_missing() -> None:
    """Missing or empty part gives None."""
    assert mime(None, "pdf") is None
    assert mime("application", " ") is None


def test_family_unknown() -> None:
    """Missing MIME is unknown."""
    assert family(None, cfg()) == "unknown"


def test_family_unlisted_is_own() -> None:
    """Unlisted MIME is its own family."""
    assert family("Image/PNG", cfg()) == "image/png"


def test_family_listed() -> None:
    """Listed MIME maps to its family."""
    assert family("Application/PDF", cfg()) == "docs"
