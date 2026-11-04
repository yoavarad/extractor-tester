using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;
using ExtractorTester.Models;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace ExtractorTester.Render;

/// <summary>
/// Port of src/extractor_tester/render.py: report.json on disk to one self-contained HTML file.
/// The Jinja2 template is translated statement for statement; values print as Python str()
/// and are escaped like Jinja2 autoescape (markupsafe); None prints as an em dash (finalize).
/// </summary>
public static class HtmlRenderer
{
    private const string TemplateText = """
<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8">
<title>extractor-tester report</title>
<style>
body{font-family:sans-serif;margin:1.5em;color:#222}
table{border-collapse:collapse;margin:.5em 0}
th,td{border:1px solid #ccc;padding:.2em .6em;text-align:left}
th.sortable{cursor:pointer}
.warn{color:#b45309;font-weight:bold}
details{margin:.4em 0 .4em 1em}
code,pre{background:#f4f4f4}
</style></head><body>
{{- func metric(m) -}}
{{ m.key }}: {{ m.count }}/{{ m.denominator }} ({{ fmt "%.1f" (m.rate * 100) }}%)
{{- if truthy m.examples }}
<details><summary>{{ m.examples | array.size }} examples</summary>
{{- for e in m.examples }}
<div>item {{ e.item_id }}
{{- if (truthy e.path_old) || (truthy e.path_new) }} &middot; {{ e.path_old ?? "—" }} / {{ e.path_new ?? "—" }}{{ end }}

{{- if e.similarity != null }} &middot; similarity <code>{{ e.similarity }}</code>{{ end }}
{{- if e.similarity == null }}
 &middot; old <code>{{ e.old_value ?? "—" }}</code> &middot; new <code>{{ e.new_value ?? "—" }}</code>{{ end }}
{{- if truthy e.note }} &middot; {{ e.note }}{{ end }}</div>
{{- end }}
</details>
{{- end -}}
{{- end -}}
<h1>extractor-tester report</h1>
<p>old: {{ (get r.inputs "old" null) ?? "—" }} &nbsp; new: {{ (get r.inputs "new" null) ?? "—" }}
 &nbsp; started {{ isoformat r.started_at }} &nbsp; v{{ r.tool_version }}</p>
<p id="totals">Items
{{- for kv in (items r.totals) }} {{ kv.key }} {{ kv.value }}
{{- if !for.last }} &middot;{{ end }}{{ end }}
 &middot; {{ r.items_only_old.count }} only old &middot; {{ r.items_only_new.count }} only new</p>
{{ if truthy r.warnings }}<h2>Warnings</h2>
<ul>{{ for w in r.warnings }}<li class="warn">{{ w }}</li>{{ end }}</ul>{{ end }}
<h2>Type families</h2>
<table id="families"><thead><tr>
{{- for h in ["family", "pairs", "identical", "only old", "only new", "median sim", "warning"] }}
<th class="sortable">{{ h }}</th>{{ end }}</tr></thead><tbody>
{{- for f in r.families }}
<tr><td>{{ f.family }}</td><td>{{ f.pairs }}</td>
<td>{{ fmt "%.0f" (f.identical.rate * 100) }}%</td>
<td>{{ f.only_in_old.count }}</td><td>{{ f.only_in_new.count }}</td>
<td>{{ (get f.edit_similarity "median" "") ?? "—" }}</td>
<td>{{ if f.cut_lines_warning }}<span class="warn">&#9888;</span>{{ end }}</td></tr>
{{- end }}</tbody></table>
{{ for f in r.families }}
<details class="family"><summary>{{ f.family }}
{{- if f.cut_lines_warning }} <span class="warn">&#9888;</span>{{ end }}</summary>
<h3>Fields</h3>
<ul>{{ for m in f.fields }}<li>{{ metric m }}</li>{{ else }}<li>none</li>{{ end }}</ul>
<h3>Text</h3>
<ul>
<li>edit similarity: {{ for kv in (items f.edit_similarity) }}{{ kv.key }} {{ kv.value ?? "—" }}; {{ end }}</li>
<li>word-set similarity: {{ for kv in (items f.jaccard) }}{{ kv.key }} {{ kv.value ?? "—" }}; {{ end }}</li>
{{ for m in f.relations }}<li>{{ metric m }}</li>{{ end }}
<li>methods: {{ for kv in (items f.methods) }}{{ kv.key }} {{ kv.value }}; {{ end }}</li>
<li>cut lines: {{ fmt "%.1f" f.lines_cut_pct }}%</li>
</ul>
<h3>Matching tiers</h3>
<ul>{{ for m in f.tiers }}<li>{{ metric m }}</li>{{ end }}</ul>
<h3>Hints</h3>
<ul>{{ for m in f.hints }}<li>{{ metric m }}</li>{{ end }}</ul>
<h3>File errors</h3>
<ul><li>{{ metric f.decode_errors }}</li>
{{ for m in f.file_errors }}<li>{{ metric m }}</li>{{ end }}</ul>
</details>
{{ end }}
<h2>Item metadata changes</h2>
<ul>{{ for m in r.item_fields }}<li>{{ metric m }}</li>{{ else }}<li>none</li>{{ end }}</ul>
<h2>Errors by code</h2>
<ul>{{ for m in r.errors }}<li>{{ metric m }}</li>{{ else }}<li>none</li>{{ end }}</ul>
<h2>Run statistics</h2>
<ul>
<li>wall seconds: {{ r.run_stats.wall_seconds }}</li>
<li>workers: {{ r.run_stats.workers }} (replaced {{ r.run_stats.workers_replaced }})</li>
{{ for kv in (items r.run_stats.stage_seconds) }}
<li>stage {{ kv.key }}: {{ kv.value }}s</li>{{ end }}
{{ for kv in (items r.run_stats.text_seconds_by_family) }}
<li>text {{ kv.key }}: {{ kv.value }}s</li>{{ end }}
{{ for s in r.run_stats.slowest_items }}<li>slow item: {{ s }}</li>{{ end }}
</ul>
<script>
document.querySelectorAll("th.sortable").forEach(function(th){
th.addEventListener("click",function(){
var t=th.closest("table"),b=t.tBodies[0],i=th.cellIndex,
d=th.dataset.dir==="asc"?-1:1;th.dataset.dir=d===1?"asc":"desc";
Array.from(b.rows).sort(function(x,y){
var a=x.cells[i].textContent.trim(),c=y.cells[i].textContent.trim(),
n=parseFloat(a),m=parseFloat(c);
return(isNaN(n)||isNaN(m)?a.localeCompare(c):n-m)*d;
}).forEach(function(r){b.appendChild(r);});});});
</script>
</body></html>
""";

