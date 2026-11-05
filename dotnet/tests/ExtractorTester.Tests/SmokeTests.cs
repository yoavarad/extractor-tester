using System.CommandLine;
using System.Text;
using System.Text.Json;
using ExtractorTester.Cli;

namespace ExtractorTester.Tests;

/// <summary>Port of tests/test_smoke.py (module-import checks are Python-only; --profile dropped, ADR-009).</summary>
public class SmokeTests
{
    [Fact]
    public void CompareCommandIsRegistered()
    {
        var root = CliApp.Build();
        Assert.Contains(root.Subcommands, c => c.Name == "compare");
    }

    [Fact]
    public void CompareHelpListsArguments()
    {
        var sw = new StringWriter();
        Assert.Equal(0, CliApp.Run(["compare", "--help"], sw, sw));
        var text = sw.ToString();
        foreach (var token in new[] { "OLD", "NEW", "--config", "--out", "--overwrite", "--workers" })
        {
            Assert.Contains(token, text);
        }
    }

    [Fact]
    public void WriteItemRoundTripsText()
    {
        var dir = Directory.CreateTempSubdirectory("et-smoke-").FullName;
        try
        {
            var path = TestItems.WriteItem(
                dir, "i1", [new Dictionary<string, object?> { ["fileId"] = "1", ["text"] = "héllo" }], [("extra", "x")]);
            using var data = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            Assert.Equal("i1", data.RootElement.GetProperty("itemId").GetString());
            Assert.Equal("x", data.RootElement.GetProperty("extra").GetString());
            var text = data.RootElement.GetProperty("files")[0].GetProperty("text").GetString()!;
            Assert.Equal("héllo", Encoding.UTF8.GetString(Convert.FromBase64String(text)));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
