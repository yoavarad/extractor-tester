using System.Text;
using System.Text.Json;
using ExtractorTester.Models;

namespace ExtractorTester.Tests;

// Fixtures come from Fixtures/Models/generate_fixtures.py (pydantic model_dump_json output).
public class ModelsJsonTests
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Models");

    private static readonly Dictionary<string, Type> ModelTypes = new[]
    {
        typeof(Item), typeof(FileRecord), typeof(FilePair), typeof(FieldDiff), typeof(TextFacts),
        typeof(PairFacts), typeof(ItemResult), typeof(ErrorRecord), typeof(Example),
        typeof(MetricResult), typeof(FamilyAggregate), typeof(RunStats), typeof(Report),
    }.ToDictionary(t => t.Name);

    public static IEnumerable<object[]> ModelNames => ModelTypes.Keys.Select(k => new object[] { k });

    private static string Fixture(string name) =>
        Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(Dir, name)));

    [Theory]
    [MemberData(nameof(ModelNames))]
    public void FieldNamesAndOrderMatchPythonModel(string model)
    {
        using var schema = JsonDocument.Parse(Fixture("schema.json"));
        var expected = schema.RootElement.GetProperty(model).EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Equal(expected, PyJson.FieldNames(ModelTypes[model]));
    }

    [Fact]
    public void ErrorCodesMatchPythonEnum()
    {
        using var schema = JsonDocument.Parse(Fixture("schema.json"));
        var expected = schema.RootElement.GetProperty("ErrorCode").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Equal(expected, Enum.GetNames<ErrorCode>());
    }

    [Theory]
    [MemberData(nameof(ModelNames))]
    public void SampleSerializesToPydanticBytes(string model)
    {
        var compact = Fixture($"sample-{model}.json");
        var value = PyJson.Deserialize(compact, ModelTypes[model]);
        Assert.Equal(compact, PyJson.Serialize(value));
        Assert.Equal(Fixture($"sample-{model}.indent.json"), PyJson.Serialize(value, indent: true));
    }

    [Theory]
    [InlineData("golden-pairs.jsonl", typeof(PairFacts))]
    [InlineData("golden-items.jsonl", typeof(ItemResult))]
    public void GoldenJsonlLinesRoundTrip(string file, Type type)
    {
        var lines = Fixture(file).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            Assert.Equal(line, PyJson.Serialize(PyJson.Deserialize(line, type)));
        }
    }

    [Fact]
    public void GoldenReportRoundTrips()
    {
        var text = Fixture("golden-report.json");
        var report = PyJson.Deserialize<Report>(text);
        var bytes = PyJson.SerializeToUtf8Bytes(report, indent: true);
        Assert.Equal(File.ReadAllBytes(Path.Combine(Dir, "golden-report.json")), bytes);
        var again = PyJson.Deserialize<Report>(Encoding.UTF8.GetString(bytes));
        Assert.Equal(bytes, PyJson.SerializeToUtf8Bytes(again, indent: true));
    }

    // Expected text is what pydantic-core emits (verified against pydantic); note 1e-05 -> 0.00001.
    [Theory]
    [InlineData(1.0, "1.0")]
    [InlineData(1e-05, "0.00001")]
    [InlineData(0.1, "0.1")]
    [InlineData(1.0 / 3, "0.3333333333333333")]
    [InlineData(123456789.0, "123456789.0")]
    [InlineData(1e16, "1e+16")]
    [InlineData(1e15, "1000000000000000.0")]
    [InlineData(1e-06, "1e-6")]
    [InlineData(1.5e-06, "1.5e-6")]
    [InlineData(0.0000123, "0.0000123")]
    [InlineData(12345678901234567890.0, "1.2345678901234567e+19")]
    [InlineData(1.7976931348623157e308, "1.7976931348623157e+308")]
    [InlineData(5e-324, "5e-324")]
    [InlineData(-2.5, "-2.5")]
    [InlineData(-0.0, "-0.0")]
    [InlineData(0.0, "0.0")]
    [InlineData(double.NaN, "null")]
    [InlineData(double.PositiveInfinity, "null")]
    public void FloatsFormatLikePydantic(double value, string expected)
    {
        Assert.Equal(expected, PyJson.FormatFloat(value));
    }

    [Fact]
    public void ErrorCodeSerializesByStringCode()
    {
        var err = new ErrorRecord { Code = ErrorCode.ITEM_TIMEOUT, Scope = "item", Severity = "error", Message = "m" };
        var json = PyJson.Serialize(err);
        Assert.Equal(
            "{\"code\":\"ITEM_TIMEOUT\",\"scope\":\"item\",\"severity\":\"error\",\"item_id\":null,\"side\":null,\"file_path\":null,\"message\":\"m\"}",
            json);
        Assert.Equal(err, PyJson.Deserialize<ErrorRecord>(json));
    }
}