    // Line endings normalized so the output is LF whatever the checkout does to this file.
    private static readonly Template Parsed = ParseTemplate();

    private static Template ParseTemplate()
    {
        var template = Template.Parse(TemplateText.Replace("\r\n", "\n"));
        if (template.HasErrors)
        {
            throw new InvalidOperationException("report template: " + string.Join("; ", template.Messages));
        }
        return template;
    }

    /// <summary>Read report.json from disk and write the HTML report (UTF-8, no BOM).</summary>
    public static void Render(string reportJsonPath, string htmlPath)
    {
        var report = PyJson.Deserialize<Report>(File.ReadAllText(reportJsonPath, Encoding.UTF8));
        File.WriteAllText(htmlPath, RenderReport(report), new UTF8Encoding(false));
    }

    /// <summary>Render a report model to HTML text.</summary>
    internal static string RenderReport(Report report)
    {
        var globals = new ScriptObject();
        globals.Import("fmt", new Func<string, object?, string>(PyText.Format));
        globals.Import("truthy", new Func<object?, bool>(PyText.Truthy));
        globals.Import("get", new Func<object?, string, object?, object?>(Get));
        globals.Import("items", new Func<object?, ScriptArray>(Items));
        globals.Import("isoformat", new Func<DateTimeOffset, string>(PyText.IsoFormat));
        globals["r"] = report;
        var context = new AutoEscapeContext { LoopLimit = 0, AutoIndent = false };
        context.PushGlobal(globals);
        return Parsed.Render(context);
    }

