# ADR-006: Tech stack and scale design

## Status
Accepted (2026-10-26)

## Context
Runs cover 10k-100k files. Tool must stay simple and explainable.

## Decision
Stack (locked):
- Python 3.12, uv
- typer (CLI), PyYAML + pydantic v2 (config validation, report.json schema),
  rapidfuzz (character-level similarity), stdlib difflib (block alignment; ADR-003 amendment), Jinja2 (single HTML template)
- pytest with hand-built JSON fixtures; ruff + ty (YDK python-quality)

Scale:
- Equal-text fast path: identical normalized text hash => relation `equal`, skip similarity.
- Items processed in parallel via `multiprocessing` pool (`--workers`, default CPU count).
  Workers return compact per-pair facts, never full texts.
- Outputs: `pairs.jsonl` (one line per file pair, streamed), `items.jsonl` (one
  line per item, incl. item errors; added during spec writing), `report.json`
  (aggregates + capped examples), `report.html` (rendered from report.json).
- Progress line on stderr.

## Alternatives considered
- Sequential only: >1 h worst case at 100k files.
- All pair facts inside report.json: ~100 MB, unusable HTML.
- asyncio/threads: work is CPU-bound; processes needed.
- No database: not needed at this scale.

## Consequences
- Item-level parallelism means one huge item bounds wall time.
