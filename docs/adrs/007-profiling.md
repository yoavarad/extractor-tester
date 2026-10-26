# ADR-007: Built-in profiling

## Status
Accepted (2026-10-26)

## Context
Future optimization needs data on where time goes.

## Decision
- Always-on stage timers (`time.perf_counter`), aggregated into a `run_stats`
  section of report.json and shown in report.html: load, decode, match,
  text-compare (split full / buffered / fast-path), aggregate, render; totals,
  plus text-compare time per type family; slowest N items.
- `--profile` flag: runs cProfile in each worker and writes
  `profile/worker-<n>.prof` (standard pstats format; viewable with snakeviz or
  `python -m pstats`).

## Alternatives considered
- External profilers only (py-spy): no in-report numbers, extra install.
- Always-on cProfile: ~2x slowdown.

## Consequences
- Stage timers add negligible overhead; cProfile only when asked.
