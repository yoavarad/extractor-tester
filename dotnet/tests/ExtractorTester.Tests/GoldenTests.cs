using System.Text;
using System.Text.Json.Nodes;

namespace ExtractorTester.Tests;

/// <summary>Port of tests/test_golden.py: exact reproduction, example presence and metric keys.</summary>
[Collection("Pool")]
public sealed class GoldenTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("et-golden-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmp, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string Md() => File.ReadAllText(GoldenChecks.MetricsMd, Encoding.UTF8);

    private static JsonNode Report() => JsonNode.Parse(GoldenCorpus.ExpectedText("report.json"))!;

    private static void AssertMatchesExpected(Dictionary<string, string> actual)
    {
        foreach (var name in GoldenCorpus.OutputFiles)
        {
            var (exp, act) = (GoldenCorpus.ExpectedText(name), actual[name]);
            if (exp != act)
            {
                var at = exp.Zip(act).TakeWhile(p => p.First == p.Second).Count();
                Assert.Fail($"{name} differs at char {at}: expected '{Around(exp, at)}', actual '{Around(act, at)}'");
            }
        }
        // The checks also pass on the C# report itself (normalizing drops no MetricResult).
        var report = JsonNode.Parse(actual["report.json"])!;
        Assert.Empty(GoldenChecks.MissingExamples(report));
        Assert.Empty(GoldenChecks.UndefinedKeys(report, Md()));
    }

    private static string Around(string s, int at) => s.Substring(Math.Max(0, at - 80), Math.Min(s.Length, at + 80) - Math.Max(0, at - 80));

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void GoldenOutputsMatch(int workers)
    {
        AssertMatchesExpected(GoldenCorpus.Run(_tmp, workers));
    }

    [Fact]
    public void GoldenOutputsMatchFromLongTempPath()
    {
        // Error notes are cut at snippet_chars, so they must not contain the input folder.
        // The path stays a little under the classic 260-char Windows limit.
        var @base = Path.Combine(_tmp, new string('d', Math.Max(1, 205 - _tmp.Length - 1)));
        Assert.True(@base.Length >= 200, "temp root too long to build a 200-char test path");
        Directory.CreateDirectory(@base);
        AssertMatchesExpected(GoldenCorpus.Run(@base, 1));
    }

    [Fact]
    public void GoldenReportHasExamplesOnEveryNonzeroResult()
    {
        var report = Report();
        Assert.Contains(GoldenChecks.MetricResults(report), x => x.Result["count"]!.GetValue<long>() != 0);
        Assert.Empty(GoldenChecks.MissingExamples(report));
    }

    [Fact]
    public void ExamplePresenceCheckFailsOnEmptiedList()
    {
        var report = Report();
        var (where, result) = GoldenChecks.MetricResults(report).First(x => x.Result["count"]!.GetValue<long>() != 0);
        result["examples"] = new JsonArray();
        Assert.Equal([where], GoldenChecks.MissingExamples(report));
    }

    [Fact]
    public void GoldenReportKeysDefinedInMetricsMd()
    {
        var report = Report();
        Assert.True(GoldenChecks.MetricResults(report).Count() >= 30);
        var bad = GoldenChecks.UndefinedKeys(report, Md());
        Assert.True(bad.Count == 0, GoldenChecks.UndefinedKeysMessage(bad, Md(), GoldenChecks.FieldNames(report)));
    }

    [Fact]
    public void MetricsKeyCheckFailsOnUndefinedKey()
    {
        var report = Report();
        report["families"]![0]!["hints"]!.AsArray().Add(JsonNode.Parse(
            "{\"key\": \"made-up-key\", \"count\": 1, \"denominator\": 1, \"rate\": 1.0, \"examples\": []}"));
        Assert.Equal(["made-up-key"], GoldenChecks.UndefinedKeys(report, Md()));
    }

    private static JsonObject Result(string key) =>
        new() { ["key"] = key, ["count"] = 0, ["denominator"] = 1, ["rate"] = 0.0, ["examples"] = new JsonArray() };

    /// <summary>A tiny report: keys are plain results; fields are per-field results (the field names).</summary>
    private static JsonObject KeysReport(string[] keys, params string[] fields) => new()
    {
        ["results"] = new JsonArray(keys.Select(k => (JsonNode)Result(k)).ToArray()),
        ["fields"] = new JsonArray(fields.Select(f => (JsonNode)Result(f)).ToArray()),
    };

    [Fact]
    public void FieldPlaceholderMatchesFieldNamesInReport()
    {
        // <field> is a field name the report lists, so it may contain ':' (e.g. 'dc:title').
        string[] fields = ["author:changed", "dc:title:changed"];
        string[] good = ["author:changed", "dc:title:changed", "dc:title:only-in-new", "author:only-in-old"];
        string[] bad = [":changed", "a:bogus", "nosuch:changed", "dc:changed", "author:changed:changed"];
        Assert.Empty(GoldenChecks.UndefinedKeys(KeysReport(good, fields), Md()));
        Assert.Equal(bad.Order(StringComparer.Ordinal), GoldenChecks.UndefinedKeys(KeysReport(bad, fields), Md()));
    }

    [Fact]
    public void FieldWithColonAndExtraSuffixIsAFieldNameOnlyIfListed()
    {
        Assert.Empty(GoldenChecks.UndefinedKeys(KeysReport(["a:changed:changed"], "a:changed:changed"), Md()));
        Assert.Equal(["a:changed:changed"], GoldenChecks.UndefinedKeys(KeysReport(["a:changed:changed"], "a:changed"), Md()));
    }

    [Fact]
    public void EmptyToolErrorFolderIsAClearError()
    {
        // No manifests would make '<error code>' match an empty key, so fail naming the folder.
        var e = Assert.Throws<ArgumentException>(() => GoldenChecks.UndefinedKeys(KeysReport(["x"]), Md(), _tmp));
        Assert.Matches("no tool-error manifests found in .*" + System.Text.RegularExpressions.Regex.Escape(Path.GetFileName(_tmp)), e.Message);
    }

    [Fact]
    public void UndefinedKeyMessageNamesKeyAndExpectedForms()
    {
        var text = GoldenChecks.UndefinedKeysMessage(["nosuch:changed"], Md(), ["author"]);
        Assert.Contains("'nosuch:changed'", text);
        Assert.Contains("<field>:changed", text);
        Assert.Contains("field part 'nosuch' is not a field name in this report", text);
        Assert.DoesNotContain("field part", GoldenChecks.UndefinedKeysMessage(["made-up-key"], Md(), ["author"]));
    }

    [Fact]
    public void ErrorCodePlaceholderMatchesOnlyKnownCodes()
    {
        var code = GoldenChecks.ToolErrorCodes()[0];
        Assert.Equal(["not-a-code"], GoldenChecks.UndefinedKeys(KeysReport([code, "not-a-code"]), Md()));
    }

    [Theory]
    [InlineData(1.0, "1.0")]
    [InlineData(1e-05, "1e-05")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(0.1, "0.1")]
    [InlineData(1.0 / 3, "0.3333333333333333")]
    [InlineData(1e16, "1e+16")]
    [InlineData(1e15, "1000000000000000.0")]
    [InlineData(1.5e-06, "1.5e-06")]
    [InlineData(123.456, "123.456")]
    [InlineData(-2.5, "-2.5")]
    [InlineData(0.0, "0.0")]
    [InlineData(1.7976931348623157e308, "1.7976931348623157e+308")]
    public void FloatReprMatchesPython(double value, string expected)
    {
        Assert.Equal(expected, GoldenCorpus.FloatRepr(value));
    }

    [Fact]
    public void NormalizerMatchesPythonForIntegerNegativeZeroAndLineBreaks()
    {
        // Expected text from Python: json.dumps(json.loads(l)) over str.splitlines() of this input.
        File.WriteAllText(Path.Combine(_tmp, "items.jsonl"),
            "{\"a\": -0}\u001c{\"b\": -0.0}\u0085{\"c\": [-0, 0, -5]}\u2028{\"d\": 1}\r\n{\"e\": -0}\r{\"f\": 2}\n");
        foreach (var other in new[] { "pairs.jsonl", "report.json" })
        {
            File.WriteAllText(Path.Combine(_tmp, other), "{}");
        }
        var actual = GoldenCorpus.NormalizedOutputs(_tmp, "<base>")["items.jsonl"];
        Assert.Equal("{\"a\": 0}\n{\"b\": -0.0}\n{\"c\": [0, 0, -5]}\n{\"d\": 1}\n{\"e\": 0}\n{\"f\": 2}\n", actual);
    }
}
