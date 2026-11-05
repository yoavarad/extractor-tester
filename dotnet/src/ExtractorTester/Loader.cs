using System.Globalization;
using System.Numerics;
using System.Text;
using ExtractorTester.Config;
using ExtractorTester.Models;

namespace ExtractorTester.Loader;

/// <summary>Tool-error raised by discovery or item loading (port of loader.LoaderError).</summary>
public sealed class LoaderException : Exception
{
    public LoaderException(ErrorCode code, string message) : base(message)
    {
        Code = code;
    }

    public ErrorCode Code { get; }
}

/// <summary>One item id with its OLD and NEW file paths (null when absent).</summary>
public sealed record DiscoveredItem(string ItemId, string? OldPath, string? NewPath);

/// <summary>Result of input discovery.</summary>
public sealed record Discovery(List<DiscoveredItem> Items, int IgnoredCount, List<ErrorRecord> Warnings);

/// <summary>Input discovery and item JSON parsing (port of loader.py).</summary>
/// <remarks>
/// Paths are used as given; Python prints <c>str(Path(p))</c>, which equals the input for
/// normalized paths (native separators, no trailing separator, no "." segments).
/// </remarks>
public static class ItemLoader
{
    /// <summary>Classify OLD/NEW as file pair or folder pair and list items sorted by item id.</summary>
    public static Discovery Discover(string old, string @new)
    {
        foreach (var p in new[] { old, @new })
        {
            if (!File.Exists(p) && !Directory.Exists(p))
            {
                throw new LoaderException(ErrorCode.INPUT_NOT_FOUND, $"Input not found: {p}");
            }
        }
        var oldIsFile = File.Exists(old);
        if (oldIsFile != File.Exists(@new))
        {
            var kinds = oldIsFile ? ("a file", "a folder") : ("a folder", "a file");
            throw new LoaderException(
                ErrorCode.MIXED_INPUT_KINDS,
                $"OLD is {kinds.Item1} but NEW is {kinds.Item2}; both must be files or both folders");
        }
        if (oldIsFile)
        {
            return new Discovery([new DiscoveredItem(Stem(Path.GetFileName(old)), old, @new)], 0, []);
        }
        var (oldFound, oldIgnored) = Scan(old);
        var (newFound, newIgnored) = Scan(@new);
        var ids = oldFound.Keys.Union(newFound.Keys).ToList();
        ids.Sort(CodePointComparer.Instance);
        var items = ids
            .Select(i => new DiscoveredItem(i, oldFound.GetValueOrDefault(i), newFound.GetValueOrDefault(i)))
            .ToList();
        var warnings = new List<ErrorRecord>();
        if (items.Count == 0)
        {
            warnings.Add(new ErrorRecord
            {
                Code = ErrorCode.EMPTY_INPUT,
                Scope = "run",
                Severity = "warning",
                Message = $"No *.json items found in {old} or {@new}",
            });
        }
        return new Discovery(items, oldIgnored + newIgnored, warnings);
    }

    private static (Dictionary<string, string> Found, int Ignored) Scan(string folder)
    {
        var found = new Dictionary<string, string>();
        var ignored = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
        {
            var name = Path.GetFileName(entry);
            if (name.EndsWith(".json", StringComparison.Ordinal) && File.Exists(entry))
            {
                found[Stem(name)] = entry;
            }
            else
            {
                ignored++;
            }
        }
        return (found, ignored);
    }

    /// <summary>pathlib stem: name without its last suffix; a leading-dot name has no suffix.</summary>
    internal static string Stem(string name)
    {
        var i = name.LastIndexOf('.');
        return i > 0 && i < name.Length - 1 ? name[..i] : name;
    }

    private static LoaderException Invalid(string prefix, string path, string reason) =>
        // File name only: notes stay short, the same on every machine, and free of local paths.
        new(ErrorCode.INVALID_JSON, $"{prefix}file {Path.GetFileName(path)} is not a valid item: {reason}");

