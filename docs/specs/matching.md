# Matching

Decision record: docs/adrs/001-file-matching-by-tree-path.md.

## Why matching is needed

Each pipeline assigns its own file ids, so the same extracted subfile has different ids in old and new. Only the item id is shared. Before anything can be compared, every file of the old [ydk:entity:model/Item] must be paired with its counterpart in the new one. The result of matching is a list of [ydk:entity:model/FilePair] records: paired, only-in-old, or only-in-new. [ydk:core-module:core/matcher] owns this step.

## The tree

[ydk:core-module:core/tree-builder] turns the flat list of files into a tree. The root is the single file whose file id, parent id and root id all equal the item id. Every other file hangs under the file its parent id names; children keep the order they have in the item's file list. Each [ydk:entity:model/FileRecord] gets a tree path made of file names from the root down; when a name repeats among siblings, the path marks its occurrence number so every path is unique and readable.

Problems with the tree are item or file errors, never crashes: no root ([ydk:tool-error:errors/root-not-found]), several roots ([ydk:tool-error:errors/multiple-roots]), a repeated file id ([ydk:tool-error:errors/duplicate-file-id]), a parent link loop ([ydk:tool-error:errors/parent-cycle]), or a parent id pointing nowhere ([ydk:tool-error:errors/orphan-file], which excludes that file and its subtree but keeps the rest of the item).

## The walk

The two roots are always paired with each other (tier "root"). From there the matcher works top-down, one paired parent at a time: the children of the old parent are matched only against the children of the new parent. It never searches elsewhere in the tree. Once a pair is found, its children are matched in turn.

When a file stays unpaired, all its descendants are reported one-sided with the reason "parent unpaired". They are not searched for elsewhere.

## Tiers

Within one parent pair, unpaired children pass through an ordered list of tiers. Each tier implements [ydk:contract:core/MatchTier]: it receives the still-unpaired children of both sides, pairs some of them one-to-one, and passes the rest on. A tier only pairs when the pairing is unambiguous.

1. **Name** — [ydk:core-module:core/tier-name]. Files with the identical name (case-sensitive) are paired. When several siblings share a name, the tie is broken first by identical text hash and type family, then by order of appearance; if one side has more of that name, the extra files pass on to the next tier. Files with an empty name never pair in this tier. Each pair records how its tie was broken, so position-based pairs are visible in the report.
2. **Text hash** — [ydk:core-module:core/tier-text-hash]. Files whose normalized text (whitespace collapsed, first million characters) has the same hash and that belong to the same type family are paired, provided exactly one such file exists on each side. Files with empty text never match here; otherwise every image would match every other image. This tier catches files the new pipeline renamed.
3. **Type family** — [ydk:core-module:core/tier-type-family]. If exactly one unpaired file of a type family remains on each side, they are paired. This is the weakest tier.

Whatever remains is one-sided with the reason "no candidate".

Every pair records its tier. The report shows the tier breakdown per type family so a reader knows how much of the comparison rests on weaker evidence.

## Type family

A type family groups MIME types that mean the same kind of file for this purpose, defined in [ydk:config-file:config/type-families] and looked up by [ydk:core-module:core/type-family]. A file's MIME type is its two MIME parts joined with a slash, lowercased. A MIME type not listed in any family forms its own family; a file with no MIME at all is in the family "unknown". The same family decides the report section a file pair appears in: paired and only-in-old files use the old file's family, only-in-new files use the new file's family.

## Tier list as the single source of matching rules

The tier list is the only place that knows the matching rules ([ydk:req:rules/matcher-pluggable]). Adding, removing or reordering tiers changes nothing in comparison, aggregation or rendering, which read only the recorded tier label.

## Worked example

Old item: root email with attachments "invoice.pdf", "image001.png", "image001.png". New item: root email with attachments "Invoice 2026.pdf", "image001.png", "image001.png", "logo.png".

- Roots pair (tier root).
- The two "image001.png" on each side: same name, several per side. Their texts are empty, so no hash tie-break; they pair first-with-first, second-with-second (tier name, tie broken by position).
- "invoice.pdf" and "Invoice 2026.pdf": names differ; texts identical after normalization, both in the pdf family, one on each side, so they pair (tier text-hash).
- "logo.png" remains alone in new; no png is left in old, so tier type-family cannot pair it. It is only-in-new.
