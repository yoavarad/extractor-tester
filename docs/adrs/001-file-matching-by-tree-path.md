# ADR-001: Match files across pipelines by tree path

## Status
Accepted (2026-10-26)

## Context
To compare two outputs of the same item, every file in the old output must be
paired with its counterpart in the new output. `fileId` and `parentId` values are
generated independently by each pipeline and do not match. Only `itemId` is
stable. No content hash is available: the old pipeline cannot be modified to emit one.

## Decision
Match files by position in the extraction tree with tiered sibling matching:
1. The root file in each output is the file where `fileId == parentId == rootId == itemId`.
   Roots always match each other.
2. Matching proceeds top-down, one matched parent pair at a time: the children of
   matched parent P(old) are paired only against the children of P(new).
3. Unpaired children go through tiers in order. A tier pairs files only when the
   pairing is one-to-one:
   - Tier 1 `name`: identical file name (case-sensitive). Same-name siblings are
     disambiguated by tier 2 rules, then by order of appearance in `files`.
   - Tier 2 `text-hash`: identical hash of the normalized first 1,000,000 chars of
     `text` (whitespace collapsed) AND same type family. Empty/missing text never
     matches by hash.
   - Tier 3 `type-family`: exactly one unpaired child of a given type family remains
     on each side.
4. Every pair records the tier that produced it. Leftovers are reported as
   "only in old" / "only in new". An unmatched parent's subtree stays unmatched.
5. Type family = MIME type mapped through a `type-families` config (unlisted MIME
   type = its own family). The same family drives report grouping.
6. The matcher is an ordered list of tiers behind one small interface, so tiers can
   be added, removed or reordered without touching comparison or reporting code.

## Alternatives considered
- Content hash: most reliable, but not available (old pipeline frozen).
- Layered hash + path: no file hash, so not possible.
- Search whole item (not same parent) for tier 2: catches moved files, but less explainable.
- Raw (non-normalized) text hash: stricter, but whitespace changes in new pipeline would break matching.
- Text-similarity matching across the whole item: robust to renames but harder
  to explain and can produce surprising pairings.

## Consequences
- Matching is deterministic and explainable: every pair is traceable to "same parent + same name".
- If the pipelines name subfiles differently, those files show up as only-in-old/only-in-new
  rather than as a metadata diff.
