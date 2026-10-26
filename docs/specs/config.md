# Configuration

Decision record: docs/adrs/005-compare-config.md.

The exact JSON layout of the pipeline outputs is not fixed yet, so nothing about it is hardcoded. Two files describe it. [ydk:core-module:core/config-loader] reads both, applies defaults and produces one validated [ydk:entity:model/CompareConfig]. Any problem — file missing, bad YAML, unknown key, wrong value type, a value out of range, one MIME type listed in two families — stops the run before any work with [ydk:tool-error:errors/invalid-config], naming the file and the reason.

## compare-config

[ydk:config-file:config/compare-config] is passed with the required config option of [ydk:cli-command:cli/compare]. Both pipelines share the same keys and nesting, so one description covers both sides; there are no per-pipeline sections and no field renames.

It names, by dotted path, the field holding the file name, the two fields holding the MIME parts, and the field holding the base64 text. It lists which fields are generic metadata (a listed path covers everything nested below it; every other compared field is format-specific) and which fields are never compared, such as extraction timestamps or pipeline version. The structural id fields and the text field are always excluded from metadata comparison, whether listed or not.

It also holds every threshold used elsewhere, each with a default: how many characters the matching hash covers, the full-comparison threshold, the block limit, the cut-line warning threshold, the containment threshold, how many examples each result carries and how long example values may be, the item size limit, and the per-item timeout. The manifest lists every key with its type, default and constraint.

## type-families

[ydk:config-file:config/type-families] maps MIME types to type families. It is optional; the compare-config points to it by a path relative to itself. Without it every MIME type is its own family. Its role in matching and grouping is described in matching.md.

## Effective config in the report

The [ydk:entity:model/Report] records the effective config, defaults included, so every report states the settings that produced it.
