using System.Text.Json;
using ExtractorTester.Compare;

namespace ExtractorTester.Tests;

public class SequenceMatcherTests
{
    private static readonly JsonElement Cases = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "seqmatcher_cases.json"))).RootElement;

    private static string[] Strs(JsonElement e) => e.EnumerateArray().Select(x => x.GetString()!).ToArray();

    [Fact]
    public void FixtureHasEnoughCases()
    {
        Assert.True(Cases.GetArrayLength() >= 300);
        Assert.Contains(Cases.EnumerateArray(), c => c.GetProperty("a").GetArrayLength() > 200);
    }

    [Fact]
    public void MatchesDifflibOnEveryCase()
    {
        var idx = 0;
        foreach (var c in Cases.EnumerateArray())
        {
            var sm = new SequenceMatcher<string>(Strs(c.GetProperty("a")), Strs(c.GetProperty("b")));

            var blocks = c.GetProperty("blocks").EnumerateArray()
                .Select(x => x.EnumerateArray().Select(n => n.GetInt32()).ToArray())
                .Select(x => new MatchingBlock(x[0], x[1], x[2])).ToList();
            Assert.True(blocks.SequenceEqual(sm.GetMatchingBlocks()), $"blocks case {idx}");

            var ops = c.GetProperty("opcodes").EnumerateArray().Select(x =>
            {
                var arr = x.EnumerateArray().ToArray();
                return new Opcode(arr[0].GetString()!, arr[1].GetInt32(), arr[2].GetInt32(), arr[3].GetInt32(), arr[4].GetInt32());
            }).ToList();
            Assert.True(ops.SequenceEqual(sm.GetOpcodes()), $"opcodes case {idx}");

            Assert.Equal(c.GetProperty("ratio").GetDouble(), sm.Ratio());
            idx++;
        }
    }
}
