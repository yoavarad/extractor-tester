from pathlib import Path

import extractor_tester


def test_imports_worktree_src() -> None:
    """extractor_tester resolves under this checkout, not another install."""
    repo_root = Path(__file__).resolve().parent.parent
    assert Path(extractor_tester.__file__).resolve().is_relative_to(repo_root)
