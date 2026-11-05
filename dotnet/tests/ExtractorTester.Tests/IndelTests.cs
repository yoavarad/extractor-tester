using System.Diagnostics;
using System.Text.Json;
using ExtractorTester.Compare;
using ExtractorTester.Decode;

namespace ExtractorTester.Tests;

public class IndelTests
{
    private static readonly JsonElement Cases = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "indel_cases.json"))).RootElement;

    [Fact]
    public void FixtureHasEnoughCases()
    {
        Assert.True(Cases.GetArrayLength() >= 500);
        Assert.Contains(Cases.EnumerateArray(), c => c.GetProperty("a").GetString()!.Any(char.IsHighSurrogate));
    }

    [Fact]
    public void MatchesRapidfuzzOnEveryCase()
    {
        var idx = 0;
        foreach (var c in Cases.EnumerateArray())
        {
            var a = Text.ToCodePoints(c.GetProperty("a").GetString()!);
            var b = Text.ToCodePoints(c.GetProperty("b").GetString()!);

            Assert.True(c.GetProperty("distance").GetInt32() == Indel.Distance(a, b), $"distance case {idx}");
            Assert.True(c.GetProperty("similarity").GetDouble() == Indel.NormalizedSimilarity(a, b), $"similarity case {idx}");

            var expected = c.GetProperty("opcodes").EnumerateArray().Select(x =>
            {
                var arr = x.EnumerateArray().ToArray();
                return new Opcode(arr[0].GetString()!, arr[1].GetInt32(), arr[2].GetInt32(), arr[3].GetInt32(), arr[4].GetInt32());
            }).ToList();
            Assert.True(expected.SequenceEqual(Indel.Opcodes(a, b)), $"opcodes case {idx}");
            idx++;
        }
    }

    [Fact]
    public void FiftyThousandCodePointPairIsFast()
    {
        var rng = new Random(7);
        var a = Enumerable.Range(0, 50_000).Select(_ => 'a' + rng.Next(26)).ToArray();
        var b = (int[])a.Clone();
        for (var i = 0; i < 500; i++) b[rng.Next(b.Length)] = 'a' + rng.Next(26);
        var sw = Stopwatch.StartNew();
        var d = Indel.Distance(a, b);
        sw.Stop();
        Assert.True(d > 0);
        // Opcodes keep the full bit matrix (as rapidfuzz does); keep this one small for CI memory.
        var ops = Indel.Opcodes(a[..5_000], b[..5_000]);
        Assert.True(ops.Count > 0);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");
    }
}
