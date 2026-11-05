using ExtractorTester.Config;
using ExtractorTester.Decode;

namespace ExtractorTester.Compare;

/// <summary>Buffered comparison numbers merged into TextFacts by the text comparer.</summary>
public sealed record BufferedFacts(
    double EditSimilarity,
    double OldFoundInNew,
    double NewFoundInOld,
    List<Dictionary<string, object?>> Snippets,
    long LinesTotal,
    long LinesCut,
    double LinesCutPct,
    long OversizedRuns);

/// <summary>
/// Buffered text comparison for long texts: block alignment, then local character diffs
/// (port of compare/buffered.py). Lengths are in code points.
/// </summary>
public static class BufferedComparer
{
    /// <summary>Split text into normalized line blocks; cut long lines into several blocks.</summary>
    public static (List<string> Blocks, int Total, int Cut) SplitBlocks(string text, long maxBlockChars)
    {
        var blocks = new List<string>();
        int total = 0, cut = 0;
        foreach (var line in Text.PySplitLines(text))
        {
            var norm = Text.Normalize(line);
            if (norm.Length == 0) continue;
            total++;
            var cps = Text.ToCodePoints(norm);
            if (cps.Length > maxBlockChars) cut++;
            for (long s = 0; s < cps.Length; s += maxBlockChars)
            {
                blocks.Add(Text.FromCodePoints(cps.AsSpan((int)s, (int)Math.Min(maxBlockChars, cps.Length - s))));
            }
        }
        return (blocks, total, cut);
    }

    /// <summary>Align line blocks of two raw texts and measure how much text they share.</summary>
    public static BufferedFacts CompareBuffered(string old, string @new, CompareConfig config)
    {
        var (a, totalA, cutA) = SplitBlocks(old, config.MaxBlockChars);
        var (b, totalB, cutB) = SplitBlocks(@new, config.MaxBlockChars);
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var ha = a.Select(x => Id(ids, x)).ToList();
        var hb = b.Select(x => Id(ids, x)).ToList();
        long la = a.Sum(x => (long)Text.CodePointLength(x));
        long lb = b.Sum(x => (long)Text.CodePointLength(x));
        long matched = 0, oversized = 0;
        var snippets = new List<Dictionary<string, object?>>();
        var n = (int)Math.Min(config.SnippetChars, int.MaxValue);
        foreach (var (tag, i1, i2, j1, j2) in new SequenceMatcher<int>(ha, hb).GetOpcodes())
        {
            var runA = a.GetRange(i1, i2 - i1);
            var runB = b.GetRange(j1, j2 - j1);
            if (tag == "equal")
            {
                matched += Len(runA);
                continue;
            }
            if (snippets.Count < TextComparer.MaxSnippets)
            {
                snippets.Add(TextComparer.Snippet(
                    Text.Prefix(string.Join(" ", runA), n), Text.Prefix(string.Join(" ", runB), n)));
            }
            if (tag != "replace") continue;
            if (Math.Max(Len(runA), Len(runB)) <= config.FullCompareMaxChars)
            {
                matched += Lcs(string.Concat(runA), string.Concat(runB));
            }
            else
            {
                oversized++;
                for (var k = 0; k < Math.Min(runA.Count, runB.Count); k++) matched += Lcs(runA[k], runB[k]);
            }
        }
        long total = totalA + totalB, cut = cutA + cutB;
        return new BufferedFacts(
            EditSimilarity: la + lb != 0 ? (double)(2 * matched) / (la + lb) : 1.0,
            OldFoundInNew: la != 0 ? (double)matched / la : 1.0,
            NewFoundInOld: lb != 0 ? (double)matched / lb : 1.0,
            Snippets: snippets,
            LinesTotal: total,
            LinesCut: cut,
            LinesCutPct: total != 0 ? (double)(100 * cut) / total : 0.0,
            OversizedRuns: oversized);
    }

    private static int Id(Dictionary<string, int> ids, string x)
    {
        if (!ids.TryGetValue(x, out var id)) ids[x] = id = ids.Count;
        return id;
    }

    private static long Len(List<string> run) => run.Sum(x => (long)Text.CodePointLength(x));

    /// <summary>Length of the longest common subsequence of two strings.</summary>
    private static long Lcs(string x, string y) => Indel.LcsLength(Text.ToCodePoints(x), Text.ToCodePoints(y));
}
