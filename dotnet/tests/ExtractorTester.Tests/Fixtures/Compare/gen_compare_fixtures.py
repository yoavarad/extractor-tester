"""Regenerate compare_cases.json from the Python text, field and item comparers.

Run from the repository root:

    uv run python dotnet/tests/ExtractorTester.Tests/Fixtures/Compare/gen_compare_fixtures.py

Text and field cases mirror tests/test_text.py, tests/test_buffered.py and
tests/test_compare_fields.py. Item cases mirror tests/test_item.py plus error-mapping cases
(flatten collision, int digit limit, nesting limit, non-string text). Outputs are pydantic
model_dump_json() strings with timings zeroed (stage_seconds keys kept) so the C# port can
compare bytes.
"""

import importlib
import json
import sys
import tempfile
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[5]
sys.path[:0] = [str(ROOT), str(ROOT / "src")]

text_mod = importlib.import_module("extractor_tester.compare.text")
fields_mod = importlib.import_module("extractor_tester.compare.fields")
item_mod = importlib.import_module("extractor_tester.compare.item")
helpers = importlib.import_module("tests.helpers")
tt = importlib.import_module("tests.test_text")
tb = importlib.import_module("tests.test_buffered")
tf = importlib.import_module("tests.test_compare_fields")
ti = importlib.import_module("tests.test_item")

OUT = Path(__file__).parent / "compare_cases.json"


def text_case(name: str, old: str, new: str, **cfg: Any) -> dict[str, Any]:
    """Run compare_text; seconds zeroed."""
    facts = text_mod.compare_text(old, new, tt._cfg(**cfg))
    return {
        "name": name,
        "old": old,
        "new": new,
        "config": cfg,
        "facts": facts.model_copy(update={"seconds": 0.0}).model_dump_json(),
    }


def text_cases() -> list[dict[str, Any]]:
    """Inputs of every compare_text call in test_text.py and test_buffered.py."""
    old = tt.OLD
    blocks_old = " ".join(f"block{i} " + "x" * 50 for i in range(10))
    blocks_new = " ".join(f"other{i} " + "y" * 50 for i in range(10))
    lines = "\n".join(tb._lines(900))
    edited = "\n".join(tb._edit(tb._lines(900)))
    words = " ".join(f"word{i}" for i in range(2000))
    l200 = "\n".join(tb._lines(200))
    small = tb._lines(200)
    small[50] = "completely different content here"
    return [
        text_case("identical_fast_path", old, old),
        text_case(
            "whitespace_only_change",
            old,
            "  The quick\n brown\tfox jumps over the lazy dog near the river bank today.",
        ),
        text_case("both_empty", "", "  "),
        text_case("appended_old_within_new", old, old + " And then it slept."),
        text_case("truncated_new_within_old", old, old[:40]),
        text_case(
            "containment_default_overlapping",
            old,
            old.replace("lazy", "sleepy") + " More words here.",
        ),
        text_case(
            "containment_80_old_within_new",
            old,
            old.replace("lazy", "sleepy") + " More words here.",
            containment_pct=80,
        ),
        text_case(
            "partially_shared_overlapping",
            old,
            "A quick red fox ran under the bridge yesterday evening.",
        ),
        text_case("no_shared_disjoint", "alpha beta gamma", "xyz qrs tuv"),
        text_case("empty_old", "", old),
        text_case("empty_new", old, ""),
        text_case("snippets_capped", blocks_old, blocks_new, snippet_chars=20),
        text_case(
            "snippets_in_order",
            "one two three four five six seven eight",
            "one TWO three four FIVE six seven EIGHT",
        ),
        text_case("long_snippet_cut", "a" * 500, "b" * 500, snippet_chars=30),
        text_case("region_longer_than_window", "Z" * 2000 + " TAIL", "TAIL"),
        text_case("over_threshold_equal", "a" * 101, "a" * 101, full_compare_max_chars=100),
        text_case("counts_and_timing", "abc", "abcd"),
        text_case("non_bmp_code_points", "a\U0001f600b c", "a\U0001f601b c d"),
        text_case("line_edits_full", lines, edited),
        text_case("line_edits_buffered", lines, edited, full_compare_max_chars=1000),
        text_case(
            "no_line_breaks_cut",
            words,
            words.replace("word1000", "changed"),
            full_compare_max_chars=1000,
            max_block_chars=500,
        ),
        text_case("uncut_lines", l200, l200 + "\nextra", full_compare_max_chars=1000),
        text_case(
            "oversized_replaced_run",
            "\n".join(f"old line number {i} filler" for i in range(100)),
            "\n".join(f"new line number {i} filler" for i in range(100)),
            full_compare_max_chars=1000,
        ),
        text_case("small_replaced_run", l200, "\n".join(small), full_compare_max_chars=1000),
        text_case("buffered_one_side_empty", "", l200, full_compare_max_chars=1000),
        text_case(
            "wrapped_vs_unwrapped",
            "\n".join("x" for _ in range(1000)),
            " ".join("x" for _ in range(1000)) + " tail",
            full_compare_max_chars=500,
        ),
    ]


