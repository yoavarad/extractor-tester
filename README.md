# extractor-tester

Compares the JSON outputs of two binary-file extraction pipelines (old vs new) and reports the differences grouped by file type family, with examples for every result.

Status: design stage. No code yet.

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
