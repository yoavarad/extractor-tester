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

    [Fact]
    public void CompareStubExitsZero()
    {
        var exit = CliApp.Build().Parse(["compare", "a", "b", "--config", "c.yaml"]).Invoke();
        Assert.Equal(0, exit);
    }
}
