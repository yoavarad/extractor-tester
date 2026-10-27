import re
import subprocess
import sys
from pathlib import Path

from extractor_tester.render import render

FIX = Path(__file__).parent / "fixtures"


def _html(tmp_path: Path) -> str:
    """Render the fixture report and return the HTML."""
    out = tmp_path / "report.html"
    render(FIX / "report.json", out)
    return out.read_text("utf-8")


def test_golden(tmp_path: Path) -> None:
    """Rendered fixture matches the checked-in golden HTML."""
    golden = (FIX / "report.golden.html").read_text("utf-8")
    assert _html(tmp_path) == golden


def test_no_external_urls(tmp_path: Path) -> None:
    """No http(s) src or href attributes."""
    assert not re.search(r"""(src|href)\s*=\s*["']?https?://""", _html(tmp_path))


def test_script_escaped(tmp_path: Path) -> None:
    """Untrusted field values are escaped."""
    html = _html(tmp_path)
    assert "&lt;script&gt;alert(1)&lt;/script&gt;" in html
    assert "<script>alert(1)" not in html


def test_import_graph() -> None:
    """Render imports no compare, matching or aggregate module."""
    code = (
        "import sys, extractor_tester.render\n"
        "banned = ('compare', 'matching', 'aggregate')\n"
        "print([m for m in sys.modules if m.startswith('extractor_tester.')"
        " and m.split('.')[1] in banned])"
    )
    out = subprocess.run([sys.executable, "-c", code], capture_output=True, text=True, check=True)
    assert out.stdout.strip() == "[]"