    /// <summary>Size-check, parse and validate one item JSON; return the raw object.</summary>
    /// <remarks>
    /// Folder mode callers pass the file stem as expectedItemId; file mode callers load OLD
    /// with null, then NEW with expectedItemId = old["itemId"].
    /// Throws <see cref="JsonRecursionException"/> (Python RecursionError) on nesting past the limit and
    /// <see cref="JsonIntDigitsException"/> (Python ValueError) on an int literal over 4300 digits.
    /// </remarks>
    public static Dictionary<string, object?> LoadItem(
        string path, string? expectedItemId, CompareConfig config, string? side = null)
    {
        var prefix = string.IsNullOrEmpty(side) ? "" : $"{side} ";
        var itemId = string.IsNullOrEmpty(expectedItemId) ? Stem(Path.GetFileName(path)) : expectedItemId;
        long size;
        try
        {
            size = new FileInfo(path).Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw Invalid(prefix, path, $"cannot read file ({StrError(e)})");
        }
        if (size > config.MaxItemBytes)
        {
            throw new LoaderException(
                ErrorCode.ITEM_TOO_LARGE,
                $"{prefix}item {itemId} is {size} bytes (limit {config.MaxItemBytes})");
        }
        byte[] raw;
        try
        {
            raw = ReadUpTo(path, config.MaxItemBytes + 1);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw Invalid(prefix, path, $"cannot read file ({StrError(e)})");
        }
        if (raw.Length > config.MaxItemBytes)
        {
            throw new LoaderException(
                ErrorCode.ITEM_TOO_LARGE,
                $"{prefix}item {itemId} grew past the limit ({config.MaxItemBytes})");
        }
        object? data;
        try
        {
            data = PyJsonDecoder.Loads(PyJsonDecoder.DecodeUtf8Sig(raw));
        }
        catch (PyDecodeException e)
        {
            throw Invalid(prefix, path, $"cannot parse JSON ({e.Message})");
        }
        if (data is not Dictionary<string, object?> obj)
        {
            throw Invalid(prefix, path, "top level must be an object");
        }
        if (obj.GetValueOrDefault("itemId") is not string found)
        {
            throw Invalid(prefix, path, "itemId missing or not a string");
        }
        if (obj.GetValueOrDefault("files") is not List<object?> files)
        {
            throw Invalid(prefix, path, "files missing or not a list");
        }
        foreach (var entry in files)
        {
            if (entry is not Dictionary<string, object?> e)
            {
                throw Invalid(prefix, path, "files entry is not an object");
            }
            if (!e.ContainsKey("fileId") || !e.ContainsKey("parentId"))
            {
                throw Invalid(prefix, path, "files entry missing fileId or parentId");
            }
        }
        if (expectedItemId is not null && found != expectedItemId)
        {
            throw new LoaderException(
                ErrorCode.ITEM_ID_MISMATCH,
                $"{prefix}file {Path.GetFileName(path)} has itemId {found}, expected {expectedItemId}");
        }
        return obj;
    }

    private static byte[] ReadUpTo(string path, long limit)
    {
        using var fh = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        while (ms.Length < limit)
        {
            var n = fh.Read(buf, 0, (int)Math.Min(buf.Length, limit - ms.Length));
            if (n == 0) break;
            ms.Write(buf, 0, n);
        }
        return ms.ToArray();
    }

    // Python OSError.strerror for the common cases (stat/open run first, so these are the stat texts).
    private static string StrError(Exception e)
    {
        var win = OperatingSystem.IsWindows();
        return e switch
        {
            FileNotFoundException => win ? "The system cannot find the file specified" : "No such file or directory",
            DirectoryNotFoundException => win ? "The system cannot find the path specified" : "No such file or directory",
            UnauthorizedAccessException => win ? "Access is denied" : "Permission denied",
            _ => e.Message,
        };
    }
}

/// <summary>Orders strings by Unicode code point, like Python's str comparison.</summary>
public sealed class CodePointComparer : IComparer<string>
{
    public static readonly CodePointComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null) return x is null ? (y is null ? 0 : -1) : 1;
        var n = Math.Min(x.Length, y.Length);
        for (var i = 0; i < n; i++)
        {
            int a = x[i], b = y[i];
            if (a == b) continue;
            // Surrogates (astral code points) sort above all BMP chars, unlike UTF-16 order.
            if (char.IsSurrogate((char)a) != char.IsSurrogate((char)b))
            {
                return char.IsSurrogate((char)a) ? 1 : -1;
            }
            return a.CompareTo(b);
        }
        return x.Length.CompareTo(y.Length);
    }
}

/// <summary>Python UnicodeDecodeError or json.JSONDecodeError; Message equals Python's str(e).</summary>
public sealed class PyDecodeException : Exception
{
    public PyDecodeException(string message) : base(message) { }
}

/// <summary>Python ValueError for an int literal over 4300 digits (not caught by load_item).</summary>
public sealed class JsonIntDigitsException : Exception
{
    public JsonIntDigitsException(string message) : base(message) { }
}

/// <summary>Python RecursionError raised by json.loads on deep nesting (not caught by load_item).</summary>
public sealed class JsonRecursionException : Exception
{
    public JsonRecursionException(string message) : base(message) { }
}

