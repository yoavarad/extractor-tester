"""Matching tiers: the MatchTier contract and the name tier."""

from typing import Literal, Protocol

from extractor_tester.models import FilePair, FileRecord

TierResult = tuple[list[FilePair], list[FileRecord], list[FileRecord]]
_Match = tuple[FileRecord, FileRecord, Literal["none", "text-hash", "position"]]


class MatchTier(Protocol):
    """One matching rule run on the unpaired children of a parent pair."""

    @property
    def name(self) -> str:
        """Tier label recorded on FilePair.tier."""
        ...

    def pair(self, old_children: list[FileRecord], new_children: list[FileRecord]) -> TierResult:
        """Pair some children one-to-one; return pairs, old rest, new rest (input order kept)."""
        ...


class NameTier:
    """Tier 1: identical, non-empty, case-sensitive file name."""

    name = "name"

    def pair(self, old_children: list[FileRecord], new_children: list[FileRecord]) -> TierResult:
        """Pair same-name siblings; ties by text hash + family, then position."""
        new_by_name: dict[str, list[FileRecord]] = {}
        for n in new_children:
            if n.name:
                new_by_name.setdefault(n.name, []).append(n)
        old_by_name: dict[str, list[FileRecord]] = {}
        for o in old_children:
            if o.name in new_by_name:
                old_by_name.setdefault(o.name, []).append(o)

        pairs: list[FilePair] = []
        used: set[int] = set()  # id() of paired records
        for o in old_children:
            group = old_by_name.pop(o.name, None)
            if group is not None:
                for a, b, tb in _pair_group(group, new_by_name[o.name]):
                    pairs.append(FilePair(kind="paired", old=a, new=b, tier="name", tie_break=tb))
                    used.update((id(a), id(b)))
        old_rest = [o for o in old_children if id(o) not in used]
        new_rest = [n for n in new_children if id(n) not in used]
        return pairs, old_rest, new_rest


def _pair_group(old: list[FileRecord], new: list[FileRecord]) -> list[_Match]:
    """Pair one same-name group; extras stay unpaired."""
    if len(old) == 1 and len(new) == 1:
        return [(old[0], new[0], "none")]
    by_hash: dict[tuple[str, str], list[int]] = {}
    for i, n in enumerate(new):
        if n.text_hash is not None:
            by_hash.setdefault((n.text_hash, n.family), []).append(i)
    out: list[_Match] = []
    taken: set[int] = set()
    old_left: list[FileRecord] = []
    for o in old:
        slots = by_hash.get((o.text_hash, o.family)) if o.text_hash is not None else None
        if slots:
            i = slots.pop(0)
            taken.add(i)
            out.append((o, new[i], "text-hash"))
        else:
            old_left.append(o)
    new_left = [n for i, n in enumerate(new) if i not in taken]
    out.extend((o, n, "position") for o, n in zip(old_left, new_left, strict=False))
    return out
