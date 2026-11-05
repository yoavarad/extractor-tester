using System.Text;
using ExtractorTester.Config;
using ExtractorTester.Decode;
using ExtractorTester.Matching;
using ExtractorTester.Models;
using ExtractorTester.Tree;

namespace ExtractorTester.Tests;

// Port of tests/test_tree.py.
public sealed class TreeTests
{
    private static readonly CompareConfig Cfg = new()
    {
        NameField = "name",
        MimePrimaryField = "mt.a",
        MimeSecondaryField = "mt.b",
    };

    private static Dictionary<string, object?> F(string fid, string parent, string? name = "n", string? text = "hi")
    {
        var e = new Dictionary<string, object?>
        {
            ["fileId"] = fid,
            ["parentId"] = parent,
            ["rootId"] = "I",
            ["mt"] = new Dictionary<string, object?> { ["a"] = "text", ["b"] = "plain" },
        };
        if (name is not null) e["name"] = name;
        if (text is not null) e["text"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        return e;
    }

    private static Dictionary<string, object?> Item(params Dictionary<string, object?>[] files) => new()
    {
        ["itemId"] = "I",
        ["files"] = files.Cast<object?>().ToList(),
    };

    private static List<FileRecord> Walk(FileRecord rec)
    {
        var output = new List<FileRecord>();
        var stack = new Stack<FileRecord>();
        stack.Push(rec);
        while (stack.Count > 0)
        {
            var r = stack.Pop();
            output.Add(r);
            foreach (var c in r.Children) stack.Push(c);
        }
        return output;
    }

    private static TreeException Fails(Dictionary<string, object?> data) =>
        Assert.Throws<TreeException>(() => TreeBuilder.Build(data, Cfg, "old"));

    [Fact]
    public void TreeAndOrder()
    {
        var res = TreeBuilder.Build(Item(F("I", "I", "root"), F("c", "I", "b"), F("d", "I", "a")), Cfg, "old");
        var root = res.Item.Files[0];
        Assert.Equal(["b", "a"], root.Children.Select(c => c.Name));
        Assert.Equal(["root/b", "root/a"], root.Children.Select(c => c.Path));
        Assert.Equal("hi", root.Text);
        Assert.Equal("text/plain", root.Mime);
        Assert.Equal("text", root.Fields["mt.a"]);
        Assert.Empty(res.Errors);
    }

    [Fact]
    public void SiblingOccurrencePaths()
    {
        var res = TreeBuilder.Build(
            Item(F("I", "I", "r"), F("1", "I", "a.png"), F("2", "I", "a.png"), F("3", "I", "b")), Cfg, "new");
        Assert.Equal(["r/a.png[1]", "r/a.png[2]", "r/b"], res.Item.Files[0].Children.Select(c => c.Path));
    }

    [Fact]
    [CoversError("ROOT_NOT_FOUND")]
    public void RootNotFound()
    {
        var e = Fails(Item(F("x", "I")));
        Assert.Equal(ErrorCode.ROOT_NOT_FOUND, e.Code);
        Assert.Equal("item I: no root file", e.Message);
    }

    [Fact]
    [CoversError("MULTIPLE_ROOTS")]
    public void MultipleRoots()
    {
        var e = Fails(Item(F("I", "I"), F("I", "I")));
        Assert.Equal(ErrorCode.MULTIPLE_ROOTS, e.Code);
        Assert.Equal("item I: 2 root files", e.Message);
    }

    [Fact]
    [CoversError("DUPLICATE_FILE_ID")]
    public void DuplicateFileId()
    {
        var e = Fails(Item(F("I", "I"), F("a", "I"), F("a", "I")));
        Assert.Equal(ErrorCode.DUPLICATE_FILE_ID, e.Code);
        Assert.Equal("item I: repeated file id 'a'", e.Message);
    }

    [Fact]
    [CoversError("PARENT_CYCLE")]
    public void ParentCycle()
    {
        var e = Fails(Item(F("I", "I"), F("a", "b"), F("b", "a")));
        Assert.Equal(ErrorCode.PARENT_CYCLE, e.Code);
        Assert.Equal("item I: parent loop at file 'a'", e.Message);
    }

    [Fact]
    [CoversError("ORPHAN_FILE")]
    public void OrphanKeepsRest()
    {
        var res = TreeBuilder.Build(Item(F("I", "I", "r"), F("a", "I", "ok"), F("o", "zz"), F("p", "o")), Cfg, "old");
        Assert.Equal(["ok", "r"], Walk(res.Item.Files[0]).Select(r => r.Name).Order(StringComparer.Ordinal));
        var err = Assert.Single(res.Errors);
        Assert.Equal(
            "{\"code\":\"ORPHAN_FILE\",\"scope\":\"file\",\"severity\":\"warning\",\"item_id\":\"I\",\"side\":\"old\","
            + "\"file_path\":null,\"message\":\"file 'o' parent 'zz' names no file; file and subtree dropped\"}",
            PyJson.Serialize(err));
    }

    [Fact]
    [CoversError("MISSING_FIELD")]
    public void MissingFieldWarning()
    {
        var res = TreeBuilder.Build(Item(F("I", "I", "r"), F("a", "I", name: null, text: null)), Cfg, "old");
        var child = res.Item.Files[0].Children[0];
        Assert.Equal("", child.Name);
        Assert.Equal("", child.Text);
        Assert.Equal(["name", "text"], child.MissingFields);
        Assert.All(res.Errors, e => Assert.Equal((ErrorCode.MISSING_FIELD, "warning"), (e.Code, e.Severity)));
        Assert.Equal(
            ["{\"code\":\"MISSING_FIELD\",\"scope\":\"file\",\"severity\":\"warning\",\"item_id\":\"I\",\"side\":\"old\",\"file_path\":\"r/\",\"message\":\"field 'name' absent or null\"}",
             "{\"code\":\"MISSING_FIELD\",\"scope\":\"file\",\"severity\":\"warning\",\"item_id\":\"I\",\"side\":\"old\",\"file_path\":\"r/\",\"message\":\"field 'text' absent or null\"}"],
            res.Errors.Select(e => PyJson.Serialize(e)));
    }

    [Fact]
    public void InvalidBase64Errors()
    {
        var root = F("I", "I", "r");
        root["text"] = 5L;
        var b = F("b", "I");
        b["text"] = "@@";
        var c = F("c", "I");
        c["text"] = new List<object?> { 1L };
        var res = TreeBuilder.Build(Item(root, b, c), Cfg, "new");
        Assert.Equal(
            [("r/n[2]", "expected base64 string, got list"), ("r/n[1]", "Only base64 data is allowed"), ("r", "expected base64 string, got int")],
            res.Errors.Select(e => (e.FilePath, e.Message)));
        Assert.All(res.Errors, e => Assert.Equal((ErrorCode.INVALID_BASE64, "error"), (e.Code, e.Severity)));
    }

    [Fact]
    public void DeepChain()
    {
        var files = new List<Dictionary<string, object?>> { F("I", "I", "r") };
        for (var i = 0; i < 5000; i++) files.Add(F(i.ToString(), i == 0 ? "I" : (i - 1).ToString(), "d"));
        var res = TreeBuilder.Build(Item(files.ToArray()), Cfg, "old");
        Assert.Equal(5001, Walk(res.Item.Files[0]).Count);
    }

    [Fact]
    public void PathsUniqueDespiteSuffixAndSlashNames()
    {
        var d = Item(F("I", "I", "r"), F("1", "I", "a"), F("2", "I", "a"), F("3", "I", "a[2]"), F("4", "1", "b"), F("5", "I", "a/b"));
        var paths = Walk(TreeBuilder.Build(d, Cfg, "old").Item.Files[0]).Select(r => r.Path).ToList();
        Assert.Equal(6, paths.Count);
        Assert.Equal(6, paths.Distinct().Count());
    }

    [Fact]
    public void EscapedDuplicateNamesKeepPythonCounting()
    {
        // Python counts raw names but looks up escaped ones, so repeated "x/y" gets no occurrence mark.
        var d = Item(F("I", "I", "r"), F("1", "I", "a"), F("2", "I", "a"), F("3", "I", "a[2]"), F("4", "1", "b"),
            F("5", "I", "a/b"), F("6", "I", "x/y"), F("7", "I", "x/y"), F("8", "I", "x\\/y"));
        var root = TreeBuilder.Build(d, Cfg, "old").Item.Files[0];
        Assert.Equal(
            ["r/a[1]", "r/a[2]", "r/a\\[2]", "r/a\\/b", "r/x\\/y", "r/x\\/y", "r/x\\\\\\/y"],
            root.Children.Select(c => c.Path));
        Assert.Equal(["r/a[1]/b"], root.Children[0].Children.Select(c => c.Path));
    }

    [Fact]
    public void NonStringIdsAreTreeErrors()
    {
        var bad = F("x", "I");
        bad["fileId"] = new List<object?> { "x" };
        var e = Fails(Item(F("I", "I"), bad));
        Assert.Equal(ErrorCode.INVALID_JSON, e.Code);
        Assert.Equal("item I: fileId missing or not a string", e.Message);
    }

    [Fact]
    public void TextHashSetAndEmptyNone()
    {
        var res = TreeBuilder.Build(Item(F("I", "I", "r", "hi"), F("c", "I", "e", "")), Cfg, "old");
        var root = res.Item.Files[0];
        Assert.Equal(Text.TextHash("hi", (int)Cfg.TextHashChars), root.TextHash);
        Assert.NotNull(root.TextHash);
        Assert.Null(root.Children[0].TextHash);
    }

    [Fact]
    public void TextHashIgnoresWhitespaceDifferences()
    {
        var a = TreeBuilder.Build(Item(F("I", "I", "r", "a  b\n c")), Cfg, "old").Item.Files[0];
        var b = TreeBuilder.Build(Item(F("I", "I", "r", " a b c ")), Cfg, "new").Item.Files[0];
        Assert.NotNull(a.TextHash);
        Assert.Equal(a.TextHash, b.TextHash);
    }

    [Fact]
    public void MatcherPairsRenamedFileByTextHash()
    {
        var old = TreeBuilder.Build(Item(F("I", "I", "r"), F("c", "I", "old.txt", "same body")), Cfg, "old").Item;
        var @new = TreeBuilder.Build(Item(F("I", "I", "r"), F("c", "I", "new.txt", "same  body")), Cfg, "new").Item;
        var pairs = Matcher.MatchItem(old, @new);
        Assert.Equal([("paired", "text-hash")], pairs.Skip(1).Select(p => (p.Kind, p.Tier)).ToList());
    }

    private static Dictionary<string, object?> Bad(string fid, string parent, string name, byte[] raw)
    {
        var e = F(fid, parent, name, "");
        e["text"] = Convert.ToBase64String(raw);
        return e;
    }

    [Fact]
    public void TextHashNoneWhenDecodeErrors()
    {
        var res = TreeBuilder.Build(Item(F("I", "I", "r", "hi"), Bad("c", "I", "bad", [0x61, 0x62, 0xFF, 0xFE])), Cfg, "old");
        var root = res.Item.Files[0];
        Assert.NotNull(root.TextHash);
        Assert.True(root.Children[0].DecodeErrors > 0);
        Assert.Null(root.Children[0].TextHash);
    }

    [Fact]
    public void MatcherDoesNotPairBrokenFilesByTextHash()
    {
        var old = TreeBuilder.Build(Item(F("I", "I", "r"), Bad("c", "I", "old.txt", [0xFF, 0xFE, 0xFD])), Cfg, "old").Item;
        var @new = TreeBuilder.Build(Item(F("I", "I", "r"), Bad("c", "I", "new.txt", [0xFA, 0xFB, 0xFC])), Cfg, "new").Item;
        Assert.All(Matcher.MatchItem(old, @new), p => Assert.NotEqual("text-hash", p.Tier));
    }

    [Fact]
    public void FileRecordJsonMatchesPython()
    {
        var d = Item(F("I", "I", "r"), F("1", "I", "a"));
        var root = TreeBuilder.Build(d, Cfg, "old").Item.Files[0];
        Assert.StartsWith(
            "{\"file_id\":\"I\",\"parent_id\":\"I\",\"index\":0,\"name\":\"r\",\"mime\":\"text/plain\",\"family\":\"text/plain\","
            + "\"path\":\"r\",\"fields\":{\"fileId\":\"I\",\"parentId\":\"I\",\"rootId\":\"I\",\"name\":\"r\",\"mt.a\":\"text\",\"mt.b\":\"plain\"},"
            + "\"text\":\"hi\",\"text_hash\":\"8f434346648f6b96df89dda901c5176b10a6d83961dd3c1ac88b59b2dc327aa4\",\"decode_errors\":0,"
            + "\"children\":[{\"file_id\":\"1\",\"parent_id\":\"I\",\"index\":1,\"name\":\"a\"",
            PyJson.Serialize(root));
    }

    [Fact]
    public void LoadedItemBuilds()
    {
        var dir = Directory.CreateTempSubdirectory("et-tree-").FullName;
        try
        {
            var p = TestItems.WriteItem(dir, "I", [F("I", "I", "r"), F("c", "I", "kid")], [("meta", 1L)]);
            var data = Loader.ItemLoader.LoadItem(p, "I", Cfg, "old");
            var item = TreeBuilder.Build(data, Cfg, "old", p).Item;
            Assert.Equal(["meta"], item.Metadata.Keys);
            Assert.Equal(p, item.SourcePath);
            Assert.Equal("r/kid", item.Files[0].Children[0].Path);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
