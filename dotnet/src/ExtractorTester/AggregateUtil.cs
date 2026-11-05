using ExtractorTester.Loader;
using ExtractorTester.Models;

namespace ExtractorTester.Aggregate;

/// <summary>
/// The <c>cap</c> lowest-similarity examples, ordered by (similarity, item id, path). Port of
/// aggregate_util.Lowest: Python keeps a bounded max-heap on (-sim, reversed item, reversed path, seq);
/// the kept set is the cap largest keys under that total order and the output is sorted by
/// (sim, item, path, seq) ascending. Strings compare by code point.
/// </summary>
public sealed class Lowest
{
    private readonly long _cap;
    private readonly List<(double Sim, string Item, string Path, long Seq, Example Ex)> _kept = new();
    private long _seq;

    public Lowest(long cap) => _cap = cap;

    /// <summary>Offer an example; only the lowest <c>cap</c> survive.</summary>
    public void Add(double sim, Example example)
    {
        _seq++;
        if (_cap <= 0) return;
        var path = example.PathNew ?? example.PathOld ?? "";
        _kept.Add((sim, example.ItemId, path, _seq, example));
        if (_kept.Count > _cap)
        {
            var worst = 0;
            for (var i = 1; i < _kept.Count; i++)
            {
                if (KeyCompare(_kept[i], _kept[worst]) < 0) worst = i;
            }
            _kept.RemoveAt(worst);
        }
    }

    /// <summary>Kept examples, lowest similarity first.</summary>
    public List<Example> Examples()
    {
        var ordered = new List<(double Sim, string Item, string Path, long Seq, Example Ex)>(_kept);
        ordered.Sort((a, b) =>
        {
            var c = Cmp(a.Sim, b.Sim);
            if (c != 0) return c;
            c = CodePointComparer.Instance.Compare(a.Item, b.Item);
            if (c != 0) return c;
            c = CodePointComparer.Instance.Compare(a.Path, b.Path);
            return c != 0 ? c : a.Seq.CompareTo(b.Seq);
        });
        return ordered.Select(t => t.Ex).ToList();
    }

    // Heap key order: (-sim, reversed item, reversed path, seq); bigger key survives.
    private static int KeyCompare(
        (double Sim, string Item, string Path, long Seq, Example Ex) a,
        (double Sim, string Item, string Path, long Seq, Example Ex) b)
    {
        var c = Cmp(-a.Sim, -b.Sim);
        if (c != 0) return c;
        c = CodePointComparer.Instance.Compare(b.Item, a.Item);
        if (c != 0) return c;
        c = CodePointComparer.Instance.Compare(b.Path, a.Path);
        return c != 0 ? c : a.Seq.CompareTo(b.Seq);
    }

    private static int Cmp(double a, double b) => a < b ? -1 : a > b ? 1 : 0;
}

/// <summary>Percentile statistics matching Python's statistics module.</summary>
public static class AggStats
{
    /// <summary>Median, 10th percentile and minimum; empty when no data.</summary>
    public static Dictionary<string, object?> Stats(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return new Dictionary<string, object?>();
        var sorted = values.ToArray();
        Array.Sort(sorted);
        var p10 = sorted.Length > 1 ? FirstDecile(sorted) : sorted[0];
        return new Dictionary<string, object?>
        {
            ["median"] = Median(sorted),
            ["p10"] = p10,
            ["min"] = values.Min(),
        };
    }

    /// <summary>statistics.median on sorted data.</summary>
    private static double Median(double[] d)
    {
        var n = d.Length;
        return n % 2 == 1 ? d[n / 2] : (d[n / 2 - 1] + d[n / 2]) / 2;
    }

    /// <summary>statistics.quantiles(data, n=10)[0] (method "exclusive") on sorted data, len &gt;= 2.</summary>
    private static double FirstDecile(double[] d)
    {
        const int n = 10;
        long ld = d.Length;
        var m = ld + 1;
        var j = m / n;
        if (j < 1) j = 1;
        else if (j > ld - 1) j = ld - 1;
        var delta = m - j * n;
        return (d[j - 1] * (n - delta) + d[j] * delta) / n;
    }
}
