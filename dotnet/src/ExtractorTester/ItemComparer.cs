using ExtractorTester.Config;
using ExtractorTester.Fields;
using ExtractorTester.Loader;
using ExtractorTester.Matching;
using ExtractorTester.Models;
using ExtractorTester.Profiling;
using ExtractorTester.Tree;

namespace ExtractorTester.Compare;

/// <summary>Compare one item end to end: load, build trees, match, record per-pair facts (port of compare/item.py).</summary>
public static class ItemComparer
{
    /// <summary>Compare one item's OLD and NEW files; never throws.</summary>
    /// <param name="match">Matcher override for tests (Python monkeypatches match_item); null uses <see cref="Matcher.MatchItem"/>.</param>
    public static (ItemResult Result, List<PairFacts> Facts) CompareItem(
        string oldPath, string newPath, CompareConfig config, Func<Item, Item, List<FilePair>>? match = null)
    {
        var t0 = Stages.Clock();
        var itemId = ItemLoader.Stem(Path.GetFileName(oldPath));
        var stages = new Dictionary<string, double>();
        Stages.Take("decode");
        Stages.Take("field_compare");
        List<PairFacts> facts;
        List<ErrorRecord> unattached;
        List<FieldDiff> meta;
        try
        {
            var oldData = ItemLoader.LoadItem(oldPath, null, config, "old");
            itemId = (string)oldData["itemId"]!;
            var newData = ItemLoader.LoadItem(newPath, itemId, config, "new");
            var t1 = Stages.Clock();
            stages["load"] = t1 - t0;
            var oldBuild = TreeBuilder.Build(oldData, config, "old", oldPath);
            var newBuild = TreeBuilder.Build(newData, config, "new", newPath);
            var t2 = Stages.Clock();
            stages["decode"] = Stages.Take("decode");
            stages["tree"] = Math.Max(0.0, t2 - t1 - stages["decode"]);
            var pairs = (match ?? ((o, n) => Matcher.MatchItem(o, n)))(oldBuild.Item, newBuild.Item);
            stages["match"] = Stages.Clock() - t2;
            facts = pairs.Select(p => Facts(itemId, p, pairs, oldBuild, newBuild, config)).ToList();
            stages["field_compare"] = Stages.Take("field_compare");
            foreach (var f in facts)
            {
                if (f.Text != null && Stages.TextStage.TryGetValue(f.Text.Method, out var key))
                {
                    stages[key] = stages.GetValueOrDefault(key) + f.Text.Seconds;
                }
            }
            var attached = new HashSet<ErrorRecord>(facts.SelectMany(f => f.Errors), ReferenceEqualityComparer.Instance);
            unattached = oldBuild.Errors.Concat(newBuild.Errors).Where(e => !attached.Contains(e)).ToList();
            meta = MetadataDiffs(oldBuild.Item, newBuild.Item, config);
        }
        catch (LoaderException e)
        {
            return (Failed(itemId, e.Code, e.Message, t0, stages), []);
        }
        catch (TreeException e)
        {
            return (Failed(itemId, e.Code, e.Message, t0, stages), []);
        }
        catch (Exception e) // any unexpected failure is an item-level tool-error
        {
            return (Failed(itemId, ErrorCode.ITEM_PROCESSING_FAILED, $"{PyTypeName(e)}: {e.Message}", t0, stages), []);
        }
        var result = new ItemResult
        {
            ItemId = itemId,
            Status = "compared",
            MetadataDiffs = meta,
            FilesIdentical = facts.Count(f => f.Identical),
            FilesChanged = facts.Count(f => f.Kind == "paired" && !f.Identical),
            FilesOnlyOld = facts.Count(f => f.Kind == "only-in-old"),
            FilesOnlyNew = facts.Count(f => f.Kind == "only-in-new"),
            Errors = unattached,
            Seconds = Stages.Clock() - t0,
            StageSeconds = stages,
        };
        return (result, facts);
    }

    /// <summary>Python exception class name for errors the port raises as Python built-ins.</summary>
    private static string PyTypeName(Exception e) => e switch
    {
        FlattenCollisionException or JsonIntDigitsException => "ValueError",
        JsonRecursionException => "RecursionError",
        OverflowException => "OverflowError",
        _ => e.GetType().Name,
    };

    private static ItemResult Failed(string itemId, ErrorCode code, string message, double t0, Dictionary<string, double> stages)
    {
        var err = new ErrorRecord { Code = code, Scope = "item", Severity = "error", ItemId = itemId, Message = message };
        return new ItemResult
        {
            ItemId = itemId,
            Status = "error",
            MetadataDiffs = [],
            FilesIdentical = 0,
            FilesChanged = 0,
            FilesOnlyOld = 0,
            FilesOnlyNew = 0,
            Errors = [err],
            Seconds = Stages.Clock() - t0,
            StageSeconds = stages,
        };
    }

