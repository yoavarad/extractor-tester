# .NET port

A C# .NET 8 port of the tool lives in `dotnet/`, side by side with the Python package. Decision and parity rules: [ADR-009](adrs/009-dotnet-port.md). Plan: `docs/plans/dotnet-port-batch.yaml`. Python stays the reference implementation; the port reproduces the same matching, metrics, error codes, exit codes and output files (the golden outputs in `tests/golden/expected/` and `tests/fixtures/report.golden.html`).

## Layout

- `dotnet/ExtractorTester.sln`: solution.
- `dotnet/src/ExtractorTester/`: the console app (one source file per Python module, e.g. `Loader.cs`, `Matching.cs`, `Compare.cs`, `Pool.cs`, `Render.cs`).
- `dotnet/tests/ExtractorTester.Tests/`: xUnit tests, including golden and differential tests against Python outputs.

## Build, test, run

Prerequisite: the .NET 8 SDK.

```
dotnet build dotnet/ExtractorTester.sln --configuration Release     # or Debug
dotnet test dotnet/ExtractorTester.sln --configuration Release      # Debug works too
```

Run the built exe (`ExtractorTester` without `.exe` on Linux/macOS; or `dotnet run --project dotnet/src/ExtractorTester -c Release -- ...`):

```
dotnet/src/ExtractorTester/bin/Release/net8.0/ExtractorTester.exe compare OLD NEW --config compare-config.yaml --out compare-out
```

Options match the Python CLI except `--profile`: `--config` (required), `--out` (default `./compare-out`), `--overwrite`, `--workers N` (N >= 1; default is the CPU count). Input format, config and outputs are identical; see [Configuration keys](config-reference.md) and [JSON report structure](report-format.md).

Exit codes: 0 success, 1 fatal error (code and reason on stderr), 2 usage error, 130 interrupted.

Workers are child processes: the exe re-launches itself as a hidden `worker` subcommand and talks to it over stdin/stdout, so per-item timeout and crash recovery work as in Python.

## CI

The `.NET build and test` job (ubuntu-latest) in `.github/workflows/ci.yml` runs `dotnet build` and `dotnet test` on `dotnet/ExtractorTester.sln` in Release, next to the Python lint and test jobs.

## Intentional deviations from Python

- No profiling: `--profile` is dropped (ADR-009 supersedes ADR-007's flag), so `run_stats.profile_dir` is always null. Stage timers and `run_stats` stay. For deeper profiling run `dotnet-trace` externally.
- Usage errors (exit 2) use click's plain format (`Usage:`, `Try ... --help`, `Error: ...`), not typer's rich box.
- `INTERNAL_FAILURE` also prints the .NET stack trace on stderr, and an unmapped .NET exception reports its .NET class name (Python reports the Python class name).
- The default `--workers` is `Environment.ProcessorCount`, which respects container CPU limits.
- YAML syntax-error text in `INVALID_CONFIG` comes from YamlDotNet, so it differs from PyYAML's.
- `report.html` always uses LF line endings.
- Worker and reader threads use 256 MB stacks, so deeply nested items do not overflow the stack.
- Nesting depth: Python fails items nested deeper than about 496 levels in the comparison (`RecursionError`). The port compares them instead of failing them (a user-approved decision). The loader nesting limit (about 2997) still matches Python.
