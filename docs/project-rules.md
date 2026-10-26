# Project Rules

Conventions, preferences, and domain knowledge.

## Product Context

Local CLI tool comparing JSON outputs of two binary-file extraction pipelines
(old vs new). Input: one pair of item JSON files, or two folders of parallel
`<itemId>.json` files. Per item, files are matched across outputs and compared
on file type, generic metadata, format-specific metadata, extracted text, and
subfile tree. Output: report of differences grouped by file type, so per-extractor
regressions/improvements are visible. Must be simple, explainable, well documented.

## Domain Knowledge

- `itemId` is stable across pipelines; `fileId` / `parentId` are NOT — files must
  be matched by other means (see matching ADR).

## Conventions

- File matching strategy is pluggable: an ordered list of tiers behind one interface
  (ADR-001). Comparison/reporting code MUST NOT depend on which tier matched,
  only read the recorded tier label.
- Text hashing for matching: normalized (whitespace collapsed), first 1,000,000 chars,
  empty text never hash-matches.
- MIME type = `<primary>/<secondary>` joined from two config-named fields.
- `text` is base64 + UTF-8 (errors replaced, counted). Decode first, always.
- Bad input in one file/item never aborts the run: record error, continue.

## Process Rules

- Never use ydk features that need an API key (`ydk spec verify`, ai-code-review,
  `ydk task analyze-complexity`, `ydk memory extract/retrospective`). Deterministic
  checks only (`ydk component validate`, `ydk verify` non-AI plugins).
  Substitute each skipped AI feature with a subagent doing equivalent work
  (e.g. reviewer subagent applying .ydk/spec-reviewers/n01-n10 rubrics).

## Known Gotchas

- Windows: run ydk with `PYTHONUTF8=1`; otherwise `ydk component validate` fails with
  `UnicodeDecodeError: 'charmap' codec can't decode byte` on UTF-8 spec files.
- Specs are UTF-8; project code MUST open text files with explicit `encoding="utf-8"`.
