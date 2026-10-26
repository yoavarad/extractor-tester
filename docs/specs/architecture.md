# Architecture

One package with one module per responsibility. Data flows one way: config, then load, then build trees, then match, then compare, then aggregate, then write, then render. Each piece of functionality has exactly one owner below.

## Data flow

```
compare command
   │
   ▼
runner ── config-loader ──► CompareConfig
   │   └─ loader (discovery) ──► item list (sorted by item id)
   │
   │  per item, in worker processes
   ▼
pair-comparer
   ├─ loader (item loading, old and new)
   ├─ tree-builder ── decoder, type-family, field-flattener ──► Item + FileRecord tree
   ├─ matcher ── tiers: name → text-hash → type-family ──► FilePairs
   ├─ field-differ ──► FieldDiffs
   └─ text-comparer ──► TextFacts
   ▼
ItemResult + PairFacts  (back to main process, in item-id order)
   ├─ report-writer ──► items file, pairs file
   └─ aggregator ──► Report ──► report-writer ──► JSON report
                                     ▼
                              html-renderer ──► HTML report
```

Names in the diagram are the modules and models linked in the tables below.

## Owners

| Concern | Owner |
|---|---|
| Command-line parsing, exit codes | [ydk:cli-command:cli/compare] |
| Run orchestration, worker pool, crash recovery, progress line | [ydk:core-module:core/runner] |
| Config reading and validation | [ydk:core-module:core/config-loader] |
| Input discovery, item pairing by id, JSON parsing, size limit | [ydk:core-module:core/loader] |
| Base64 and UTF-8 decoding, normalization, text hash | [ydk:core-module:core/decoder] |
| MIME joining, type family lookup | [ydk:core-module:core/type-family] |
| Dotted-path flattening, field classification | [ydk:core-module:core/field-flattener] |
| File tree, root rule, tree errors | [ydk:core-module:core/tree-builder] |
| Tree walk and tier order | [ydk:core-module:core/matcher] |
| Tier interface | [ydk:contract:core/MatchTier] |
| Name tier | [ydk:core-module:core/tier-name] |
| Text-hash tier | [ydk:core-module:core/tier-text-hash] |
| Type-family tier | [ydk:core-module:core/tier-type-family] |
| Field status | [ydk:core-module:core/field-differ] |
| Text metrics and method choice | [ydk:core-module:core/text-comparer] |
| Per-item comparison, hints, identical flag | [ydk:core-module:core/pair-comparer] |
| Aggregates and example selection | [ydk:core-module:core/aggregator] |
| Writing pairs file, items file, JSON report | [ydk:core-module:core/report-writer] |
| HTML | [ydk:core-module:core/html-renderer] |
| Stage timers, profiling | [ydk:core-module:core/timers] |

## Forbidden dependencies

- No comparison, matching or aggregation module imports [ydk:core-module:core/html-renderer], so the renderer can be tested from a hand-written JSON report.
- The renderer imports nothing from comparison, matching or aggregation; it reads only the [ydk:entity:model/Report] model ([ydk:req:rules/json-is-source-of-truth]).
- The matcher knows nothing about comparison; comparison knows nothing about tier rules, only the recorded tier label ([ydk:req:rules/matcher-pluggable]).
- Only [ydk:core-module:core/decoder] decodes or normalizes text ([ydk:req:rules/decode-first]).

## Models

The models are plain data, defined once and used for validation and for the JSON outputs: [ydk:entity:model/CompareConfig], [ydk:entity:model/Item], [ydk:entity:model/FileRecord], [ydk:entity:model/FilePair], [ydk:entity:model/FieldDiff], [ydk:entity:model/TextFacts], [ydk:entity:model/PairFacts], [ydk:entity:model/ItemResult], [ydk:entity:model/ErrorRecord], [ydk:entity:model/Example], [ydk:entity:model/MetricResult], [ydk:entity:model/FamilyAggregate], [ydk:entity:model/RunStats], [ydk:entity:model/Report].

## Parallelism

Items are independent, so the runner processes them in a pool of worker processes; one worker means in-process execution, which keeps debugging simple. Workers return compact facts, never full texts. Results are consumed in item-id order, which keeps outputs identical for any worker count ([ydk:req:rules/deterministic-output]).
