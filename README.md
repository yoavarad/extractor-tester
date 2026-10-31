# extractor-tester

Compares the JSON outputs of two binary-file extraction pipelines (old vs new) and reports the differences grouped by file type family, with examples for every result.

Status: implemented. The `compare` command runs end to end and writes `report.json`, `report.html`, `pairs.jsonl` and `items.jsonl`.

## Installation and quick start

Prerequisites: Python 3.12 and [uv](https://docs.astral.sh/uv/).

```
uv sync                      # create .venv and install runtime + dev dependencies
uv run extractor-tester --help
```

Run a comparison (OLD and NEW are each a file or a folder of the same kind; the compare-config YAML is required, see [Configuration](docs/specs/config.md)):

```
uv run extractor-tester compare OLD NEW --config compare-config.yaml --out compare-out
```

Optional flags: `--overwrite` (write into a non-empty output folder), `--workers N` (default: CPU count), `--profile` (cProfile output per worker).

Run the tests (slow benchmarks are excluded by default) and lint:

```
uv run pytest -q
uv run ruff check
```

## Input format

OLD and NEW are each one `.json` file or a folder of top-level `*.json` files (items are paired by file name without `.json`; `itemId` inside must equal it). Each item file is an object with `itemId` and a `files` list. Every file entry has `fileId`, `parentId` (its own id for the root), `rootId`, the fields named in the config for name and MIME parts, and `text` as base64 (UTF-8). Any other keys are compared as metadata.

## Output

`--out` (default `./compare-out`) receives `report.json` (aggregates and capped examples), `report.html` (self-contained, rendered from `report.json`), `pairs.jsonl` (one line per file pair), `items.jsonl` (one line per item) and, with `--profile`, `profile/worker-<n>.prof`. Exit code: 0 success, 1 fatal error (code and reason on stderr), 130 interrupted.

Reference: [Configuration keys](docs/config-reference.md) and [JSON report structure](docs/report-format.md).

## Documentation

- [Overview](docs/specs/overview.md): problem, success criteria, end-to-end walkthrough
- [Architecture](docs/specs/architecture.md)
- [Matching](docs/specs/matching.md): how files are paired across pipelines
- [Metrics](docs/specs/metrics.md): what is compared, and the text comparison algorithm
- [Configuration](docs/specs/config.md)
- [Outputs](docs/specs/outputs.md): output files, HTML report, profiling
- [Errors and edge cases](docs/specs/errors-and-edge-cases.md)
- [Scope](docs/specs/scope.md)
- [Testing strategy](docs/specs/testing-strategy.md)
- [Glossary](docs/specs/glossary.md)
- Decisions: [docs/adrs/](docs/adrs/)
- Conventions: [docs/project-rules.md](docs/project-rules.md)
- Component manifests (machine-readable spec): `.ydk/components/`

## Process

Managed with YDK: specs, then skeleton, then tasks, then implementation. Every change goes through a reviewed PR.