/// <summary>
/// Port of <c>bytes.decode("utf-8-sig")</c> plus CPython's C <c>json.loads</c> scanner, with
/// identical results and error messages. Values: null, bool, string, long, BigInteger, double,
/// List&lt;object?&gt;, Dictionary&lt;string, object?&gt; (key order kept, last duplicate wins).
/// </summary>
public static class PyJsonDecoder
{
    /// <summary>Nesting depth CPython 3.12 accepts before RecursionError (measured at shallow stack).</summary>
    public const int MaxDepth = 2997;

    /// <summary>Python 3.12 default sys.get_int_max_str_digits().</summary>
    public const int MaxIntDigits = 4300;

    /// <summary>Strict UTF-8 decode after stripping one BOM; returns code points.</summary>
    public static int[] DecodeUtf8Sig(byte[] input)
    {
        var start = input.Length >= 3 && input[0] == 0xEF && input[1] == 0xBB && input[2] == 0xBF ? 3 : 0;
        var s = input.AsSpan(start);
        var cps = new int[s.Length];
        var n = 0;
        var i = 0;
        while (i < s.Length)
        {
            int b = s[i];
            if (b < 0x80)
            {
                cps[n++] = b;
                i++;
                continue;
            }
            int need;
            if (b < 0xC2 || b >= 0xF5) throw Utf8Error(s, i, i + 1, "invalid start byte");
            if (b < 0xE0) need = 2;
            else if (b < 0xF0) need = 3;
            else need = 4;
            var avail = Math.Min(need, s.Length - i);
            // Second byte: continuation, with the range limits of E0/ED/F0/F4.
            if (avail >= 2)
            {
                int c2 = s[i + 1];
                var bad = !IsCont(c2)
                    || (b == 0xE0 && c2 < 0xA0) || (b == 0xED && c2 >= 0xA0)
                    || (b == 0xF0 && c2 < 0x90) || (b == 0xF4 && c2 >= 0x90);
                if (bad) throw Utf8Error(s, i, i + 1, "invalid continuation byte");
            }
            for (var k = 2; k < avail; k++)
            {
                if (!IsCont(s[i + k])) throw Utf8Error(s, i, i + k, "invalid continuation byte");
            }
            if (avail < need) throw Utf8Error(s, i, s.Length, "unexpected end of data");
            var cp = need switch
            {
                2 => ((b & 0x1F) << 6) | (s[i + 1] & 0x3F),
                3 => ((b & 0x0F) << 12) | ((s[i + 1] & 0x3F) << 6) | (s[i + 2] & 0x3F),
                _ => ((b & 0x07) << 18) | ((s[i + 1] & 0x3F) << 12) | ((s[i + 2] & 0x3F) << 6) | (s[i + 3] & 0x3F),
            };
            cps[n++] = cp;
            i += need;
        }
        Array.Resize(ref cps, n);
        return cps;
    }

    private static bool IsCont(int b) => (b & 0xC0) == 0x80;

    private static PyDecodeException Utf8Error(ReadOnlySpan<byte> s, int start, int end, string reason) =>
        new(end - start == 1
            ? $"'utf-8' codec can't decode byte 0x{s[start]:x2} in position {start}: {reason}"
            : $"'utf-8' codec can't decode bytes in position {start}-{end - 1}: {reason}");

    /// <summary>Port of json.loads(str) over code points.</summary>
    public static object? Loads(int[] s) => new Scanner(s).Decode();

    private sealed class Scanner
    {
        private readonly int[] _s;
        private readonly int _endIdx;
        private int _depth;

        public Scanner(int[] s)
        {
            _s = s;
            _endIdx = s.Length - 1;
        }

        public object? Decode()
        {
            if (_s.Length > 0 && _s[0] == 0xFEFF)
            {
                throw Error("Unexpected UTF-8 BOM (decode using utf-8-sig)", 0);
            }
            var idx = SkipWs(0);
            var (obj, end) = ScanOnceOrExpectingValue(idx);
            end = SkipWs(end);
            if (end != _s.Length) throw Error("Extra data", end);
            return obj;
        }

        private int SkipWs(int idx)
        {
            while (idx <= _endIdx && IsWs(_s[idx])) idx++;
            return idx;
        }

        private static bool IsWs(int c) => c is ' ' or '\t' or '\n' or '\r';

