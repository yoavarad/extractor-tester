using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ExtractorTester.Render;

namespace ExtractorTester.Tests;

// Port of tests/test_render.py. Fixtures are byte copies of tests/fixtures/report.json and
// tests/fixtures/report.golden.html (Jinja2 output).
public sealed class RenderTests : IDisposable
{
    private static readonly string Fix = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Render");
    private readonly string _tmp = Directory.CreateTempSubdirectory("et-render-").FullName;

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

    private byte[] RenderBytes(string src)
    {
        var outPath = Path.Combine(_tmp, "report.html");
        HtmlRenderer.Render(src, outPath);
        return File.ReadAllBytes(outPath);
    }

    private string Html() => Encoding.UTF8.GetString(RenderBytes(Path.Combine(Fix, "report.json")));

    [Fact]
    public void Golden()
    {
        var golden = File.ReadAllBytes(Path.Combine(Fix, "report.golden.html"));
        Assert.Equal(Encoding.UTF8.GetString(golden), Html());
        Assert.Equal(golden, RenderBytes(Path.Combine(Fix, "report.json")));
    }

    [Fact]
    public void GoldenEdgeCases()
    {
        // report-edge.*: empty sections, None/missing values, odd floats, offset datetime, repr
        // of dicts; golden bytes produced by the Python render() (Jinja2) from report-edge.json.
        var golden = File.ReadAllBytes(Path.Combine(Fix, "report-edge.golden.html"));
        var actual = RenderBytes(Path.Combine(Fix, "report-edge.json"));
        Assert.Equal(Encoding.UTF8.GetString(golden), Encoding.UTF8.GetString(actual));
        Assert.Equal(golden, actual);
    }

    [Fact]
    public void NoExternalUrls() =>
        Assert.DoesNotMatch(new Regex("""(src|href)\s*=\s*["']?https?://"""), Html());

    [Fact]
    public void ScriptEscaped()
    {
        var html = Html();
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)", html);
    }

    [Fact]
    public void ScriptInNamesAndTextEscaped()
    {
        var rep = JsonNode.Parse(File.ReadAllText(Path.Combine(Fix, "report.json")))!;
        const string evil = "<script>x('\"&')</script>";
        rep["inputs"]!["old"] = evil;
        rep["families"]![0]!["family"] = evil;
        rep["families"]![0]!["fields"]![0]!["key"] = evil;
        rep["warnings"]![0] = evil;
        var src = Path.Combine(_tmp, "r.json");
        File.WriteAllText(src, rep.ToJsonString());
        var html = Encoding.UTF8.GetString(RenderBytes(src));
        Assert.DoesNotContain("<script>x", html);
        Assert.Contains("&lt;script&gt;x(&#39;&#34;&amp;&#39;)&lt;/script&gt;", html);
    }

    [Fact]
    public void ImportGraph()
    {
        // Render must not depend on compare, matching or aggregate code.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "ExtractorTester", "Render.cs")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src", "ExtractorTester", "Render.cs"));
        Assert.DoesNotMatch(new Regex(@"ExtractorTester\.(Compare|Matching|Aggregate)\b"), source);
    }

    [Fact]
    public void SimilarityLabel()
    {
        var rep = JsonNode.Parse(File.ReadAllText(Path.Combine(Fix, "report.json")))!;

        static void Walk(JsonNode? o)
        {
            if (o is JsonObject obj)
            {
                if (obj.ContainsKey("item_id") && obj.ContainsKey("path_old"))
                {
                    obj["similarity"] = 0.42;
                    obj["old_value"] = null;
                    obj["new_value"] = null;
                }
                foreach (var kv in obj.ToList())
                {
                    Walk(kv.Value);
                }
            }
            else if (o is JsonArray arr)
            {
                foreach (var v in arr.ToList())
                {
                    Walk(v);
                }
            }
        }

        Walk(rep);
        var src = Path.Combine(_tmp, "r.json");
        File.WriteAllText(src, rep.ToJsonString());
        var html = Encoding.UTF8.GetString(RenderBytes(src));
        Assert.Contains("similarity <code>0.42</code>", html);
        Assert.DoesNotContain("old <code>", html);
    }

    [Theory]
    [InlineData(0.5, 0, "0")]
    [InlineData(2.5, 0, "2")]
    [InlineData(1.5, 0, "2")]
    [InlineData(0.05, 1, "0.1")]
    [InlineData(0.25, 1, "0.2")]
    [InlineData(-0.01, 1, "-0.0")]
    [InlineData(12.5, 1, "12.5")]
    [InlineData(1e20, 1, "100000000000000000000.0")]
    public void FixedPointMatchesPython(double value, int places, string expected) =>
        Assert.Equal(expected, PyText.FixedPoint(value, places));

    [Theory]
    [InlineData(1.0, "1.0")]
    [InlineData(0.97, "0.97")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(0.00001, "1e-05")]
    [InlineData(1e16, "1e+16")]
    [InlineData(1234567890123456.0, "1234567890123456.0")]
    [InlineData(1.5e300, "1.5e+300")]
    [InlineData(-2.5, "-2.5")]
    public void FloatReprMatchesPython(double value, string expected) =>
        Assert.Equal(expected, PyText.FloatRepr(value));

    [Fact]
    public void ReprOfDictMatchesPython()
    {
        var d = new Dictionary<string, object?> { ["item_id"] = "it's", ["seconds"] = 3.0, ["x"] = null, ["ok"] = true, ["n"] = 2L };
        Assert.Equal("{'item_id': \"it's\", 'seconds': 3.0, 'x': None, 'ok': True, 'n': 2}", PyText.Repr(d));
    }
}
