using System.Text.Json.Serialization;

namespace ExtractorTester.Models;

// Port of src/extractor_tester/models.py. Property order and names (snake_case via PyJson)
// mirror the pydantic models exactly. Literal-typed fields are plain strings.
// Frozen models use init-only setters; the others are mutable like their Python originals.

/// <summary>Tool-error codes; serialized by their string code.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ErrorCode>))]
public enum ErrorCode
{
    DUPLICATE_FILE_ID,
    EMPTY_INPUT,
    INPUT_NOT_FOUND,
    INTERNAL_FAILURE,
    INVALID_BASE64,
    INVALID_CONFIG,
    INVALID_JSON,
    ITEM_ID_MISMATCH,
    ITEM_PROCESSING_FAILED,
    ITEM_TIMEOUT,
    ITEM_TOO_LARGE,
    MISSING_FIELD,
    MIXED_INPUT_KINDS,
    MULTIPLE_ROOTS,
    ORPHAN_FILE,
    OUTPUT_EXISTS,
    OUTPUT_WRITE_FAILED,
    PARENT_CYCLE,
    ROOT_NOT_FOUND,
    WORKER_CRASHED,
}

/// <summary>One occurrence of a tool-error.</summary>
public sealed record ErrorRecord
{
    public required ErrorCode Code { get; init; }
    public required string Scope { get; init; }
    public required string Severity { get; init; }
    public string? ItemId { get; init; }
    public string? Side { get; init; }
    public string? FilePath { get; init; }
    public required string Message { get; init; }
}

/// <summary>One decoded file in an item tree.</summary>
public sealed record FileRecord
{
    public required string FileId { get; init; }
    public required string ParentId { get; init; }
    public required long Index { get; init; }
    public required string Name { get; init; }
    public required string Mime { get; init; }
    public required string Family { get; init; }
    public required string Path { get; init; }
    public required Dictionary<string, object?> Fields { get; init; }
    public required string Text { get; init; }
    public string? TextHash { get; init; }
    public required long DecodeErrors { get; init; }
    public required List<FileRecord> Children { get; init; }
    public required List<string> MissingFields { get; init; }
}

/// <summary>One pipeline's output for one item.</summary>
public sealed record Item
{
    public required string ItemId { get; init; }
    public required string Side { get; init; }
    public required Dictionary<string, object?> Metadata { get; init; }
    public required List<FileRecord> Files { get; init; }
    public required string SourcePath { get; init; }
}

/// <summary>Match outcome for one file.</summary>
public sealed record FilePair
{
    public required string Kind { get; init; }
    public FileRecord? Old { get; init; }
    public FileRecord? New { get; init; }
    public string? Tier { get; init; }
    public string TieBreak { get; init; } = "none";
    public string? UnmatchedReason { get; init; }
}

/// <summary>Comparison of one dotted-path field.</summary>
public sealed record FieldDiff
{
    public required string Path { get; init; }
    public required string Group { get; init; }
    public required string Status { get; init; }
    public object? OldValue { get; init; }
    public object? NewValue { get; init; }
    public string? OldType { get; init; }
    public string? NewType { get; init; }
}

/// <summary>Text comparison result for one pair.</summary>
public sealed record TextFacts
{
    public required bool RawEqual { get; init; }
    public required bool NormalizedEqual { get; init; }
    public required string Method { get; init; }
    public double? EditSimilarity { get; init; }
    public double? Jaccard { get; init; }
    public double? OldFoundInNew { get; init; }
    public double? NewFoundInOld { get; init; }
    public string? Relation { get; init; }
    public required long OldChars { get; init; }
    public required long NewChars { get; init; }
    public required long LengthDelta { get; init; }
    public required bool OldEmpty { get; init; }
    public required bool NewEmpty { get; init; }
    public required long LinesTotal { get; init; }
    public required long LinesCut { get; init; }
    public required double LinesCutPct { get; init; }
    public required long OversizedRuns { get; init; }
    public required List<Dictionary<string, object?>> Snippets { get; init; }
    public required double Seconds { get; init; }
}

