# ADR-009: Port to C# .NET 8

## Status
Proposed (2026-11-04)

## Context
The team knows C# far better than Python. The tool (`src/extractor_tester/`, ~2,800 LOC)
must keep exactly the same logic and outputs: same matching, metrics, error codes, exit
codes and byte-identical `pairs.jsonl`, `items.jsonl`, `report.json` and `report.html`.
Specs and component manifests describe behaviour, not language, and stay valid.

## Decision
- .NET 8 console app in a `dotnet/` folder, side by side with the Python package.
  Python stays the reference implementation until the C# golden outputs match; it is
  removed in a separate, later change.
- Dependencies:
  - YamlDotNet (compare-config and type-families loading)
  - Scriban (HTML template; port of the Jinja2 template in `render.py`)
  - System.CommandLine (CLI)
  - System.Text.Json (models and JSON output)
  - xUnit (tests)
- Hand-ported algorithms (no NuGet equivalent with identical results):
  - rapidfuzz `Indel` (distance + opcodes). The backtrack must reproduce rapidfuzz's
    tie-break order, because `_first_equal` in `compare/text.py` takes the first equal opcode.
  - `difflib.SequenceMatcher` with `autojunk=False` and `get_opcodes()`.
  - Both checked by differential tests against outputs captured from the Python libraries.
- Workers: child processes. The exe re-launches itself as a hidden `worker` subcommand and
  talks over stdin/stdout pipes. Keeps the ADR-006 behaviour: per-item timeout kill and
  replace (`ITEM_TIMEOUT`), dead worker replaced with only its item recorded
  (`WORKER_CRASHED`), worker startup timeout, results written in item-id order, worker
  processes for every worker count including 1. The test-hook env vars of `pool.py`
  (`EXTRACTOR_TESTER_TEST_*`) are ported so the same failure tests can run.
- `--profile` is dropped (supersedes ADR-007's cProfile flag). Stage timers and `run_stats`
  stay. For deeper profiling run `dotnet-trace` externally.

Parity rules (C# must match Python exactly):
- Strings are handled as Unicode code points (`int[]` / `Rune`), not UTF-16 code units.
  Affects char counts, `length_delta`, snippet cuts, the text-hash prefix (first N chars)
  and Indel.
- Whitespace predicate equals Python `str.split()` (includes `\x1c`-`\x1f`).
- Line splitter equals Python `str.splitlines()`: `\n`, `\r`, `\r\n`, `\v`, `\f`, `\x1c`,
  `\x1d`, `\x1e`, `\x85`, U+2028, U+2029.
- Strict base64 equal to `b64decode(validate=True)`: any whitespace or non-alphabet
  character is rejected.
- UTF-8 decode with counted replacements matching Python's `errors="replace"`: one U+FFFD
  per invalid sequence, same sequence boundaries.
- JSON output byte-identical to pydantic `model_dump_json`: floats as Python `repr`
  (`1.0`, `1e-05`), non-ASCII characters unescaped, field order as declared, `indent=2`
  for `report.json`, compact for the JSONL files.
- Tie order mirrored for `Counter.most_common`, `heapq` and `statistics.median` /
  `statistics.quantiles`.
- Rounding is round-half-even, as Python `round()`.

Acceptance:
- The C# run reproduces `tests/golden/expected/{pairs.jsonl,items.jsonl,report.json}`
  byte-for-byte (timings excluded, as in `tests/test_golden.py`) with `--workers 1` and
  `--workers 4`.
- The C# HTML render of the golden report equals `tests/fixtures/report.golden.html`.

## Alternatives considered
- Threads with cooperative cancel: cannot kill a hung item; a crash kills the whole run.
- Keep `--profile` via stage timings or a `dotnet-trace` wrapper: extra complexity for a
  rarely used flag.
- Replace Python in place or start a new repo: loses the live reference needed for
  differential and golden tests.
- BCL only (no YamlDotNet / Scriban / System.CommandLine): more hand-written code to own.

## Consequences
- Amends ADR-006 stack section (Python stack replaced by the .NET stack above once parity
  is reached; process model unchanged).
- Supersedes ADR-007's `--profile` flag; stage timers remain.
- Specs and component manifests unchanged.
- Two implementations live in the repo until golden parity; CI builds and tests both.
