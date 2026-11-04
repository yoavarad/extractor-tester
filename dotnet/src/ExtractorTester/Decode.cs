using System.Security.Cryptography;
using System.Text;

namespace ExtractorTester.Decode;

/// <summary>Raised when a value is not strictly valid base64 (error code INVALID_BASE64).</summary>
public sealed class InvalidBase64Exception : Exception
{
    public InvalidBase64Exception(string message) : base(message) { }
}

/// <summary>
/// Python-parity text helpers (ADR-009): code points, str.split / splitlines semantics,
/// strict base64, counted UTF-8 replacement decoding and the text hash.
/// </summary>
public static class Text
{
    // ---- code points -------------------------------------------------------------------

    /// <summary>Number of Unicode code points (Python len(str)).</summary>
    public static int CodePointLength(string s)
    {
        var n = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
            n++;
        }
        return n;
    }

    public static int[] ToCodePoints(string s)
    {
        var list = new List<int>(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                list.Add(char.ConvertToUtf32(s[i], s[i + 1]));
                i++;
            }
            else
            {
                list.Add(s[i]);
            }
        }
        return list.ToArray();
    }

    public static string FromCodePoints(ReadOnlySpan<int> cps)
    {
        var sb = new StringBuilder(cps.Length);
        foreach (var cp in cps)
        {
            if (cp >= 0x10000) sb.Append(char.ConvertFromUtf32(cp));
            else sb.Append((char)cp);
        }
        return sb.ToString();
    }

    /// <summary>First <paramref name="count"/> code points (Python s[:count]).</summary>
    public static string Prefix(string s, int count)
    {
        if (count <= 0) return "";
        var seen = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (seen == count) return s[..i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
            seen++;
        }
        return s;
    }

    /// <summary>Code point slice (Python s[start:end], non-negative bounds).</summary>
    public static string Slice(string s, int start, int end)
    {
        var cps = ToCodePoints(s);
        start = Math.Clamp(start, 0, cps.Length);
        end = Math.Clamp(end, start, cps.Length);
        return FromCodePoints(cps.AsSpan(start, end - start));
    }

    // ---- whitespace / split ------------------------------------------------------------

    /// <summary>Equal to Python str.isspace() for one code point (what str.split() uses).</summary>
    public static bool IsPyWhitespace(int cp) => cp switch
    {
        >= 0x09 and <= 0x0D => true,
        >= 0x1C and <= 0x20 => true,
        0x85 or 0xA0 or 0x1680 or 0x2028 or 0x2029 or 0x202F or 0x205F or 0x3000 => true,
        >= 0x2000 and <= 0x200A => true,
        _ => false,
    };

    /// <summary>Python str.split() with no arguments.</summary>
    public static List<string> PySplit(string s)
    {
        var result = new List<string>();
        var start = -1;
        for (var i = 0; i < s.Length; i++)
        {
            // All whitespace code points are in the BMP, so UTF-16 units are enough here.
            if (IsPyWhitespace(s[i]))
            {
                if (start >= 0) { result.Add(s[start..i]); start = -1; }
            }
            else if (start < 0)
            {
                start = i;
            }
        }
        if (start >= 0) result.Add(s[start..]);
        return result;
    }

    private static bool IsLineBreak(char c) =>
        c is '\n' or '\r' or '\v' or '\f' or '\u001c' or '\u001d' or '\u001e' or '\u0085' or '\u2028' or '\u2029';

    /// <summary>Python str.splitlines(keepends).</summary>
    public static List<string> PySplitLines(string s, bool keepEnds = false)
    {
        var result = new List<string>();
        var i = 0;
        var n = s.Length;
        while (i < n)
        {
            var j = i;
            while (j < n && !IsLineBreak(s[j])) j++;
            var eol = j;
            if (j < n)
            {
                if (s[j] == '\r' && j + 1 < n && s[j + 1] == '\n') j += 2;
                else j++;
            }
            result.Add(s[i..(keepEnds ? j : eol)]);
            i = j;
        }
        return result;
    }

    // ---- base64 ------------------------------------------------------------------------

    private static int B64Value(char c) => c switch
    {
        >= 'A' and <= 'Z' => c - 'A',
        >= 'a' and <= 'z' => c - 'a' + 26,
        >= '0' and <= '9' => c - '0' + 52,
        '+' => 62,
        '/' => 63,
        _ => -1,
    };

    /// <summary>Equal to Python base64.b64decode(value, validate=True).</summary>
    public static byte[] StrictBase64Decode(string value)
    {
        var n = value.Length;
        var pad = 0;
        while (pad < 2 && pad < n && value[n - 1 - pad] == '=') pad++;
        var dataLen = n - pad;
        for (var i = 0; i < dataLen; i++)
        {
            if (B64Value(value[i]) < 0) throw new InvalidBase64Exception("Only base64 data is allowed");
        }
        // With only alphabet chars plus <=2 trailing '=', a multiple-of-4 length fixes the padding.
        if (n % 4 != 0) throw new InvalidBase64Exception("Incorrect padding");

        var output = new List<byte>(dataLen * 3 / 4);
        var acc = 0;
        var bits = 0;
        for (var i = 0; i < dataLen; i++)
        {
            acc = (acc << 6) | B64Value(value[i]);
            bits += 6;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)((acc >> bits) & 0xFF));
            }
            acc &= (1 << bits) - 1;
        }
        return output.ToArray();
    }

    // ---- UTF-8 with counted replacements -----------------------------------------------

    /// <summary>
    /// Decode UTF-8 like Python errors="replace": one U+FFFD per maximal invalid subpart.
    /// Returns the text and the number of replacements.
    /// </summary>
    public static (string Text, int InvalidCount) DecodeUtf8Counted(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder(data.Length);
        var count = 0;
        var i = 0;
        while (i < data.Length)
        {
            int b = data[i];
            if (b < 0x80) { sb.Append((char)b); i++; continue; }

            int need;
            int cp;
            int lo = 0x80, hi = 0xBF;
            if (b >= 0xC2 && b <= 0xDF) { need = 1; cp = b & 0x1F; }
            else if (b >= 0xE0 && b <= 0xEF)
            {
                need = 2; cp = b & 0x0F;
                if (b == 0xE0) lo = 0xA0;
                else if (b == 0xED) hi = 0x9F;
            }
            else if (b >= 0xF0 && b <= 0xF4)
            {
                need = 3; cp = b & 0x07;
                if (b == 0xF0) lo = 0x90;
                else if (b == 0xF4) hi = 0x8F;
            }
            else
            {
                sb.Append('�'); count++; i++; continue;
            }

            var j = i + 1;
            var ok = true;
            for (var k = 0; k < need; k++, j++)
            {
                if (j >= data.Length || data[j] < lo || data[j] > hi) { ok = false; break; }
                cp = (cp << 6) | (data[j] & 0x3F);
                lo = 0x80; hi = 0xBF;
            }
            if (ok)
            {
                sb.Append(char.ConvertFromUtf32(cp));
            }
            else
            {
                sb.Append('�'); count++;
            }
            i = j;
        }
        return (sb.ToString(), count);
    }

    /// <summary>Port of decode_text: null yields ("", 0); malformed base64 throws.</summary>
    public static (string Text, int InvalidCount) DecodeText(string? value)
    {
        if (value is null) return ("", 0);
        return DecodeUtf8Counted(StrictBase64Decode(value));
    }

    // ---- normalize / hash --------------------------------------------------------------

    /// <summary>Collapse whitespace runs to single spaces and trim.</summary>
    public static string Normalize(string text) => string.Join(' ', PySplit(text));

    /// <summary>sha256 hex of the first <paramref name="limit"/> code points, or null if empty.</summary>
    public static string? TextHash(string normalized, int limit)
    {
        if (normalized.Length == 0 || limit <= 0) return null;
        var bytes = Encoding.UTF8.GetBytes(Prefix(normalized, limit));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
