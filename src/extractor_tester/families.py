"""MIME composition and type-family lookup."""

from extractor_tester.config import CompareConfig


def mime(primary: str | None, secondary: str | None) -> str | None:
    """Join parts as 'primary/secondary' lowercased; None if either is missing."""
    if primary is None or secondary is None:
        return None
    p, s = primary.strip().lower(), secondary.strip().lower()
    if not p or not s:
        return None
    return f"{p}/{s}"


def family(mime: str | None, config: CompareConfig) -> str:
    """Return the configured family for a MIME, the MIME itself if unlisted, or 'unknown'."""
    if not mime:
        return "unknown"
    key = mime.lower()
    return config.family_map.get(key, key)