def field_case(name: str, old: dict[str, Any], new: dict[str, Any], **cfg: Any) -> dict[str, Any]:
    """Run fields.diff; inputs stored as JSON text so number types survive."""
    diffs = fields_mod.diff(old, new, tf.cfg(**cfg))
    return {
        "name": name,
        "old": json.dumps(old),
        "new": json.dumps(new),
        "config": cfg,
        "diffs": [d.model_dump_json() for d in diffs],
    }


def field_cases() -> list[dict[str, Any]]:
    """Inputs of every diff call in test_compare_fields.py, plus big-number cases."""
    big = 2**70
    return [
        field_case("number_equality", {"x": 1, "y": True, "z": 0}, {"x": 1.0, "y": 1, "z": False}),
        field_case("null_is_value", {"a": None, "b": None}, {"c": None, "b": None}),
        field_case("type_change", {"a": 1}, {"a": "1"}),
        field_case(
            "lists_whole_and_nested",
            {"l": [1, 2], "m": [True], "o": {}},
            {"l": [1.0, 2], "m": [1], "o": {}},
        ),
        field_case(
            "ignored_structural_sorted",
            {"skip": 1, "n": "x", "mp": "m", "ms": "t", "text": "t", "z": 1, "meta.k": 1},
            {"skip": 2, "n": "y", "mp": "m", "ms": "t", "text": "u", "z": 1, "meta.k": 2},
        ),
        field_case("truncates_strings", {"a": "x" * 500}, {"a": "y"}, snippet_chars=20),
        field_case("nan_same", {"a": float("nan")}, {"a": float("nan")}),
        field_case(
            "big_ints_and_floats",
            {"a": big, "b": big, "c": 0.5, "d": {"k": [1, {"z": 2}]}, "Ã©": 1, "Z": 1},
            {"a": float(big), "b": big + 1, "c": 0.5, "d": {"k": [1, {"z": 2.0}]}, "Ã©": 2},
        ),
    ]


def raw_item(dir: Path, item_id: str, data: str) -> Path:
    """Write an item file with exact text."""
    path = dir / f"{item_id}.json"
    path.write_text(data, encoding="utf-8")
    return path


def _zero_facts(p: Any) -> str:
    if p.text is not None:
        p = p.model_copy(update={"text": p.text.model_copy(update={"seconds": 0.0})})
    return str(p.model_dump_json())


def item_case(name: str, old_path: Path, new_path: Path) -> dict[str, Any]:
    """Run compare_item; timings zeroed, stage keys kept."""
    cfg = ti.CFG
    result, facts = item_mod.compare_item(old_path, new_path, cfg)
    result = result.model_copy(
        update={"seconds": 0.0, "stage_seconds": dict.fromkeys(result.stage_seconds, 0.0)}
    )
    return {
        "name": name,
        "old_name": old_path.name,
        "new_name": new_path.name,
        "old_side": old_path.parent.name,
        "new_side": new_path.parent.name,
        "old_file": old_path.read_text(encoding="utf-8"),
        "new_file": new_path.read_text(encoding="utf-8"),
        "result": result.model_dump_json(),
        "facts": [_zero_facts(p) for p in facts],
    }


