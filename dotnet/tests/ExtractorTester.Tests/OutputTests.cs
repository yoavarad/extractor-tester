using System.Text;
using ExtractorTester.Models;
using ExtractorTester.Output;

namespace ExtractorTester.Tests;

// Port of tests/test_output.py.
public sealed class OutputTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("et-output-").FullName;

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

    private static PairFacts Pair(string itemId = "i1") => new()
    {
        ItemId = itemId,
        Kind = "paired",
        Family = "text",
        TieBreak = "none",
        FieldDiffs = [],
        FieldsSame = [],
        Hints = [],
        Identical = true,
        Errors = [],
    };

    private static MetricResult Mr() => new() { Key = "k", Count = 0, Denominator = 0, Rate = 0.0, Examples = [] };

    private static Report MakeReport() => new()
    {
        SchemaVersion = 1,
        ToolVersion = "0",
        StartedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Inputs = [],
        Config = [],
        Totals = [],
        ItemsOnlyOld = Mr(),
        ItemsOnlyNew = Mr(),
        ItemFields = [],
        Families = [],
        Errors = [],
        Warnings = [],
        RunStats = new RunStats
        {
            WallSeconds = 0.0,
            Workers = 1,
            StageSeconds = [],
            TextSecondsByFamily = [],
            SlowestItems = [],
            WorkersReplaced = 0,
        },
    };

    private static string[] Names(string dir) =>
        Directory.EnumerateFileSystemEntries(dir).Select(p => Path.GetFileName(p)!).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void PrepareCreatesMissing()
    {
        var outDir = Path.Combine(_tmp, "a", "b");
        Assert.Equal(outDir, ReportWriter.PrepareOut(outDir, false));
        Assert.True(Directory.Exists(outDir));
    }

    [Fact]
    public void PrepareEmptyOk() => ReportWriter.PrepareOut(_tmp, false);

    [Fact]
    public void PrepareNonEmptyRefused()
    {
        File.WriteAllText(Path.Combine(_tmp, "x"), "1");
        var e = Assert.Throws<OutputError>(() => ReportWriter.PrepareOut(_tmp, false));
        Assert.Equal(ErrorCode.OUTPUT_EXISTS, e.Code);
    }

    [Fact]
    public void PrepareNonEmptyOverwrite()
    {
        File.WriteAllText(Path.Combine(_tmp, "x"), "1");
        ReportWriter.PrepareOut(_tmp, true);
        Assert.True(File.Exists(Path.Combine(_tmp, "x")));
    }

    [Fact]
    public void PrepareUnwritable()
    {
        var f = Path.Combine(_tmp, "file");
        File.WriteAllText(f, "1");
        foreach (var bad in new[] { f, Path.Combine(f, "sub") })
        {
            var e = Assert.Throws<OutputError>(() => ReportWriter.PrepareOut(bad, true));
            Assert.Equal(ErrorCode.OUTPUT_WRITE_FAILED, e.Code);
        }
    }

    [Fact]
    public void WriterOpenFailure()
    {
        var e = Assert.Throws<OutputError>(() => ReportWriter.PairsWriter(Path.Combine(_tmp, "missing")));
        Assert.Equal(ErrorCode.OUTPUT_WRITE_FAILED, e.Code);
    }

    [Fact]
    public void PairsJsonlOnePerLine()
    {
        using (var w = ReportWriter.PairsWriter(_tmp))
        {
            w.Write(Pair("é1"));
            w.Write(Pair("i2"));
        }
        var bytes = File.ReadAllBytes(Path.Combine(_tmp, ReportWriter.PairsFile));
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        var text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain('\r', text);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["é1", "i2"], lines.Select(x => PyJson.Deserialize<PairFacts>(x).ItemId));
        Assert.Equal(PyJson.Serialize(Pair("é1")) + "\n" + PyJson.Serialize(Pair("i2")) + "\n", text);
    }

    [Fact]
    public void ItemsJsonl()
    {
        var r = new ItemResult
        {
            ItemId = "i",
            Status = "compared",
            MetadataDiffs = [],
            FilesIdentical = 1,
            FilesChanged = 0,
            FilesOnlyOld = 0,
            FilesOnlyNew = 0,
            Errors = [],
            Seconds = 0.1,
            StageSeconds = [],
        };
        using (var w = ReportWriter.ItemsWriter(_tmp))
        {
            w.Write(r);
        }
        var text = File.ReadAllText(Path.Combine(_tmp, ReportWriter.ItemsFile), Encoding.UTF8);
        Assert.Equal(PyJson.Serialize(r) + "\n", text);
        Assert.Equal(PyJson.Serialize(r), PyJson.Serialize(PyJson.Deserialize<ItemResult>(text)));
    }

    [Fact]
    public void EvilItemIdStaysInside()
    {
        var outDir = Path.Combine(_tmp, "out");
        ReportWriter.PrepareOut(outDir, false);
        using (var w = ReportWriter.PairsWriter(outDir))
        {
            w.Write(Pair("../../evil"));
        }
        ReportWriter.WriteReport(outDir, MakeReport());
        Assert.Equal(["out"], Names(_tmp));
        Assert.Equal(["pairs.jsonl", "report.json"], Names(outDir));
    }

    [Fact]
    public void WriteReportAtomic()
    {
        var rep = MakeReport();
        var path = ReportWriter.WriteReport(_tmp, rep);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(PyJson.SerializeToUtf8Bytes(rep, indent: true), bytes);
        Assert.Equal(PyJson.Serialize(rep), PyJson.Serialize(PyJson.Deserialize<Report>(Encoding.UTF8.GetString(bytes))));
        Assert.Equal(["report.json"], Names(_tmp));
    }

    [Fact]
    public void WriteReportFailureKeepsOld()
    {
        ReportWriter.WriteReport(_tmp, MakeReport());
        var before = File.ReadAllText(Path.Combine(_tmp, ReportWriter.ReportFile));
        var e = Assert.Throws<OutputError>(() => ReportWriter.WriteReport(
            _tmp, MakeReport(), (_, _) => throw new UnauthorizedAccessException("denied")));
        Assert.Equal(ErrorCode.OUTPUT_WRITE_FAILED, e.Code);
        Assert.Equal(before, File.ReadAllText(Path.Combine(_tmp, ReportWriter.ReportFile)));
        Assert.Equal(["report.json"], Names(_tmp));
    }
}