/// <summary>All facts about one file pair.</summary>
public sealed record PairFacts
{
    public required string ItemId { get; init; }
    public required string Kind { get; init; }
    public string? PathOld { get; init; }
    public string? PathNew { get; init; }
    public required string Family { get; init; }
    public string? Tier { get; init; }
    public required string TieBreak { get; init; }
    public string? MimeOld { get; init; }
    public string? MimeNew { get; init; }
    public bool? MimeEqual { get; init; }
    public bool? FamilyEqual { get; init; }
    public required List<FieldDiff> FieldDiffs { get; init; }
    public required List<string> FieldsSame { get; init; }
    public TextFacts? Text { get; init; }
    public long? ChildrenOld { get; init; }
    public long? ChildrenNew { get; init; }
    public long? ChildrenOnlyOld { get; init; }
    public long? ChildrenOnlyNew { get; init; }
    public required List<string> Hints { get; init; }
    public required bool Identical { get; init; }
    public required List<ErrorRecord> Errors { get; init; }
    public long? DecodeErrorsOld { get; init; }
    public long? DecodeErrorsNew { get; init; }
}

/// <summary>Outcome for one item.</summary>
public sealed record ItemResult
{
    public required string ItemId { get; init; }
    public required string Status { get; init; }
    public required List<FieldDiff> MetadataDiffs { get; init; }
    public required long FilesIdentical { get; init; }
    public required long FilesChanged { get; init; }
    public required long FilesOnlyOld { get; init; }
    public required long FilesOnlyNew { get; init; }
    public required List<ErrorRecord> Errors { get; init; }
    public required double Seconds { get; init; }
    public required Dictionary<string, double> StageSeconds { get; init; }
}

/// <summary>A concrete instance backing a reported result.</summary>
public sealed record Example
{
    public required string ItemId { get; set; }
    public string? PathOld { get; set; }
    public string? PathNew { get; set; }
    public object? OldValue { get; set; }
    public object? NewValue { get; set; }
    public double? Similarity { get; set; }
    public string? Note { get; set; }
}

/// <summary>One reported number with its examples.</summary>
public sealed record MetricResult
{
    public required string Key { get; set; }
    public required long Count { get; set; }
    public required long Denominator { get; set; }
    public required double Rate { get; set; }
    public required List<Example> Examples { get; set; }
}

/// <summary>Everything reported for one type family.</summary>
public sealed record FamilyAggregate
{
    public required string Family { get; set; }
    public required long Pairs { get; set; }
    public required MetricResult OnlyInOld { get; set; }
    public required MetricResult OnlyInNew { get; set; }
    public required MetricResult Identical { get; set; }
    public required MetricResult MimeChanged { get; set; }
    public required MetricResult FamilyChanged { get; set; }
    public required List<MetricResult> Fields { get; set; }
    public required Dictionary<string, object?> EditSimilarity { get; set; }
    public required Dictionary<string, object?> Jaccard { get; set; }
    public required List<MetricResult> Relations { get; set; }
    public required Dictionary<string, long> Methods { get; set; }
    public required double LinesCutPct { get; set; }
    public required bool CutLinesWarning { get; set; }
    public required List<MetricResult> Tiers { get; set; }
    public required List<MetricResult> Hints { get; set; }
    public required MetricResult DecodeErrors { get; set; }
    public required List<MetricResult> FileErrors { get; set; }
}

/// <summary>Timing and profiling numbers of the run.</summary>
public sealed record RunStats
{
    public required double WallSeconds { get; set; }
    public required long Workers { get; set; }
    public required Dictionary<string, double> StageSeconds { get; set; }
    public required Dictionary<string, double> TextSecondsByFamily { get; set; }
    public required List<Dictionary<string, object?>> SlowestItems { get; set; }
    public required long WorkersReplaced { get; set; }
    public string? ProfileDir { get; set; }
}

/// <summary>Content of report.json.</summary>
public sealed record Report
{
    public required long SchemaVersion { get; set; }
    public required string ToolVersion { get; set; }
    public required DateTimeOffset StartedAt { get; set; }
    public required Dictionary<string, object?> Inputs { get; set; }
    public required Dictionary<string, object?> Config { get; set; }
    public required Dictionary<string, long> Totals { get; set; }
    public required MetricResult ItemsOnlyOld { get; set; }
    public required MetricResult ItemsOnlyNew { get; set; }
    public required List<MetricResult> ItemFields { get; set; }
    public required List<FamilyAggregate> Families { get; set; }
    public required List<MetricResult> Errors { get; set; }
    public required List<string> Warnings { get; set; }
    public required RunStats RunStats { get; set; }
}
