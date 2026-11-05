using ExtractorTester.Aggregate;
using ExtractorTester.Config;
using ExtractorTester.Models;
using ExtractorTester.Profiling;

namespace ExtractorTester.Tests;

/// <summary>Port of tests/test_profiling.py: slowest-items list and aggregator folding.</summary>
public class ProfilingTests
{
    [Fact]
    public void SlowestKeepsTopNDescending()
    {
        var s = new Slowest(3);
        var seconds = new[] { 1.0, 5.0, 3.0, 2.0, 4.0 };
        for (var i = 0; i < seconds.Length; i++)
        {
            s.Add($"i{i}", seconds[i], "compared");
        }
        Assert.Equal([5.0, 4.0, 3.0], s.Items().Select(x => (double)x["seconds"]!));
    }

    [Fact]
    public void DecodeAccumulatesAndResets()
    {
        Stages.Take("decode");
        Stages.Add("decode", 0.5);
        Stages.Add("decode", 0.25);
        Assert.Equal(0.75, Stages.Take("decode"));
        Assert.Equal(0.0, Stages.Take("decode"));
    }

    [Fact]
    public void AggregatorSumsItemStagesWithEveryStagePresent()
    {
        var agg = new Aggregator(new CompareConfig { NameField = "n", MimePrimaryField = "a", MimeSecondaryField = "b" });
        for (var i = 0; i < 2; i++)
        {
            agg.AddItem(new ItemResult
            {
                ItemId = $"i{i}",
                Status = "compared",
                MetadataDiffs = [],
                FilesIdentical = 0,
                FilesChanged = 0,
                FilesOnlyOld = 0,
                FilesOnlyNew = 0,
                Errors = [],
                Seconds = 1.0 + i,
                StageSeconds = new Dictionary<string, double> { ["load"] = 0.5 },
            });
        }
        agg.AddStage("render", 0.1);
        var (stages, _, slowest) = agg.Timings();
        Assert.Equal(StageNames.All.ToHashSet(), stages.Keys.ToHashSet());
        Assert.Equal(1.0, stages["load"]);
        Assert.Equal(0.1, stages["render"]);
        Assert.Equal(["i1", "i0"], slowest.Select(x => (string)x["item_id"]!));
    }
}
