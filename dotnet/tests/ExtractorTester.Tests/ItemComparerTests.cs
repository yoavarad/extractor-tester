using System.Text;
using ExtractorTester.Compare;
using ExtractorTester.Config;
using ExtractorTester.Models;

namespace ExtractorTester.Tests;

// Port of tests/test_item.py over the item files in Fixtures/Compare/compare_cases.json, plus a
// byte-parity theory: ItemResult and every PairFacts equal Python's JSON (timings zeroed).
public sealed class ItemComparerTests
{
    private static readonly CompareConfig Cfg = PostInit(new() { NameField = "name", MimePrimaryField = "mp", MimeSecondaryField = "ms" });

    private static CompareConfig PostInit(CompareConfig c)
    {
        c.ApplyPostInit();
        return c;
    }

    private static (string Old, string New) Write(string name)
    {
        var c = CompareFixtures.Case("items", name);
        var dir = Directory.CreateTempSubdirectory("compare-item-").FullName;
        string Put(string side, string file, string body)
        {
            Directory.CreateDirectory(Path.Combine(dir, side));
            var p = Path.Combine(dir, side, file);
            File.WriteAllText(p, body, new UTF8Encoding(false));
            return p;
        }
        var o = Put(c.GetProperty("old_side").GetString()!, c.GetProperty("old_name").GetString()!, c.GetProperty("old_file").GetString()!);
        var n = Put(c.GetProperty("new_side").GetString()!, c.GetProperty("new_name").GetString()!, c.GetProperty("new_file").GetString()!);
        return (o, n);
    }

    private static (ItemResult Result, List<PairFacts> Facts) Run(string name)
    {
        var (o, n) = Write(name);
        return ItemComparer.CompareItem(o, n, Cfg);
    }

    private static PairFacts PairEndingWith(List<PairFacts> facts, string suffix) =>
        facts.First(p => p.PathOld is { } po && po.EndsWith(suffix, StringComparison.Ordinal));

    [Theory]
    [MemberData(nameof(ItemNames))]
    public void ItemResultAndPairFactsJsonEqualPython(string name)
    {
        var c = CompareFixtures.Case("items", name);
        var (result, facts) = Run(name);
        var zeroed = result with
        {
            Seconds = 0.0,
            StageSeconds = result.StageSeconds.ToDictionary(kv => kv.Key, _ => 0.0),
        };
        Assert.Equal(c.GetProperty("result").GetString(), PyJson.Serialize(zeroed));
        var expected = c.GetProperty("facts").EnumerateArray().Select(f => f.GetString()!).ToList();
        Assert.Equal(expected.Count, facts.Count);
        for (var i = 0; i < facts.Count; i++)
        {
            var f = facts[i];
            Assert.Equal(expected[i], PyJson.Serialize(f.Text is null ? f : f with { Text = f.Text with { Seconds = 0.0 } }));
        }
    }

    public static IEnumerable<object[]> ItemNames() => CompareFixtures.Names("items");

    [Fact]
    public void OneFactPerPairedAndOneSidedFile()
    {
        var (result, facts) = Run("one_fact_per_paired_and_one_sided_file");
        Assert.Equal("compared", result.Status);
        Assert.Equal(
            [("paired", "application/pdf"), ("paired", "application/pdf"), ("only-in-old", "application/pdf"), ("only-in-new", "application/docx")],
            facts.Select(p => (p.Kind, p.Family)));
        Assert.Equal((1L, 1L), (result.FilesIdentical, result.FilesChanged));
        Assert.Equal((1L, 1L), (result.FilesOnlyOld, result.FilesOnlyNew));
        var root = facts[0];
        Assert.Equal((2L, 2L), (root.ChildrenOld, root.ChildrenNew));
        Assert.Equal((1L, 1L), (root.ChildrenOnlyOld, root.ChildrenOnlyNew));
        Assert.False(root.Identical);
        Assert.Equal("name", facts[1].Tier);
        Assert.Equal("none", facts[1].TieBreak);
        Assert.True(facts[1].MimeEqual);
        Assert.True(facts[1].FamilyEqual);
        Assert.NotNull(facts[1].Text);
    }

    [Fact]
    public void MetadataChangeIsOneChangedDiffOnRightPair()
    {
        var (result, facts) = Run("metadata_change_on_right_pair");
        var byPath = facts.ToDictionary(p => p.PathOld!);
        var changed = byPath["root/b.pdf"].FieldDiffs.Where(d => d.Status == "changed");
        Assert.Equal([("author", (object?)"y", (object?)"z")], changed.Select(d => (d.Path, d.OldValue, d.NewValue)));
        Assert.Empty(byPath["root/a.pdf"].FieldDiffs);
        Assert.Equal(["author"], byPath["root/a.pdf"].FieldsSame);
        Assert.True(byPath["root/a.pdf"].Identical);
        Assert.False(byPath["root/b.pdf"].Identical);
        Assert.Equal((2L, 1L), (result.FilesIdentical, result.FilesChanged));
    }

    [Fact]
    public void ItemMetadataDiffsExcludeIdAndFiles()
    {
        var (result, _) = Run("item_metadata_diffs");
        Assert.Equal(
            new Dictionary<string, string> { ["k"] = "same", ["lang"] = "changed" },
            result.MetadataDiffs.ToDictionary(d => d.Path, d => d.Status));
        Assert.All(result.MetadataDiffs, d => Assert.Equal("item", d.Group));
    }

