using ExtractorTester.Config;
using ExtractorTester.Decode;
using ExtractorTester.Models;
using ExtractorTester.Profiling;

namespace ExtractorTester.Compare;

/// <summary>
/// Text comparison for one file pair: fast path, full and buffered methods (port of
/// compare/text.py, see metrics.md). All lengths and slices are in code points.
/// </summary>
public static class TextComparer
{
    public const int MaxSnippets = 3;

    /// <summary>Pick the comparison method and compute TextFacts for one pair.</summary>
    public static TextFacts CompareText(string old, string @new, CompareConfig config)
    {
        var start = Stages.Clock();
        var na = Text.Normalize(old);
        var nb = Text.Normalize(@new);
        long oldChars = Text.CodePointLength(old);
        long newChars = Text.CodePointLength(@new);
        var facts = new TextFacts
        {
            RawEqual = old == @new,
            NormalizedEqual = na == nb,
            Method = "fast-path",
            OldChars = oldChars,
            NewChars = newChars,
            LengthDelta = newChars - oldChars,
            OldEmpty = na.Length == 0,
            NewEmpty = nb.Length == 0,
            LinesTotal = 0,
            LinesCut = 0,
            LinesCutPct = 0.0,
            OversizedRuns = 0,
            Snippets = [],
            Seconds = 0.0,
        };
        if (na == nb)
        {
            facts = facts with { Relation = "equal", EditSimilarity = 1.0, Jaccard = 1.0 };
        }
        else if (Math.Max(oldChars, newChars) <= config.FullCompareMaxChars)
        {
            facts = Full(facts, na, nb, config);
        }
        else
        {
            facts = Buffered(facts, old, @new, na, nb, config);
        }
        return facts with { Seconds = Stages.Clock() - start };
    }

    /// <summary>Compare two long, unequal texts by aligned line blocks.</summary>
    private static TextFacts Buffered(TextFacts facts, string old, string @new, string na, string nb, CompareConfig config)
    {
        var b = BufferedComparer.CompareBuffered(old, @new, config);
        var (shared, jaccard) = Words(na, nb);
        return facts with
        {
            Method = "buffered",
            EditSimilarity = b.EditSimilarity,
            Jaccard = jaccard,
            OldFoundInNew = b.OldFoundInNew,
            NewFoundInOld = b.NewFoundInOld,
            Relation = Relation(
                Text.CodePointLength(na), Text.CodePointLength(nb),
                b.OldFoundInNew, b.NewFoundInOld, shared, config.ContainmentPct / 100),
            LinesTotal = b.LinesTotal,
            LinesCut = b.LinesCut,
            LinesCutPct = b.LinesCutPct,
            OversizedRuns = b.OversizedRuns,
            Snippets = b.Snippets,
        };
    }

    /// <summary>Compare two normalized, unequal texts character by character.</summary>
    private static TextFacts Full(TextFacts facts, string na, string nb, CompareConfig config)
    {
        var a = Text.ToCodePoints(na);
        var b = Text.ToCodePoints(nb);
        int la = a.Length, lb = b.Length;
        long lcs = Indel.LcsLength(a, b);
        var oldFound = la != 0 ? (double)lcs / la : 1.0;
        var newFound = lb != 0 ? (double)lcs / lb : 1.0;
        var (shared, jaccard) = Words(na, nb);
        return facts with
        {
            Method = "full",
            EditSimilarity = (double)(2 * lcs) / (la + lb),
            Jaccard = jaccard,
            OldFoundInNew = oldFound,
            NewFoundInOld = newFound,
            Relation = Relation(la, lb, oldFound, newFound, shared, config.ContainmentPct / 100),
            Snippets = Snippets(a, b, config.SnippetChars),
        };
    }

    /// <summary>Shared word count and Jaccard of the whitespace-split word sets.</summary>
    private static (int Shared, double Jaccard) Words(string na, string nb)
    {
        var wa = new HashSet<string>(Text.PySplit(na), StringComparer.Ordinal);
        var wb = new HashSet<string>(Text.PySplit(nb), StringComparer.Ordinal);
        var shared = wa.Count(wb.Contains);
        return (shared, (double)shared / (wa.Count + wb.Count - shared));
    }

    /// <summary>Label how two unequal texts relate, from containment of the shorter in the longer.</summary>
    private static string Relation(int la, int lb, double oldFound, double newFound, int sharedWords, double threshold)
    {
        if (la == 0) return "old-within-new";
        if (lb == 0) return "new-within-old";
        if (lb > la && oldFound >= threshold) return "old-within-new";
        if (la > lb && newFound >= threshold) return "new-within-old";
        return sharedWords != 0 ? "overlapping" : "disjoint";
    }

    /// <summary>Up to three first differing regions, each side cut to n code points.</summary>
    private static List<Dictionary<string, object?>> Snippets(int[] a, int[] b, long n)
    {
        var output = new List<Dictionary<string, object?>>();
        var window = n > long.MaxValue / 4 ? long.MaxValue : 4 * n;
        int i = 0, j = 0;
        while (output.Count < MaxSnippets)
        {
            var k = CommonPrefix(a, b, i, j);
            i += k;
            j += k;
            if (i >= a.Length && j >= b.Length) break;
            var equal = FirstEqual(a, b, i, j, window);
            if (equal is not { } eq)
            {
                output.Add(Snippet(Slice(a, i, n), Slice(b, j, n)));
                break;
            }
            output.Add(Snippet(Slice(a, i, Math.Min(eq.I1, n)), Slice(b, j, Math.Min(eq.J1, n))));
            i += eq.I1;
            j += eq.J1;
        }
        return output;
    }

    /// <summary>First equal opcode aligning a[i:] with b[j:], growing the window; null when nothing is shared.</summary>
    private static Opcode? FirstEqual(int[] a, int[] b, int i, int j, long window)
    {
        while (true)
        {
            var ops = Indel.Opcodes(SliceSpan(a, i, window), SliceSpan(b, j, window));
            foreach (var op in ops)
            {
                if (op.Tag == "equal") return op;
            }
            if (window >= a.Length - i && window >= b.Length - j) return null;
            window = window > long.MaxValue / 2 ? long.MaxValue : window * 2;
        }
    }

    private static int CommonPrefix(int[] a, int[] b, int i, int j)
    {
        var k = 0;
        while (i + k < a.Length && j + k < b.Length && a[i + k] == b[j + k]) k++;
        return k;
    }

    /// <summary>s[start:start+count] with Python slice clamping.</summary>
    private static ReadOnlySpan<int> SliceSpan(int[] s, int start, long count)
    {
        var from = Math.Min(start, s.Length);
        return s.AsSpan(from, (int)Math.Min(count, s.Length - from));
    }

    private static string Slice(int[] s, int start, long count) => Text.FromCodePoints(SliceSpan(s, start, count));

    internal static Dictionary<string, object?> Snippet(string old, string @new) => new() { ["old"] = old, ["new"] = @new };
}
