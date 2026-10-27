from pathlib import Path
from typing import Annotated

import typer

from extractor_tester import runner

app = typer.Typer(help="Compare JSON outputs of two extraction pipelines.")


@app.callback()
def main() -> None:
    """Compare JSON outputs of two extraction pipelines."""


@app.command()
def compare(
    old: Annotated[Path, typer.Argument(metavar="OLD", help="Old output: file or folder")],
    new: Annotated[Path, typer.Argument(metavar="NEW", help="New output: same kind as OLD")],
    config: Annotated[Path, typer.Option("--config", help="Path to compare-config YAML")],
    out: Annotated[Path, typer.Option("--out", help="Output folder")] = Path("./compare-out"),
    overwrite: Annotated[
        bool, typer.Option("--overwrite", help="Allow writing into a non-empty output folder")
    ] = False,
    workers: Annotated[
        int | None, typer.Option("--workers", min=1, help="Worker processes (default: CPU count)")
    ] = None,
    profile: Annotated[
        bool, typer.Option("--profile", help="Write cProfile output per worker")
    ] = False,
) -> None:
    """Compare the outputs of the old and new pipelines and write the report."""
    raise typer.Exit(runner.run(old, new, config, out, overwrite, workers, profile))
