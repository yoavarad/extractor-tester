using ExtractorTester.Compare;
using ExtractorTester.Config;
using ExtractorTester.Models;

namespace ExtractorTester.Tests;

// Port of tests/test_text.py and tests/test_buffered.py, plus a byte-parity theory over the
// text cases in Fixtures/Compare/compare_cases.json. Buffered inputs built with Python's
// random module are read from the fixture.
public sealed class CompareTextTests
{
    private const string Old = "The quick brown fox jumps over the lazy dog near the river bank today.";

    private static CompareConfig Cfg(long? snippetChars = null, double? containmentPct = null, long? fullMax = null, long? maxBlock = null)
    {
        var c = new CompareConfig { NameField = "name", MimePrimaryField = "a", MimeSecondaryField = "b" };
        if (snippetChars is { } s) c.SnippetChars = s;
        if (containmentPct is { } p) c.ContainmentPct = p;
        if (fullMax is { } f) c.FullCompareMaxChars = f;
        if (maxBlock is { } m) c.MaxBlockChars = m;
        c.ApplyPostInit();
        return c;
    }

    private static TextFacts Run(string old, string @new, CompareConfig? cfg = null) => TextComparer.CompareText(old, @new, cfg ?? Cfg());

    private static (string Old, string New) Inputs(string name)
    {
        var c = CompareFixtures.Case("text", name);
        return (c.GetProperty("old").GetString()!, c.GetProperty("new").GetString()!);
    }

    private static Dictionary<string, object?> Snip(string old, string @new) => new() { ["old"] = old, ["new"] = @new };

    [Theory]
    [MemberData(nameof(TextNames))]
    public void TextFactsJsonEqualsPython(string name)
    {
        var c = CompareFixtures.Case("text", name);
        var facts = TextComparer.CompareText(
            c.GetProperty("old").GetString()!, c.GetProperty("new").GetString()!, CompareFixtures.TextConfig(c.GetProperty("config")));
        Assert.Equal(c.GetProperty("facts").GetString(), PyJson.Serialize(facts with { Seconds = 0.0 }));
    }

    public static IEnumerable<object[]> TextNames() => CompareFixtures.Names("text");

    [Theory]
    [InlineData("identical_fast_path", "equal")]
    [InlineData("appended_old_within_new", "old-within-new")]
    [InlineData("truncated_new_within_old", "new-within-old")]
    [InlineData("partially_shared_overlapping", "overlapping")]
    [InlineData("no_shared_disjoint", "disjoint")]
    public void EachRelationHasItsDedicatedInput(string name, string relation)
    {
        var (o, n) = Inputs(name);
        Assert.Equal(relation, Run(o, n).Relation);
    }

    // ---- test_text.py ----

    [Fact]
    public void IdenticalFastPath()
    {
        var f = Run(Old, Old);
        Assert.Equal("fast-path", f.Method);
        Assert.True(f.RawEqual);
        Assert.True(f.NormalizedEqual);
        Assert.Equal("equal", f.Relation);
        Assert.Equal(1.0, f.EditSimilarity);
        Assert.Equal(1.0, f.Jaccard);
        Assert.Empty(f.Snippets);
    }

    [Fact]
    public void WhitespaceOnlyChangeIsEqual()
    {
        var f = Run(Old, "  The quick\n brown\tfox jumps over the lazy dog near the river bank today.");
        Assert.Equal("fast-path", f.Method);
        Assert.False(f.RawEqual);
        Assert.True(f.NormalizedEqual);
        Assert.Equal("equal", f.Relation);
        Assert.Equal(1.0, f.EditSimilarity);
        Assert.Equal(1.0, f.Jaccard);
    }

    [Fact]
    public void BothEmptyIsEqual()
    {
        var f = Run("", "  ");
        Assert.Equal("equal", f.Relation);
        Assert.True(f.OldEmpty);
        Assert.True(f.NewEmpty);
    }

