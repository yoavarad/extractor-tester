using ExtractorTester.Models;

namespace ExtractorTester.Matching;

/// <summary>Pairs made by one tier plus the old/new records it left unpaired (input order kept).</summary>
public sealed record TierResult(List<FilePair> Pairs, List<FileRecord> OldRest, List<FileRecord> NewRest);

/// <summary>One matching rule run on the unpaired children of a parent pair (port of tiers.MatchTier).</summary>
public interface IMatchTier
{
    /// <summary>Tier label recorded on FilePair.Tier.</summary>
    string Name { get; }

    /// <summary>Pair some children one-to-one; return pairs, old rest, new rest (input order kept).</summary>
    TierResult Pair(List<FileRecord> oldChildren, List<FileRecord> newChildren);
}

/// <summary>Tier 1: identical, non-empty, case-sensitive file name.</summary>
public sealed class NameTier : IMatchTier
{
    public string Name => "name";

    /// <summary>Pair same-name siblings; ties by text hash + family, then position.</summary>
    public TierResult Pair(List<FileRecord> oldChildren, List<FileRecord> newChildren)
    {
        var newByName = new Dictionary<string, List<FileRecord>>(StringComparer.Ordinal);
        foreach (var n in newChildren)
        {
            if (n.Name.Length > 0) Tiers.Group(newByName, n.Name, n);
        }
        var oldByName = new Dictionary<string, List<FileRecord>>(StringComparer.Ordinal);
        foreach (var o in oldChildren)
        {
            if (newByName.ContainsKey(o.Name)) Tiers.Group(oldByName, o.Name, o);
        }

        var pairs = new List<FilePair>();
        var used = new HashSet<FileRecord>(ReferenceEqualityComparer.Instance);
        foreach (var o in oldChildren)
        {
            if (!oldByName.Remove(o.Name, out var group)) continue;
            foreach (var (a, b, tb) in PairGroup(group, newByName[o.Name]))
            {
                pairs.Add(new FilePair { Kind = "paired", Old = a, New = b, Tier = "name", TieBreak = tb });
                used.Add(a);
                used.Add(b);
            }
        }
        return Tiers.Result(pairs, oldChildren, newChildren, used);
    }

    /// <summary>Pair one same-name group; extras stay unpaired.</summary>
    private static List<(FileRecord Old, FileRecord New, string TieBreak)> PairGroup(List<FileRecord> old, List<FileRecord> @new)
    {
        if (old.Count == 1 && @new.Count == 1) return [(old[0], @new[0], "none")];
        var byHash = new Dictionary<(string, string), List<int>>();
        for (var i = 0; i < @new.Count; i++)
        {
            var n = @new[i];
            if (n.TextHash is not null) Tiers.Group(byHash, (n.TextHash, n.Family), i);
        }
        var output = new List<(FileRecord, FileRecord, string)>();
        var taken = new HashSet<int>();
        var oldLeft = new List<FileRecord>();
        foreach (var o in old)
        {
            if (o.TextHash is not null && byHash.TryGetValue((o.TextHash, o.Family), out var slots) && slots.Count > 0)
            {
                var i = slots[0];
                slots.RemoveAt(0);
                taken.Add(i);
                output.Add((o, @new[i], "text-hash"));
            }
            else
            {
                oldLeft.Add(o);
            }
        }
        var newLeft = @new.Where((_, i) => !taken.Contains(i)).ToList();
        for (var i = 0; i < Math.Min(oldLeft.Count, newLeft.Count); i++)
        {
            output.Add((oldLeft[i], newLeft[i], "position"));
        }
        return output;
    }
}

/// <summary>Tier 2: same normalized-text hash and family, exactly one candidate per side.</summary>
public sealed class TextHashTier : IMatchTier
{
    public string Name => "text-hash";

    /// <summary>Pair files whose (hash, family) key is unique on both sides; empty text never pairs.</summary>
    public TierResult Pair(List<FileRecord> oldChildren, List<FileRecord> newChildren) =>
        Tiers.PairUnique(oldChildren, newChildren, Name,
            r => r.TextHash is null ? null : (object)(r.TextHash, r.Family));
}