    [Fact]
    public void MimeChangeMarksPair()
    {
        var (_, facts) = Run("mime_change");
        Assert.False(facts[1].MimeEqual);
        Assert.False(facts[1].FamilyEqual);
        Assert.False(facts[1].Identical);
    }

    [Fact]
    public void ExceptionBecomesItemProcessingFailed()
    {
        var (o, n) = Write("item_metadata_diffs");
        var (result, facts) = ItemComparer.CompareItem(o, n, Cfg, (_, _) => throw new InvalidOperationException("kaput"));
        Assert.Empty(facts);
        Assert.Equal("error", result.Status);
        var err = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.ITEM_PROCESSING_FAILED, err.Code);
        Assert.Equal("item", err.Scope);
        Assert.Equal("I", err.ItemId);
        Assert.Equal("InvalidOperationException: kaput", err.Message);
    }

    [Theory]
    [InlineData("metadata_flatten_collision_value_error", "ValueError: flatten path collision: 'a.b'")]
    [InlineData("file_flatten_collision_value_error", "ValueError: flatten path collision: 'x.y'")]
    [InlineData("int_digit_limit_value_error", "ValueError: Exceeds the limit (4300 digits)")]
    [InlineData("nesting_limit_recursion_error", "RecursionError: maximum recursion depth exceeded")]
    public void PortExceptionsSurfaceAsPythonErrors(string name, string messageStart)
    {
        var (result, facts) = Run(name);
        Assert.Empty(facts);
        var err = Assert.Single(result.Errors);
        Assert.Equal(ErrorCode.ITEM_PROCESSING_FAILED, err.Code);
        Assert.StartsWith(messageStart, err.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("non_string_text_int", "expected base64 string, got int")]
    [InlineData("non_string_text_float", "expected base64 string, got float")]
    [InlineData("non_string_text_bool", "expected base64 string, got bool")]
    [InlineData("non_string_text_list", "expected base64 string, got list")]
    public void NonStringTextIsInvalidBase64(string name, string message)
    {
        var (_, facts) = Run(name);
        var err = Assert.Single(facts[0].Errors);
        Assert.Equal((ErrorCode.INVALID_BASE64, message), (err.Code, err.Message));
        Assert.Null(facts[0].Text);
    }

    [Fact]
    public void LoaderErrorKeepsItsCode()
    {
        var (result, facts) = Run("loader_error_keeps_code");
        Assert.Empty(facts);
        Assert.Equal("error", result.Status);
        Assert.Equal(ErrorCode.INVALID_JSON, result.Errors[0].Code);
    }

    [Fact]
    public void FactsNeverContainFullText()
    {
        var (_, facts) = Run("facts_never_contain_full_text");
        Assert.DoesNotContain("secret-body", PyJson.Serialize(facts[0]));
    }

    [Fact]
    public void FileLevelErrorAttachedToItsPair()
    {
        var (_, facts) = Run("file_level_error_attached");
        Assert.Contains(facts[1].Errors, e => e.Code == ErrorCode.MISSING_FIELD);
    }

    [Fact]
    public void UnattachedBuildWarningsReachItemResult()
    {
        var (result, facts) = Run("unattached_build_warnings");
        Assert.Equal([ErrorCode.ORPHAN_FILE], result.Errors.Select(e => e.Code));
        Assert.Single(facts);
    }

    [Fact]
    public void PairedTextFactsAttached()
    {
        var (_, facts) = Run("paired_text_facts");
        Assert.All(facts.Where(p => p.Kind == "paired"), p => Assert.NotNull(p.Text));
        Assert.Null(facts.First(p => p.Kind == "only-in-new").Text);
    }

    [Fact]
    public void InvalidBase64SkipsTextButKeepsFieldDiffs()
    {
        var (_, facts) = Run("invalid_base64");
        var pair = facts.First(p => p.PathOld == p.PathNew && (p.PathOld ?? "").EndsWith("a.pdf", StringComparison.Ordinal));
        Assert.Null(pair.Text);
        Assert.Contains(pair.Errors, e => e.Code == ErrorCode.INVALID_BASE64);
        Assert.Equal(["title"], pair.FieldDiffs.Select(d => d.Path));
        Assert.False(pair.Identical);
    }

    [Fact]
    public void WhitespaceOnlyChangeIsIdentical()
    {
        var (result, facts) = Run("whitespace_only_identical");
        Assert.All(facts, p => Assert.True(p.Identical));
        Assert.Equal(2, result.FilesIdentical);
    }

    [Fact]
    public void TextChangeIsNotIdenticalAndHinted()
    {
        var pair = PairEndingWith(Run("text_change_more").Facts, "a.pdf");
        Assert.Contains("new-more-text", pair.Hints);
        Assert.False(pair.Identical);
        Assert.Contains("new-less-text", PairEndingWith(Run("text_change_less").Facts, "a.pdf").Hints);
    }

    [Fact]
    public void FieldFilledAndEmptiedHints()
    {
        var pair = PairEndingWith(Run("field_filled_and_emptied").Facts, "a.pdf");
        Assert.Contains("new-filled-field", pair.Hints);
        Assert.Contains("new-emptied-field", pair.Hints);
    }

    [Fact]
    public void SubfileCountHints()
    {
        var pair = PairEndingWith(Run("subfiles_more").Facts, "a.pdf");
        Assert.Contains("new-more-subfiles", pair.Hints);
        Assert.False(pair.Identical);
        Assert.Contains("new-fewer-subfiles", PairEndingWith(Run("subfiles_fewer").Facts, "a.pdf").Hints);
    }
}
