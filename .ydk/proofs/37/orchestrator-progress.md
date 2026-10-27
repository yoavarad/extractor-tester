# Task 37 orchestrator progress
## Plan (done)
- runner.run(old,new,config_path,out,overwrite,workers,profile)->int, sequential; per-item via _compare_one(item, config) (swap point for parallel).
- Fatal: ConfigError/LoaderError/OutputError -> "CODE: msg" stderr, exit 1; other -> INTERNAL_FAILURE + traceback, exit 1, no report.json.
- One-sided items -> ItemResult only-in-old/new. Ignored-entry count + discovery warnings appended to report.warnings.
- cli.compare -> raise typer.Exit(runner.run(...)). Tests: tests/test_runner.py via CliRunner; drop stub smoke test.
## Build (done)
- runner.py, cli.py, tests/test_runner.py (10 tests), test_smoke stub removed. 202 pass; ruff+ty clean.

## Review (done)
- 0 blocking, 3 minor: Report mutability (verified by tests), run_stats stage/slowest aggregation not populated (timers task), Ctrl-C/130 handling (later task).

