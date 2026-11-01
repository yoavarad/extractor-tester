# JSON report structure

`report.json` in the output folder is the source of truth; `report.html` is rendered from it. It holds aggregates and capped examples only, so its size does not grow with the number of files. It is written atomically (temp file, then rename). Models: `Report` and related classes in `src/extractor_tester/models.py`. A sample is `tests/fixtures/report.json`.

## Top level

| Field | Meaning |
|---|---|
| `schema_version` | Integer, currently `1` |
| `tool_version` | Installed package version |
| `started_at` | UTC timestamp of the run start |
| `inputs` | `old`, `new` (paths), `mode` (`file` or `folder`), `config` (config path) |
| `config` | Effective config with defaults applied (see [config reference](config-reference.md)) |
| `totals` | Counts: `items`, `compared`, `only_in_old`, `only_in_new`, `error`, `pairs` |
| `items_only_old`, `items_only_new` | A metric result each: items present on one side only |
| `item_fields` | List of metric results, one per item-level field that changed |
| `families` | One entry per type family, sorted by name (below) |
| `errors` | Metric results, one per error code (e.g. `ITEM_TIMEOUT`) |
| `warnings` | List of strings |
| `run_stats` | Timing and profiling numbers (below) |

## Metric result

Every reported number has this shape:

```json
{"key": "author:changed", "count": 1, "denominator": 10, "rate": 0.1, "examples": []}
```

`examples` holds up to `examples_per_result` entries: `item_id`, `path_old`, `path_new`, `old_value`, `new_value`, `similarity`, `note` (values may be null). Keys of `fields` and `item_fields` results are `<path>:<status>` (e.g. `author:changed`). Selection is deterministic: lowest similarity first for similarity results, otherwise the first occurrences in item-id order, then matcher order within an item. A `note` is cut at `snippet_chars`. Error notes name the item file by its file name only (e.g. `new file g11-malformed-json.json is not a valid item: ...`); the `old`/`new` word says which input it came from, so notes never contain local absolute paths.

## Family entry

| Field | Meaning |
|---|---|
| `family`, `pairs` | Family name and number of file pairs |
| `only_in_old`, `only_in_new`, `identical`, `mime_changed`, `family_changed` | Metric results |
| `fields` | Metric results per changed metadata field |
| `edit_similarity` | `median`, `p10`, `min` |
| `jaccard` | `median`, `p10`, `min` |
| `relations` | Metric results per text relation: `equal`, `old-within-new`, `new-within-old`, `overlapping`, `disjoint` |
| `methods` | Pairs per text method: `fast-path`, `full`, `buffered` |
| `lines_cut_pct`, `cut_lines_warning` | Share of lines cut in buffered comparison, and whether it exceeds `cut_lines_warn_pct` |
| `tiers` | Metric results per matching tier: `root`, `name`, `text-hash`, `type-family` |
| `hints` | `new-more-text`, `new-less-text`, `new-filled-field`, `new-emptied-field`, `new-more-subfiles`, `new-fewer-subfiles` |
| `decode_errors` | Metric result |
| `file_errors` | Metric results per file-scope error code |

## run_stats

`wall_seconds`, `workers`, `stage_seconds` (keys `load`, `decode`, `tree`, `match`, `field_compare`, `text_fast`, `text_full`, `text_buffered`, `aggregate`, `render`), `text_seconds_by_family`, `slowest_items` (the ten slowest: `item_id`, `seconds`, `status`), `workers_replaced`, and `profile_dir` (`"profile"` when run with `--profile`, else null).

## Other output files

`pairs.jsonl`: one `PairFacts` object per line (item id, paths, family, tier, field diffs, text facts, hints, errors). `items.jsonl`: one `ItemResult` per line (status `compared`, `only-in-old`, `only-in-new` or `error`, metadata diffs, file counts, errors, seconds).
