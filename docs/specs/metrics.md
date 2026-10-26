# Metrics

Decision records: docs/adrs/002-comparison-metrics.md, docs/adrs/003-buffered-text-comparison.md.

Metrics exist at two levels. Facts are computed once per file pair into [ydk:entity:model/PairFacts] by [ydk:core-module:core/pair-comparer]. Every aggregate is derived from those facts by [ydk:core-module:core/aggregator]; nothing is recomputed later.

## Per file pair

**MIME type and type family.** Whether the MIME types are equal and whether the type families are equal.

**Metadata.** [ydk:core-module:core/field-flattener] turns nested objects into dotted paths, so a nested author field is compared like any flat field; lists are compared as one whole value. Each field is classed as generic metadata, format-specific metadata or ignored, using the lists in [ydk:config-file:config/compare-config]. [ydk:core-module:core/field-differ] gives each field one status — same, changed, only-in-old or only-in-new — as a [ydk:entity:model/FieldDiff]. A value that changes type (a number becoming a string) counts as changed and the example shows both types. Whole and decimal numbers of the same value are equal; true/false never equals a number; an explicit null is a value.

**Text.** [ydk:core-module:core/text-comparer] produces [ydk:entity:model/TextFacts] on the full decoded text, never a truncated one:

- exact equality, and equality after whitespace normalization;
- edit similarity from 0 to 1: the share of characters the two texts have in common, in order;
- word-set similarity from 0 to 1: shared distinct words over all distinct words;
- length difference in characters;
- containment: how much of the old text is found in the new, and the reverse;
- a relation label derived from containment: equal, old-within-new (the new text is the old text plus more), new-within-old (the new text lost content), overlapping, or disjoint. A "within" label needs at least the containment threshold (by default 98%) of the shorter text found in the longer one. Overlapping means some content is shared and no other label applies; disjoint means no shared content was found.

Texts that are equal after normalization take the fast path: they are labelled equal, both similarities are 1, and nothing else is computed. Whitespace-only changes therefore never count as text differences, though the exact-equality flag still records them. If one side has no text and the other does, the relation is old-within-new (text appeared) or new-within-old (text vanished), and the new-more-text or new-less-text hint is raised.

**Subfiles.** Child count on each side and how many children of this pair stayed unpaired on each side.

**Matching tier.** The tier that paired the files and how a tie was broken (see matching.md).

**Directional hints.** Hints say which way a difference points, never which side is right: the new text is longer or shorter; the new pipeline filled a field the old left empty, or emptied one the old had filled (empty means absent, null, empty string, empty list or empty object); the new pipeline found more or fewer subfiles.

**Identical.** A file pair is identical when the MIME types are equal, no compared field differs, the text relation is equal and no child stayed unpaired.

## Text comparison algorithm

A character-level comparison of two long texts costs time proportional to the product of their lengths: measured at a quarter second for two 100,000-character texts and 24 seconds for two of a million (docs/research/text-compare-benchmark.md). The tool therefore uses two methods and records which one ran.

**Full method.** When both texts are at most the full-comparison threshold (by default 100,000 characters), the whole texts are compared character by character. The target cost is in [ydk:nfr:perf/full-compare-pair].

**Buffered method.** For longer texts:

1. Each text is split into blocks, one per line. Lines longer than the block limit (by default 4,000 characters) are cut into several blocks, so text without line breaks is still split.
2. Each block is hashed, and the two sequences of block hashes are aligned. This yields runs of equal, inserted, deleted and replaced blocks. Alignment uses a longest-matching-run method measured at under 5 seconds for a million blocks with 1% changed (docs/research/text-compare-benchmark.md).
3. Characters are compared only inside replaced runs. A replaced run larger than the full-comparison threshold is compared block by block in order, so no single comparison grows unbounded; such runs are counted.
4. Edit similarity counts characters in equal runs plus characters matched inside replaced runs. Containment comes from the same counts divided by each text's length.
5. Word-set similarity is computed in one pass per text.

The buffered method is an approximation: text moved outside its aligned run counts as removed and added. Each pair records how many lines were cut into blocks, and [ydk:entity:model/FamilyAggregate] carries a warning when cut lines exceed the cut-line warning threshold (by default 20%), because then line-based alignment is weak.

**Diff snippets.** Each pair keeps up to three snippets of the first differing regions, each side cut to the configured example length (by default 300 characters), old and new side by side, for use as examples. Snippets come from the alignment above, so they cost nothing extra.

## Per type family

[ydk:entity:model/FamilyAggregate] holds, for each type family: file pair count; only-in-old and only-in-new counts; share identical; MIME and family changes; change rate per metadata field, split by status; median, 10th percentile and minimum of both text similarities; count per relation label; count per comparison method and cut-line share; tier breakdown; hint counts; decode-error and file-error counts. Each number is a [ydk:entity:model/MetricResult] with examples.

## Per item

[ydk:entity:model/ItemResult] holds item metadata field differences and counts of identical, changed and one-sided files. Item-level fields are every top-level field except the item id and the file list. Item metadata change rates across all items appear at the top of the report.
