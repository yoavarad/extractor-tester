using System.Text.Json;
using ExtractorTester.Decode;

namespace ExtractorTester.Tests;

public class DecodeParityTests
{
    private static readonly JsonElement Fixture = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity.json"))).RootElement;

    private static IEnumerable<JsonElement> Cases(string name) => Fixture.GetProperty(name).EnumerateArray();

    private static string[] Strs(JsonElement arr) => arr.EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public void StringHelpersMatchPython()
    {
        foreach (var c in Cases("strings"))
        {
            var s = c.GetProperty("s").GetString()!;
            Assert.Equal(c.GetProperty("cp_len").GetInt32(), Text.CodePointLength(s));
            Assert.Equal(Strs(c.GetProperty("split")), Text.PySplit(s));
            Assert.Equal(Strs(c.GetProperty("lines")), Text.PySplitLines(s));
            Assert.Equal(Strs(c.GetProperty("lines_keep")), Text.PySplitLines(s, true));
            var n = Text.Normalize(s);
            Assert.Equal(c.GetProperty("normalize").GetString(), n);
            foreach (var p in c.GetProperty("hash_limits").EnumerateObject())
            {
                Assert.Equal(p.Value.GetString(), Text.TextHash(n, int.Parse(p.Name)));
            }
        }
    }

    [Fact]
    public void Base64MatchesPython()
    {
        foreach (var c in Cases("base64"))
        {
            var input = c.GetProperty("in").GetString()!;
            if (c.GetProperty("ok").GetBoolean())
            {
                var (text, count) = Text.DecodeText(input);
                Assert.Equal(c.GetProperty("text").GetString(), text);
                Assert.Equal(c.GetProperty("count").GetInt32(), count);
            }
            else
            {
                Assert.Throws<InvalidBase64Exception>(() => Text.DecodeText(input));
            }
        }
    }

    [Fact]
    public void Utf8ReplacementCountsMatchPython()
    {
        foreach (var c in Cases("utf8"))
        {
            var raw = Convert.FromHexString(c.GetProperty("hex").GetString()!);
            var (text, count) = Text.DecodeUtf8Counted(raw);
            Assert.Equal(c.GetProperty("text").GetString(), text);
            Assert.Equal(c.GetProperty("count").GetInt32(), count);
        }
    }

    [Fact]
    public void NullDecodesToEmpty() => Assert.Equal(("", 0), Text.DecodeText(null));
}
