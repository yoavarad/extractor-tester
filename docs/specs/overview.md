# Overview

## Problem

Two pipelines turn the same binary files into extracted content: the **old** pipeline (frozen, cannot be changed) and the **new** pipeline (being improved). Each extracts, per file, the MIME type, generic metadata, format-specific metadata, text and subfiles. Each format has its own extraction logic, so a change in the new pipeline usually affects one type family and leaves the others untouched.

Today there is no way to tell, across tens of thousands of files, where the new pipeline differs from the old one, in which direction, and for which type families. Without a tool, differences can only be found by opening single files by hand, so a change that breaks one extractor for one type family can go unnoticed until someone downstream reports missing text or metadata. extractor-tester answers that question with one command, [ydk:cli-command:cli/compare], and produces a report grouped by type family.

The tool does not decide which side is correct. It reports differences, the direction of each difference, and concrete examples, so a person can judge.

## Users

One to three engineers working on the new pipeline, running the tool locally on output folders of 10,000 to 100,000 files.

## Success criteria

All criteria hold from the first release.

1. For a folder pair of 100,000 file pairs the run finishes within the target of [ydk:nfr:perf/run-time-100k] and the memory ceiling of [ydk:nfr:perf/peak-memory].
2. For every type family the report shows how many file pairs are identical, which metadata fields changed and how often, the median, 10th percentile and minimum of edit similarity and word-set similarity, and how files were matched ([ydk:entity:model/FamilyAggregate]).
3. Every reported number links to concrete examples ([ydk:req:rules/examples-on-every-result]).
4. A text that the new pipeline extended (old text plus more) is reported as such, not as a generic "different" ([ydk:entity:model/TextFacts]).
5. A malformed item never stops the run ([ydk:req:rules/never-abort-on-bad-input]); it is listed with its reason.
6. Two runs on the same inputs produce the same results ([ydk:req:rules/deterministic-output]).
7. Every result key that can appear in the JSON report is defined in metrics.md; a test lists the result keys of the golden report and checks each against metrics.md.

## End-to-end walkthrough

An engineer has run both pipelines over the same 8,000 items, producing two folders of item JSON files named by item id.

1. They write a [ydk:config-file:config/compare-config] naming the file-name field, the two MIME fields and the text field, listing which fields are generic metadata and which fields to ignore (timestamps, pipeline version). They write a [ydk:config-file:config/type-families] grouping, for example, the MIME types used for emails into one "email" family.
2. They run [ydk:cli-command:cli/compare] with both folders. [ydk:core-module:core/config-loader] validates the config into [ydk:entity:model/CompareConfig]; a typo stops the run immediately with [ydk:tool-error:errors/invalid-config].
3. [ydk:core-module:core/loader] pairs the two folders by item id. 7,990 items exist on both sides; 6 exist only in old and 4 only in new. Those 10 become item results with status only-in-old / only-in-new.
4. [ydk:core-module:core/runner] hands items to worker processes. For each item, [ydk:core-module:core/pair-comparer] loads both sides, [ydk:core-module:core/tree-builder] decodes every text ([ydk:req:rules/decode-first]) and builds the file tree into [ydk:entity:model/Item] and [ydk:entity:model/FileRecord], and [ydk:core-module:core/matcher] pairs files into [ydk:entity:model/FilePair] (see matching.md).
5. For every file pair, [ydk:core-module:core/field-differ] compares metadata into [ydk:entity:model/FieldDiff] records and [ydk:core-module:core/text-comparer] computes [ydk:entity:model/TextFacts]. The result per file pair is one [ydk:entity:model/PairFacts]; per item one [ydk:entity:model/ItemResult] (see metrics.md).
6. One item has a broken JSON file. It is listed with [ydk:tool-error:errors/invalid-json] and the run continues.
7. [ydk:core-module:core/report-writer] writes facts to disk as they arrive and [ydk:core-module:core/aggregator] folds them into the [ydk:entity:model/Report]. [ydk:core-module:core/html-renderer] renders the HTML report from the written JSON report (see outputs.md).
8. The engineer opens the HTML report. Under "pdf" they see that the author field changed in 12% of pairs, with five examples showing old and new values; under "email" they see 300 pairs whose relation is old-within-new, meaning the new pipeline now extracts more of the body; under "zip" they see 40 files only in new, meaning the new pipeline finds more subfiles. Run statistics show where time went ([ydk:entity:model/RunStats]).

## Documents

- architecture.md — modules and their single owners
- matching.md — how files are paired across pipelines
- metrics.md — what is compared and how text is compared
- config.md — the two config files
- outputs.md — output files, example selection, HTML layout, profiling
- errors-and-edge-cases.md — every failure mode and its behaviour
- scope.md — what is and is not built
- testing-strategy.md — how correctness and targets are proven
- glossary.md — canonical terms
- Decisions: docs/adrs/001 to 007
