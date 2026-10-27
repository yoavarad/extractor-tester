import base64
import json
from pathlib import Path


def write_item(dir: Path, item_id: str, files: list[dict[str, object]], **fields: object) -> Path:
    """Write <item_id>.json; each file's "text" is base64-encoded (UTF-8)."""
    encoded = []
    for f in files:
        f = dict(f)
        if "text" in f:
            f["text"] = base64.b64encode(str(f["text"]).encode("utf-8")).decode("ascii")
        encoded.append(f)
    path = Path(dir) / f"{item_id}.json"
    path.write_text(json.dumps({"itemId": item_id, **fields, "files": encoded}), encoding="utf-8")
    return path
