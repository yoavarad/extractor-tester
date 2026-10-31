# Configuration reference

Passed with `compare --config PATH`. A YAML mapping; unknown keys, wrong types and out-of-range values stop the run with `INVALID_CONFIG` (exit code 1, file and reason on stderr). Both pipelines share the same keys, so there are no per-pipeline sections. Design: [config spec](specs/config.md). Source: `src/extractor_tester/config.py`.

Field names are dotted paths into each file entry of the input JSON.

## Required keys

| Key | Type | Meaning |
|---|---|---|
| `name_field` | string | Field holding the file name |
| `mime_primary_field` | string | Field holding the MIME main part (e.g. `application`) |
| `mime_secondary_field` | string | Field holding the MIME subtype (e.g. `pdf`) |

## Optional keys

| Key | Type | Default | Constraint | Meaning |
|---|---|---|---|---|
| `text_field` | string | `text` | | Field holding the base64 text |
| `generic_fields` | list of strings | `[]` | | Generic metadata paths (a path covers everything nested below it); other compared fields are format-specific |
| `ignore_fields` | list of strings | `[]` | | Paths never compared (timestamps, pipeline version). `fileId`, `parentId`, `rootId` and the text field are always added |
| `type_families_file` | string or null | null | | Path to the type-families YAML, relative to the config file. Without it every MIME type is its own family |
| `text_hash_chars` | int | 1000000 | >= 1 | Characters covered by the matching text hash |
| `full_compare_max_chars` | int | 100000 | >= 0 | Texts up to this size get the full comparison; larger ones the buffered one |
| `max_block_chars` | int | 4000 | >= 100 | Block size limit of the buffered comparison |
| `cut_lines_warn_pct` | number | 20 | 0..100 | Cut-line share above which a family is flagged |
| `containment_pct` | number | 98 | 0..100 | Containment threshold for the old-within-new / new-within-old relations |
| `examples_per_result` | int | 5 | >= 0 | Examples kept per reported result |
| `snippet_chars` | int | 300 | >= 20 | Longest example value, in characters |
| `max_item_bytes` | int | 52428800 | >= 1 | Items larger than this fail with `ITEM_TOO_LARGE` |
| `item_timeout_seconds` | int | 600 | >= 1 | Per-item time limit (`ITEM_TIMEOUT`) |

Values are strictly typed: `examples_per_result: "5"` is rejected.

## Example

```yaml
name_field: name
mime_primary_field: mimePrimary
mime_secondary_field: mimeSecondary
generic_fields: [author, title]
ignore_fields: [extractedAt, pipelineVersion]
type_families_file: type-families.yaml
examples_per_result: 5
```

## Type-families file

```yaml
families:
  email: [message/rfc822, application/vnd.ms-outlook]
  pdf: [application/pdf]
```

Only the `families` key is allowed. Each family is a list of MIME strings (compared lowercased); a MIME type in two families is an error.