    // dict.get(key, default)
    private static object? Get(object? dict, string key, object? fallback) =>
        dict is IDictionary d && d.Contains(key) ? d[key] : fallback;

    // dict.items() as a list of {key, value} objects, in insertion order.
    private static ScriptArray Items(object? dict)
    {
        var list = new ScriptArray();
        if (dict is IDictionary d)
        {
            foreach (DictionaryEntry entry in d)
            {
                list.Add(new ScriptObject { ["key"] = entry.Key, ["value"] = entry.Value });
            }
        }
        return list;
    }

    /// <summary>
    /// Autoescape: every printed value is str()-ed and HTML-escaped. Scriban never prints null,
    /// so Jinja2's finalize (None as an em dash) is spelled `?? "—"` at each nullable value.
    /// </summary>
    private sealed class AutoEscapeContext : TemplateContext
    {
        public override TemplateContext Write(SourceSpan span, object? textAsObject)
        {
            Write(PyText.Escape(PyText.Str(textAsObject)));
            return this;
        }
    }
}

/// <summary>Python str()/repr(), %-format and markupsafe escaping for template values.</summary>
internal static class PyText
{
    /// <summary>markupsafe.escape: &amp; &lt; &gt; " ' become entities.</summary>
    public static string Escape(string s)
    {
        if (s.AsSpan().IndexOfAny("&<>\"'") < 0)
        {
            return s;
        }
        var sb = new StringBuilder(s.Length + 16);
        foreach (var c in s)
        {
            sb.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&#34;",
                '\'' => "&#39;",
                _ => c.ToString(),
            });
        }
        return sb.ToString();
    }

    /// <summary>Python truthiness for model and JSON values.</summary>
    public static bool Truthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0,
        long l => l != 0,
        int i => i != 0,
        double d => d != 0,
        BigInteger bi => !bi.IsZero,
        ICollection c => c.Count > 0,
        _ => true,
    };

    /// <summary>Python str(value).</summary>
    public static string Str(object? v) => v is string s ? s : Repr(v);

    /// <summary>Python repr(value) for null, bool, numbers, strings, lists and dicts.</summary>
    public static string Repr(object? v)
    {
        switch (v)
        {
            case null:
                return "None";
            case bool b:
                return b ? "True" : "False";
            case string s:
                return ReprString(s);
            case double d:
                return FloatRepr(d);
            case float f:
                return FloatRepr(f);
            case DateTimeOffset dto:
                return IsoFormat(dto).Replace('T', ' ');
            case IDictionary dict:
            {
                var parts = new List<string>();
                foreach (DictionaryEntry e in dict)
                {
                    parts.Add(Repr(e.Key) + ": " + Repr(e.Value));
                }
                return "{" + string.Join(", ", parts) + "}";
            }
            case IEnumerable list:
            {
                var parts = new List<string>();
                foreach (var item in list)
                {
                    parts.Add(Repr(item));
                }
                return "[" + string.Join(", ", parts) + "]";
            }
            case IFormattable num:
                return num.ToString(null, CultureInfo.InvariantCulture);
            default:
                return v.ToString() ?? "";
        }
    }

    private static string ReprString(string s)
    {
        var quote = s.Contains('\'') && !s.Contains('"') ? '"' : '\'';
        var sb = new StringBuilder(s.Length + 2);
        sb.Append(quote);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == quote || c == '\\')
            {
                sb.Append('\\').Append(c);
            }
            else if (c == '\n')
            {
                sb.Append("\\n");
            }
            else if (c == '\r')
            {
                sb.Append("\\r");
            }
            else if (c == '\t')
            {
                sb.Append("\\t");
            }
            else if (c < 0x20 || c == 0x7f)
            {
                sb.Append("\\x").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(c);
            }
        }
        sb.Append(quote);
        return sb.ToString();
    }

    /// <summary>Python repr(float): shortest round-trip digits, exponent form outside 1e-4..1e16.</summary>
    public static string FloatRepr(double value)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }
        if (double.IsInfinity(value))
        {
            return value > 0 ? "inf" : "-inf";
        }
        if (value == 0)
        {
            return double.IsNegative(value) ? "-0.0" : "0.0";
        }
        var r = value.ToString("R", CultureInfo.InvariantCulture);
        var sign = "";
        if (r[0] == '-')
        {
            sign = "-";
            r = r[1..];
        }
        var exp = 0;
        var e = r.IndexOfAny(['E', 'e']);
        if (e >= 0)
        {
            exp = int.Parse(r[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            r = r[..e];
        }
        var dot = r.IndexOf('.');
        if (dot >= 0)
        {
            exp -= r.Length - dot - 1;
            r = r.Remove(dot, 1);
        }
        var digits = r.TrimStart('0');
        var trimmed = digits.TrimEnd('0');
        exp += digits.Length - trimmed.Length;
        digits = trimmed;
        // value = 0.digits * 10^decpt
        var decpt = digits.Length + exp;
        string body;
        if (decpt > -4 && decpt <= 16)
        {
            if (decpt <= 0)
            {
                body = "0." + new string('0', -decpt) + digits;
            }
            else if (decpt >= digits.Length)
            {
                body = digits + new string('0', decpt - digits.Length) + ".0";
            }
            else
            {
                body = digits[..decpt] + "." + digits[decpt..];
            }
        }
        else
        {
            var mantissa = digits.Length == 1 ? digits : digits[..1] + "." + digits[1..];
            var x = decpt - 1;
            body = mantissa + "e" + (x < 0 ? "-" : "+") + Math.Abs(x).ToString("D2", CultureInfo.InvariantCulture);
        }
        return sign + body;
    }

    /// <summary>Python "%.Nf" % value: exact binary value, round half to even.</summary>
    public static string Format(string spec, object? value)
    {
        if (spec.Length < 4 || !spec.StartsWith("%.", StringComparison.Ordinal) || spec[^1] != 'f'
            || !int.TryParse(spec[2..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var places))
        {
            throw new ArgumentException($"unsupported format {spec}");
        }
        return FixedPoint(Convert.ToDouble(value, CultureInfo.InvariantCulture), places);
    }

    /// <summary>Fixed-point text with <paramref name="places"/> decimals, as C printf %f.</summary>
    public static string FixedPoint(double value, int places)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }
        if (double.IsInfinity(value))
        {
            return value > 0 ? "inf" : "-inf";
        }
        var bits = BitConverter.DoubleToInt64Bits(value);
        var negative = bits < 0;
        var biased = (int)((bits >> 52) & 0x7FF);
        var fraction = bits & 0xFFFFFFFFFFFFFL;
        BigInteger mantissa = biased == 0 ? fraction : fraction | (1L << 52);
        var exponent = (biased == 0 ? 1 : biased) - 1075;
        var scaled = mantissa * BigInteger.Pow(10, places);
        BigInteger q;
        if (exponent >= 0)
        {
            q = scaled << exponent;
        }
        else
        {
            var den = BigInteger.One << -exponent;
            q = BigInteger.DivRem(scaled, den, out var rem);
            var twice = rem * 2;
            if (twice > den || (twice == den && !q.IsEven))
            {
                q += 1;
            }
        }
        var text = q.ToString(CultureInfo.InvariantCulture).PadLeft(places + 1, '0');
        if (places > 0)
        {
            text = text[..^places] + "." + text[^places..];
        }
        return (negative ? "-" : "") + text;
    }

    /// <summary>Python datetime.isoformat() for an aware datetime.</summary>
    public static string IsoFormat(DateTimeOffset value)
    {
        var text = value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var micros = value.Ticks % TimeSpan.TicksPerSecond / 10;
        if (micros != 0)
        {
            text += "." + micros.ToString("D6", CultureInfo.InvariantCulture);
        }
        var off = value.Offset;
        var offSign = off < TimeSpan.Zero ? "-" : "+";
        off = off.Duration();
        return text + offSign + off.Hours.ToString("D2", CultureInfo.InvariantCulture) + ":"
            + off.Minutes.ToString("D2", CultureInfo.InvariantCulture);
    }
}
