using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace ExtractorTester.Tests;

/// <summary>
/// C# equal of tests/golden/checks.py: example presence, result keys defined in metrics.md,
/// tool-error codes. metrics.md and the tool-error manifests are copied next to the tests (csproj links).
/// </summary>
internal static class GoldenChecks
{
    public static readonly string MetricsMd = Path.Combine(GoldenCorpus.Dir, "metrics.md");
    public static readonly string ToolErrors = Path.Combine(GoldenCorpus.Dir, "tool-errors");
    private static readonly Regex Placeholder = new("<([^>]+)>");
    private static readonly string[] MetricKeys = ["key", "count", "denominator", "rate", "examples"];

    /// <summary>Codes of every tool-error manifest in a folder, sorted.</summary>
    public static List<string> ToolErrorCodes(string? folder = null)
    {
        var yaml = new DeserializerBuilder().Build();
        return Directory.GetFiles(folder ?? ToolErrors, "*.yaml")
            .Select(p => yaml.Deserialize<Dictionary<string, object>>(File.ReadAllText(p, Encoding.UTF8))["code"].ToString()!)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>(location, MetricResult object) for every MetricResult in a report.</summary>
    public static IEnumerable<(string Where, JsonObject Result)> MetricResults(JsonNode? value, string where = "report")
    {
        if (value is JsonObject obj)
        {
            if (MetricKeys.All(obj.ContainsKey))
            {
                yield return (where, obj);
            }
            foreach (var (k, v) in obj)
            {
                foreach (var r in MetricResults(v, $"{where}.{k}"))
                {
                    yield return r;
                }
            }
        }
        else if (value is JsonArray arr)
        {
            for (var i = 0; i < arr.Count; i++)
            {
                foreach (var r in MetricResults(arr[i], $"{where}[{i}]"))
                {
                    yield return r;
                }
            }
        }
    }

    private static bool NonZero(JsonObject m) => m["count"]!.GetValue<long>() != 0;

    /// <summary>Locations of non-zero MetricResults that carry no examples.</summary>
    public static List<string> MissingExamples(JsonNode report) =>
        MetricResults(report).Where(x => NonZero(x.Result) && x.Result["examples"]!.AsArray().Count == 0).Select(x => x.Where).ToList();

    /// <summary>Field names the report lists in its per-field results ('author:changed' -> 'author').</summary>
    public static HashSet<string> FieldNames(JsonNode report)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var list in ListsNamed(report, ["fields", "item_fields"]))
        {
            foreach (var result in list)
            {
                var key = result!["key"]!.GetValue<string>();
                var cut = key.LastIndexOf(':');
                var name = cut < 0 ? key : key[..cut];
                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }
        }
        return names;
    }

    private static IEnumerable<JsonArray> ListsNamed(JsonNode? value, HashSet<string> wanted)
    {
        if (value is JsonObject obj)
        {
            foreach (var (k, v) in obj)
            {
                if (wanted.Contains(k) && v is JsonArray a)
                {
                    yield return a;
                }
                foreach (var l in ListsNamed(v, wanted))
                {
                    yield return l;
                }
            }
        }
        else if (value is JsonArray arr)
        {
            foreach (var v in arr)
            {
                foreach (var l in ListsNamed(v, wanted))
                {
                    yield return l;
                }
            }
        }
    }

    private static Dictionary<string, string> PlaceholderPatterns(HashSet<string> fields, string toolErrors)
    {
        var codes = ToolErrorCodes(toolErrors);
        if (codes.Count == 0)
        {
            throw new ArgumentException($"no tool-error manifests found in {toolErrors}");
        }
        var sorted = fields.Order(StringComparer.Ordinal).Select(Regex.Escape).ToList();
        return new Dictionary<string, string>
        {
            // a field name that appears in this report (it may itself contain ':', like 'dc:title')
            ["field"] = sorted.Count > 0 ? string.Join("|", sorted) : "(?!)",
            // the code of one tool-error manifest
            ["error code"] = string.Join("|", codes.Select(Regex.Escape)),
        };
    }

    /// <summary>Every backticked key form in the 'Result keys' list, e.g. '&lt;field&gt;:changed'.</summary>
    public static List<string> KeyForms(string metricsMd)
    {
        var section = metricsMd.Split("## Result keys", 2)[1].Split("\n## ", 2)[0];
        var body = string.Join("\n", section.Replace("\r\n", "\n").Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal)));
        return Regex.Matches(body, "`([^`]+)`").Select(m => m.Groups[1].Value).ToList();
    }

    private static List<Regex> KeyPatterns(string metricsMd, HashSet<string> fields, string toolErrors)
    {
        var holes = PlaceholderPatterns(fields, toolErrors);
        var patterns = new List<Regex>();
        foreach (var token in KeyForms(metricsMd))
        {
            foreach (Match m in Placeholder.Matches(token))
            {
                if (!holes.ContainsKey(m.Groups[1].Value))
                {
                    throw new ArgumentException($"metrics.md key '{token}' uses unknown placeholder <{m.Groups[1].Value}>");
                }
            }
            var parts = Placeholder.Split(token); // literal, name, literal, name, ...
            var rx = string.Concat(parts.Select((p, i) => i % 2 == 0 ? Regex.Escape(p) : $"(?:{holes[p]})"));
            patterns.Add(new Regex($"^(?:{rx})\\z"));
        }
        return patterns;
    }

    /// <summary>Result keys of the report that no key in metrics.md defines.</summary>
    public static List<string> UndefinedKeys(JsonNode report, string metricsMd, string? toolErrors = null)
    {
        var patterns = KeyPatterns(metricsMd, FieldNames(report), toolErrors ?? ToolErrors);
        var keys = MetricResults(report).Select(x => x.Result["key"]!.GetValue<string>()).Distinct().Order(StringComparer.Ordinal);
        return keys.Where(k => !patterns.Any(p => p.IsMatch(k))).ToList();
    }

    /// <summary>Plain-words failure text: each bad key, with the metrics.md key forms ending the same way.</summary>
    public static string UndefinedKeysMessage(IEnumerable<string> keys, string metricsMd, HashSet<string>? fields = null)
    {
        var forms = KeyForms(metricsMd);
        var lines = new List<string>();
        foreach (var key in keys)
        {
            var cut = key.LastIndexOf(':');
            var ending = cut < 0 ? key : key[(cut + 1)..];
            var close = forms.Where(f => f.EndsWith(ending, StringComparison.Ordinal)).ToList();
            if (close.Count == 0)
            {
                close = forms;
            }
            var line = $"key {Repr(key)} is not defined in metrics.md; expected one of: [{string.Join(", ", close.Select(Repr))}]";
            var field = cut < 0 ? key : key[..cut];
            if (fields is not null && cut >= 0 && !fields.Contains(field))
            {
                line += $"; its field part {Repr(field)} is not a field name in this report";
            }
            lines.Add(line);
        }
        return string.Join("\n", lines);
    }

    /// <summary>Python repr of a plain str.</summary>
    private static string Repr(string s)
    {
        var q = s.Contains('\'') && !s.Contains('"') ? '"' : '\'';
        var body = s.Replace("\\", "\\\\");
        if (q == '\'')
        {
            body = body.Replace("'", "\\'");
        }
        return q + body + q;
    }
}
