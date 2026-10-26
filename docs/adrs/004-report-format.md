# ADR-004: Report as JSON + self-contained HTML

## Status
Accepted (2026-10-26)

## Context
Reports carry many examples, including text diff snippets. They must be
readable by humans and usable by scripts (e.g. diffing two runs).

## Decision
- `report.json` is the single source of truth: every computed fact.
- `report.html` is rendered from `report.json` only: one file, no server, no
  CDN, no JS framework. Inline CSS + minimal vanilla JS for sorting tables and
  collapsing example sections. Side-by-side old/new values, highlighted diff snippets.
- No metric is computed in the rendering step.

## Alternatives considered
- JSON + Markdown: simpler, but long diff snippets and many examples become unreadable.
- JSON + Excel: good for filtering numbers, poor for text diffs.

## Consequences
- HTML renderer can be rewritten without touching comparison logic.
- report.json schema is a contract and must be documented.
