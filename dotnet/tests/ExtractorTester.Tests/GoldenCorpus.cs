using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ExtractorTester.Tests;

/// <summary>
/// C# equal of tests/golden/corpus.py: run the built exe on the checked-in corpus inputs
/// (dotnet/tests/golden/input/, exported by `uv run python -m tests.golden.corpus --dotnet`)
/// and normalize the outputs exactly like the Python normalizer (json.loads + json.dumps).
/// </summary>
internal static class GoldenCorpus
{
    public static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "Golden");
    public static readonly string Expected = Path.Combine(Dir, "expected");
    public static readonly string[] OutputFiles = ["pairs.jsonl", "items.jsonl", "report.json"];

    // Fields that differ between any two runs: timings, the run start and the input paths.
    private static readonly HashSet<string> Volatile = ["seconds", "stage_seconds", "started_at", "run_stats", "inputs"];

    /// <summary>Expected file text as Python read_text gives it (universal newlines).</summary>
    public static string ExpectedText(string name) =>
        File.ReadAllText(Path.Combine(Expected, name), Encoding.UTF8).Replace("\r\n", "\n");

    /// <summary>Copy the corpus under tmp, run compare with N workers via the exe, return normalized outputs.</summary>
    public static Dictionary<string, string> Run(string tmp, int workers)
    {
        var (old, @new, outDir) = (Path.Combine(tmp, "old"), Path.Combine(tmp, "new"), Path.Combine(tmp, $"out{workers}"));
        if (!Directory.Exists(old))
        {
            foreach (var side in new[] { "old", "new" })
            {
                Directory.CreateDirectory(Path.Combine(tmp, side));
                foreach (var src in Directory.GetFiles(Path.Combine(Dir, "input", side)))
                {
                    File.Copy(src, Path.Combine(tmp, side, Path.GetFileName(src)));
                }
            }
        }
        var cfg = Path.Combine(tmp, "c.yaml");
        File.Copy(Path.Combine(Dir, "input", "c.yaml"), cfg, overwrite: true);
        var (code, output) = RunExe("compare", old, @new, "--config", cfg, "--out", outDir, "--workers", workers.ToString(CultureInfo.InvariantCulture));
        Assert.True(code == 0, output);
        return NormalizedOutputs(outDir, tmp);
    }

    private static (int Code, string Output) RunExe(params string[] args)
    {
        var exe = Path.ChangeExtension(typeof(ExtractorTester.Cli.CliApp).Assembly.Location, OperatingSystem.IsWindows() ? ".exe" : null);
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(300_000))
        {
            proc.Kill(entireProcessTree: true);
            Assert.Fail("exe did not finish");
        }
        return (proc.ExitCode, stdout.Result + stderr);
    }

    /// <summary>Each output file normalized and serialized like normalized_outputs in corpus.py.</summary>
    public static Dictionary<string, string> NormalizedOutputs(string outDir, string @base)
    {
        var result = new Dictionary<string, string>();
        foreach (var name in OutputFiles)
        {
            var text = File.ReadAllText(Path.Combine(outDir, name), Encoding.UTF8);
            var sb = new StringBuilder();
            if (name.EndsWith(".jsonl", StringComparison.Ordinal))
            {
                foreach (var line in ExtractorTester.Decode.Text.PySplitLines(text))
                {
                    using var doc = JsonDocument.Parse(line);
                    Dumps(sb, doc.RootElement, @base, indent: null, depth: 0);
                    sb.Append('\n');
                }
            }
            else
            {
                using var doc = JsonDocument.Parse(text);
                Dumps(sb, doc.RootElement, @base, indent: 1, depth: 0);
                sb.Append('\n');
            }
            result[name] = sb.ToString();
        }
        return result;
    }

    /// <summary>json.dumps(normalize(value, base)) with default separators, ensure_ascii and float repr.</summary>
    private static void Dumps(StringBuilder sb, JsonElement v, string @base, int? indent, int depth)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.Object:
                var props = v.EnumerateObject().Where(p => !Volatile.Contains(p.Name)).ToList();
                Container(sb, '{', '}', props.Count, indent, depth, i =>
                {
                    PyString(sb, props[i].Name);
                    sb.Append(": ");
                    Dumps(sb, props[i].Value, @base, indent, depth + 1);
                });
                break;
            case JsonValueKind.Array:
                var items = v.EnumerateArray().ToList();
                Container(sb, '[', ']', items.Count, indent, depth, i => Dumps(sb, items[i], @base, indent, depth + 1));
                break;
            case JsonValueKind.String:
                var s = v.GetString()!;
                PyString(sb, s.Contains(@base, StringComparison.Ordinal) ? s.Replace(@base, "<tmp>").Replace('\\', '/') : s);
                break;
            case JsonValueKind.Number:
                var raw = v.GetRawText();
                // Python reads an integer -0 as int 0, so it dumps as 0 (a float -0.0 keeps its sign).
                sb.Append(raw.IndexOfAny(['.', 'e', 'E']) >= 0 ? FloatRepr(v.GetDouble()) : raw == "-0" ? "0" : raw);
                break;
            case JsonValueKind.True:
                sb.Append("true");
                break;
            case JsonValueKind.False:
                sb.Append("false");
                break;
            default:
                sb.Append("null");
                break;
        }
    }

    private static void Container(StringBuilder sb, char open, char close, int count, int? indent, int depth, Action<int> item)
    {
        sb.Append(open);
        if (count > 0)
        {
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    sb.Append(indent is null ? ", " : ",");
                }
                if (indent is int n)
                {
                    sb.Append('\n').Append(' ', n * (depth + 1));
                }
                item(i);
            }
            if (indent is int m)
            {
                sb.Append('\n').Append(' ', m * depth);
            }
        }
        sb.Append(close);
    }

    /// <summary>json.dumps string with ensure_ascii=True.</summary>
    private static void PyString(StringBuilder sb, string s)
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
                    if (c < 0x20 || c > 0x7e)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('"');
    }

    /// <summary>Python float repr: shortest round-trip digits; scientific when the exponent is &lt; -4 or &gt;= 16.</summary>
    public static string FloatRepr(double d)
    {
        if (d == 0)
        {
            return double.IsNegative(d) ? "-0.0" : "0.0";
        }
        // Shortest round-trip digits come from "R"; parse them into digits + decimal exponent.
        var shortest = d.ToString("R", CultureInfo.InvariantCulture);
        var neg = shortest.StartsWith('-');
        if (neg)
        {
            shortest = shortest[1..];
        }
        var ePos = shortest.IndexOfAny(['E', 'e']);
        var mant = ePos < 0 ? shortest : shortest[..ePos];
        var exp = ePos < 0 ? 0 : int.Parse(shortest[(ePos + 1)..], CultureInfo.InvariantCulture);
        var dot = mant.IndexOf('.');
        var intPart = dot < 0 ? mant : mant[..dot];
        var frac = dot < 0 ? "" : mant[(dot + 1)..];
        var digits = (intPart + frac).TrimStart('0');
        var lead = (intPart + frac).Length - digits.Length;
        digits = digits.TrimEnd('0');
        // value = 0.digits * 10^decpt
        var decpt = intPart.Length - lead + exp;
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
            var e = decpt - 1;
            body = digits[..1] + (digits.Length > 1 ? "." + digits[1..] : "") + "e" + (e < 0 ? "-" : "+") + Math.Abs(e).ToString("00", CultureInfo.InvariantCulture);
        }
        return neg ? "-" + body : body;
    }
}
