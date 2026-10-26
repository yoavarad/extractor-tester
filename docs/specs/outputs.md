# Outputs

Decision records: docs/adrs/004-report-format.md, docs/adrs/006-tech-stack-and-scale.md, docs/adrs/007-profiling.md.

## Files written

[ydk:cli-command:cli/compare] writes four files into the output folder, plus a profile folder when profiling is on. [ydk:core-module:core/report-writer] owns all of them except the HTML.

- **pairs file** — one [ydk:entity:model/PairFacts] per line, one line per file pair (paired or one-sided). Written as results arrive, so memory does not grow with the number of files. Searchable with standard text tools.
- **items file** — one [ydk:entity:model/ItemResult] per line, including item errors.
- **JSON report** — the [ydk:entity:model/Report]: totals, item-level results, one [ydk:entity:model/FamilyAggregate] per type family, error counts, warnings, the effective config and run statistics. It holds aggregates and capped examples only, so its size depends on the number of type families, fields and examples, not on the number of files.
- **HTML report** — rendered by [ydk:core-module:core/html-renderer] from the JSON report read back from disk, never from in-memory state ([ydk:req:rules/json-is-source-of-truth]).

A non-empty output folder is refused unless overwriting is requested ([ydk:tool-error:errors/output-exists]). Write failures and interrupts are described in errors-and-edge-cases.md ([ydk:tool-error:errors/output-write-failed]).

## Examples

Every reported number is a [ydk:entity:model/MetricResult] carrying up to the configured number of [ydk:entity:model/Example] records ([ydk:req:rules/examples-on-every-result]). An example names the item, the tree path on each side, the old and new value (or a diff snippet for text), and a short note such as the similarity value or a type change. Long values are cut to the configured length.

Selection rules, so results are reproducible ([ydk:req:rules/deterministic-output]):

- similarity results: the lowest-similarity file pairs, ties by item id then tree path;
- every other result: the first occurrences in item-id order, then tree-path order.

The [ydk:core-module:core/aggregator] keeps only the current best examples per result while results arrive, so memory stays bounded.

## HTML layout

One self-contained file: no server, no external downloads, inline styles and inline script only for sorting tables and collapsing sections. Its size target is in [ydk:nfr:perf/html-size]. All content from the inputs is escaped ([ydk:req:rules/escape-untrusted-content]).

Sections, top to bottom:

```
┌──────────────────────────────────────────────────────────────┐
│ extractor-tester report   old: <path>   new: <path>          │
│ Items 7,990 compared · 6 only old · 4 only new · 1 error     │
│ Warnings (if any)                                            │
├──────────────────────────────────────────────────────────────┤
│ Type families (sortable)                                     │
│ family  pairs  identical  only old  only new  median sim  ⚠  │
│ pdf     2,300  71%        3         0         0.97           │
│ email   1,950  40%        0         12        0.91        ⚠  │
├──────────────────────────────────────────────────────────────┤
│ ▸ pdf                                                        │
│   Fields: author changed 12% [▸ 5 examples] ...              │
│   Text: relations, similarity percentiles, methods, cut %    │
│   Matching tiers · Hints · File errors                       │
│     ▾ example: item 123 · root.eml/invoice.pdf               │
│       old │ Jane Doe          new │ J. Doe                   │
├──────────────────────────────────────────────────────────────┤
│ Item metadata changes · Errors by code · Run statistics      │
└──────────────────────────────────────────────────────────────┘
```

The warning mark appears for a type family whose cut-line share is above the warning threshold.

## Run statistics and profiling

[ydk:core-module:core/timers] measures, for every item, time spent loading, decoding, building the tree, matching, comparing fields and comparing text (fast path, full and buffered separately). The main process adds aggregation and rendering time. [ydk:entity:model/RunStats] holds the totals, text comparison time per type family, the ten slowest items and the number of worker-pool restarts. These are always on; the cost is one clock read per stage per item.

With profiling requested, each worker runs under the standard Python profiler and writes one profile file per worker into the profile folder, viewable with standard profile viewers. It is off by default because the profiler adds a cost to every function call.

## Progress

While running, [ydk:core-module:core/runner] keeps one progress line on standard error that shows items done, items total and elapsed time.
