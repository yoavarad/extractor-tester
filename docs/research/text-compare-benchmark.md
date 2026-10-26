# Benchmark: full-text Indel similarity (rapidfuzz)

Date: 2026-10-26. Machine: dev laptop (Windows 11), rapidfuzz latest, single pair,
random text with 1% char substitutions.

| Text length (chars each) | Time |
|---|---|
| 100,000 | 0.25 s |
| 300,000 | 2.22 s |
| 1,000,000 | 24.26 s |

Cost grows quadratically. 1M-char full comparison is too slow for batch use;
100K is the default full-comparison threshold (ADR-003).

## Block-hash alignment (buffered method, step 2)

Sequences of random 62-bit block hashes, 1% of blocks replaced.

| Blocks each | rapidfuzz Indel.opcodes | difflib.SequenceMatcher (autojunk=False) |
|---|---|---|
| 50,000 | 0.57 s | 0.09 s |
| 200,000 | 12.12 s | 0.53 s |
| 1,000,000 | not run | 4.67 s |

difflib is used for block alignment (ADR-003 amendment).
