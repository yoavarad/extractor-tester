"""Tests for matching tiers."""

from extractor_tester.matching.tiers import MatchTier, NameTier
from extractor_tester.models import FilePair, FileRecord


def rec(
    fid: str, name: str, index: int = 0, text_hash: str | None = None, family: str = "pdf"
) -> FileRecord:
    """Build a bare FileRecord."""
    return FileRecord(
        file_id=fid,
        parent_id="P",
        index=index,
        name=name,
        mime="application/pdf",
        family=family,
        path=f"r/{name}",
        fields={},
        text="",
        text_hash=text_hash,
        decode_errors=0,
        children=[],
        missing_fields=[],
    )


def ids(pairs: list[FilePair]) -> list[tuple]:
    """Return (old id, new id, tie_break) per pair."""
    return [(p.old and p.old.file_id, p.new and p.new.file_id, p.tie_break) for p in pairs]


def test_name_tier_is_a_match_tier() -> None:
    """Name tier is a match tier."""
    tier: MatchTier = NameTier()
    assert tier.name == "name"


def test_unique_names_pair_without_tie_break() -> None:
    """Unique names pair without tie break."""
    pairs, old_rest, new_rest = NameTier().pair(
        [rec("o1", "a"), rec("o2", "b")], [rec("n1", "b"), rec("n2", "a"), rec("n3", "c")]
    )
    assert ids(pairs) == [("o1", "n2", "none"), ("o2", "n1", "none")]
    assert all(p.tier == "name" and p.kind == "paired" for p in pairs)
    assert old_rest == []
    assert [r.file_id for r in new_rest] == ["n3"]


def test_names_are_case_sensitive() -> None:
    """Names are case sensitive."""
    pairs, old_rest, new_rest = NameTier().pair([rec("o1", "A")], [rec("n1", "a")])
    assert pairs == []
    assert len(old_rest) == 1
    assert len(new_rest) == 1


def test_empty_names_never_pair() -> None:
    """Empty names never pair."""
    pairs, old_rest, new_rest = NameTier().pair([rec("o1", "")], [rec("n1", "")])
    assert pairs == []
    assert [r.file_id for r in old_rest] == ["o1"]
    assert [r.file_id for r in new_rest] == ["n1"]


def test_same_name_empty_text_pairs_by_position() -> None:
    """Same name empty text pairs by position."""
    pairs, _, _ = NameTier().pair(
        [rec("o1", "x", 1), rec("o2", "x", 2)], [rec("n1", "x", 1), rec("n2", "x", 2)]
    )
    assert ids(pairs) == [("o1", "n1", "position"), ("o2", "n2", "position")]


def test_same_name_text_hash_breaks_tie_before_position() -> None:
    """Same name text hash breaks tie before position."""
    old = [rec("o1", "x", text_hash="h1"), rec("o2", "x", text_hash="h2")]
    new = [rec("n1", "x", text_hash="h2"), rec("n2", "x", text_hash="h1")]
    pairs, _, _ = NameTier().pair(old, new)
    assert ids(pairs) == [("o1", "n2", "text-hash"), ("o2", "n1", "text-hash")]


def test_hash_tie_break_requires_same_family() -> None:
    """Hash tie break requires same family."""
    old = [rec("o1", "x", text_hash="h", family="pdf"), rec("o2", "x")]
    new = [rec("n1", "x"), rec("n2", "x", text_hash="h", family="image")]
    pairs, _, _ = NameTier().pair(old, new)
    assert ids(pairs) == [("o1", "n1", "position"), ("o2", "n2", "position")]


def test_extra_same_name_files_pass_on_in_order() -> None:
    """Extra same name files pass on in order."""
    old = [rec("o1", "x"), rec("o2", "y")]
    new = [rec("n1", "x"), rec("n2", "z"), rec("n3", "x"), rec("n4", "x")]
    pairs, old_rest, new_rest = NameTier().pair(old, new)
    assert ids(pairs) == [("o1", "n1", "position")]
    assert [r.file_id for r in old_rest] == ["o2"]
    assert [r.file_id for r in new_rest] == ["n2", "n3", "n4"]
