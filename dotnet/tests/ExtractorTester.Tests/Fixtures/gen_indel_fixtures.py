"""Generate indel_cases.json from rapidfuzz.distance.Indel.

Run: uv run python -I gen_indel_fixtures.py   (output written next to this script)
"""

import json
import random
from pathlib import Path

from rapidfuzz.distance import Indel


def mutate(rng, s, alphabet):
    out = list(s)
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
    return "".join(out)


def rand_str(rng, alpha, lo, hi):
    return "".join(rng.choice(alpha) for _ in range(rng.randint(lo, hi)))


def main():
    rng = random.Random(133)
    alphabets = [
        list("ab"),
        list("abc"),
        list("abcdefghij"),
        list("abcdefghijklmnopqrstuvwxyz "),
        list("a\U0001f600\U0001f4a9éb"),
        [chr(c) for c in range(0x4E00, 0x4E00 + 40)],
    ]
    cases = [
        ("", ""),
        ("a", ""),
        ("", "a"),
        ("a", "a"),
        ("abc", "abc"),
        ("abc", "xyz"),
        ("aaaa", "aa"),
        ("aa", "aaaa"),
        ("abab" * 10, "baba" * 10),
        ("\U0001f600" * 5, "\U0001f600" * 3),
        ("\U0001f600a\U0001f4a9", "a\U0001f4a9\U0001f600"),
        ("x" * 64, "x" * 64 + "y"),
        ("x" * 65, "y" + "x" * 65),
    ]
    for _ in range(560):
        alpha = rng.choice(alphabets)
        size = rng.choice([1, 2, 3, 5, 8, 15, 30, 63, 64, 65, 70, 130, 200])
        a = rand_str(rng, alpha, 0, size)
        r = rng.random()
        if r < 0.6:
            b = mutate(rng, a, alpha)
        elif r < 0.8:
            b = rand_str(rng, alpha, 0, size)
        else:
            b = rand_str(rng, rng.choice(alphabets), 0, size)
        cases.append((a, b))
    for _ in range(10):
        alpha = rng.choice(alphabets[2:4])
        a = rand_str(rng, alpha, 1000, 3000)
        cases.append((a, mutate(rng, a, alpha)))

    out = []
    for a, b in cases:
        out.append(
            {
                "a": a,
                "b": b,
                "distance": Indel.distance(a, b),
                "similarity": Indel.normalized_similarity(a, b),
                "opcodes": [
                    [o.tag, o.src_start, o.src_end, o.dest_start, o.dest_end]
                    for o in Indel.opcodes(a, b)
                ],
            }
        )
    assert len(out) >= 500
    path = Path(__file__).with_name("indel_cases.json")
    path.write_text(json.dumps(out, separators=(",", ":")), encoding="utf-8")
    print(len(out), "cases")


if __name__ == "__main__":
    main()
