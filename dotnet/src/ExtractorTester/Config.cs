using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using ExtractorTester.Models;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace ExtractorTester.Config;

/// <summary>INVALID_CONFIG: names the file and the reason (port of config.ConfigError).</summary>
public sealed class ConfigException : Exception
{
    public const string Code = "INVALID_CONFIG";

    public string ConfigPath { get; }
    public string Reason { get; }

    public ConfigException(string path, string reason)
        : base($"Invalid config {path}: {reason}")
    {
        ConfigPath = path;
        Reason = reason;
    }
}

/// <summary>Validated compare-config; see docs/specs/config.md. Property order mirrors the pydantic model.</summary>
public sealed class CompareConfig
{
    public static readonly string[] StructuralFields = { "fileId", "parentId", "rootId" };

    public string NameField { get; set; } = "";
    public string MimePrimaryField { get; set; } = "";
    public string MimeSecondaryField { get; set; } = "";
    public string TextField { get; set; } = "text";
    public List<string> GenericFields { get; set; } = new();
    public List<string> IgnoreFields { get; set; } = new();
    public string? TypeFamiliesFile { get; set; }
    public long TextHashChars { get; set; } = 1_000_000;
    public long FullCompareMaxChars { get; set; } = 100_000;
    public long MaxBlockChars { get; set; } = 4000;
    // pydantic does not validate defaults: an unset 20 / 98 dumps as the int 20 / 98, a set value as a float.
    // Public fields (not properties) so PyJson leaves them out of the dump.
    public bool CutLinesWarnPctSet;
    public bool ContainmentPctSet;
    private double _cutLinesWarnPct = 20;
    private double _containmentPct = 98;
    public double CutLinesWarnPct { get => _cutLinesWarnPct; set { _cutLinesWarnPct = value; CutLinesWarnPctSet = true; } }
    public double ContainmentPct { get => _containmentPct; set { _containmentPct = value; ContainmentPctSet = true; } }
    public long ExamplesPerResult { get; set; } = 5;
    public long SnippetChars { get; set; } = 300;
    public long MaxItemBytes { get; set; } = 52_428_800;
    public long ItemTimeoutSeconds { get; set; } = 600;

    /// <summary>Lowercased MIME to family name; built from type_families_file, not a config key.</summary>
    /// A field, not a property, so PyJson (reflection over properties) leaves it out of the dump.
    public Dictionary<string, string> FamilyMap = new();

    public void SetFamilyMap(Dictionary<string, string> mapping) => FamilyMap = mapping;

    /// <summary>Add structural ids and the text field to ignore_fields (pydantic model_post_init).</summary>
    public void ApplyPostInit()
    {
        foreach (var f in StructuralFields.Append(TextField))
        {
            if (!IgnoreFields.Contains(f)) IgnoreFields.Add(f);
        }
    }
}

/// <summary>compare-config + type-families loader producing one validated CompareConfig.</summary>
public static class ConfigLoader
{
    /// <summary>Load and validate compare-config (and its type-families file) or throw ConfigException.</summary>
    public static CompareConfig Load(string path)
    {
        var data = ReadYaml(path);
        var config = Validate(path, data);
        if (config.TypeFamiliesFile is not null)
        {
            var dir = Path.GetDirectoryName(path) ?? "";
            config.SetFamilyMap(LoadFamilies(Path.Combine(dir, config.TypeFamiliesFile)));
        }
        return config;
    }

    // ---- validation (pydantic strict + extra=forbid messages) -----------------------------

    private enum Kind { Str, StrOrNull, StrList, Int, Float }

    private sealed record Field(string Name, Kind Kind, bool Required, double? Min = null, double? Max = null);

    private static readonly Field[] Fields =
    {
        new("name_field", Kind.Str, true),
        new("mime_primary_field", Kind.Str, true),
        new("mime_secondary_field", Kind.Str, true),
        new("text_field", Kind.Str, false),
        new("generic_fields", Kind.StrList, false),
        new("ignore_fields", Kind.StrList, false),
        new("type_families_file", Kind.StrOrNull, false),
        new("text_hash_chars", Kind.Int, false, 1),
        new("full_compare_max_chars", Kind.Int, false, 0),
        new("max_block_chars", Kind.Int, false, 100),
        new("cut_lines_warn_pct", Kind.Float, false, 0, 100),
        new("containment_pct", Kind.Float, false, 0, 100),
        new("examples_per_result", Kind.Int, false, 0),
        new("snippet_chars", Kind.Int, false, 20),
        new("max_item_bytes", Kind.Int, false, 1),
        new("item_timeout_seconds", Kind.Int, false, 1),
    };

