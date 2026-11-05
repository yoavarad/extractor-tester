namespace ExtractorTester.Profiling;

/// <summary>Stage names reported in RunStats.stage_seconds (profiling.STAGES).</summary>
public static class StageNames
{
    public static readonly string[] All =
    {
        "load", "decode", "tree", "match", "field_compare",
        "text_fast", "text_full", "text_buffered", "aggregate", "render",
    };
}

/// <summary>The N slowest items seen, in bounded memory (profiling.Slowest).</summary>
public sealed class Slowest
{
    public const int DefaultN = 10;

    private readonly int _n;
    private readonly List<(double Seconds, long NegSeq, Dictionary<string, object?> Item)> _kept = new();
    private long _seq;

    public Slowest(int n = DefaultN) => _n = n;

    /// <summary>Offer one item; only the n slowest survive (earlier wins on equal seconds).</summary>
    public void Add(string itemId, double seconds, string status)
    {
        _seq++;
        var entry = (seconds, -_seq, new Dictionary<string, object?>
        {
            ["item_id"] = itemId,
            ["seconds"] = seconds,
            ["status"] = status,
        });
        _kept.Add(entry);
        if (_kept.Count > _n)
        {
            var min = 0;
            for (var i = 1; i < _kept.Count; i++)
            {
                if (Compare(_kept[i], _kept[min]) < 0) min = i;
            }
            _kept.RemoveAt(min);
        }
    }

    /// <summary>Slowest first.</summary>
    public List<Dictionary<string, object?>> Items()
    {
        var copy = new List<(double Seconds, long NegSeq, Dictionary<string, object?> Item)>(_kept);
        copy.Sort((a, b) => Compare(b, a));
        return copy.Select(e => e.Item).ToList();
    }

    private static int Compare(
        (double Seconds, long NegSeq, Dictionary<string, object?> Item) a,
        (double Seconds, long NegSeq, Dictionary<string, object?> Item) b)
    {
        if (a.Seconds < b.Seconds) return -1;
        if (a.Seconds > b.Seconds) return 1;
        return a.NegSeq.CompareTo(b.NegSeq);
    }
}
