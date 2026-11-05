using ExtractorTester.Config;
using ExtractorTester.Families;
using ExtractorTester.Models;

namespace ExtractorTester.Tests;

// Port of tests/test_config.py and tests/test_families.py. Expected messages were produced by the
// Python loader (pydantic strict + PyYAML safe_load) for the same inputs.
public class ConfigTests : IDisposable
{
    private const string Min = "name_field: name\nmime_primary_field: a\nmime_secondary_field: b\n";
    private readonly string _dir = Directory.CreateTempSubdirectory("cfgtest").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string Write(string rel, string text)
    {
        var p = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, text);
        return p;
    }

    private ConfigException Fail(string text)
    {
        var p = Write("c.yaml", text);
        var e = Assert.Throws<ConfigException>(() => ConfigLoader.Load(p));
        Assert.Equal("INVALID_CONFIG", ConfigException.Code);
        Assert.Equal($"Invalid config {p}: {e.Reason}", e.Message);
        return e;
    }

    [Fact]
    public void MinimalDefaultsEqualPythonDump()
    {
        var c = ConfigLoader.Load(Write("c.yaml", Min));
        const string Expected = "{\"name_field\":\"name\",\"mime_primary_field\":\"a\",\"mime_secondary_field\":\"b\"," +
            "\"text_field\":\"text\",\"generic_fields\":[],\"ignore_fields\":[\"fileId\",\"parentId\",\"rootId\",\"text\"]," +
            "\"type_families_file\":null,\"text_hash_chars\":1000000,\"full_compare_max_chars\":100000," +
            "\"max_block_chars\":4000,\"cut_lines_warn_pct\":20.0,\"containment_pct\":98.0,\"examples_per_result\":5," +
            "\"snippet_chars\":300,\"max_item_bytes\":52428800,\"item_timeout_seconds\":600}";
        Assert.Equal(Expected, PyJson.Serialize(c));
        Assert.Empty(c.FamilyMap);
    }

    [Fact]
    public void StructuralAndTextAlwaysIgnored()
    {
        var c = ConfigLoader.Load(Write("c.yaml", Min + "text_field: body\nignore_fields: [x]\n"));
        Assert.Equal(new[] { "x", "fileId", "parentId", "rootId", "body" }, c.IgnoreFields);
    }

    [Fact]
    [CoversError("INVALID_CONFIG")]
    public void MissingFile()
    {
        var p = Path.Combine(_dir, "nope.yaml");
        var e = Assert.Throws<ConfigException>(() => ConfigLoader.Load(p));
        Assert.Equal($"Invalid config {p}: cannot read file (No such file or directory)", e.Message);
    }

    [Fact]
    public void BadYamlNamesFile()
    {
        var e = Fail("a: [unclosed\n");
        Assert.StartsWith("YAML parse error: ", e.Reason);
        Assert.Contains("c.yaml", e.Message);
    }

    [Theory]
    [InlineData("", "top level must be a mapping")]
    [InlineData("[1]\n", "top level must be a mapping")]
    [InlineData("mime_primary_field: a\nmime_secondary_field: b\n", "name_field: Field required")]
    [InlineData("name_field: 1\nmime_primary_field: a\nmime_secondary_field: b\n", "name_field: Input should be a valid string")]
    [InlineData("name_field: a\nname_field: b\n", "mime_primary_field: Field required; mime_secondary_field: Field required")]
    [InlineData("MIN+bogus: 1\nzzz: 2\n", "bogus: Extra inputs are not permitted; zzz: Extra inputs are not permitted")]
    [InlineData("MIN+family_map: {a: b}\n", "family_map: Extra inputs are not permitted")]
    [InlineData("MIN+text_hash_chars: abc\n", "text_hash_chars: Input should be a valid integer")]
    [InlineData("MIN+text_hash_chars: 1.5\n", "text_hash_chars: Input should be a valid integer")]
    [InlineData("MIN+text_hash_chars: yes\n", "text_hash_chars: Input should be a valid integer")]
    [InlineData("MIN+text_hash_chars: null\n", "text_hash_chars: Input should be a valid integer")]
    [InlineData("MIN+text_hash_chars: 1e3\n", "text_hash_chars: Input should be a valid integer")]
    [InlineData("MIN+text_hash_chars: '5'\n", "text_hash_chars: Input should be a valid integer")]
    [InlineData("MIN+text_hash_chars: 0\n", "text_hash_chars: Input should be greater than or equal to 1")]
    [InlineData("MIN+max_block_chars: 99\n", "max_block_chars: Input should be greater than or equal to 100")]
    [InlineData("MIN+full_compare_max_chars: -1\n", "full_compare_max_chars: Input should be greater than or equal to 0")]
    [InlineData("MIN+snippet_chars: 5\n", "snippet_chars: Input should be greater than or equal to 20")]
    [InlineData("MIN+max_item_bytes: 0\n", "max_item_bytes: Input should be greater than or equal to 1")]
    [InlineData("MIN+item_timeout_seconds: 0\n", "item_timeout_seconds: Input should be greater than or equal to 1")]
    [InlineData("MIN+examples_per_result: -2\n", "examples_per_result: Input should be greater than or equal to 0")]
    [InlineData("MIN+cut_lines_warn_pct: 101\n", "cut_lines_warn_pct: Input should be less than or equal to 100")]
    [InlineData("MIN+cut_lines_warn_pct: abc\n", "cut_lines_warn_pct: Input should be a valid number")]
    [InlineData("MIN+cut_lines_warn_pct: true\n", "cut_lines_warn_pct: Input should be a valid number")]
    [InlineData("MIN+cut_lines_warn_pct: -1\n", "cut_lines_warn_pct: Input should be greater than or equal to 0")]
    [InlineData("MIN+containment_pct: 100.5\n", "containment_pct: Input should be less than or equal to 100")]
    [InlineData("MIN+generic_fields: abc\n", "generic_fields: Input should be a valid list")]
    [InlineData("MIN+generic_fields: null\n", "generic_fields: Input should be a valid list")]
    [InlineData("MIN+generic_fields: {a: b}\n", "generic_fields: Input should be a valid list")]
    [InlineData("MIN+generic_fields: [1, a]\n", "generic_fields.0: Input should be a valid string")]
    [InlineData("MIN+generic_fields: [a, 2, 3]\n", "generic_fields.1: Input should be a valid string; generic_fields.2: Input should be a valid string")]
    [InlineData("MIN+type_families_file: 3\n", "type_families_file: Input should be a valid string")]
    [InlineData("MIN+text_field: 3\n", "text_field: Input should be a valid string")]
    [InlineData("MIN+name_field: [a]\n", "name_field: Input should be a valid string")]
    [InlineData("MIN+name_field: null\n", "name_field: Input should be a valid string")]
    public void InvalidConfigMessagesMatchPython(string text, string reason)
    {
        if (text.StartsWith("MIN+")) text = Min + text[4..];
        Assert.Equal(reason, Fail(text).Reason);
    }

    [Fact]
    public void NonStringKey() => Assert.Equal("1: Keys should be strings", Fail("1: 2\n" + Min).Reason);

    [Fact]
    public void FloatFieldsAcceptIntsAndYaml11Numbers()
    {
        var c = ConfigLoader.Load(Write("c.yaml", Min + "cut_lines_warn_pct: 5\ncontainment_pct: 1_0.5\nsnippet_chars: 0x20\n"));
        Assert.Equal(5.0, c.CutLinesWarnPct);
        Assert.Equal(10.5, c.ContainmentPct);
        Assert.Equal(32, c.SnippetChars);
    }

    [Fact]
    public void FamilyRelativeToConfigNotCwd()
    {
        Write("cfg/fam.yaml", "families:\n  office: [Application/PDF, text/plain]\n");
        var p = Write("cfg/c.yaml", Min + "type_families_file: fam.yaml\n");
        var prev = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_dir);
        try
        {
            var c = ConfigLoader.Load(p);
            Assert.Equal(new Dictionary<string, string> { ["application/pdf"] = "office", ["text/plain"] = "office" }, c.FamilyMap);
        }
        finally
        {
            Directory.SetCurrentDirectory(prev);
        }
    }

    [Fact]
    public void MimeInTwoFamilies()
    {
        var fam = Write("fam.yaml", "families:\n  a: [x/y]\n  b: [X/Y]\n");
        var p = Write("c.yaml", Min + "type_families_file: fam.yaml\n");
        var e = Assert.Throws<ConfigException>(() => ConfigLoader.Load(p));
        Assert.Equal($"Invalid config {fam}: MIME type 'x/y' listed in two families: 'a' and 'b'", e.Message);
    }

    [Theory]
    [InlineData("zeta: 1\nfamilies: {}\nalpha: 2\n", "unknown key(s): alpha, zeta")]
    [InlineData("families: [a]\n", "'families' must be a mapping of family name to list of MIME types")]
    [InlineData("other: 1\n", "unknown key(s): other")]
    [InlineData("families:\n  a: x/y\n", "family 'a' must be a list of MIME strings")]
    [InlineData("families:\n  a: [1]\n", "family 'a' must be a list of MIME strings")]
    public void FamilyFileErrors(string famText, string reason)
    {
        var fam = Write("fam.yaml", famText);
        var p = Write("c.yaml", Min + "type_families_file: fam.yaml\n");
        var e = Assert.Throws<ConfigException>(() => ConfigLoader.Load(p));
        Assert.Equal($"Invalid config {fam}: {reason}", e.Message);
    }

    [Fact]
    public void FamilyMapNotAnInputKey() =>
        Assert.Equal("family_map: Extra inputs are not permitted", Fail(Min + "family_map: {a/b: x}\n").Reason);

    // ---- families.py ----

    private static CompareConfig Cfg()
    {
        var c = new CompareConfig { NameField = "n", MimePrimaryField = "a", MimeSecondaryField = "b" };
        c.SetFamilyMap(new() { ["application/pdf"] = "docs" });
        return c;
    }

    [Fact]
    public void MimeJoin() => Assert.Equal("application/pdf", FamilyResolver.Mime("Application", "PDF"));

    [Fact]
    public void MimeMissing()
    {
        Assert.Null(FamilyResolver.Mime(null, "pdf"));
        Assert.Null(FamilyResolver.Mime("application", " "));
    }

    [Fact]
    public void FamilyUnknown() => Assert.Equal("unknown", FamilyResolver.Family(null, Cfg()));

    [Fact]
    public void FamilyUnlistedIsOwn() => Assert.Equal("image/png", FamilyResolver.Family("Image/PNG", Cfg()));

    [Fact]
    public void FamilyListed() => Assert.Equal("docs", FamilyResolver.Family("Application/PDF", Cfg()));
}
