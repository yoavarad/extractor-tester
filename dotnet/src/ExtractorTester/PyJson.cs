using System.Buffers;
using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ExtractorTester.Models;

/// <summary>
/// JSON text byte-identical to pydantic <c>model_dump_json()</c> (compact) and
/// <c>model_dump_json(indent=2)</c>. Reading uses System.Text.Json with the same names.
/// </summary>
/// <remarks>
/// Untyped (<c>Any</c>) values are plain .NET values: null, bool, string, long, BigInteger,
/// double, List&lt;object?&gt; and Dictionary&lt;string, object?&gt;.
/// </remarks>
public static class PyJson
{
    /// <summary>Options used for reading and for the writer's property names/order.</summary>
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            // Deep file trees (3 JSON levels per archive level) exceed the default of 64.
            MaxDepth = 1024,
        };
        options.Converters.Add(new PyAnyConverter());
        options.MakeReadOnly();
        return options;
    }

    /// <summary>Serialize like pydantic: compact, or indented by 2 spaces.</summary>
    public static string Serialize(object? value, bool indent = false)
    {
        var sb = new StringBuilder();
        Write(sb, value, indent ? 0 : -1);
        return sb.ToString();
    }

    /// <summary>Serialize to UTF-8 bytes (no BOM, no trailing newline).</summary>
    public static byte[] SerializeToUtf8Bytes(object? value, bool indent = false) =>
        Encoding.UTF8.GetBytes(Serialize(value, indent));

    /// <summary>Deserialize JSON text produced by pydantic or by <see cref="Serialize"/>.</summary>
    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("JSON value is null");

    /// <summary>Deserialize into a runtime type.</summary>
    public static object Deserialize(string json, Type type) =>
        JsonSerializer.Deserialize(json, type, Options) ?? throw new JsonException("JSON value is null");

    /// <summary>JSON property names of a model type, in serialization order.</summary>
    public static IReadOnlyList<string> FieldNames(Type type) =>
        Options.GetTypeInfo(type).Properties.Select(p => p.Name).ToList();

    /// <summary>
    /// Float text as pydantic-core writes it: shortest round-trip digits; fixed notation when
    /// the decimal exponent is in (-5, 16], else d[.ddd]e±X; integral values end in ".0";
    /// NaN and infinities become null.
    /// </summary>
    public static string FormatFloat(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return "null";
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
        // value = digits * 10^exp; kk = position of the decimal point.
        var length = digits.Length;
        var kk = length + exp;
        string body;
        if (exp >= 0 && kk <= 16)
        {
            body = digits + new string('0', exp) + ".0";
        }
        else if (kk > 0 && kk <= 16)
        {
            body = digits[..kk] + "." + digits[kk..];
        }
        else if (kk > -5 && kk <= 0)
        {
            body = "0." + new string('0', -kk) + digits;
        }
        else
        {
            var mantissa = length == 1 ? digits : digits[..1] + "." + digits[1..];
            var x = kk - 1;
            body = mantissa + "e" + (x < 0 ? "-" : "+") + Math.Abs(x).ToString(CultureInfo.InvariantCulture);
        }
        return sign + body;
    }

    /// <summary>Datetime text as pydantic writes it: microseconds only when non-zero, Z for UTC.</summary>
    public static string FormatDateTime(DateTimeOffset value)
    {
        var text = value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var micros = value.Ticks % TimeSpan.TicksPerSecond / 10;
        if (micros != 0)
        {
            text += "." + micros.ToString("D6", CultureInfo.InvariantCulture);
        }
        if (value.Offset == TimeSpan.Zero)
        {
            return text + "Z";
        }
        var off = value.Offset;
        return text + (off < TimeSpan.Zero ? "-" : "+") + off.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture);
    }

    // depth < 0 means compact output; otherwise current indent level (2 spaces each).
    private static void Write(StringBuilder sb, object? value, int depth)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                return;
            case bool b:
                sb.Append(b ? "true" : "false");
                return;
            case string s:
                WriteString(sb, s);
                return;
            case double d:
                sb.Append(FormatFloat(d));
                return;
            case long or int or short or sbyte or ulong or uint or ushort or byte or BigInteger:
                sb.Append(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                return;
            case DateTimeOffset dto:
                WriteString(sb, FormatDateTime(dto));
                return;
            case Enum en:
                WriteString(sb, en.ToString());
                return;
            case IDictionary dict:
                WriteObject(sb, depth, dict.Count, Entries(dict));
                return;
            case IEnumerable list:
                WriteArray(sb, list, depth);
                return;
        }
        var info = Options.GetTypeInfo(value.GetType());
        if (info.Kind != JsonTypeInfoKind.Object)
        {
            throw new NotSupportedException($"Cannot serialize {value.GetType()}");
        }
        WriteObject(sb, depth, info.Properties.Count, info.Properties.Select(p => (p.Name, p.Get!(value))));
    }

    private static IEnumerable<(string, object?)> Entries(IDictionary dict)
    {
        foreach (DictionaryEntry entry in dict)
        {
            yield return ((string)entry.Key, entry.Value);
        }
    }

    private static void WriteObject(StringBuilder sb, int depth, int count, IEnumerable<(string Name, object? Value)> members)
    {
        if (count == 0)
        {
            sb.Append("{}");
            return;
        }
        sb.Append('{');
        var first = true;
        foreach (var (name, val) in members)
        {
            if (!first)
            {
                sb.Append(',');
            }
            first = false;
            NewLine(sb, depth, 1);
            WriteString(sb, name);
            sb.Append(depth < 0 ? ":" : ": ");
            Write(sb, val, depth < 0 ? -1 : depth + 1);
        }
        NewLine(sb, depth, 0);
        sb.Append('}');
    }

    private static void WriteArray(StringBuilder sb, IEnumerable items, int depth)
    {
        sb.Append('[');
        var first = true;
        foreach (var item in items)
        {
            if (!first)
            {
                sb.Append(',');
            }
            first = false;
            NewLine(sb, depth, 1);
            Write(sb, item, depth < 0 ? -1 : depth + 1);
        }
        if (!first)
        {
            NewLine(sb, depth, 0);
        }
        sb.Append(']');
    }

    private static void NewLine(StringBuilder sb, int depth, int extra)
    {
        if (depth >= 0)
        {
            sb.Append('\n').Append(' ', 2 * (depth + extra));
        }
    }

    // serde_json escaping: quote, backslash and control characters only; all else verbatim.
    private static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u00").Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
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

    /// <summary>Reads untyped JSON into plain .NET values; integers stay integers, floats stay doubles.</summary>
    private sealed class PyAnyConverter : JsonConverter<object>
    {
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;
                case JsonTokenType.True:
                    return true;
                case JsonTokenType.False:
                    return false;
                case JsonTokenType.String:
                    return reader.GetString();
                case JsonTokenType.Number:
                    var raw = Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan);
                    if (raw.IndexOfAny(['.', 'e', 'E']) >= 0)
                    {
                        return double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
                    }
                    return long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)
                        ? l
                        : BigInteger.Parse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                case JsonTokenType.StartArray:
                    var list = new List<object?>();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        list.Add(Read(ref reader, typeToConvert, options));
                    }
                    return list;
                case JsonTokenType.StartObject:
                    var dict = new Dictionary<string, object?>();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        var key = reader.GetString()!;
                        reader.Read();
                        dict[key] = Read(ref reader, typeToConvert, options);
                    }
                    return dict;
                default:
                    throw new JsonException($"Unexpected token {reader.TokenType}");
            }
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options) =>
            throw new NotSupportedException("Use PyJson.Serialize");
    }
}
