"""Tests for the matcher walk."""

import subprocess
import sys

from extractor_tester.matching.matcher import default_tiers, match_item
from extractor_tester.matching.tiers import NameTier, TierResult
from extractor_tester.models import FilePair, FileRecord, Item, Side


def rec(
    fid: str,
    name: str,
    *children: FileRecord,
    family: str = "pdf",
    text_hash: str | None = None,
) -> FileRecord:
    """Build a FileRecord with children."""
    return FileRecord(
        file_id=fid,
        parent_id="P",
        index=0,
        name=name,
        mime="application/pdf",
        family=family,
        path=name,
        fields={},
        text="",
        text_hash=text_hash,
        decode_errors=0,
        children=list(children),
        missing_fields=[],
    )


def item(side: Side, root: FileRecord) -> Item:
    """Wrap a root in an Item."""
    return Item(item_id="I", side=side, metadata={}, files=[root], source_path="")


def summary(pairs: list[FilePair]) -> list[tuple]:
    """Compact view of pairs."""
    return [
        (
            p.kind,
            p.old.file_id if p.old else None,
            p.new.file_id if p.new else None,
            p.tier,
            p.unmatched_reason,
        )
        for p in pairs
    ]


def test_roots_pair_with_tier_root() -> None:
    """Roots pair with tier root."""
    pairs = match_item(item("old", rec("O", "root.eml")), item("new", rec("N", "other.eml")))
    assert summary(pairs) == [("paired", "O", "N", "root", None)]


def test_children_pair_and_leftovers_have_no_candidate() -> None:
    """Children pair and leftovers have no candidate."""
    old = rec("O", "r", rec("o1", "a"), rec("o2", "b", family="image"))
    new = rec("N", "r", rec("n1", "a"), rec("n2", "c", family="doc"))
    pairs = match_item(item("old", old), item("new", new))
    assert summary(pairs) == [
        ("paired", "O", "N", "root", None),
        ("paired", "o1", "n1", "name", None),
        ("only-in-old", "o2", None, None, "no-candidate"),
        ("only-in-new", None, "n2", None, "no-candidate"),
    ]


def test_same_name_empty_text_siblings_pair_by_position() -> None:
    """Same name empty text siblings pair by position."""
    old = rec("O", "r", rec("o1", "img.png"), rec("o2", "img.png"))
    new = rec("N", "r", rec("n1", "img.png"), rec("n2", "img.png"))
    pairs = match_item(item("old", old), item("new", new))
    assert [(p.tier, p.tie_break) for p in pairs[1:]] == [("name", "position")] * 2
    assert summary(pairs)[1:] == [
        ("paired", "o1", "n1", "name", None),
        ("paired", "o2", "n2", "name", None),
    ]


def test_descendants_of_unpaired_parent_are_parent_unpaired() -> None:
    """Descendants of unpaired parent are parent unpaired."""
    old = rec("O", "r", rec("o1", "a", rec("o11", "deep", rec("o111", "deeper")), family="x"))
    new = rec("N", "r", rec("n1", "b", rec("n11", "deep"), family="y"))
    pairs = match_item(item("old", old), item("new", new))
    assert summary(pairs)[1:] == [
        ("only-in-old", "o1", None, None, "no-candidate"),
        ("only-in-old", "o11", None, None, "parent-unpaired"),
        ("only-in-old", "o111", None, None, "parent-unpaired"),
        ("only-in-new", None, "n1", None, "no-candidate"),
        ("only-in-new", None, "n11", None, "parent-unpaired"),
    ]


def test_paired_children_are_walked_in_turn() -> None:
    """Paired children are walked in turn."""
    old = rec("O", "r", rec("o1", "a", rec("o11", "x")))
    new = rec("N", "r", rec("n1", "a", rec("n11", "x")))
    pairs = match_item(item("old", old), item("new", new))
    assert summary(pairs)[2] == ("paired", "o11", "n11", "name", None)


class FamilyFake:
    """Fake tier pairing the first child of each side."""

    name = "type-family"

    def pair(self, old_children: list[FileRecord], new_children: list[FileRecord]) -> TierResult:
        """Pair heads, pass the rest."""
        if not old_children or not new_children:
            return [], old_children, new_children
        p = FilePair(kind="paired", old=old_children[0], new=new_children[0], tier="type-family")
        return [p], old_children[1:], new_children[1:]


def test_custom_tier_list_labels_pairs() -> None:
    """Custom tier list labels pairs."""
    old = rec("O", "r", rec("o1", "a"))
    new = rec("N", "r", rec("n1", "b"))
    pairs = match_item(item("old", old), item("new", new), [NameTier(), FamilyFake()])
    assert summary(pairs)[1] == ("paired", "o1", "n1", "type-family", None)


def test_default_tiers_order() -> None:
    """Default tiers: name, text-hash, type-family."""
    assert [t.name for t in default_tiers()] == ["name", "text-hash", "type-family"]


def test_worked_example() -> None:
    """The matching.md worked example."""
    old = rec(
        "O",
        "root.eml",
        rec("o1", "invoice.pdf", text_hash="h"),
        rec("o2", "image001.png", family="image"),
        rec("o3", "image001.png", family="image"),
    )
    new = rec(
        "N",
        "root.eml",
        rec("n1", "Invoice 2026.pdf", text_hash="h"),
        rec("n2", "image001.png", family="image"),
        rec("n3", "image001.png", family="image"),
        rec("n4", "logo.png", family="image"),
    )
    pairs = match_item(item("old", old), item("new", new))
    assert summary(pairs) == [
        ("paired", "O", "N", "root", None),
        ("paired", "o2", "n2", "name", None),
        ("paired", "o3", "n3", "name", None),
        ("paired", "o1", "n1", "text-hash", None),
        ("only-in-new", None, "n4", None, "no-candidate"),
    ]


def test_matching_does_not_import_compare_or_render() -> None:
    """Matching does not import compare or render."""
    code = (
        "import sys, extractor_tester.matching.matcher, extractor_tester.matching.tiers;"
        "bad=[m for m in sys.modules if m.startswith(('extractor_tester.compare',"
        "'extractor_tester.render'))];print(bad);sys.exit(1 if bad else 0)"
    )
    result = subprocess.run([sys.executable, "-c", code], capture_output=True, text=True)
    assert result.returncode == 0, result.stdout + result.stderr