    [Fact]
    public void AppendedTextIsOldWithinNew()
    {
        var f = Run(Old, Old + " And then it slept.");
        Assert.Equal("full", f.Method);
        Assert.Equal("old-within-new", f.Relation);
        Assert.Equal(1.0, f.OldFoundInNew);
        Assert.True(f.NewFoundInOld < 1);
        Assert.InRange(f.EditSimilarity!.Value, double.Epsilon, 1 - 1e-12);
        Assert.Equal(" And then it slept.".Length, f.LengthDelta);
    }

    [Fact]
    public void TruncatedTextIsNewWithinOld()
    {
        var f = Run(Old, Old[..40]);
        Assert.Equal("new-within-old", f.Relation);
        Assert.Equal(1.0, f.NewFoundInOld);
        Assert.True(f.LengthDelta < 0);
    }

    [Fact]
    public void ContainmentThresholdRespected()
    {
        var @new = Old.Replace("lazy", "sleepy") + " More words here.";
        Assert.Equal("overlapping", Run(Old, @new).Relation);
        Assert.Equal("old-within-new", Run(Old, @new, Cfg(containmentPct: 80)).Relation);
    }

    [Fact]
    public void PartiallySharedIsOverlapping()
    {
        var f = Run(Old, "A quick red fox ran under the bridge yesterday evening.");
        Assert.Equal("overlapping", f.Relation);
        Assert.InRange(f.Jaccard!.Value, double.Epsilon, 1 - 1e-12);
    }

    [Fact]
    public void NoSharedContentIsDisjoint()
    {
        var f = Run("alpha beta gamma", "xyz qrs tuv");
        Assert.Equal("disjoint", f.Relation);
        Assert.Equal(0.0, f.Jaccard);
    }

    [Fact]
    public void EmptyOldIsOldWithinNew()
    {
        var f = Run("", Old);
        Assert.Equal("old-within-new", f.Relation);
        Assert.True(f.OldEmpty);
        Assert.False(f.NewEmpty);
        Assert.Equal(0.0, f.EditSimilarity);
        Assert.Equal(0.0, f.Jaccard);
    }

    [Fact]
    public void EmptyNewIsNewWithinOld()
    {
        var f = Run(Old, "");
        Assert.Equal("new-within-old", f.Relation);
        Assert.True(f.NewEmpty);
    }

    [Fact]
    public void SnippetsCappedAtThreeAndSnippetChars()
    {
        var old = string.Join(" ", Enumerable.Range(0, 10).Select(i => $"block{i} " + new string('x', 50)));
        var @new = string.Join(" ", Enumerable.Range(0, 10).Select(i => $"other{i} " + new string('y', 50)));
        var f = Run(old, @new, Cfg(snippetChars: 20));
        Assert.InRange(f.Snippets.Count, 1, 3);
        foreach (var s in f.Snippets)
        {
            Assert.True(((string)s["old"]!).Length <= 20);
            Assert.True(((string)s["new"]!).Length <= 20);
        }
    }

    [Fact]
    public void SnippetsShowFirstDifferencesInOrder()
    {
        var f = Run("one two three four five six seven eight", "one TWO three four FIVE six seven EIGHT");
        Assert.Equal([Snip("two", "TWO"), Snip("five", "FIVE"), Snip("eight", "EIGHT")], f.Snippets);
    }

    [Fact]
    public void LongSnippetRegionIsCut()
    {
        var f = Run(new string('a', 500), new string('b', 500), Cfg(snippetChars: 30));
        Assert.Equal([Snip(new string('a', 30), new string('b', 30))], f.Snippets);
    }

    [Fact]
    public void RegionLongerThanWindowIsNotMispaired()
    {
        var f = Run(new string('Z', 2000) + " TAIL", "TAIL");
        Assert.Equal([Snip(new string('Z', 300), "")], f.Snippets);
    }

    [Fact]
    public void OverThresholdEqualStillFastPath()
    {
        Assert.Equal("fast-path", Run(new string('a', 101), new string('a', 101), Cfg(fullMax: 100)).Method);
    }

