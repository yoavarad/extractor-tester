# Initial plan summary (Stage 02)

Source: `docs/plans/initial-batch.yaml`, created with `ydk task create-batch`
(placeholder-to-issue map in `.ydk/batch-mapping.json`).
Totals: 7 epics, 13 stories, 29 tasks (#24-#52).

## Epics
| Epic | Stories | Tasks |
|---|---|---|
| #4 Foundation: scaffolding, models, config | #11, #12 | #24-#27 |
| #5 Thin slice: load, match by name, field diff, report.json | #13, #14, #15 | #28-#37 |
| #6 Text metrics | #16 | #38-#41 |
| #7 Remaining matching tiers | #17 | #42 |
| #8 Scale: parallel runner, timeout, profiling, benchmarks | #18, #19, #20 | #43-#48 |
| #9 HTML report | #21 | #49, #50 |
| #10 Golden tests and documentation | #22, #23 | #51, #52 |

## Demo increments
1. After #37: `extractor-tester compare OLD NEW --config C` writes pairs/items/report.json
   (name-tier matching, field diffs, sequential runner).
2. After #41: text metrics and relation labels in the report.
3. After #42: all three matching tiers.
4. After #44/#45: parallel workers, timeout, crash recovery, progress, Ctrl-C.
5. After #50: report.html. Then #46/#47 profiling, #48 benchmarks, #51 golden, #52 docs.

## Schedule (`ydk task plan-waves --agents 3`)
11 waves. Dependency-only critical path (11 tasks):
#24 -> #25 -> #28 -> #31 -> #32 -> #34 -> #37 -> #43 -> #44 -> #46 -> #48

## Checks
- `ydk task validate-dag`: valid, no cycles.
- `ydk task component-coverage --strict`: all 67 components covered.
- `ydk task coverage`: 10/10 spec files have stories.
- Complexity: judged by hand (no API key). Each task is scoped to about 1 day or less and at most 4 modules.

## First ready task
#24 Scaffold project: uv pyproject, package layout, typer stub, test/lint config.

## Config
Turned off the `cli-error-handling` and `cli-output-format` verification plugins. They assume a REST ApiError and a `--json` flag, and this tool has neither.
