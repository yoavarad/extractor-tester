# extractor-tester

Compares the JSON outputs of two binary-file extraction pipelines (old vs new) and reports the differences grouped by file type family, with examples for every result.

Status: design stage. No code yet.

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