    private static List<FieldDiff> MetadataDiffs(Item old, Item @new, CompareConfig config) =>
        FieldDiffer.Diff(FieldFlattener.Flatten(old.Metadata), FieldFlattener.Flatten(@new.Metadata), config)
            .Select(d => d with { Group = "item" })
            .ToList();

    private static List<ErrorRecord> FileErrors(BuildResult build, string side, string? path) =>
        path is null ? [] : build.Errors.Where(e => e.Side == side && e.FilePath == path).ToList();

    private static PairFacts Facts(
        string itemId, FilePair pair, List<FilePair> allPairs, BuildResult oldBuild, BuildResult newBuild, CompareConfig config)
    {
        var (old, @new) = (pair.Old, pair.New);
        var shown = old ?? @new ?? throw new InvalidOperationException("pair has no file");
        var errors = FileErrors(oldBuild, "old", old?.Path).Concat(FileErrors(newBuild, "new", @new?.Path)).ToList();
        var common = new PairFacts
        {
            ItemId = itemId,
            Kind = pair.Kind,
            PathOld = old?.Path,
            PathNew = @new?.Path,
            Family = shown.Family,
            Tier = pair.Tier,
            TieBreak = pair.TieBreak,
            MimeOld = old?.Mime,
            MimeNew = @new?.Mime,
            Errors = errors,
            DecodeErrorsOld = old?.DecodeErrors,
            DecodeErrorsNew = @new?.DecodeErrors,
            FieldDiffs = [],
            FieldsSame = [],
            Hints = [],
            ChildrenOld = old?.Children.Count,
            ChildrenNew = @new?.Children.Count,
            Identical = false,
        };
        if (old is null || @new is null) return common;

        var idsOld = old.Children.Select(c => c.FileId).ToHashSet(StringComparer.Ordinal);
        var idsNew = @new.Children.Select(c => c.FileId).ToHashSet(StringComparer.Ordinal);
        long onlyOld = allPairs.Count(p => p.Kind == "only-in-old" && p.Old is not null && idsOld.Contains(p.Old.FileId));
        long onlyNew = allPairs.Count(p => p.Kind == "only-in-new" && p.New is not null && idsNew.Contains(p.New.FileId));
        var tFields = Stages.Clock();
        var diffs = FieldDiffer.Diff(old.Fields, @new.Fields, config);
        Stages.Add("field_compare", Stages.Clock() - tFields);
        var changed = diffs.Where(d => d.Status != "same").ToList();
        var mimeEqual = old.Mime == @new.Mime;
        var badBase64 = errors.Any(e => e.Code == ErrorCode.INVALID_BASE64);
        var text = badBase64 ? null : TextComparer.CompareText(old.Text, @new.Text, config);
        return common with
        {
            MimeEqual = mimeEqual,
            FamilyEqual = old.Family == @new.Family,
            FieldDiffs = changed,
            FieldsSame = diffs.Where(d => d.Status == "same").Select(d => d.Path).ToList(),
            Text = text,
            ChildrenOnlyOld = onlyOld,
            ChildrenOnlyNew = onlyNew,
            Hints = Hints(text, changed, onlyOld, onlyNew),
            Identical = mimeEqual && changed.Count == 0 && onlyOld + onlyNew == 0 && text is { Relation: "equal" },
        };
    }

    private static bool Blank(string status, string side, object? value) =>
        status == $"only-in-{(side == "old" ? "new" : "old")}" || FieldDiffer.IsEmpty(value);

    private static List<string> Hints(TextFacts? text, List<FieldDiff> changed, long onlyOld, long onlyNew)
    {
        var hints = new List<string>();
        if (text?.Relation == "old-within-new") hints.Add("new-more-text");
        else if (text?.Relation == "new-within-old") hints.Add("new-less-text");
        if (changed.Any(d => Blank(d.Status, "old", d.OldValue) && !Blank(d.Status, "new", d.NewValue)))
            hints.Add("new-filled-field");
        if (changed.Any(d => Blank(d.Status, "new", d.NewValue) && !Blank(d.Status, "old", d.OldValue)))
            hints.Add("new-emptied-field");
        if (onlyNew != 0) hints.Add("new-more-subfiles");
        if (onlyOld != 0) hints.Add("new-fewer-subfiles");
        return hints;
    }
}
