using System.CommandLine;
using ExtractorTester.Cli;

namespace ExtractorTester.Tests;

public class SmokeTests
{
    [Fact]
    public void CompareCommandIsRegistered()
    {
        var root = CliApp.Build();
        Assert.Contains(root.Subcommands, c => c.Name == "compare");
    }
}