    [Fact]
    public void CountsAndTimingRecorded()
    {
        var f = Run("abc", "abcd");
        Assert.Equal((3L, 4L, 1L), (f.OldChars, f.NewChars, f.LengthDelta));
        Assert.Equal((0L, 0L, 0L), (f.LinesTotal, f.LinesCut, f.OversizedRuns));
        Assert.True(f.Seconds >= 0);
    }

    [Fact]
    public void LengthsAreCodePoints()
    {
        var f = Run("a\U0001F600b", "a\U0001F600bc");
        Assert.Equal((3L, 4L, 1L), (f.OldChars, f.NewChars, f.LengthDelta));
    }

    // ---- test_buffered.py ----

    [Fact]
    public void BufferedCloseToFullOnLineEdits()
    {
        var (old, @new) = Inputs("line_edits_full");
        Assert.True(old.Length > 40_000);
        var full = Run(old, @new);
        var buf = Run(old, @new, Cfg(fullMax: 1000));
        Assert.Equal("full", full.Method);
        Assert.Equal("buffered", buf.Method);
        Assert.True(Math.Abs(full.EditSimilarity!.Value - buf.EditSimilarity!.Value) <= 0.02);
        Assert.Equal(full.Relation, buf.Relation);
        Assert.Equal(full.Jaccard, buf.Jaccard);
        Assert.Equal(3, buf.Snippets.Count);
    }

    [Fact]
    public void NoLineBreaksIsCutAndCounted()
    {
        var old = string.Join(" ", Enumerable.Range(0, 2000).Select(i => $"word{i}"));
        var f = Run(old, old.Replace("word1000", "changed"), Cfg(fullMax: 1000, maxBlock: 500));
        Assert.Equal("buffered", f.Method);
        Assert.Equal((2L, 2L, 100.0), (f.LinesTotal, f.LinesCut, f.LinesCutPct));
        Assert.InRange(f.EditSimilarity!.Value, 0.9 + 1e-12, 1 - 1e-12);
    }

    [Fact]
    public void UncutLinesNotCounted()
    {
        var (old, @new) = Inputs("uncut_lines");
        var f = Run(old, @new, Cfg(fullMax: 1000));
        Assert.Equal(401, f.LinesTotal);
        Assert.Equal(0, f.LinesCut);
        Assert.Equal("old-within-new", f.Relation);
    }

    [Fact]
    public void OversizedReplacedRunComparedBlockByBlock()
    {
        var old = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"old line number {i} filler"));
        var @new = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"new line number {i} filler"));
        var f = Run(old, @new, Cfg(fullMax: 1000));
        Assert.Equal("buffered", f.Method);
        Assert.Equal(1, f.OversizedRuns);
        Assert.InRange(f.EditSimilarity!.Value, 0.5 + 1e-12, 1 - 1e-12);
    }

    [Fact]
    public void SmallReplacedRunNotOversized()
    {
        var (old, @new) = Inputs("small_replaced_run");
        Assert.Equal(0, Run(old, @new, Cfg(fullMax: 1000)).OversizedRuns);
    }

    [Fact]
    public void OneSideWithoutText()
    {
        var (_, @new) = Inputs("buffered_one_side_empty");
        var f = Run("", @new, Cfg(fullMax: 1000));
        Assert.Equal("buffered", f.Method);
        Assert.Equal("old-within-new", f.Relation);
        Assert.Equal(0.0, f.EditSimilarity);
    }

    [Fact]
    public void WrappedVsUnwrappedRatiosStayWithinOne()
    {
        var old = string.Join("\n", Enumerable.Repeat("x", 1000));
        var @new = string.Join(" ", Enumerable.Repeat("x", 1000)) + " tail";
        var f = Run(old, @new, Cfg(fullMax: 500));
        foreach (var v in new[] { f.EditSimilarity, f.OldFoundInNew, f.NewFoundInOld })
        {
            Assert.NotNull(v);
            Assert.InRange(v.Value, 0, 1);
        }
    }
}
