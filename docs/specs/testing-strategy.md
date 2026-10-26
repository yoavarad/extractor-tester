# Testing strategy

Tests are the definition of done. Every test runs with pytest; lint and type checks use the project's quality plugins (docs/adrs/006-tech-stack-and-scale.md).

## Rules

- No mocking of the project's own modules. Tests call real code with real inputs.
- Input data is hand-built item JSON written by test helpers in temporary folders, at most 10 files and 1,000 characters of text per item. Texts are base64-encoded by the helper, so every test also exercises [ydk:req:rules/decode-first].
- The only boundaries are the file system and worker processes, and both are used for real (temporary folders; two workers in worker tests). A worker test checks [ydk:tool-error:errors/item-timeout] with real slow work: a one-second timeout, a raised full-comparison threshold, and one item whose old and new texts are 300,000 different characters each (measured at over 2 seconds to compare).
- Every tool-error manifest has at least one test that triggers it and checks the recorded code and the run's continuation or exit code.

## Test types

**Unit tests**, one file per module: decoding and normalization ([ydk:core-module:core/decoder]); MIME joining and family lookup ([ydk:core-module:core/type-family]); flattening and classification ([ydk:core-module:core/field-flattener]); tree building and its errors ([ydk:core-module:core/tree-builder]); each tier alone and the matcher with a custom tier list ([ydk:core-module:core/matcher], [ydk:req:rules/matcher-pluggable]); field differences including type changes and numeric equality ([ydk:core-module:core/field-differ]); text comparison for each method and each relation label, with the buffered method checked against the full method on 50,000-character texts with line-level edits: edit similarity may differ by at most 0.02 ([ydk:core-module:core/text-comparer]); aggregation and example selection ([ydk:core-module:core/aggregator]); config validation ([ydk:core-module:core/config-loader]).

**Integration tests**: one item end to end through [ydk:core-module:core/pair-comparer]; folder runs through [ydk:core-module:core/runner] covering items on one side only, a malformed item among good ones, and output-folder handling.

**Golden tests**: a fixed corpus of 12 items: one identical item, and one item each for metadata change, type change, text extension, text loss, rewritten text, renamed file, same-name siblings, one-sided subfile, invalid base64, malformed JSON, and an item present on one side only with expected pairs file, items file and JSON report checked in. The run must reproduce them exactly with one worker and with four ([ydk:req:rules/deterministic-output]). A separate golden test renders a hand-written JSON report to HTML without running any comparison ([ydk:req:rules/json-is-source-of-truth]), and an import check asserts the renderer depends on no comparison module.

**Security tests**: script tags in names, values and text render as visible text; item ids shaped like paths create no file outside the output folder ([ydk:req:rules/escape-untrusted-content]).

**Benchmark tests**: marked slow and excluded from the default run. A generator builds the corpus described in [ydk:nfr:perf/run-time-100k]; the tests check [ydk:nfr:perf/run-time-100k], [ydk:nfr:perf/peak-memory], [ydk:nfr:perf/html-size] and [ydk:nfr:perf/full-compare-pair].

## Example presence

A golden-report check walks every [ydk:entity:model/MetricResult] and asserts that each non-zero result carries examples ([ydk:req:rules/examples-on-every-result]). A second check confirms every result key of the golden report is defined in metrics.md.
