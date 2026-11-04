"""Dump Python reference outputs for the C# parity helpers (T-dn-decode).

Usage: python dump_parity_fixtures.py <out.json>   (run from repo root)
"""

import base64
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "src"))
from extractor_tester.decode import InvalidBase64Error, decode_text, normalize, text_hash  # noqa: E402

STRINGS = [
    "", " ", "a", "a b", "  a   b\t\nc  ", "x\x1cy\x1dz\x1e\x1fw", "a\x85b", "a b c",
    "a b　c d e​f", "a b c d", "line1\nline2\r\nline3\rline4",
    "a\vb\fc\x1cd\x1de\x1ef\x85g h i", "trail\n", "\n\n", "\r\n\r\n", "\r", "\n\r", "a\r\n",
    "\U0001F600 emoji \U0001F600\U0001F601", "\U00010000\U0010FFFFabc", "café 中文",
    "\x1f", "\x1b", "a\x1b b",
]
B64 = [
    "", "YQ==", "YWI=", "YWJj", "YQ=", "YQ", "YWJ", "Y", "YQ===", "====", "=", "YQ==YQ==", "YQ==\n",
    " YQ==", "Y Q==", "YW\nJj", "YWJj\n", "Y=Q=", "=YQ=", "YR==", "YWJjZA", "YWJjZA==", "!!!!", "YQ-_",
    "w6k=", "éééé", "4pyT", "8J+YgA==", "//8=", "gA==", "wA==", "/w==",
]
UTF8 = [
    b"", b"abc", "café".encode(), "\U0001F600".encode(), b"\xff", b"\xc0\xaf", b"\xc1\xbf", b"\xc2",
    b"\xe2\x82", b"\xe2\x82\xac", b"\xe2\x28\xa1", b"\xed\xa0\x80", b"\xed\xbf\xbf", b"\xed\x9f\xbf",
    b"\xf0\x80\x80\x80", b"\xf0\x90\x80", b"\xf0\x90\x80\x80", b"\xf4\x90\x80\x80", b"\xf4\x8f\xbf\xbf",
    b"\xf5\x80\x80\x80", b"\xf8\x88\x80\x80\x80", b"\x80", b"\x80\x80\x80", b"a\x80b\xffc",
    b"\xe0\x80\x80", b"\xe0\xa0", b"\xf0\x9f\x98", b"\xf0\x9f", b"\xf0", b"abc\xf0\x9f\x98",
    b"\xf0\x9f\x98a", b"\xe2\x82\xe2\x82\xac", b"\xc3\xa9\xc3", b"\xef\xbf\xbd", b"\xfe\xff",
]


def main(out: str) -> None:
    strings = []
    for s in STRINGS:
        n = normalize(s)
        strings.append({
            "s": s,
            "cp_len": len(s),
            "split": s.split(),
            "lines": s.splitlines(),
            "lines_keep": s.splitlines(True),
            "normalize": n,
            "hash_limits": {str(k): text_hash(n, k) for k in (1, 2, 3, 5, 1000)},
        })
    b64 = []
    for v in B64:
        try:
            text, cnt = decode_text(v)
            b64.append({"in": v, "ok": True, "text": text, "count": cnt})
        except InvalidBase64Error:
            b64.append({"in": v, "ok": False})
    utf8 = []
    for raw in UTF8:
        text, cnt = decode_text(base64.b64encode(raw).decode())
        utf8.append({"hex": raw.hex(), "text": text, "count": cnt})
    Path(out).write_text(
        json.dumps({"strings": strings, "base64": b64, "utf8": utf8}, indent=1), encoding="utf-8"
    )


if __name__ == "__main__":
    main(sys.argv[1])
