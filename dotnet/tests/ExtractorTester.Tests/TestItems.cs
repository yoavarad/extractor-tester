using System.Globalization;
using System.Text;

namespace ExtractorTester.Tests;

/// <summary>C# equal of tests/helpers.py.</summary>
internal static class TestItems
{
    /// <summary>Write &lt;itemId&gt;.json like helpers.write_item; each file's "text" is base64 (UTF-8).</summary>
    public static string WriteItem(
        string dir, string itemId, IEnumerable<Dictionary<string, object?>> files,
        IEnumerable<(string Key, object? Value)>? fields = null)
    {
        var encoded = new List<object?>();
        foreach (var f in files)
        {
            var copy = new Dictionary<string, object?>(f);
            if (copy.TryGetValue("text", out var t))
            {
                copy["text"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(Convert.ToString(t, CultureInfo.InvariantCulture)!));
            }
            encoded.Add(copy);
        }
        var obj = new Dictionary<string, object?> { ["itemId"] = itemId };
        foreach (var (k, v) in fields ?? []) obj[k] = v;
        obj["files"] = encoded;
        var path = Path.Combine(dir, $"{itemId}.json");
        File.WriteAllText(path, Dumps(obj), new UTF8Encoding(false));
        return path;
    }

    /// <summary>json.dumps with default separators and ensure_ascii.</summary>
    public static string Dumps(object? v)
    {
        var sb = new StringBuilder();
        Write(sb, v);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, object? v)
    {
        switch (v)
        {
            case null: sb.Append("null"); break;
            case bool b: sb.Append(b ? "true" : "false"); break;
            case string s: WriteString(sb, s); break;
            case int or long: sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); break;
            case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
            case IDictionary<string, object?> dict:
                sb.Append('{');
                var first = true;
                foreach (var (k, val) in dict)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    WriteString(sb, k);
                    sb.Append(": ");
                    Write(sb, val);
                }
                sb.Append('}');
                break;
            case System.Collections.IEnumerable list:
                sb.Append('[');
                var firstItem = true;
                foreach (var item in list)
                {
                    if (!firstItem) sb.Append(", ");
                    firstItem = false;
                    Write(sb, item);
                }
                sb.Append(']');
                break;
            default: throw new ArgumentException($"unsupported {v.GetType()}");
        }
    }

    private static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20 || c > 0x7E) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
