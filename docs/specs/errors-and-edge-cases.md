# Errors and edge cases

Rule: only run-level problems stop a run. Any problem confined to one item or one file is recorded as an [ydk:entity:model/ErrorRecord] and the run goes on to a complete report ([ydk:req:rules/never-abort-on-bad-input]). Errors appear in the items file, in the pairs file (file errors), and as counts with examples in the [ydk:entity:model/Report].

Severity: **fatal** stops the run; fatal errors found at startup write nothing, and fatal errors during the run leave already-written lines in the pairs and items files but no JSON or HTML report; **error** removes the item (or the file's text metrics) from comparison and lists it; **warning** keeps comparing and lists the condition.

## Run level (fatal, exit code 1)

| Case | Error |
|---|---|
| Config missing or invalid | [ydk:tool-error:errors/invalid-config] |
| Old or new path does not exist | [ydk:tool-error:errors/input-not-found] |
| One input is a file, the other a folder | [ydk:tool-error:errors/mixed-input-kinds] |
| Output folder not empty without overwrite | [ydk:tool-error:errors/output-exists] |
| Output cannot be written (permission, full disk) | [ydk:tool-error:errors/output-write-failed] |
| Unexpected failure in the main process (aggregation, writing, rendering) | [ydk:tool-error:errors/internal-failure] |

An interrupt (Ctrl-C) stops the workers and exits with code 130, leaving the same output state as a fatal error during the run, so a JSON report on disk always describes a complete run.

In folder mode only top-level files ending in .json are items; subfolders and other files are ignored, and their count appears in the warnings of the [ydk:entity:model/Report] (a warning, not a tool-error).

Both folders containing no items is a valid, empty run with [ydk:tool-error:errors/empty-input] as a warning and exit code 0.

## Item level

| Case | Behaviour |
|---|---|
| Item present in only one folder | Item result with status only-in-old or only-in-new; counted and exemplified at the top of the report. Not an error. |
| Unreadable file, invalid JSON, or missing item id / file list | [ydk:tool-error:errors/invalid-json] |
| Item JSON file above the size limit | [ydk:tool-error:errors/item-too-large], checked before parsing |
| Item id inside the file differs from the file name, or (file mode) old and new item ids differ | [ydk:tool-error:errors/item-id-mismatch] |
| No root file | [ydk:tool-error:errors/root-not-found] |
| Several root files | [ydk:tool-error:errors/multiple-roots] |
| Repeated file id | [ydk:tool-error:errors/duplicate-file-id] |
| Parent links loop | [ydk:tool-error:errors/parent-cycle] |
| Unexpected failure while comparing | [ydk:tool-error:errors/item-processing-failed], with the exception type and message |
| Worker process dies | [ydk:tool-error:errors/worker-crashed] |

## File level

| Case | Behaviour |
|---|---|
| Parent id names no file | [ydk:tool-error:errors/orphan-file] warning; the file and its subtree are left out; the rest of the item is compared |
| Name, MIME part or text field absent or null | [ydk:tool-error:errors/missing-field] warning; treated as empty; counted per field |
| Text present but not a string, or not valid base64 | [ydk:tool-error:errors/invalid-base64]; text treated as empty for matching; text metrics skipped for its pair; metadata still compared |
| Text not valid UTF-8 after decoding | Invalid bytes replaced; decode-error count recorded on the file and counted per type family. Not an error. |
| Field changes type | Counted as changed; example shows both types. Not an error. |

## Adversarial review

| Challenge | Mechanism |
|---|---|
| One huge item (thousands of files, gigabytes of text) | Item JSON files above the size limit are rejected before parsing. Below it, the item runs in one worker while others continue; it appears in the slowest items of [ydk:entity:model/RunStats]. Long texts use the buffered method, where no single character comparison exceeds the full-comparison threshold. |
| 100,000 tiny items | Items are sent to workers in batches ([ydk:core-module:core/runner]), so process communication happens once per batch, not once per item; results are written to disk as they arrive; aggregates hold counters and capped example lists, not per-pair data. Target: [ydk:nfr:perf/run-time-100k]. |
| Very deep nesting | Tree building, the matching walk and cycle detection are iterative, so depth has no recursion limit. Nested fields are flattened iteratively as well. |
| Every file is binary with empty text | Empty text never pairs by hash, so tiers fall to name and type family. Texts are equal (both empty) and take the fast path. Empty-text counts are visible per type family. |
| Duplicate names everywhere | Name grouping is a single pass with lookup tables, not pairwise comparison. Ties resolve by hash, then position, and every position-based pair is labelled. |
| Worker crash (killed, out of memory) | The runner detects a broken pool, marks items whose results never arrived with [ydk:tool-error:errors/worker-crashed], recreates the pool and continues. After three restarts, remaining items are marked and the run still writes its report. |
| Worker hangs without dying | Not detected: there is no per-item timeout. The progress line stops advancing; the user interrupts the run and finds the item among the slowest items of a rerun with one worker. |
| Text that is not UTF-8 | Bytes replaced and counted; never a crash ([ydk:req:rules/decode-first]). |
| Malicious content in names, values or text | Standard JSON parsing only, nothing is executed; all input content is escaped in HTML; output file names never come from input content ([ydk:req:rules/escape-untrusted-content]). |
| Huge single JSON file | Size limit (by default 50 MB) checked before parsing, so no worker parses a file larger than the one the memory target [ydk:nfr:perf/peak-memory] is measured with. |
| Same inputs, different worker count | Results are collected in item-id order regardless of which worker finishes first ([ydk:req:rules/deterministic-output]). |