        private PyDecodeException Error(string msg, int pos)
        {
            var lineno = 1;
            var lastNl = -1;
            for (var i = 0; i < pos && i < _s.Length; i++)
            {
                if (_s[i] == '\n')
                {
                    lineno++;
                    lastNl = i;
                }
            }
            var colno = pos - lastNl;
            return new PyDecodeException($"{msg}: line {lineno} column {colno} (char {pos})");
        }

        // StopIteration(idx) inside the C scanner surfaces as "Expecting value" at idx.
        private sealed class StopIteration(int idx) : Exception
        {
            public int Idx { get; } = idx;
        }

        private (object? Value, int Next) ScanOnceOrExpectingValue(int idx)
        {
            try
            {
                return ScanOnce(idx);
            }
            catch (StopIteration e)
            {
                throw Error("Expecting value", e.Idx);
            }
        }

        private bool Matches(int idx, string lit)
        {
            for (var k = 0; k < lit.Length; k++)
            {
                if (_s[idx + k] != lit[k]) return false;
            }
            return true;
        }

        private (object? Value, int Next) ScanOnce(int idx)
        {
            var length = _s.Length;
            if (idx >= length) throw new StopIteration(idx);
            switch (_s[idx])
            {
                case '"':
                    return ScanString(idx + 1);
                case '{':
                    Enter("object");
                    try
                    {
                        return ParseObject(idx + 1);
                    }
                    finally
                    {
                        _depth--;
                    }
                case '[':
                    Enter("array");
                    try
                    {
                        return ParseArray(idx + 1);
                    }
                    finally
                    {
                        _depth--;
                    }
                case 'n':
                    if (idx + 3 < length && Matches(idx + 1, "ull")) return (null, idx + 4);
                    break;
                case 't':
                    if (idx + 3 < length && Matches(idx + 1, "rue")) return (true, idx + 4);
                    break;
                case 'f':
                    if (idx + 4 < length && Matches(idx + 1, "alse")) return (false, idx + 5);
                    break;
                case 'N':
                    if (idx + 2 < length && Matches(idx + 1, "aN")) return (double.NaN, idx + 3);
                    break;
                case 'I':
                    if (idx + 7 < length && Matches(idx + 1, "nfinity")) return (double.PositiveInfinity, idx + 8);
                    break;
                case '-':
                    if (idx + 8 < length && Matches(idx + 1, "Infinity")) return (double.NegativeInfinity, idx + 9);
                    break;
            }
            return MatchNumber(idx);
        }

        private void Enter(string kind)
        {
            if (++_depth > MaxDepth)
            {
                _depth--;
                throw new JsonRecursionException(
                    $"maximum recursion depth exceeded while decoding a JSON {kind} from a unicode string");
            }
        }

        private static bool IsDigit(int c) => c >= '0' && c <= '9';

