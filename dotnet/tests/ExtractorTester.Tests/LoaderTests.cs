using System.Numerics;
using System.Text;
using ExtractorTester.Config;
using ExtractorTester.Loader;
using ExtractorTester.Models;

namespace ExtractorTester.Tests;

// Port of tests/test_loader.py plus json.loads / utf-8-sig message parity (expected texts from CPython 3.12).
public sealed class LoaderTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("et-loader-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmp, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static CompareConfig Cfg(long maxItemBytes = 52_428_800) => new()
    {
        NameField = "name",
        MimePrimaryField = "a",
        MimeSecondaryField = "b",
        MaxItemBytes = maxItemBytes,
    };

    private (string Old, string New) Dirs()
    {
        var old = Path.Combine(_tmp, "old");
        var @new = Path.Combine(_tmp, "new");
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(@new);
        return (old, @new);
    }

    private static Dictionary<string, object?> F(params (string K, object? V)[] kv) =>
        kv.ToDictionary(p => p.K, p => p.V);

    [Fact]
    public void SortedAndPaired()
    {
        var (old, @new) = Dirs();
        foreach (var d in new[] { old, @new })
        {
            foreach (var id in new[] { "c", "a", "d", "b" }) TestItems.WriteItem(d, id, []);
        }
        var r = ItemLoader.Discover(old, @new);
        Assert.Equal(["a", "b", "c", "d"], r.Items.Select(i => i.ItemId));
        Assert.Equal(Path.Combine(old, "a.json"), r.Items[0].OldPath);
        Assert.Equal(Path.Combine(@new, "a.json"), r.Items[0].NewPath);
        Assert.Equal(0, r.IgnoredCount);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void OneSided()
    {
        var (old, @new) = Dirs();
        TestItems.WriteItem(old, "x", []);
        TestItems.WriteItem(@new, "y", []);
        var items = ItemLoader.Discover(old, @new).Items;
        Assert.Equal(2, items.Count);
        Assert.Equal(("x", (string?)null), (items[0].ItemId, items[0].NewPath));
        Assert.Equal(("y", (string?)null), (items[1].ItemId, items[1].OldPath));
    }

    [Fact]
    public void IgnoredCounted()
    {
        var (old, @new) = Dirs();
        TestItems.WriteItem(old, "a", []);
        Directory.CreateDirectory(Path.Combine(old, "sub"));
        File.WriteAllText(Path.Combine(old, "n.txt"), "x");
        Directory.CreateDirectory(Path.Combine(@new, "sub.json"));
        var d = ItemLoader.Discover(old, @new);
        Assert.Equal(["a"], d.Items.Select(i => i.ItemId));
        Assert.Equal(3, d.IgnoredCount);
    }

    [Fact]
    public void StemAndCodePointOrder()
    {
        var (old, @new) = Dirs();
        foreach (var name in new[] { ".json", "x.tar.json", "A.JSON", "\uFF5A.json", "\U0001F600.json" })
        {
            File.WriteAllText(Path.Combine(old, name), "{}");
        }
        var d = ItemLoader.Discover(old, @new);
        // Python: ".json" has no suffix (stem ".json"); astral code points sort after U+FF5A.
        Assert.Equal([".json", "x.tar", "\uFF5A", "\U0001F600"], d.Items.Select(i => i.ItemId));
        Assert.Equal(1, d.IgnoredCount);
    }

    [Fact]
    public void FileMode()
    {
        var (old, @new) = Dirs();
        var o = TestItems.WriteItem(old, "a", []);
        var n = TestItems.WriteItem(@new, "b", []);
        var d = ItemLoader.Discover(o, n);
        Assert.Single(d.Items);
        Assert.Equal(new DiscoveredItem("a", o, n), d.Items[0]);
        Assert.Equal(0, d.IgnoredCount);
    }

    [Fact]
    public void InputNotFound()
    {
        var e = Assert.Throws<LoaderException>(() =>
            ItemLoader.Discover(Path.Combine(_tmp, "nope"), Path.Combine(_tmp, "nope2")));
        Assert.Equal(ErrorCode.INPUT_NOT_FOUND, e.Code);
        Assert.Equal($"Input not found: {Path.Combine(_tmp, "nope")}", e.Message);
    }

    [Fact]
    public void MixedKinds()
    {
        var (old, @new) = Dirs();
        var f = TestItems.WriteItem(old, "a", []);
        var e = Assert.Throws<LoaderException>(() => ItemLoader.Discover(f, @new));
        Assert.Equal(ErrorCode.MIXED_INPUT_KINDS, e.Code);
        Assert.Equal("OLD is a file but NEW is a folder; both must be files or both folders", e.Message);
        e = Assert.Throws<LoaderException>(() => ItemLoader.Discover(@new, f));
        Assert.Equal("OLD is a folder but NEW is a file; both must be files or both folders", e.Message);
    }

    [Fact]
    public void EmptyInputWarning()
    {
        var (old, @new) = Dirs();
        var d = ItemLoader.Discover(old, @new);
        Assert.Empty(d.Items);
        var w = Assert.Single(d.Warnings);
        Assert.Equal((ErrorCode.EMPTY_INPUT, "run", "warning"), (w.Code, w.Scope, w.Severity));
        Assert.Equal($"No *.json items found in {old} or {@new}", w.Message);
    }

    [Fact]
    public void LoadOk()
    {
        var p = TestItems.WriteItem(_tmp, "a", [F(("fileId", "1"), ("parentId", ""))]);
        Assert.Equal("a", ItemLoader.LoadItem(p, "a", Cfg())["itemId"]);
        Assert.Equal("a", ItemLoader.LoadItem(p, null, Cfg())["itemId"]);
    }

    [Theory]
    [InlineData("{not json", "cannot parse JSON (Expecting property name enclosed in double quotes: line 1 column 2 (char 1))")]
    [InlineData("\\xff\\xfe", "cannot parse JSON ('utf-8' codec can't decode byte 0xff in position 0: invalid start byte)")]
    [InlineData("[]", "top level must be an object")]
    [InlineData("{\"files\": []}", "itemId missing or not a string")]
    [InlineData("{\"itemId\": 1, \"files\": []}", "itemId missing or not a string")]
    [InlineData("{\"itemId\": \"a\"}", "files missing or not a list")]
    [InlineData("{\"itemId\": \"a\", \"files\": {}}", "files missing or not a list")]
    [InlineData("{\"itemId\": \"a\", \"files\": [1]}", "files entry is not an object")]
    [InlineData("{\"itemId\": \"a\", \"files\": [{\"parentId\": \"\"}]}", "files entry missing fileId or parentId")]
    [InlineData("{\"itemId\": \"a\", \"files\": [{\"fileId\": \"1\"}]}", "files entry missing fileId or parentId")]
    public void InvalidJson(string content, string reason)
    {
        var p = Path.Combine(_tmp, "a.json");
        File.WriteAllBytes(p, Bytes(content));
        var e = Assert.Throws<LoaderException>(() => ItemLoader.LoadItem(p, "a", Cfg(), "old"));
        Assert.Equal(ErrorCode.INVALID_JSON, e.Code);
        Assert.Equal($"old file a.json is not a valid item: {reason}", e.Message);
    }

    [Fact]
    public void UnreadableIsInvalidJson()
    {
        var e = Assert.Throws<LoaderException>(() => ItemLoader.LoadItem(Path.Combine(_tmp, "gone.json"), null, Cfg()));
        Assert.Equal(ErrorCode.INVALID_JSON, e.Code);
        var reason = OperatingSystem.IsWindows() ? "The system cannot find the file specified" : "No such file or directory";
        Assert.Equal($"file gone.json is not a valid item: cannot read file ({reason})", e.Message);
    }

    [Fact]
    public void TooLargeNotParsed()
    {
        var p = Path.Combine(_tmp, "a.json");
        File.WriteAllBytes(p, Encoding.ASCII.GetBytes("{not json" + new string(' ', 100)));
        var e = Assert.Throws<LoaderException>(() => ItemLoader.LoadItem(p, "a", Cfg(maxItemBytes: 10), "new"));
        Assert.Equal(ErrorCode.ITEM_TOO_LARGE, e.Code);
        Assert.Equal("new item a is 109 bytes (limit 10)", e.Message);
    }

    [Fact]
    public void IdMismatchFolderMode()
    {
        var p = TestItems.WriteItem(_tmp, "a", []);
        var e = Assert.Throws<LoaderException>(() => ItemLoader.LoadItem(p, "b", Cfg(), "old"));
        Assert.Equal(ErrorCode.ITEM_ID_MISMATCH, e.Code);
        Assert.Equal("old file a.json has itemId a, expected b", e.Message);
    }

    [Fact]
    public void IdMismatchFileMode()
    {
        var (old, @new) = Dirs();
        var o = TestItems.WriteItem(old, "a", []);
        var n = TestItems.WriteItem(@new, "b", []);
        var expected = (string)ItemLoader.LoadItem(o, null, Cfg(), "old")["itemId"]!;
        var e = Assert.Throws<LoaderException>(() => ItemLoader.LoadItem(n, expected, Cfg(), "new"));
        Assert.Equal(ErrorCode.ITEM_ID_MISMATCH, e.Code);
        Assert.Equal("new file b.json has itemId b, expected a", e.Message);
    }

    [Fact]
    public void Utf8BomTolerated()
    {
        var p = Path.Combine(_tmp, "a.json");
        File.WriteAllBytes(p, Bytes("\\xef\\xbb\\xbf{\"itemId\": \"a\", \"files\": []}"));
        Assert.Equal("a", ItemLoader.LoadItem(p, "a", Cfg(), "old")["itemId"]);
    }

    // Python bytes literal subset: \xNN escapes, everything else ASCII.
    private static byte[] Bytes(string s)
    {
        var bytes = new List<byte>();
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 3 < s.Length && s[i + 1] == 'x')
            {
                bytes.Add(Convert.ToByte(s.Substring(i + 2, 2), 16));
                i += 3;
            }
            else
            {
                bytes.Add((byte)s[i]);
            }
        }
        return bytes.ToArray();
    }

    [Theory]
    [InlineData("{not json", "Expecting property name enclosed in double quotes: line 1 column 2 (char 1)")]
    [InlineData("\\xff\\xfe", "'utf-8' codec can't decode byte 0xff in position 0: invalid start byte")]
    [InlineData("", "Expecting value: line 1 column 1 (char 0)")]
    [InlineData("   ", "Expecting value: line 1 column 4 (char 3)")]
    [InlineData("\\xef\\xbb\\xbf\\xef\\xbb\\xbf{}", "Unexpected UTF-8 BOM (decode using utf-8-sig): line 1 column 1 (char 0)")]
    [InlineData("{\"a\":1,}", "Expecting property name enclosed in double quotes: line 1 column 8 (char 7)")]
    [InlineData("[1,]", "Expecting value: line 1 column 4 (char 3)")]
    [InlineData("{\"a\" 1}", "Expecting ':' delimiter: line 1 column 6 (char 5)")]
    [InlineData("{\"a\":1 \"b\"}", "Expecting ',' delimiter: line 1 column 8 (char 7)")]
    [InlineData("[1 2]", "Expecting ',' delimiter: line 1 column 4 (char 3)")]
    [InlineData("\"abc", "Unterminated string starting at: line 1 column 1 (char 0)")]
    [InlineData("\"a\\x01\"", "Invalid control character at: line 1 column 3 (char 2)")]
    [InlineData("\"\\x5cx\"", "Invalid \\escape: line 1 column 2 (char 1)")]
    [InlineData("\"\\x5cu12\"", "Invalid \\uXXXX escape: line 1 column 3 (char 2)")]
    [InlineData("\"\\x5cu12g4\"", "Invalid \\uXXXX escape: line 1 column 3 (char 2)")]
    [InlineData("\"\\x5cud800\\x5cu12g4\"", "Invalid \\uXXXX escape: line 1 column 9 (char 8)")]
    [InlineData("\"\\x5cu1234", "Invalid \\uXXXX escape: line 1 column 3 (char 2)")]
    [InlineData("\"\\x5c", "Unterminated string starting at: line 1 column 1 (char 0)")]
    [InlineData("01", "Extra data: line 1 column 2 (char 1)")]
    [InlineData("1.", "Extra data: line 1 column 2 (char 1)")]
    [InlineData("-", "Expecting value: line 1 column 1 (char 0)")]
    [InlineData("nul", "Expecting value: line 1 column 1 (char 0)")]
    [InlineData("\\x0a\\x0a  {x", "Expecting property name enclosed in double quotes: line 3 column 4 (char 5)")]
    [InlineData("{\"a\":\"\\xe2\\x82\"}", "'utf-8' codec can't decode bytes in position 6-7: invalid continuation byte")]
    [InlineData("ab\\xc0\\xaf", "'utf-8' codec can't decode byte 0xc0 in position 2: invalid start byte")]
    [InlineData("\\xed\\xa0\\x80", "'utf-8' codec can't decode byte 0xed in position 0: invalid continuation byte")]
    [InlineData("\\xf0\\x9f\\x98", "'utf-8' codec can't decode bytes in position 0-2: unexpected end of data")]
    [InlineData("\\xf0\\x9f\\x98\\x80{", "Expecting value: line 1 column 1 (char 0)")]
    [InlineData("\"\\xf0\\x9f\\x98\\x80\" x", "Extra data: line 1 column 5 (char 4)")]
    [InlineData("[", "Expecting value: line 1 column 2 (char 1)")]
    [InlineData("{", "Expecting property name enclosed in double quotes: line 1 column 2 (char 1)")]
    [InlineData("{\"a\"", "Expecting ':' delimiter: line 1 column 5 (char 4)")]
    [InlineData("{\"a\":", "Expecting value: line 1 column 6 (char 5)")]
    [InlineData("[1,", "Expecting value: line 1 column 4 (char 3)")]
    [InlineData("tru", "Expecting value: line 1 column 1 (char 0)")]
    [InlineData("\"\\x5cud800\\x5c\"", "Unterminated string starting at: line 1 column 1 (char 0)")]
    [InlineData("\"\\x5cud800\\x5cu\"", "Invalid \\uXXXX escape: line 1 column 9 (char 8)")]
    [InlineData("[]]", "Extra data: line 1 column 3 (char 2)")]
    [InlineData("\\xef\\xbb\\xbf\\xff", "'utf-8' codec can't decode byte 0xff in position 0: invalid start byte")]
    [InlineData("\\xe2\\x82\\xac\\xe2(\\xa1", "'utf-8' codec can't decode byte 0xe2 in position 3: invalid continuation byte")]
    [InlineData("\\xf4\\x90\\x80\\x80", "'utf-8' codec can't decode byte 0xf4 in position 0: invalid continuation byte")]
    [InlineData("\\xe0\\x80\\x80", "'utf-8' codec can't decode byte 0xe0 in position 0: invalid continuation byte")]
    [InlineData("\"\\x5cu123\"", "Invalid \\uXXXX escape: line 1 column 3 (char 2)")]
    [InlineData("1.5e", "Extra data: line 1 column 4 (char 3)")]
    [InlineData("{1:2}", "Expecting property name enclosed in double quotes: line 1 column 2 (char 1)")]
    [InlineData("[1,2", "Expecting ',' delimiter: line 1 column 5 (char 4)")]
    [InlineData("\\xc2", "'utf-8' codec can't decode byte 0xc2 in position 0: unexpected end of data")]
    [InlineData("x\\x80", "'utf-8' codec can't decode byte 0x80 in position 1: invalid start byte")]
    [InlineData("\\x00", "Expecting value: line 1 column 1 (char 0)")]
    [InlineData("\"\\x5cud800\\x5cu12\"", "Invalid \\uXXXX escape: line 1 column 9 (char 8)")]
    [InlineData("\"\\x5cud800\\x5cudc0", "Invalid \\uXXXX escape: line 1 column 9 (char 8)")]
    [InlineData("\"\\x5cu12", "Invalid \\uXXXX escape: line 1 column 3 (char 2)")]
    [InlineData("{\"a\":1,", "Expecting property name enclosed in double quotes: line 1 column 8 (char 7)")]
    [InlineData("{\"a\":1 ", "Expecting ',' delimiter: line 1 column 8 (char 7)")]
    [InlineData("[1, ]", "Expecting value: line 1 column 5 (char 4)")]
    [InlineData("\\xa0", "'utf-8' codec can't decode byte 0xa0 in position 0: invalid start byte")]
    [InlineData("1\\xc2\\xa0", "Extra data: line 1 column 2 (char 1)")]
    public void DecodeErrorMessagesMatchPython(string content, string message)
    {
        var e = Assert.Throws<PyDecodeException>(() =>
            PyJsonDecoder.Loads(PyJsonDecoder.DecodeUtf8Sig(Bytes(content))));
        Assert.Equal(message, e.Message);
    }

    private static object? Parse(string content) => PyJsonDecoder.Loads(PyJsonDecoder.DecodeUtf8Sig(Bytes(content)));

    [Fact]
    public void ParsedValuesMatchPython()
    {
        Assert.Equal(double.NegativeInfinity, Parse("-Infinity"));
        Assert.True(double.IsNaN((double)Parse("NaN")!));
        Assert.Equal(double.PositiveInfinity, Parse("1e400"));
        Assert.Equal(0L, Assert.IsType<long>(Parse("-0")));
        Assert.Equal(100.0, Parse("1E+2"));
        Assert.Equal(BigInteger.Parse("123456789012345678901234567890"), Parse("123456789012345678901234567890"));
        Assert.Equal("\U00010000", Parse("\"\\x5cud800\\x5cudc00\""));
        Assert.Equal("\u007f", Parse("\"\\x7f\""));
        Assert.Equal("\ud800x", Parse("\"\\x5cud800x\""));
        Assert.Empty((Dictionary<string, object?>)Parse("\\x09{\\x0d}\\x0a")!);
        var dup = (Dictionary<string, object?>)Parse("{\"a\":1,\"a\":2,\"b\":3}")!;
        Assert.Equal(["a", "b"], dup.Keys);
        Assert.Equal(2L, dup["a"]);
    }

    [Fact]
    public void NestingLimitMatchesPython()
    {
        // Debug frames are large (MaxDepth recursion exceeds the default 1MB stack); product runs Release, so only the test needs the big stack.
        Exception? failure = null;
        var t = new Thread(() => { try { NestingLimitCore(); } catch (Exception ex) { failure = ex; } }, 256 * 1024 * 1024);
        t.Start();
        t.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void NestingLimitCore()
    {
        var ok = string.Concat(Enumerable.Repeat("[", PyJsonDecoder.MaxDepth)) + string.Concat(Enumerable.Repeat("]", PyJsonDecoder.MaxDepth));
        Assert.IsType<List<object?>>(Parse(ok));
        var deep = string.Concat(Enumerable.Repeat("{\"a\":", 5000)) + "1" + string.Concat(Enumerable.Repeat("}", 5000));
        var e = Assert.Throws<JsonRecursionException>(() => Parse(deep));
        Assert.Equal("maximum recursion depth exceeded while decoding a JSON object from a unicode string", e.Message);
        var over = PyJsonDecoder.MaxDepth + 1;
        var arrays = string.Concat(Enumerable.Repeat("[", over)) + string.Concat(Enumerable.Repeat("]", over));
        e = Assert.Throws<JsonRecursionException>(() => Parse(arrays));
        Assert.Equal("maximum recursion depth exceeded while decoding a JSON array from a unicode string", e.Message);
    }

    [Fact]
    public void IntDigitLimitMatchesPython()
    {
        Assert.IsType<BigInteger>(Parse(new string('1', 4300)));
        Assert.IsType<double>(Parse(new string('1', 5000) + ".5"));
        var e = Assert.Throws<JsonIntDigitsException>(() => Parse("-" + new string('1', 4301)));
        Assert.Equal(
            "Exceeds the limit (4300 digits) for integer string conversion: value has 4301 digits; "
            + "use sys.set_int_max_str_digits() to increase the limit",
            e.Message);
    }
}
