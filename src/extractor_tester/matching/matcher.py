"""Walk two file trees top-down from paired roots, running the tier list per parent pair."""

from collections import deque
from typing import Literal

from extractor_tester.matching.tiers import MatchTier, NameTier
from extractor_tester.models import FilePair, FileRecord, Item, Side


def default_tiers() -> list[MatchTier]:
    """Return the configured tier order."""
    return [NameTier()]


def match_item(old: Item, new: Item, tiers: list[MatchTier] | None = None) -> list[FilePair]:
    """Pair roots (tier root), then per paired parent run tiers over unpaired children."""
    tier_list = default_tiers() if tiers is None else tiers
    root = FilePair(kind="paired", old=old.files[0], new=new.files[0], tier="root")
    out = [root]
    queue = deque([root])
    while queue:
        parent = queue.popleft()
        assert parent.old is not None
        assert parent.new is not None
        old_rest, new_rest = list(parent.old.children), list(parent.new.children)
        for tier in tier_list:
            pairs, old_rest, new_rest = tier.pair(old_rest, new_rest)
            out.extend(pairs)
            queue.extend(pairs)
        for rec in old_rest:
            _one_sided(rec, "old", out)
        for rec in new_rest:
            _one_sided(rec, "new", out)
    return out


def _one_sided(rec: FileRecord, side: Side, out: list[FilePair]) -> None:
    """Append rec as no-candidate and its whole subtree as parent-unpaired, pre-order."""
    stack: list[tuple[FileRecord, Literal["no-candidate", "parent-unpaired"]]] = [
        (rec, "no-candidate")
    ]
    while stack:
        cur, reason = stack.pop()
        if side == "old":
            out.append(FilePair(kind="only-in-old", old=cur, unmatched_reason=reason))
        else:
            out.append(FilePair(kind="only-in-new", new=cur, unmatched_reason=reason))
        stack.extend((c, "parent-unpaired") for c in reversed(cur.children))
