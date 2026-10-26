# Scope

## In scope

- One command, [ydk:cli-command:cli/compare], for two item JSON files (one item) or two folders (many items paired by item id).
- The two config files [ydk:config-file:config/compare-config] and [ydk:config-file:config/type-families].
- Tiered file matching as described in matching.md.
- Every metric in metrics.md, with examples on every result.
- The pairs file, items file, JSON report and HTML report described in outputs.md.
- Parallel processing across worker processes, stage timing and optional profiling.
- Documentation: README, the narratives in this folder, config reference (written from the config manifests' keys), and the JSON report structure.

## Out of scope

Never built in this project:

- Running either pipeline, or reading the original binary files: the tool consumes outputs only.
- Deciding which pipeline is correct: there is no ground truth; the tool reports directional hints only.
- A web interface, server, or database: one person runs the tool locally and the JSON outputs serve scripting.
- Comparing three or more pipeline outputs at once: the question is always old against new.
- History across runs, trends, or dashboards: each run stands alone; the JSON report can be kept by the user.
- Field renames between pipelines: both share one schema.
- Exit codes that depend on how many differences were found: the tool informs, it does not gate.

## Deferred

Possible later, not now:

- Comparing list fields element by element, when whole-list changes prove too coarse in real reports.
- Matching moved files across the whole item, not only under the same parent, when a review of real reports finds that more than 10% of one-sided files in a type family are moved files.
- A command that compares two JSON reports from different runs, when the team requests run-to-run tracking.
- Spreadsheet export, when the team requests it.
