using ExtractorTester.Compare;
using ExtractorTester.Config;
using ExtractorTester.Decode;
using ExtractorTester.Loader;
using ExtractorTester.Models;

namespace ExtractorTester.Tests;

// Port of tests/test_compare_fields.py, plus a byte-parity theory over the field cases in
// Fixtures/Compare/compare_cases.json (inputs kept as JSON text so int/float types survive).
public sealed class FieldDifferTests
{
    private static CompareConfig Cfg(long? snippetChars = null)
    {
        var c = new CompareConfig
        {
            NameField = "n",
            MimePrimaryField = "mp",
            MimeSecondaryField = "ms",
            GenericFields = ["meta"],
            IgnoreFields = ["skip"],
        };
        if (snippetChars is { } s) c.SnippetChars = s;
        c.ApplyPostInit();
        return c;
    }

    private static Dictionary<string, object?> Parse(string json) =>
        (Dictionary<string, object?>)PyJsonDecoder.Loads(Text.ToCodePoints(json))!;

    private static Dictionary<string, FieldDiff> ByPath(string old, string @new, CompareConfig? cfg = null) =>
        FieldDiffer.Diff(Parse(old), Parse(@new), cfg ?? Cfg()).ToDictionary(d => d.Path);

    [Theory]
    [MemberData(nameof(FieldNames))]
    public void FieldDiffJsonEqualsPython(string name)
    {
        var c = CompareFixtures.Case("fields", name);
        var diffs = FieldDiffer.Diff(
            Parse(c.GetProperty("old").GetString()!), Parse(c.GetProperty("new").GetString()!),
            CompareFixtures.FieldConfig(c.GetProperty("config")));
        Assert.Equal(
            c.GetProperty("diffs").EnumerateArray().Select(d => d.GetString()!).ToList(),
            diffs.Select(d => PyJson.Serialize(d)).ToList());
    }

    public static IEnumerable<object[]> FieldNames() => CompareFixtures.Names("fields");

    [Fact]
    public void NumberEquality()
    {
        var r = ByPath("""{"x": 1, "y": true, "z": 0}""", """{"x": 1.0, "y": 1, "z": false}""");
        Assert.Equal("same", r["x"].Status);
        Assert.Equal("changed", r["y"].Status);
        Assert.Equal("changed", r["z"].Status);
    }

    [Fact]
    public void NullIsValue()
    {
        var r = ByPath("""{"a": null, "b": null}""", """{"c": null, "b": null}""");
        Assert.Equal("only-in-old", r["a"].Status);
        Assert.Equal("only-in-new", r["c"].Status);
        Assert.Equal("same", r["b"].Status);
    }

    [Fact]
    public void TypeChangeRecordsTypes()
    {
        var d = ByPath("""{"a": 1}""", """{"a": "1"}""")["a"];
        Assert.Equal(("changed", "number", "string"), (d.Status, d.OldType, d.NewType));
    }

    [Fact]
    public void ListsWholeAndNestedNumbers()
    {
        var r = ByPath("""{"l": [1, 2], "m": [true], "o": {}}""", """{"l": [1.0, 2], "m": [1], "o": {}}""");
        Assert.Equal("same", r["l"].Status);
        Assert.Equal("changed", r["m"].Status);
        Assert.Equal("same", r["o"].Status);
    }

    [Fact]
    public void IgnoredAndStructuralSkippedSortedGroups()
    {
        var output = FieldDiffer.Diff(
            Parse("""{"skip": 1, "n": "x", "mp": "m", "ms": "t", "text": "t", "z": 1, "meta.k": 1}"""),
            Parse("""{"skip": 2, "n": "y", "mp": "m", "ms": "t", "text": "u", "z": 1, "meta.k": 2}"""),
            Cfg());
        Assert.Equal(["meta.k", "z"], output.Select(d => d.Path));
        Assert.Equal(["generic", "specific"], output.Select(d => d.Group));
    }

    [Fact]
    public void TruncatesStrings()
    {
        var d = ByPath($$"""{"a": "{{new string('x', 500)}}"}""", """{"a": "y"}""", Cfg(20))["a"];
        Assert.Equal(new string('x', 20), d.OldValue);
        Assert.Equal("y", d.NewValue);
    }

    [Fact]
    public void IsEmpty()
    {
        Assert.All(new object?[] { null, "", new List<object?>(), new Dictionary<string, object?>() }, v => Assert.True(FieldDiffer.IsEmpty(v)));
        Assert.All(
            new object?[] { 0L, false, "a", new List<object?> { 0L }, new Dictionary<string, object?> { ["a"] = 1L } },
            v => Assert.False(FieldDiffer.IsEmpty(v)));
    }

    [Fact]
    public void NanSame()
    {
        Assert.Equal("same", ByPath("""{"a": NaN}""", """{"a": NaN}""")["a"].Status);
    }

    [Fact]
    public void BigIntComparesExactlyWithFloat()
    {
        var r = ByPath("""{"a": 1180591620717411303424, "b": 1180591620717411303424}""", """{"a": 1.1805916207174113e+21, "b": 1180591620717411303425}""");
        Assert.Equal("same", r["a"].Status);
        Assert.Equal("changed", r["b"].Status);
    }
}