    private static CompareConfig Validate(string path, List<KeyValuePair<object?, object?>> data)
    {
        var errors = new List<string>();
        var values = new Dictionary<string, object?>();
        var known = Fields.Select(f => f.Name).ToHashSet();

        foreach (var f in Fields)
        {
            var idx = data.FindIndex(p => p.Key is string s && s == f.Name);
            if (idx < 0)
            {
                if (f.Required) errors.Add($"{f.Name}: Field required");
                continue;
            }
            CheckField(f, data[idx].Value, errors, values);
        }
        foreach (var (key, _) in data)
        {
            if (key is not string ks) errors.Add($"{PyStr(key)}: Keys should be strings");
            else if (!known.Contains(ks)) errors.Add($"{ks}: Extra inputs are not permitted");
        }
        if (errors.Count > 0) throw new ConfigException(path, string.Join("; ", errors));

        var c = new CompareConfig
        {
            NameField = (string)values["name_field"]!,
            MimePrimaryField = (string)values["mime_primary_field"]!,
            MimeSecondaryField = (string)values["mime_secondary_field"]!,
        };
        if (values.TryGetValue("text_field", out var v)) c.TextField = (string)v!;
        if (values.TryGetValue("generic_fields", out v)) c.GenericFields = (List<string>)v!;
        if (values.TryGetValue("ignore_fields", out v)) c.IgnoreFields = (List<string>)v!;
        if (values.TryGetValue("type_families_file", out v)) c.TypeFamiliesFile = (string?)v;
        if (values.TryGetValue("text_hash_chars", out v)) c.TextHashChars = (long)v!;
        if (values.TryGetValue("full_compare_max_chars", out v)) c.FullCompareMaxChars = (long)v!;
        if (values.TryGetValue("max_block_chars", out v)) c.MaxBlockChars = (long)v!;
        if (values.TryGetValue("cut_lines_warn_pct", out v)) c.CutLinesWarnPct = (double)v!;
        if (values.TryGetValue("containment_pct", out v)) c.ContainmentPct = (double)v!;
        if (values.TryGetValue("examples_per_result", out v)) c.ExamplesPerResult = (long)v!;
        if (values.TryGetValue("snippet_chars", out v)) c.SnippetChars = (long)v!;
        if (values.TryGetValue("max_item_bytes", out v)) c.MaxItemBytes = (long)v!;
        if (values.TryGetValue("item_timeout_seconds", out v)) c.ItemTimeoutSeconds = (long)v!;
        c.ApplyPostInit();
        return c;
    }

