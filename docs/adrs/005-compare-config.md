# ADR-005: Single compare config; same schema both sides

## Status
Accepted (2026-10-26)

## Context
Exact JSON structure is not yet known. Known facts: both pipelines use the same
keys and nesting; fields may be nested; MIME type is split across two fields
(primary, secondary); `text` is base64-encoded.

## Decision
- One `compare-config.yaml` describes the shared schema (no per-pipeline sections,
  no field aliases).
- Fields are addressed by dotted path (e.g. `metadata.office.author`). Nested objects
  are flattened to dotted paths for per-field comparison. Arrays are compared as one
  whole value.
- Config keys: `name_field`, `mime_primary_field`, `mime_secondary_field`,
  `text_field`, `generic_fields` (all other non-ignored, non-structural fields =
  format-specific), `ignore_fields` (always includes structural `fileId`,
  `parentId`, `rootId`), plus thresholds from ADR-001/ADR-003 and examples count.
- MIME type = `<primary>/<secondary>` (e.g. `application` + `pdf` = `application/pdf`).
  Type family lookup (`type-families` config) keys on this joined value.
- `text` is base64-decoded before every comparison, hashing and matching step.
  Decoded bytes are read as UTF-8 with invalid bytes replaced; per-file
  `decode_errors` count is recorded. Invalid base64 = per-file error in the report,
  run continues.

## Alternatives considered
- Per-pipeline sections + aliases: not needed, schemas identical.
- Hardcoded field names: structure not yet known.
- Flatten arrays by index: noisy when list order changes; deferred.

## Consequences
- Adapting to the real structure later = edit config, not code.
