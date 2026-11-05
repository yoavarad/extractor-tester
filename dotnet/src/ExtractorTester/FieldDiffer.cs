using System.Collections;
using System.Numerics;
using ExtractorTester.Config;
using ExtractorTester.Decode;
using ExtractorTester.Fields;
using ExtractorTester.Loader;
using ExtractorTester.Models;

namespace ExtractorTester.Compare;

/// <summary>
/// Field-level diff of flattened metadata (port of compare/fields.py). Values are parsed JSON:
/// null, bool, string, long, BigInteger, double, List&lt;object?&gt;, Dictionary&lt;string, object?&gt;.
/// </summary>
public static class FieldDiffer
{
    /// <summary>Empty means null, empty string, empty list or empty object.</summary>
    public static bool IsEmpty(object? value) =>
        value is null or string { Length: 0 } or ICollection { Count: 0 };

    public static string JsonType(object? value) => value switch
    {
        null => "null",
        bool => "boolean",
        int or long or BigInteger or double => "number",
        string => "string",
        IList => "array",
        _ => "object",
    };

    /// <summary>JSON equality: numbers numerically, bool never equals a number, NaN equals NaN.</summary>
    public static bool JsonEqual(object? a, object? b)
    {
        var ta = JsonType(a);
        if (ta != JsonType(b)) return false;
        switch (ta)
        {
            case "array":
            {
                var (la, lb) = ((IList)a!, (IList)b!);
                if (la.Count != lb.Count) return false;
                for (var i = 0; i < la.Count; i++)
                {
                    if (!JsonEqual(la[i], lb[i])) return false;
                }
                return true;
            }
            case "object":
            {
                var (da, db) = ((IDictionary<string, object?>)a!, (IDictionary<string, object?>)b!);
                return da.Count == db.Count
                    && da.Keys.All(db.ContainsKey)
                    && da.All(kv => JsonEqual(kv.Value, db[kv.Key]));
            }
            case "number":
                return NumberEqual(a!, b!);
            default:
                return Equals(a, b);
        }
    }

    // Python int/float comparison is exact; NaN == NaN only via the isnan check in fields._equal.
    private static bool NumberEqual(object a, object b)
    {
        if (a is double x && b is double y) return x == y || (double.IsNaN(x) && double.IsNaN(y));
        if (a is double da) return IntEqualsDouble(ToBig(b), da);
        if (b is double db) return IntEqualsDouble(ToBig(a), db);
        return ToBig(a) == ToBig(b);
    }

    private static bool IntEqualsDouble(BigInteger i, double d) =>
        double.IsFinite(d) && Math.Floor(d) == d && new BigInteger(d) == i;

    private static BigInteger ToBig(object v) => v switch
    {
        int i => i,
        long l => l,
        BigInteger b => b,
        _ => throw new ArgumentException($"not an integer: {v.GetType()}"),
    };

    private static object? Snip(object? value, long limit) =>
        value is string s ? Text.Prefix(s, (int)Math.Min(limit, int.MaxValue)) : value;

    /// <summary>Diff two flattened field maps, sorted by path, skipping ignored and structural paths.</summary>
    public static List<FieldDiff> Diff(
        Dictionary<string, object?> old, Dictionary<string, object?> @new, CompareConfig config)
    {
        var structural = new HashSet<string>(StringComparer.Ordinal)
        {
            config.NameField, config.MimePrimaryField, config.MimeSecondaryField, config.TextField,
        };
        var paths = old.Keys.Union(@new.Keys).ToList();
        paths.Sort(CodePointComparer.Instance);
        var output = new List<FieldDiff>();
        foreach (var path in paths)
        {
            var group = FieldFlattener.Classify(path, config);
            if (group == "ignored" || structural.Contains(path)) continue;
            var inOld = old.TryGetValue(path, out var ov);
            var inNew = @new.TryGetValue(path, out var nv);
            var status = inOld && inNew
                ? (JsonEqual(ov, nv) ? "same" : "changed")
                : (inOld ? "only-in-old" : "only-in-new");
            output.Add(new FieldDiff
            {
                Path = path,
                Group = group,
                Status = status,
                OldValue = Snip(ov, config.SnippetChars),
                NewValue = Snip(nv, config.SnippetChars),
                OldType = inOld ? JsonType(ov) : null,
                NewType = inNew ? JsonType(nv) : null,
            });
        }
        return output;
    }
}
