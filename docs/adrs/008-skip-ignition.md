# ADR-008: No ignition pack; Stage 01.5 skipped

## Status
Accepted (2026-10-26)

## Context
YDK Stage 01.5 (`ydk ignite`) generates a skeleton from an ignition pack and
registers YDK-TODOs in `.ydk/todos.yaml`. The only Python CLI pack in the catalog
(`python-typer-cli`) generates a REST-API client wrapper, which does not fit a
local file-comparison tool. `ydk task start` refuses to run when
`.ydk/todos.yaml` is missing, even when ignition was intentionally skipped.

## Decision
- Skip ignition. Project scaffolding is task #24 (Stage 02 plan).
- Commit an empty TODO registry (`todos: {}`, `next_id: 1`), written via YDK's own
  `TodoManager` so the format matches what YDK expects.
- Upstream YDK issue filed to support skipped ignition natively.

## Alternatives considered
- `ydk ignite --skip-verify` with no pack: fails or generates unwanted files.
- Install `python-typer-cli` anyway: REST client scaffolding to delete.

## Consequences
- `ydk todo` commands show no TODOs; task completion relies on acceptance
  criteria and tests, not TODO resolution.
- Remove the empty registry workaround once YDK supports skipped ignition.
