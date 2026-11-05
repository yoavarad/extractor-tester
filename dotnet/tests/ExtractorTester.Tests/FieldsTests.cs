using ExtractorTester.Config;
using ExtractorTester.Fields;

namespace ExtractorTester.Tests;

// Port of tests/test_fields.py.
public sealed class FieldsTests
{
    private static CompareConfig Cfg()
    {
        var c = new CompareConfig
        {
            NameField = "n",
            MimePrimaryField = "a",
            MimeSecondaryField = "b",
            GenericFields = ["meta"],
            IgnoreFields = ["skip"],
        };
        c.ApplyPostInit();
        return c;
    }

    private static Dictionary<string, object?> D(params (string K, object? V)[] kv)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in kv) d[k] = v;
        return d;
    }

    [Fact]
    public void FlattenDeep()
    {
        var obj = D(("leaf", 1L));
        for (var i = 0; i < 1999; i++) obj = D(("k", obj));
        var output = FieldFlattener.Flatten(obj);
        var key = string.Join(".", Enumerable.Repeat("k", 1999).Append("leaf"));
        Assert.Equal([key], output.Keys);
        Assert.Equal(1L, output[key]);
    }

    [Fact]
    public void FlattenBasic()
    {
        var list = new List<object?> { 1L, 2L };
        var empty = new Dictionary<string, object?>();
        var output = FieldFlattener.Flatten(D(("a", D(("b", list), ("c", empty))), ("d", 3L)));
        Assert.Equal(["d", "a.b", "a.c"], output.Keys);
        Assert.Same(list, output["a.b"]);
        Assert.Same(empty, output["a.c"]);
        Assert.Equal(3L, output["d"]);
        Assert.Empty(FieldFlattener.Flatten(D()));
    }

    [Fact]
    public void FlattenOrderMatchesPythonStackWalk()
    {
        var obj = D(("a", D(("b", 1L))), ("c", D(("d", D(("e", 2L))), ("f", 3L))), ("g", 4L), ("h", D()));
        Assert.Equal(["g", "h", "c.f", "c.d.e", "a.b"], FieldFlattener.Flatten(obj).Keys);
    }

    [Fact]
    public void Classify()
    {
        var c = Cfg();
        Assert.Equal("generic", FieldFlattener.Classify("meta.author.name", c));
        Assert.Equal("generic", FieldFlattener.Classify("meta", c));
        Assert.Equal("specific", FieldFlattener.Classify("metadata", c));
        Assert.Equal("ignored", FieldFlattener.Classify("skip.x", c));
        foreach (var p in new[] { "fileId", "parentId", "rootId", "text" })
        {
            Assert.Equal("ignored", FieldFlattener.Classify(p, c));
        }
        Assert.Equal("specific", FieldFlattener.Classify("other", c));
    }

    [Fact]
    public void FlattenCollisionRaises()
    {
        var e = Assert.Throws<FlattenCollisionException>(() =>
            FieldFlattener.Flatten(D(("a", D(("b", 1L))), ("a.b", 2L))));
        Assert.Equal("flatten path collision: 'a.b'", e.Message);
    }

    [Fact]
    public void ClassifyIgnoreBeatsGeneric()
    {
        var c = Cfg();
        c.GenericFields.Add("skip");
        Assert.Equal("ignored", FieldFlattener.Classify("skip.x", c));
    }
}
