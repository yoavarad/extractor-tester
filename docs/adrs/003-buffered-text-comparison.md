# ADR-003: Hybrid full / buffered text comparison

## Status
Accepted (2026-10-26)

## Context
Text metrics (ADR-002) run on the full extracted text, which can be many MB.
Full character-level edit distance is quadratic: 24 s for one 1M-char pair
(see docs/research/text-compare-benchmark.md).

## Decision
- If both texts are at most `full_compare_max_chars` (default 100,000), compare
  full text to full text at character level.
- Otherwise use the buffered algorithm:
  1. Split each text into blocks: one block per line; lines longer than
     `max_block_chars` (default 4,000) are cut into several blocks.
  2. Hash blocks; align the two block-hash sequences (Indel opcodes).
  3. Character-level comparison only inside replaced runs.
  4. Edit similarity = matched chars weighted by total length. Containment
     ("old found in new", "new found in old") from the same alignment.
  5. Word-set Jaccard: streaming, one pass per text.
  6. Diff snippets for examples taken from the first non-equal runs.
- Every text comparison records: method used (`full` / `buffered`) and number
  and % of lines that were cut into multiple blocks. Report surfaces these per
  type family and warns when cut-line % exceeds `cut_lines_warn_pct` (default 20%).
- Relation thresholds: `equal` = normalized texts identical; `*-within-*` when
  >= `containment_pct` (default 98%) of shorter text is found in the longer one.

## Amendment (2026-10-26, spec writing)
Block-hash alignment uses Python's `difflib.SequenceMatcher` (autojunk off), not
rapidfuzz: rapidfuzz Indel opcodes on hash sequences are quadratic (12 s at 200K
blocks); SequenceMatcher took 4.7 s at 1M blocks with 1% changes. rapidfuzz stays
for character-level comparison. See docs/research/text-compare-benchmark.md.

## Alternatives considered
- Always full comparison: exact, but 1M-char pair = 24 s.
- 1M-char full threshold (suggested): too slow; 100K keeps per-pair cost < 0.3 s.
- Fixed-size chunks aligned by position: one early insertion shifts every chunk.

## Consequences
- Buffered results approximate: moved text counts as delete + insert.
- All thresholds configurable; report states which method was used.
