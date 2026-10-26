# ADR-002: Comparison metrics

## Status
Accepted (2026-10-26)

## Context
The tool must surface where the new pipeline differs from the old one, per file
type family, in a way a human can act on. The tool cannot know which side is
correct, only what differs and in which direction.

## Decision
Two levels of metrics.

**Per matched file pair (raw facts):**
- File type: MIME equal, type family equal.
- Generic metadata and format-specific metadata: per field, one of
  same / changed / only-in-old / only-in-new.
- Text (computed on the FULL text, not truncated; see ADR-003 for the
  chunked algorithm):
  - exact equal, equal after normalization
  - edit similarity 0-1 (character-level, Indel-based ratio)
  - word-set Jaccard similarity 0-1
  - length delta (chars)
  - containment: fraction of old text found in new, and of new found in old,
    yielding a relation label: `equal`, `old-within-new` (new = old + more),
    `new-within-old` (new lost content), `overlapping`, `disjoint`
- Subfiles: child count old vs new, children only-in-old / only-in-new.
- Match tier that paired the files.
- Directional hints (never verdicts): new has more/less text, new filled a field
  old had empty (or emptied one), new found more/fewer subfiles.

**Per type family (main report view):**
- Pair count, only-in-old count, only-in-new count.
- % pairs fully identical.
- Change rate per metadata field.
- Text similarity distribution (median, p10) and containment relation counts.
- Match tier breakdown.
- Directional hint counts.

**Per item:** item metadata field diffs; counts of identical / changed / unpaired files.

**Examples:** every reported result (each field change rate, each relation
count, each hint, each unpaired count) carries concrete examples: item id, file
path in tree, old value vs new value (text results: short diff snippet). Number of
examples per result is configurable.

## Alternatives considered
- Single similarity score only: hides "new = old + more" vs "rewritten" difference.
- Verdict scoring (better/worse): tool cannot know ground truth; hints only.
- Truncated text comparison: misses differences past the cutoff.

## Consequences
- Pair-level facts are computed once; all aggregates derive from them.
- Report size grows with example count; bounded by config.
