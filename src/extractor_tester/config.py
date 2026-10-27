"""compare-config + type-families loader producing one validated CompareConfig."""

from pathlib import Path
from typing import Annotated, Any

import yaml
from pydantic import BaseModel, ConfigDict, Field, PrivateAttr, ValidationError

STRUCTURAL_FIELDS = ("fileId", "parentId", "rootId")


class ConfigError(Exception):
    """INVALID_CONFIG: names the file and the reason."""

    code = "INVALID_CONFIG"

    def __init__(self, path: Path | str, reason: str) -> None:
        """Store path and reason; message names both."""
        self.path = str(path)
        self.reason = reason
        super().__init__(f"Invalid config {self.path}: {reason}")


class CompareConfig(BaseModel):
    """Validated compare-config; see docs/specs/config.md."""

    model_config = ConfigDict(extra="forbid", strict=True)

    name_field: str
    mime_primary_field: str
    mime_secondary_field: str
    text_field: str = "text"
    generic_fields: list[str] = []
    ignore_fields: list[str] = []
    type_families_file: str | None = None
    text_hash_chars: Annotated[int, Field(ge=1)] = 1_000_000
    full_compare_max_chars: Annotated[int, Field(ge=0)] = 100_000
    max_block_chars: Annotated[int, Field(ge=100)] = 4000
    cut_lines_warn_pct: Annotated[float, Field(ge=0, le=100)] = 20
    containment_pct: Annotated[float, Field(ge=0, le=100)] = 98
    examples_per_result: Annotated[int, Field(ge=0)] = 5
    snippet_chars: Annotated[int, Field(ge=20)] = 300
    max_item_bytes: Annotated[int, Field(ge=1)] = 52_428_800
    item_timeout_seconds: Annotated[int, Field(ge=1)] = 600
    # lowercased MIME -> family name; built from type_families_file, not a config key
    _family_map: dict[str, str] = PrivateAttr(default_factory=dict)

    def set_family_map(self, mapping: dict[str, str]) -> None:
        """Set the MIME to family map (not a config key)."""
        self._family_map = mapping

    @property
    def family_map(self) -> dict[str, str]:
        """Return lowercased MIME to family name map."""
        return self._family_map

    def model_post_init(self, _ctx: object) -> None:
        """Add structural ids and the text field to ignore_fields."""
        for f in (*STRUCTURAL_FIELDS, self.text_field):
            if f not in self.ignore_fields:
                self.ignore_fields.append(f)


def _read_yaml(path: Path) -> dict[str, Any]:
    try:
        text = path.read_text(encoding="utf-8")
    except OSError as e:
        raise ConfigError(path, f"cannot read file ({e.strerror or e})") from e
    try:
        data = yaml.safe_load(text)
    except yaml.YAMLError as e:
        raise ConfigError(path, f"YAML parse error: {e}") from e
    if not isinstance(data, dict):
        raise ConfigError(path, "top level must be a mapping")
    return data


def _load_families(path: Path) -> dict[str, str]:
    data = _read_yaml(path)
    extra = set(data) - {"families"}
    if extra:
        raise ConfigError(path, f"unknown key(s): {', '.join(sorted(map(str, extra)))}")
    families = data.get("families")
    if not isinstance(families, dict):
        raise ConfigError(path, "'families' must be a mapping of family name to list of MIME types")
    result: dict[str, str] = {}
    for family, mimes in families.items():
        if not isinstance(mimes, list) or not all(isinstance(m, str) for m in mimes):
            raise ConfigError(path, f"family '{family}' must be a list of MIME strings")
        for mime in mimes:
            key = mime.lower()
            if key in result and result[key] != str(family):
                raise ConfigError(
                    path,
                    f"MIME type '{key}' listed in two families: '{result[key]}' and '{family}'",
                )
            result[key] = str(family)
    return result


def load_config(path: Path | str) -> CompareConfig:
    """Load and validate compare-config (and its type-families file) or raise ConfigError."""
    path = Path(path)
    data = _read_yaml(path)
    try:
        config = CompareConfig.model_validate(data)
    except ValidationError as e:
        reason = "; ".join(
            f"{'.'.join(map(str, err['loc'])) or '<root>'}: {err['msg']}" for err in e.errors()
        )
        raise ConfigError(path, reason) from e
    if config.type_families_file is not None:
        config.set_family_map(_load_families(path.parent / config.type_families_file))
    return config
