using System.Reflection;
using ExtractorTester.Models;

namespace ExtractorTester.Tests;

/// <summary>C# equal of the pytest covers_error("CODE") marker: names the error code a test proves.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class CoversErrorAttribute(string code) : Attribute
{
    public string Code { get; } = code;
}

/// <summary>Port of tests/test_tool_error_coverage.py: every ErrorCode is proven by a test marked [CoversError].</summary>
public class ToolErrorCoverageTests
{
    private static HashSet<string> MarkedCodes() =>
        typeof(ToolErrorCoverageTests).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .Where(m => m.GetCustomAttributes<FactAttribute>().Any())
            .SelectMany(m => m.GetCustomAttributes<CoversErrorAttribute>())
            .Select(a => a.Code)
            .ToHashSet();

    [Fact]
    public void EveryErrorCodeHasAMarkedTest()
    {
        var marked = MarkedCodes();
        var known = Enum.GetNames<ErrorCode>().ToHashSet();
        var problems = new List<string>();
        var missing = known.Except(marked).Order(StringComparer.Ordinal).ToList();
        var unknown = marked.Except(known).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            problems.Add("no test marked for: " + string.Join(", ", missing));
        }
        if (unknown.Count > 0)
        {
            problems.Add("marker names unknown code: " + string.Join(", ", unknown));
        }
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }
}