/// <summary>Tier 3: exactly one unpaired file of a family on each side.</summary>
public sealed class TypeFamilyTier : IMatchTier
{
    public string Name => "type-family";

    /// <summary>Pair files whose family is unique on both sides.</summary>
    public TierResult Pair(List<FileRecord> oldChildren, List<FileRecord> newChildren) =>
        Tiers.PairUnique(oldChildren, newChildren, Name, r => r.Family);
}

internal static class Tiers
{
    public static void Group<TKey, TValue>(Dictionary<TKey, List<TValue>> by, TKey key, TValue value)
        where TKey : notnull
    {
        if (!by.TryGetValue(key, out var list)) by[key] = list = [];
        list.Add(value);
    }

    public static TierResult Result(
        List<FilePair> pairs, List<FileRecord> old, List<FileRecord> @new, HashSet<FileRecord> used) =>
        new(pairs, old.Where(o => !used.Contains(o)).ToList(), @new.Where(n => !used.Contains(n)).ToList());

    /// <summary>Pair records whose key occurs exactly once on each side; null keys never pair.</summary>
    public static TierResult PairUnique(
        List<FileRecord> old, List<FileRecord> @new, string tier, Func<FileRecord, object?> key)
    {
        var oldBy = new Dictionary<object, List<FileRecord>>();
        var newBy = new Dictionary<object, List<FileRecord>>();
        foreach (var (recs, by) in new[] { (old, oldBy), (@new, newBy) })
        {
            foreach (var r in recs)
            {
                if (key(r) is { } k) Group(by, k, r);
            }
        }
        var pairs = new List<FilePair>();
        var used = new HashSet<FileRecord>(ReferenceEqualityComparer.Instance);
        foreach (var o in old)
        {
            if (key(o) is { } k && oldBy[k].Count == 1 && newBy.TryGetValue(k, out var cands) && cands.Count == 1)
            {
                var n = cands[0];
                pairs.Add(new FilePair { Kind = "paired", Old = o, New = n, Tier = tier });
                used.Add(o);
                used.Add(n);
            }
        }
        return Result(pairs, old, @new, used);
    }
}

/// <summary>Walk two file trees top-down from paired roots, running the tier list per parent pair (port of matcher.py).</summary>
public static class Matcher
{
    /// <summary>Return the configured tier order.</summary>
    public static List<IMatchTier> DefaultTiers() => [new NameTier(), new TextHashTier(), new TypeFamilyTier()];

    /// <summary>Pair roots (tier root), then per paired parent run tiers over unpaired children.</summary>
    public static List<FilePair> MatchItem(Item old, Item @new, IReadOnlyList<IMatchTier>? tiers = null)
    {
        var tierList = tiers ?? DefaultTiers();
        var root = new FilePair { Kind = "paired", Old = old.Files[0], New = @new.Files[0], Tier = "root" };
        var output = new List<FilePair> { root };
        var queue = new Queue<FilePair>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            var oldRest = new List<FileRecord>(parent.Old!.Children);
            var newRest = new List<FileRecord>(parent.New!.Children);
            foreach (var tier in tierList)
            {
                var res = tier.Pair(oldRest, newRest);
                (oldRest, newRest) = (res.OldRest, res.NewRest);
                output.AddRange(res.Pairs);
                foreach (var p in res.Pairs) queue.Enqueue(p);
            }
            foreach (var rec in oldRest) OneSided(rec, "old", output);
            foreach (var rec in newRest) OneSided(rec, "new", output);
        }
        return output;
    }

    /// <summary>Append rec as no-candidate and its whole subtree as parent-unpaired, pre-order.</summary>
    private static void OneSided(FileRecord rec, string side, List<FilePair> output)
    {
        var stack = new Stack<(FileRecord Rec, string Reason)>();
        stack.Push((rec, "no-candidate"));
        while (stack.Count > 0)
        {
            var (cur, reason) = stack.Pop();
            output.Add(side == "old"
                ? new FilePair { Kind = "only-in-old", Old = cur, UnmatchedReason = reason }
                : new FilePair { Kind = "only-in-new", New = cur, UnmatchedReason = reason });
            for (var i = cur.Children.Count - 1; i >= 0; i--) stack.Push((cur.Children[i], "parent-unpaired"));
        }
    }
}
