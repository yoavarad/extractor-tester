# Glossary

One name per concept. Terms in the "do not use" column must not appear in specs, code or report text.

| Term | Meaning | Do not use |
|---|---|---|
| old / new | The two pipelines (old: frozen; new: being improved) and their outputs | baseline, candidate, legacy, v1/v2, left/right, A/B |
| item | One input unit and its output JSON, identified by item id ([ydk:entity:model/Item]) | document, record, job |
| item id | The id shared by both pipelines for an item | key, doc id |
| file | One entry of an item's file list ([ydk:entity:model/FileRecord]) | node, part, object |
| root file | The file whose file id, parent id and root id equal the item id | main file, top file |
| subfile / child | A file whose parent is another file | attachment (except when quoting an email), embedded file |
| tree path | File names from the root down, with an occurrence mark for repeated sibling names | location, file path (when meaning the tree) |
| file pair | A paired old file and new file, or a one-sided file ([ydk:entity:model/FilePair]) | match pair, match, couple |
| one-sided | A file present only in old or only in new | missing, orphan (orphan has its own meaning below) |
| orphan file | A file whose parent id names no file ([ydk:tool-error:errors/orphan-file]) | — |
| matching | Pairing files across old and new ([ydk:core-module:core/matcher]) | alignment (reserved for text), mapping |
| tier | One matching rule in the ordered list ([ydk:contract:core/MatchTier]) | level, strategy, pass |
| tie-break | How same-name siblings were paired | — |
| MIME type | The two MIME parts joined with a slash | content type, file type (in specs) |
| type family | Group of MIME types treated as one kind of file ([ydk:config-file:config/type-families]) | category, file type group, class |
| generic metadata | Fields listed as generic in [ydk:config-file:config/compare-config] | common fields, basic metadata |
| format-specific metadata | All other compared file fields | custom fields, extra metadata |
| field | One dotted path in an item or file ([ydk:entity:model/FieldDiff]) | attribute, property, key (in specs) |
| field status | same, changed, only-in-old, only-in-new | — |
| normalized text | Decoded text with every whitespace run collapsed to one space and ends trimmed | cleaned text |
| text hash | Hash of the first million characters of normalized text | fingerprint, checksum |
| edit similarity | Share of characters two texts have in common, in order | similarity score (unqualified), distance |
| word-set similarity | Shared distinct words over all distinct words | Jaccard (in prose), overlap |
| containment | Share of one text found in the other | coverage, inclusion |
| relation | equal, old-within-new, new-within-old, overlapping, disjoint ([ydk:entity:model/TextFacts]) | — |
| full method / buffered method / fast path | The three text comparison methods | chunked, streaming |
| block | One line, or a cut piece of a long line, in the buffered method | chunk |
| cut line | A line longer than the block limit, cut into several blocks | split line |
| alignment | Pairing of equal and differing block runs in the buffered method | matching (reserved for files) |
| hint | Directional indication of a difference, never a verdict | verdict, score, regression, improvement |
| identical | File pair with no difference of any kind | unchanged, same |
| result | One reported number with examples ([ydk:entity:model/MetricResult]) | metric (for a single number), stat |
| example | Concrete instance backing a result ([ydk:entity:model/Example]) | sample (except "sample data") |
| pairs file / items file / JSON report / HTML report | The four outputs (outputs.md) | log, dump |
| run | One execution of [ydk:cli-command:cli/compare] | job, session |
| worker | A process handling items in parallel | thread, executor |
| tool-error | A defined failure condition with a code and behaviour ([ydk:entity:model/ErrorRecord]) | exception (in specs), issue |