        private (object? Value, int Next) MatchNumber(int start)
        {
            var idx = start;
            if (_s[idx] == '-')
            {
                idx++;
                if (idx > _endIdx) throw new StopIteration(start);
            }
            if (_s[idx] >= '1' && _s[idx] <= '9')
            {
                idx++;
                while (idx <= _endIdx && IsDigit(_s[idx])) idx++;
            }
            else if (_s[idx] == '0')
            {
                idx++;
            }
            else
            {
                throw new StopIteration(start);
            }
            var isFloat = false;
            if (idx < _endIdx && _s[idx] == '.' && IsDigit(_s[idx + 1]))
            {
                isFloat = true;
                idx += 2;
                while (idx <= _endIdx && IsDigit(_s[idx])) idx++;
            }
            if (idx < _endIdx && (_s[idx] == 'e' || _s[idx] == 'E'))
            {
                var eStart = idx;
                idx++;
                if (idx < _endIdx && (_s[idx] == '-' || _s[idx] == '+')) idx++;
                while (idx <= _endIdx && IsDigit(_s[idx])) idx++;
                if (IsDigit(_s[idx - 1])) isFloat = true;
                else idx = eStart;
            }
            var sb = new StringBuilder(idx - start);
            for (var k = start; k < idx; k++) sb.Append((char)_s[k]);
            var text = sb.ToString();
            if (isFloat)
            {
                return (double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture), idx);
            }
            var digits = text.Length - (text[0] == '-' ? 1 : 0);
            if (digits > MaxIntDigits)
            {
                throw new JsonIntDigitsException(
                    $"Exceeds the limit ({MaxIntDigits} digits) for integer string conversion: value has {digits} digits; "
                    + "use sys.set_int_max_str_digits() to increase the limit");
            }
            // Cast each branch: a bare ternary would widen long to BigInteger.
            var value = long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)
                ? (object)l
                : BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            return (value, idx);
        }

        private (object? Value, int Next) ParseObject(int idx)
        {
            var obj = new Dictionary<string, object?>();
            idx = SkipWs(idx);
            if (idx > _endIdx || _s[idx] != '}')
            {
                while (true)
                {
                    if (idx > _endIdx || _s[idx] != '"')
                    {
                        throw Error("Expecting property name enclosed in double quotes", idx);
                    }
                    var (key, next) = ScanString(idx + 1);
                    idx = SkipWs(next);
                    if (idx > _endIdx || _s[idx] != ':')
                    {
                        throw Error("Expecting ':' delimiter", idx);
                    }
                    idx = SkipWs(idx + 1);
                    var (val, after) = ScanOnce(idx);
                    obj[(string)key!] = val;
                    idx = SkipWs(after);
                    if (idx <= _endIdx && _s[idx] == '}') break;
                    if (idx > _endIdx || _s[idx] != ',')
                    {
                        throw Error("Expecting ',' delimiter", idx);
                    }
                    idx = SkipWs(idx + 1);
                }
            }
            return (obj, idx + 1);
        }

        private (object? Value, int Next) ParseArray(int idx)
        {
            var list = new List<object?>();
            idx = SkipWs(idx);
            if (idx > _endIdx || _s[idx] != ']')
            {
                while (true)
                {
                    var (val, next) = ScanOnce(idx);
                    list.Add(val);
                    idx = SkipWs(next);
                    if (idx <= _endIdx && _s[idx] == ']') break;
                    if (idx > _endIdx || _s[idx] != ',')
                    {
                        throw Error("Expecting ',' delimiter", idx);
                    }
                    idx = SkipWs(idx + 1);
                }
            }
            return (list, idx + 1);
        }

        private static int HexValue(int d) => d switch
        {
            >= '0' and <= '9' => d - '0',
            >= 'a' and <= 'f' => d - 'a' + 10,
            >= 'A' and <= 'F' => d - 'A' + 10,
            _ => -1,
        };

        // Port of scanstring_unicode; end is the index just after the opening quote.
        private (object? Value, int Next) ScanString(int end)
        {
            var len = _s.Length;
            var begin = end - 1;
            var sb = new StringBuilder();
            while (true)
            {
                int next;
                var c = 0;
                for (next = end; next < len; next++)
                {
                    c = _s[next];
                    if (c == '"' || c == '\\') break;
                    if (c <= 0x1F) throw Error("Invalid control character at", next);
                }
                if (next >= len || !(c == '"' || c == '\\'))
                {
                    throw Error("Unterminated string starting at", begin);
                }
                for (var k = end; k < next; k++) AppendCp(sb, _s[k]);
                next++;
                if (c == '"')
                {
                    end = next;
                    break;
                }
                if (next == len) throw Error("Unterminated string starting at", begin);
                c = _s[next];
                if (c != 'u')
                {
                    end = next + 1;
                    c = c switch
                    {
                        '"' => '"',
                        '\\' => '\\',
                        '/' => '/',
                        'b' => '\b',
                        'f' => '\f',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => 0,
                    };
                    if (c == 0) throw Error("Invalid \\escape", end - 2);
                }
                else
                {
                    c = 0;
                    next++;
                    end = next + 4;
                    if (end >= len) throw Error("Invalid \\uXXXX escape", next - 1);
                    for (; next < end; next++)
                    {
                        var h = HexValue(_s[next]);
                        if (h < 0) throw Error("Invalid \\uXXXX escape", end - 5);
                        c = (c << 4) | h;
                    }
                    if (c >= 0xD800 && c <= 0xDBFF && end + 6 < len
                        && _s[next++] == '\\' && _s[next++] == 'u')
                    {
                        var c2 = 0;
                        end += 6;
                        for (; next < end; next++)
                        {
                            var h = HexValue(_s[next]);
                            if (h < 0) throw Error("Invalid \\uXXXX escape", end - 5);
                            c2 = (c2 << 4) | h;
                        }
                        if (c2 >= 0xDC00 && c2 <= 0xDFFF)
                        {
                            c = 0x10000 + ((c - 0xD800) << 10) + (c2 - 0xDC00);
                        }
                        else
                        {
                            end -= 6;
                        }
                    }
                }
                AppendCp(sb, c);
            }
            return (sb.ToString(), end);
        }

        private static void AppendCp(StringBuilder sb, int cp)
        {
            if (cp > 0xFFFF) sb.Append(char.ConvertFromUtf32(cp));
            else sb.Append((char)cp);
        }
    }
}