def item_cases(tmp: Path) -> list[dict[str, Any]]:
    """Every compare_item scenario of test_item.py plus error-mapping cases."""
    f = ti.f
    out: list[dict[str, Any]] = []
    counter = iter(range(1000))

    def sides(old_files: list[Any], new_files: list[Any], om: Any = None, nm: Any = None) -> Any:
        d = tmp / str(next(counter))
        d.mkdir()
        return ti.sides(d, old_files, new_files, om, nm)

    def raw_sides(old_data: str, new_data: str) -> tuple[Path, Path]:
        d = tmp / str(next(counter))
        (d / "old").mkdir(parents=True)
        (d / "new").mkdir()
        return raw_item(d / "old", "I", old_data), raw_item(d / "new", "I", new_data)

    root = f("I", "I", "root")
    o, n = sides(
        [root, f("a", "I", "a.pdf"), f("gone", "I", "gone.pdf")],
        [root, f("a", "I", "a.pdf"), f("new", "I", "new.pdf", ms="docx")],
    )
    out.append(item_case("one_fact_per_paired_and_one_sided_file", o, n))
    o, n = sides(
        [root, f("a", "I", "a.pdf", author="x"), f("b", "I", "b.pdf", author="y")],
        [root, f("a", "I", "a.pdf", author="x"), f("b", "I", "b.pdf", author="z")],
    )
    out.append(item_case("metadata_change_on_right_pair", o, n))
    o, n = sides([root], [root], {"lang": "en", "k": 1}, {"lang": "fr", "k": 1})
    out.append(item_case("item_metadata_diffs", o, n))
    o, n = sides(
        [f("I", "I", "root"), f("a", "I", "a.x", ms="pdf")],
        [f("I", "I", "root"), f("a", "I", "a.x", ms="docx")],
    )
    out.append(item_case("mime_change", o, n))
    o, n = sides([root], [root])
    n.write_text("not json", encoding="utf-8")
    out.append(item_case("loader_error_keeps_code", o, n))
    big = f("I", "I", "root", text="secret-body " * 500)
    o, n = sides([big], [big])
    out.append(item_case("facts_never_contain_full_text", o, n))
    bad = f("a", "I", "a.pdf")
    del bad["name"]
    o, n = sides([root, bad], [root, f("a", "I", "a.pdf")])
    out.append(item_case("file_level_error_attached", o, n))
    o, n = sides([root, f("orph", "nowhere", "o.pdf")], [root])
    out.append(item_case("unattached_build_warnings", o, n))
    o, n = sides(
        [f("I", "I", "root"), f("a", "I", "a.pdf", text="alpha beta")],
        [
            f("I", "I", "root"),
            f("a", "I", "a.pdf", text="alpha gamma"),
            f("b", "I", "b.docx", ms="docx"),
        ],
    )
    out.append(item_case("paired_text_facts", o, n))
    o, n = sides(
        [f("I", "I", "root"), f("a", "I", "a.pdf", title="x")],
        [f("I", "I", "root"), f("a", "I", "a.pdf", title="y")],
    )
    n.write_text(n.read_text(encoding="utf-8").replace("aGVsbG8=", "!!notbase64"), "utf-8")
    out.append(item_case("invalid_base64", o, n))
    o, n = sides(
        [f("I", "I", "root"), f("a", "I", "a.pdf", text="a b")],
        [f("I", "I", "root"), f("a", "I", "a.pdf", text="a \n  b ")],
    )
    out.append(item_case("whitespace_only_identical", o, n))
    o, n = sides(
        [f("I", "I", "root"), f("a", "I", "a.pdf", text="")],
        [f("I", "I", "root"), f("a", "I", "a.pdf", text="something new")],
    )
    out.append(item_case("text_change_more", o, n))
    out.append(item_case("text_change_less", n, o))
    o, n = sides(
        [f("I", "I", "root"), f("a", "I", "a.pdf", author="bob")],
        [f("I", "I", "root"), f("a", "I", "a.pdf", title="t")],
    )
    out.append(item_case("field_filled_and_emptied", o, n))
    base = [f("I", "I", "root"), f("a", "I", "a.pdf")]
    o, n = sides(base, [*base, f("c", "a", "c.pdf")])
    out.append(item_case("subfiles_more", o, n))
    out.append(item_case("subfiles_fewer", n, o))
    o, n = sides([root], [root], None, {"a.b": 1, "a": {"b": 2}})
    out.append(item_case("metadata_flatten_collision_value_error", o, n))
    o, n = sides([root], [f("I", "I", "root", **{"x.y": 1, "x": {"y": 2}})])
    out.append(item_case("file_flatten_collision_value_error", o, n))
    o, n = sides([root], [root], {"big": 10**400}, {"big": 1})
    out.append(item_case("int_past_float_range_overflow_error", o, n))
    o, n = sides([root], [root])
    n.write_text(
        n.read_text(encoding="utf-8").replace('"files"', '"big": ' + "7" * 4301 + ', "files"'),
        "utf-8",
    )
    out.append(item_case("int_digit_limit_value_error", o, n))
    o, n = sides([root], [root])
    n.write_text(
        n.read_text(encoding="utf-8").replace(
            '"files"', '"deep": ' + "[" * 5000 + "]" * 5000 + ', "files"'
        ),
        "utf-8",
    )
    out.append(item_case("nesting_limit_recursion_error", o, n))
    entry = {
        "fileId": "I",
        "parentId": "I",
        "rootId": "I",
        "name": "root",
        "mp": "application",
        "ms": "pdf",
    }
    good = json.dumps({"itemId": "I", "files": [{**entry, "text": "aGVsbG8="}]})
    for label, value in [
        ("int", 123),
        ("float", 1.5),
        ("bool", True),
        ("list", ["aGVsbG8="]),
        ("object", {"b": "aGVsbG8="}),
        ("null", None),
    ]:
        bad_text = json.dumps({"itemId": "I", "files": [{**entry, "text": value}]})
        o, n = raw_sides(good, bad_text)
        out.append(item_case(f"non_string_text_{label}", o, n))
    o, n = raw_sides(good, json.dumps({"itemId": "J", "files": []}))
    out.append(item_case("item_id_mismatch", o, n))
    o, n = raw_sides(good, json.dumps({"itemId": "I", "files": [{**entry, "parentId": "x"}]}))
    out.append(item_case("root_not_found", o, n))
    return out


def main() -> None:
    """Write the fixture next to this script."""
    with tempfile.TemporaryDirectory() as tmp:
        data = {
            "text": text_cases(),
            "fields": field_cases(),
            "items": item_cases(Path(tmp)),
        }
    OUT.write_bytes((json.dumps(data, indent=1, ensure_ascii=False) + "\n").encode("utf-8"))


if __name__ == "__main__":
    main()
