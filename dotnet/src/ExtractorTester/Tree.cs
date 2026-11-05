using System.Numerics;
using ExtractorTester.Config;
using ExtractorTester.Decode;
using ExtractorTester.Families;
using ExtractorTester.Fields;
using ExtractorTester.Models;
using ExtractorTester.Profiling;
using ExtractorTester.Render;

namespace ExtractorTester.Tree;

/// <summary>Item-level tool-error raised while building the tree (port of tree.TreeError).</summary>
public sealed class TreeException : Exception
{
    public TreeException(ErrorCode code, string message) : base(message)
    {
        Code = code;
    }

    public ErrorCode Code { get; }
}

/// <summary>Built item plus file-level warnings and errors.</summary>
public sealed record BuildResult(Item Item, List<ErrorRecord> Errors);

/// <summary>Build the FileRecord tree of one item from its raw JSON (port of tree.py).</summary>
public static class TreeBuilder
{
    private static int FindRoot(List<Dictionary<string, object?>> entries, string itemId)
    {
        foreach (var e in entries)
        {
            foreach (var k in new[] { "fileId", "parentId" })
            {
                if (e.GetValueOrDefault(k) is not string)
                {
                    throw new TreeException(ErrorCode.INVALID_JSON, $"item {itemId}: {k} missing or not a string");
                }
            }
        }
        var roots = new List<int>();
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (Id(e, "fileId") == itemId && Id(e, "parentId") == itemId
                && e.GetValueOrDefault("rootId") is string r && r == itemId)
            {
                roots.Add(i);
            }
        }
        if (roots.Count == 0)
        {
            throw new TreeException(ErrorCode.ROOT_NOT_FOUND, $"item {itemId}: no root file");
        }
        if (roots.Count > 1)
        {
            throw new TreeException(ErrorCode.MULTIPLE_ROOTS, $"item {itemId}: {roots.Count} root files");
        }
        var seen = new HashSet<string>();
        foreach (var e in entries)
        {
            var fid = Id(e, "fileId");
            if (!seen.Add(fid))
            {
                throw new TreeException(
                    ErrorCode.DUPLICATE_FILE_ID, $"item {itemId}: repeated file id {PyText.Repr(fid)}");
            }
        }
        return roots[0];
    }

    private static string Id(Dictionary<string, object?> e, string key) => (string)e[key]!;

    /// <summary>Raise PARENT_CYCLE on a loop; return indexes of orphan files (parent names no file).</summary>
    private static List<int> CheckUnreachable(
        List<Dictionary<string, object?>> entries, HashSet<int> reached, Dictionary<string, int> byId, string itemId)
    {
        var orphans = new List<int>();
        var state = new HashSet<int>(reached);
        for (var start = 0; start < entries.Count; start++)
        {
            if (state.Contains(start)) continue;
            var walk = new List<int>();
            var onWalk = new HashSet<int>();
            int? cur = start;
            while (cur is int c && !state.Contains(c))
            {
                if (onWalk.Contains(c))
                {
                    throw new TreeException(
                        ErrorCode.PARENT_CYCLE,
                        $"item {itemId}: parent loop at file {PyText.Repr(Id(entries[c], "fileId"))}");
                }
                walk.Add(c);
                onWalk.Add(c);
                cur = byId.TryGetValue(Id(entries[c], "parentId"), out var p) ? p : null;
            }
            if (cur is null) orphans.Add(walk[^1]);
            foreach (var w in walk) state.Add(w);
        }
        return orphans;
    }

    /// <summary>Build an Item with a FileRecord tree; throws TreeException on item-level errors.</summary>
    /// <remarks>
    /// <paramref name="data"/> is a validated <c>ItemLoader.LoadItem</c> result. Throws
    /// <see cref="FlattenCollisionException"/> (Python ValueError) on a flatten collision.
    /// </remarks>
    public static BuildResult Build(
        Dictionary<string, object?> data, CompareConfig config, string side, string sourcePath = "")
    {
        var itemId = (string)data["itemId"]!;
        var entries = ((List<object?>)data["files"]!).Cast<Dictionary<string, object?>>().ToList();
        var rootIdx = FindRoot(entries, itemId);
        var byId = new Dictionary<string, int>();
        for (var i = 0; i < entries.Count; i++) byId[Id(entries[i], "fileId")] = i;
        var kids = new Dictionary<int, List<int>>();
        for (var i = 0; i < entries.Count; i++)
        {
            if (i != rootIdx && byId.TryGetValue(Id(entries[i], "parentId"), out var parent))
            {
                if (!kids.TryGetValue(parent, out var list)) kids[parent] = list = new List<int>();
                list.Add(i);
            }
        }

        var order = new List<int> { rootIdx }; // BFS, parents before children
        var paths = new Dictionary<int, string> { [rootIdx] = Esc(Name(entries[rootIdx], config)) };
        for (var o = 0; o < order.Count; o++)
        {
            var idx = order[o];
            var children = kids.GetValueOrDefault(idx) ?? [];
            var names = children.ToDictionary(c => c, c => Name(entries[c], config));
            // Counts are keyed by raw names but looked up by escaped names, as in Python.
            var counts = new Dictionary<string, int>();
            foreach (var n in names.Values) counts[n] = counts.GetValueOrDefault(n) + 1;
            var seen = new Dictionary<string, int>();
            foreach (var c in children)
            {
                var n = Esc(names[c]);
                seen[n] = seen.GetValueOrDefault(n) + 1;
                var label = counts.GetValueOrDefault(n) > 1 ? $"{n}[{seen[n]}]" : n;
                paths[c] = $"{paths[idx]}/{label}";
                order.Add(c);
            }
        }
        var reached = new HashSet<int>(order);
        var errors = CheckUnreachable(entries, reached, byId, itemId)
            .Select(o => new ErrorRecord
            {
                Code = ErrorCode.ORPHAN_FILE,
                Scope = "file",
                Severity = "warning",
                ItemId = itemId,
                Side = side,
                Message = $"file {PyText.Repr(Id(entries[o], "fileId"))} parent "
                    + $"{PyText.Repr(Id(entries[o], "parentId"))} names no file; file and subtree dropped",
            })
            .ToList();

        var records = new Dictionary<int, FileRecord>();
        for (var o = order.Count - 1; o >= 0; o--)
        {
            var idx = order[o];
            var children = (kids.GetValueOrDefault(idx) ?? []).Select(c => records[c]).ToList();
            records[idx] = Record(entries[idx], idx, paths[idx], children, config, itemId, side, errors);
        }
        var meta = new Dictionary<string, object?>();
        foreach (var (k, v) in data)
        {
            if (k != "itemId" && k != "files") meta[k] = v;
        }
        var item = new Item
        {
            ItemId = itemId,
            Side = side,
            Metadata = meta,
            Files = [records[rootIdx]],
            SourcePath = sourcePath,
        };
        return new BuildResult(item, errors);
    }

    /// <summary>Escape path syntax so occurrence marks and separators cannot collide with names.</summary>
    private static string Esc(string name) =>
        name.Replace("\\", "\\\\").Replace("/", "\\/").Replace("[", "\\[");

    private static string Name(Dictionary<string, object?> entry, CompareConfig config) =>
        FieldFlattener.Flatten(entry).GetValueOrDefault(config.NameField) as string ?? "";

    private static FileRecord Record(
        Dictionary<string, object?> entry,
        int index,
        string path,
        List<FileRecord> children,
        CompareConfig config,
        string itemId,
        string side,
        List<ErrorRecord> errors)
    {
        var flat = FieldFlattener.Flatten(entry);
        flat.Remove(config.TextField, out var rawText);
        var missing = new[] { config.NameField, config.MimePrimaryField, config.MimeSecondaryField, config.TextField }
            .Where(f => (f == config.TextField ? rawText : flat.GetValueOrDefault(f)) is null)
            .ToList();
        errors.AddRange(missing.Select(f => new ErrorRecord
        {
            Code = ErrorCode.MISSING_FIELD,
            Scope = "file",
            Severity = "warning",
            ItemId = itemId,
            Side = side,
            FilePath = path,
            Message = $"field {PyText.Repr(f)} absent or null",
        }));
        string text;
        int bad;
        var tDec = Stages.Clock();
        try
        {
            if (rawText is not null and not string)
            {
                throw new InvalidBase64Exception($"expected base64 string, got {PyTypeName(rawText)}");
            }
            (text, bad) = Text.DecodeText((string?)rawText);
        }
        catch (InvalidBase64Exception e)
        {
            (text, bad) = ("", 0);
            errors.Add(new ErrorRecord
            {
                Code = ErrorCode.INVALID_BASE64,
                Scope = "file",
                Severity = "error",
                ItemId = itemId,
                Side = side,
                FilePath = path,
                Message = e.Message,
            });
        }
        Stages.Add("decode", Stages.Clock() - tDec);
        var m = FamilyResolver.Mime(
            flat.GetValueOrDefault(config.MimePrimaryField) as string,
            flat.GetValueOrDefault(config.MimeSecondaryField) as string);
        return new FileRecord
        {
            FileId = Id(entry, "fileId"),
            ParentId = Id(entry, "parentId"),
            Index = index,
            Name = flat.GetValueOrDefault(config.NameField) as string ?? "",
            Mime = m ?? "",
            Family = FamilyResolver.Family(m, config),
            Path = path,
            Fields = flat,
            Text = text,
            TextHash = bad != 0 ? null : Text.TextHash(Text.Normalize(text), (int)Math.Min(config.TextHashChars, int.MaxValue)),
            DecodeErrors = bad,
            Children = children,
            MissingFields = missing,
        };
    }

    // type(value).__name__ for parsed JSON values.
    private static string PyTypeName(object v) => v switch
    {
        bool => "bool",
        long or BigInteger => "int",
        double => "float",
        string => "str",
        List<object?> => "list",
        Dictionary<string, object?> => "dict",
        _ => v.GetType().Name,
    };
}