    private static void CheckField(Field f, object? value, List<string> errors, Dictionary<string, object?> values)
    {
        switch (f.Kind)
        {
            case Kind.Str:
                if (value is string s) values[f.Name] = s;
                else errors.Add($"{f.Name}: Input should be a valid string");
                break;
            case Kind.StrOrNull:
                if (value is null || value is string) values[f.Name] = value;
                else errors.Add($"{f.Name}: Input should be a valid string");
                break;
            case Kind.StrList:
                if (value is not List<object?> list)
                {
                    errors.Add($"{f.Name}: Input should be a valid list");
                    break;
                }
                var strs = new List<string>();
                var ok = true;
                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i] is string item) strs.Add(item);
                    else
                    {
                        ok = false;
                        errors.Add($"{f.Name}.{i}: Input should be a valid string");
                    }
                }
                if (ok) values[f.Name] = strs;
                break;
            case Kind.Int:
                if (value is not BigInteger n)
                {
                    errors.Add($"{f.Name}: Input should be a valid integer");
                    break;
                }
                if (n < new BigInteger(f.Min!.Value))
                {
                    errors.Add($"{f.Name}: Input should be greater than or equal to {f.Min.Value:0}");
                    break;
                }
                values[f.Name] = n > long.MaxValue ? long.MaxValue : (long)n;
                break;
            case Kind.Float:
                double d;
                if (value is BigInteger bi) d = (double)bi;
                else if (value is double dv) d = dv;
                else
                {
                    errors.Add($"{f.Name}: Input should be a valid number");
                    break;
                }
                if (!(d >= f.Min!.Value))
                {
                    errors.Add($"{f.Name}: Input should be greater than or equal to {f.Min.Value:0}");
                    break;
                }
                if (!(d <= f.Max!.Value))
                {
                    errors.Add($"{f.Name}: Input should be less than or equal to {f.Max.Value:0}");
                    break;
                }
                values[f.Name] = d;
                break;
        }
    }

    // ---- type families ---------------------------------------------------------------------

    private static Dictionary<string, string> LoadFamilies(string path)
    {
        var data = ReadYaml(path);
        var extra = data.Select(p => PyStr(p.Key)).Where(k => k != "families").Distinct().ToList();
        if (extra.Count > 0)
        {
            extra.Sort(StringComparer.Ordinal);
            throw new ConfigException(path, $"unknown key(s): {string.Join(", ", extra)}");
        }
        var famEntry = data.Where(p => p.Key is string s && s == "families").ToList();
        if (famEntry.Count == 0 || famEntry[0].Value is not List<KeyValuePair<object?, object?>> families)
        {
            throw new ConfigException(path, "'families' must be a mapping of family name to list of MIME types");
        }
        var result = new Dictionary<string, string>();
        foreach (var (family, mimes) in families)
        {
            if (mimes is not List<object?> list || !list.All(m => m is string))
            {
                throw new ConfigException(path, $"family '{PyStr(family)}' must be a list of MIME strings");
            }
            var fam = PyStr(family);
            foreach (var m in list)
            {
                var key = ((string)m!).ToLowerInvariant();
                if (result.TryGetValue(key, out var existing) && existing != fam)
                {
                    throw new ConfigException(path, $"MIME type '{key}' listed in two families: '{existing}' and '{fam}'");
                }
                result[key] = fam;
            }
        }
        return result;
    }

    // ---- YAML reading with PyYAML safe_load scalar resolution -------------------------------

    // Mappings are List<KeyValuePair<object?, object?>> (insertion order; last duplicate wins,
    // first position kept, like a Python dict). Sequences are List<object?>.
    private static List<KeyValuePair<object?, object?>> ReadYaml(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path, new UTF8Encoding(false, true));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            throw new ConfigException(path, $"cannot read file ({Strerror(e)})");
        }
        object? data;
        try
        {
            data = ParseDocument(text);
        }
        catch (YamlException e)
        {
            throw new ConfigException(path, $"YAML parse error: {e.Message}");
        }
        if (data is not List<KeyValuePair<object?, object?>> map)
        {
            throw new ConfigException(path, "top level must be a mapping");
        }
        return map;
    }

    private static string Strerror(Exception e) => e switch
    {
        FileNotFoundException or DirectoryNotFoundException => "No such file or directory",
        UnauthorizedAccessException => "Permission denied",
        _ => e.Message,
    };

    private static object? ParseDocument(string text)
    {
        var parser = new Parser(new StringReader(text));
        parser.Consume<StreamStart>();
        if (parser.Accept<StreamEnd>(out _)) return null;
        parser.Consume<DocumentStart>();
        var anchors = new Dictionary<string, object?>();
        var root = Compose(parser, anchors);
        parser.Consume<DocumentEnd>();
        if (!parser.Accept<StreamEnd>(out _))
        {
            throw new YamlException("expected a single document in the stream");
        }
        return root;
    }

    private static object? Compose(IParser parser, Dictionary<string, object?> anchors)
    {
        switch (parser.Current)
        {
            case AnchorAlias alias:
                parser.MoveNext();
                if (!anchors.TryGetValue(alias.Value.Value, out var aliased))
                {
                    throw new YamlException($"found undefined alias '{alias.Value.Value}'");
                }
                return aliased;
            case Scalar scalar:
                parser.MoveNext();
                var value = ResolveScalar(scalar);
                if (!scalar.Anchor.IsEmpty) anchors[scalar.Anchor.Value] = value;
                return value;
            case SequenceStart seq:
                parser.MoveNext();
                var list = new List<object?>();
                if (!seq.Anchor.IsEmpty) anchors[seq.Anchor.Value] = list;
                while (parser.Current is not SequenceEnd) list.Add(Compose(parser, anchors));
                parser.MoveNext();
                return list;
            case MappingStart map:
                parser.MoveNext();
                var pairs = new List<KeyValuePair<object?, object?>>();
                if (!map.Anchor.IsEmpty) anchors[map.Anchor.Value] = pairs;
                while (parser.Current is not MappingEnd)
                {
                    var key = Compose(parser, anchors);
                    var val = Compose(parser, anchors);
                    var idx = pairs.FindIndex(p => Equals(p.Key, key));
                    if (idx >= 0) pairs[idx] = new(pairs[idx].Key, val);
                    else pairs.Add(new(key, val));
                }
                parser.MoveNext();
                return pairs;
            default:
                throw new YamlException($"unexpected YAML event {parser.Current?.GetType().Name}");
        }
    }

    // PyYAML (YAML 1.1) implicit resolvers. Quoted or block scalars are always strings.
    private static object? ResolveScalar(Scalar s)
    {
        var v = s.Value;
        if (s.Style != ScalarStyle.Plain) return v;
        if (v.Length == 0 || v is "~" or "null" or "Null" or "NULL") return null;
        if (v is "yes" or "Yes" or "YES" or "true" or "True" or "TRUE" or "on" or "On" or "ON") return true;
        if (v is "no" or "No" or "NO" or "false" or "False" or "FALSE" or "off" or "Off" or "OFF") return false;
        if (TryInt(v, out var i)) return i;
        if (TryFloat(v, out var d)) return d;
        return v;
    }

    private static bool AllIn(string x, string set) => x.Length > 0 && x.All(set.Contains);

    private static bool TryInt(string v, out BigInteger result)
    {
        result = default;
        var sign = 1;
        var t = v;
        if (t.StartsWith('-')) { sign = -1; t = t[1..]; }
        else if (t.StartsWith('+')) t = t[1..];
        if (t.Length == 0) return false;
        const string Dec = "0123456789_";
        if (t == "0") return true;
        if (t.StartsWith("0b") && AllIn(t[2..], "01_")) return Parse(t[2..], 2, sign, out result);
        if (t.StartsWith("0x") && AllIn(t[2..], "0123456789abcdefABCDEF_")) return Parse(t[2..], 16, sign, out result);
        if (t[0] == '0') return AllIn(t[1..], "01234567_") && Parse(t[1..], 8, sign, out result);
        if (AllIn(t, Dec) && t[0] != '_') return Parse(t, 10, sign, out result);
        if (t.Contains(':'))
        {
            var parts = t.Split(':');
            if (AllIn(parts[0], Dec) && parts[0][0] != '_'
                && parts.Skip(1).All(p => p.Length is 1 or 2 && p.All(char.IsAsciiDigit) && int.Parse(p) <= 59))
            {
                Parse(parts[0], 10, 1, out var total);
                foreach (var p in parts.Skip(1)) total = total * 60 + int.Parse(p);
                result = total * sign;
                return true;
            }
        }
        return false;
    }

    private static bool Parse(string digits, int radix, int sign, out BigInteger result)
    {
        result = 0;
        var any = false;
        foreach (var ch in digits.Replace("_", ""))
        {
            result = result * radix + Convert.ToInt32(ch.ToString(), 16);
            any = true;
        }
        result *= sign;
        return any;
    }

    private static bool TryFloat(string v, out double result)
    {
        result = 0;
        var t = v;
        var sign = 1;
        if (t.StartsWith('-')) { sign = -1; t = t[1..]; }
        else if (t.StartsWith('+')) t = t[1..];
        if (t is ".inf" or ".Inf" or ".INF") { result = sign * double.PositiveInfinity; return true; }
        if (v is ".nan" or ".NaN" or ".NAN") { result = double.NaN; return true; }
        var clean = t.Replace("_", "");
        if (Regex.IsMatch(t, @"^(?:[0-9][0-9_]*\.[0-9_]*|\.[0-9_]+)(?:[eE][-+][0-9]+)?$"))
        {
            result = sign * double.Parse(clean, NumberStyles.Float, CultureInfo.InvariantCulture);
            return true;
        }
        if (Regex.IsMatch(t, @"^[0-9][0-9_]*(?::[0-5]?[0-9])+\.[0-9_]*$"))
        {
            double total = 0;
            foreach (var p in clean.Split(':')) total = total * 60 + double.Parse(p, NumberStyles.Float, CultureInfo.InvariantCulture);
            result = sign * total;
            return true;
        }
        return false;
    }

    /// <summary>Python str() of a YAML scalar, for messages.</summary>
    private static string PyStr(object? o) => o switch
    {
        null => "None",
        true => "True",
        false => "False",
        string s => s,
        double d => PyJson.FormatFloat(d),
        _ => o.ToString() ?? "",
    };
}
