"""Regenerate matcher_cases.json from the Python matcher and tiers.

Run from the repository root:

    uv run python dotnet/tests/ExtractorTester.Tests/Fixtures/Matching/gen_matcher_fixtures.py

Each case mirrors one test in tests/test_matcher.py or tests/test_tiers.py. Inputs and every
resulting FilePair are stored as pydantic model_dump_json() strings so the C# port can compare
bytes.
"""

import importlib
import json
import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[5]
sys.path[:0] = [str(ROOT), str(ROOT / "src")]

matcher = importlib.import_module("extractor_tester.matching.matcher")
tiers = importlib.import_module("extractor_tester.matching.tiers")
tm = importlib.import_module("tests.test_matcher")
tt = importlib.import_module("tests.test_tiers")

OUT = Path(__file__).parent / "matcher_cases.json"


def match_case(name: str, old: Any, new: Any) -> dict[str, Any]:
    """Run match_item with default tiers over two roots."""
    o, n = tm.item("old", old), tm.item("new", new)
    pairs = matcher.match_item(o, n)
    return {
        "name": name,
        "tier": "match",
        "old": o.model_dump_json(),
        "new": n.model_dump_json(),
        "pairs": [p.model_dump_json() for p in pairs],
    }


def tier_case(name: str, tier: Any, old: list[Any], new: list[Any]) -> dict[str, Any]:
    """Run one tier over two child lists."""
    pairs, old_rest, new_rest = tier.pair(old, new)
    return {
        "name": name,
        "tier": tier.name,
        "old": [r.model_dump_json() for r in old],
        "new": [r.model_dump_json() for r in new],
        "pairs": [p.model_dump_json() for p in pairs],
        "old_rest": [r.file_id for r in old_rest],
        "new_rest": [r.file_id for r in new_rest],
    }


def cases() -> list[dict[str, Any]]:
    """All fixture cases."""
    r, t = tm.rec, tt.rec
    name, th, fam = tiers.NameTier(), tiers.TextHashTier(), tiers.TypeFamilyTier()
    return [
        match_case("roots_pair_with_tier_root", r("O", "root.eml"), r("N", "other.eml")),
        match_case(
            "children_pair_and_leftovers_have_no_candidate",
            r("O", "r", r("o1", "a"), r("o2", "b", family="image")),
            r("N", "r", r("n1", "a"), r("n2", "c", family="doc")),
        ),
        match_case(
            "same_name_empty_text_siblings_pair_by_position",
            r("O", "r", r("o1", "img.png"), r("o2", "img.png")),
            r("N", "r", r("n1", "img.png"), r("n2", "img.png")),
        ),
        match_case(
            "descendants_of_unpaired_parent_are_parent_unpaired",
            r("O", "r", r("o1", "a", r("o11", "deep", r("o111", "deeper")), family="x")),
            r("N", "r", r("n1", "b", r("n11", "deep"), family="y")),
        ),
        match_case(
            "paired_children_are_walked_in_turn",
            r("O", "r", r("o1", "a", r("o11", "x"))),
            r("N", "r", r("n1", "a", r("n11", "x"))),
        ),
        match_case(
            "worked_example",
            r(
                "O",
                "root.eml",
                r("o1", "invoice.pdf", text_hash="h"),
                r("o2", "image001.png", family="image"),
                r("o3", "image001.png", family="image"),
            ),
            r(
                "N",
                "root.eml",
                r("n1", "Invoice 2026.pdf", text_hash="h"),
                r("n2", "image001.png", family="image"),
                r("n3", "image001.png", family="image"),
                r("n4", "logo.png", family="image"),
            ),
        ),
        tier_case(
            "unique_names_pair_without_tie_break",
            name,
            [t("o1", "a"), t("o2", "b")],
            [t("n1", "b"), t("n2", "a"), t("n3", "c")],
        ),
        tier_case("names_are_case_sensitive", name, [t("o1", "A")], [t("n1", "a")]),
        tier_case("empty_names_never_pair", name, [t("o1", "")], [t("n1", "")]),
        tier_case(
            "same_name_empty_text_pairs_by_position",
            name,
            [t("o1", "x", 1), t("o2", "x", 2)],
            [t("n1", "x", 1), t("n2", "x", 2)],
        ),
        tier_case(
            "same_name_text_hash_breaks_tie_before_position",
            name,
            [t("o1", "x", text_hash="h1"), t("o2", "x", text_hash="h2")],
            [t("n1", "x", text_hash="h2"), t("n2", "x", text_hash="h1")],
        ),
        tier_case(
            "hash_tie_break_requires_same_family",
            name,
            [t("o1", "x", text_hash="h", family="pdf"), t("o2", "x")],
            [t("n1", "x"), t("n2", "x", text_hash="h", family="image")],
        ),
        tier_case(
            "extra_same_name_files_pass_on_in_order",
            name,
            [t("o1", "x"), t("o2", "y")],
            [t("n1", "x"), t("n2", "z"), t("n3", "x"), t("n4", "x")],
        ),
        tier_case(
            "text_hash_tier_pairs_unique_hash_and_family",
            th,
            [t("o1", "a", text_hash="h"), t("o2", "b", text_hash="k")],
            [t("n1", "c", text_hash="k"), t("n2", "d", text_hash="z")],
        ),
        tier_case("text_hash_tier_none_hash_never_pairs", th, [t("o1", "a")], [t("n1", "b")]),
        tier_case(
            "text_hash_tier_requires_same_family",
            th,
            [t("o1", "a", text_hash="h", family="pdf")],
            [t("n1", "b", text_hash="h", family="image")],
        ),
        tier_case(
            "text_hash_tier_ambiguous_not_paired",
            th,
            [t("o1", "a", text_hash="h"), t("o2", "b", text_hash="h")],
            [t("n1", "c", text_hash="h")],
        ),
        tier_case(
            "type_family_tier_pairs_single_per_family",
            fam,
            [t("o1", "a", family="pdf"), t("o2", "b", family="image")],
            [t("n1", "c", family="image"), t("n2", "d", family="pdf")],
        ),
        tier_case(
            "type_family_tier_two_same_family_on_one_side_not_paired",
            fam,
            [t("o1", "a"), t("o2", "b")],
            [t("n1", "c")],
        ),
    ]


def main() -> None:
    """Write the fixture next to this script."""
    text = json.dumps(cases(), indent=2, ensure_ascii=False) + "\n"
    OUT.write_bytes(text.encode("utf-8"))


if __name__ == "__main__":
    main()
