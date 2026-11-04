using ExtractorTester.Config;

namespace ExtractorTester.Families;

/// <summary>MIME composition and type-family lookup (port of families.py).</summary>
public static class FamilyResolver
{
    /// <summary>Join parts as 'primary/secondary' lowercased; null if either is missing or blank.</summary>
    public static string? Mime(string? primary, string? secondary)
    {
        if (primary is null || secondary is null) return null;
        var p = primary.Trim().ToLowerInvariant();
        var s = secondary.Trim().ToLowerInvariant();
        if (p.Length == 0 || s.Length == 0) return null;
        return $"{p}/{s}";
    }

    /// <summary>Configured family for a MIME, the MIME itself if unlisted, or 'unknown'.</summary>
    public static string Family(string? mime, CompareConfig config)
    {
        if (string.IsNullOrEmpty(mime)) return "unknown";
        var key = mime.ToLowerInvariant();
        return config.FamilyMap.TryGetValue(key, out var fam) ? fam : key;
    }
}
