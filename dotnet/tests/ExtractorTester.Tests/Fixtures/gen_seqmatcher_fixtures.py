"""Generate seqmatcher_cases.json from CPython difflib (autojunk=False).

Run: python -I gen_seqmatcher_fixtures.py   (output written next to this script)
"""

import json
import random
from difflib import SequenceMatcher
from pathlib import Path


def mutate(rng, seq, alphabet):
    out = list(seq)
    for _ in range(rng.randint(0, max(1, len(out) // 4 + 1))):
        op = rng.choice(["ins", "del", "rep", "swap"])
        if op == "ins" or not out:
            out.insert(rng.randint(0, len(out)), rng.choice(alphabet))
        elif op == "del":
            del out[rng.randrange(len(out))]
        elif op == "rep":
            out[rng.randrange(len(out))] = rng.choice(alphabet)
        elif len(out) > 1:
            i, j = rng.randrange(len(out)), rng.randrange(len(out))
            out[i], out[j] = out[j], out[i]
    return out


def main():
    rng = random.Random(134)
    cases = [
        ([], []),
        (["a"], []),
        ([], ["a"]),
        (["a"], ["a"]),
        (["a"], ["b"]),
        (["a", "a", "a"], ["a", "a"]),
        (["a", "b"] * 5, ["b", "a"] * 5),
    ]
    for _ in range(330):
        size = rng.choice([1, 2, 3, 5, 8, 15, 30])
        asz = rng.choice([2, 3, 5, 20])
        alphabet = [f"line {k}" for k in range(asz)] + ["", " ", "é ü"]
        a = [rng.choice(alphabet) for _ in range(rng.randint(0, size))]
        if rng.random() < 0.7:
            b = mutate(rng, a, alphabet)
        else:
            b = [rng.choice(alphabet) for _ in range(rng.randint(0, size))]
        cases.append((a, b))
    # large inputs (>200 lines) with popular lines, where autojunk=True would differ
    for n in range(24):
        asz = rng.choice([3, 10, 60])
        alphabet = [f"row {k}" for k in range(asz)]
        a = [rng.choice(alphabet) for _ in range(rng.randint(201, 450))]
        if n % 3:
            b = mutate(rng, a, alphabet)
        else:
            b = [rng.choice(alphabet) for _ in range(rng.randint(201, 450))]
        cases.append((a, b))

    out, differs = [], 0
    for a, b in cases:
        sm = SequenceMatcher(None, a, b, autojunk=False)
        if SequenceMatcher(None, a, b).get_opcodes() != sm.get_opcodes():
            differs += 1
        out.append(
            {
                "a": a,
                "b": b,
                "blocks": [list(m) for m in sm.get_matching_blocks()],
                "opcodes": [list(o) for o in sm.get_opcodes()],
                "ratio": sm.ratio(),
            }
        )
    assert len(out) >= 300 and differs > 0, (len(out), differs)
    path = Path(__file__).with_name("seqmatcher_cases.json")
    path.write_text(json.dumps(out, separators=(",", ":")), encoding="utf-8")
    print(len(out), "cases;", differs, "differ under autojunk=True")


if __name__ == "__main__":
    main()
