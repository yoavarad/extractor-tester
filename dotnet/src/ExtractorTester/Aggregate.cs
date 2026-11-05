using ExtractorTester.Config;
using ExtractorTester.Decode;
using ExtractorTester.Loader;
using ExtractorTester.Models;
using ExtractorTester.Profiling;
using ExtractorTester.Render;

namespace ExtractorTester.Aggregate;

/// <summary>A count with the first <c>cap</c> examples seen (aggregate._Counter).</summary>
internal sealed class ExampleCounter
{
    private readonly long _cap;
    public long Count;
    public readonly List<Example> Examples = new();

    public ExampleCounter(long cap) => _cap = cap;

    public void Add(Example example, long amount = 1)
    {
        Count += amount;
        if (Examples.Count < _cap) Examples.Add(example);
    }

    public MetricResult Result(string key, long denominator) => new()
    {
        Key = key,
        Count = Count,
        Denominator = denominator,
        Rate = denominator != 0 ? (double)Count / denominator : 0.0,
        Examples = new List<Example>(Examples),
    };
}

/// <summary>Mutable per-family state (aggregate._Family).</summary>
internal sealed class FamilyState
{
    private readonly long _cap;
    public long Pairs;
    public readonly ExampleCounter OnlyOld;
    public readonly ExampleCounter OnlyNew;
    public readonly ExampleCounter Identical;
    public readonly ExampleCounter MimeChanged;
    public readonly ExampleCounter FamilyChanged;
    public readonly ExampleCounter DecodeErrors;
    public readonly Dictionary<string, ExampleCounter> Fields = new();
    public readonly Dictionary<string, ExampleCounter> Tiers = new();
    public readonly Dictionary<string, ExampleCounter> FileErrors = new();
    public readonly List<double> Edit = new();
    public readonly List<double> Jaccard = new();
    public readonly Dictionary<string, ExampleCounter> Relations = new();
    public readonly Dictionary<string, Lowest> RelationLow = new();
    public readonly Dictionary<string, long> Methods = new();
    public readonly Dictionary<string, ExampleCounter> Hints = new();
    public long LinesTotal;
    public long LinesCut;

    public FamilyState(long cap)
    {
        _cap = cap;
        OnlyOld = new(cap);
        OnlyNew = new(cap);
        Identical = new(cap);
        MimeChanged = new(cap);
        FamilyChanged = new(cap);
        DecodeErrors = new(cap);
    }

    public void Bump(Dictionary<string, ExampleCounter> table, string key, Example example)
    {
        if (!table.TryGetValue(key, out var c)) table[key] = c = new ExampleCounter(_cap);
        c.Add(example);
    }
}

/// <summary>Fold PairFacts and ItemResults into a Report with bounded memory (port of aggregate.py).</summary>
public sealed class Aggregator
{
    private readonly CompareConfig _config;
    private readonly long _cap;
    private readonly Dictionary<string, FamilyState> _families = new();
    private readonly Dictionary<string, long> _items = new();
    private readonly ExampleCounter _itemsOnlyOld;
    private readonly ExampleCounter _itemsOnlyNew;
    private readonly Dictionary<string, ExampleCounter> _itemFields = new();
    private readonly Dictionary<string, ExampleCounter> _errors = new();
    private readonly Dictionary<string, long> _warnings = new();
    private long _errorTotal;
    private readonly Dictionary<string, double> _stage = new();
    private readonly Dictionary<string, double> _textByFamily = new();
    private readonly Slowest _slowest = new();

    public Aggregator(CompareConfig config)
    {
        _config = config;
        _cap = config.ExamplesPerResult;
        _itemsOnlyOld = new(_cap);
        _itemsOnlyNew = new(_cap);
        foreach (var s in StageNames.All) _stage[s] = 0.0;
    }

    /// <summary>Add main-process time (aggregate, render) to a stage total.</summary>
    public void AddStage(string stage, double seconds) => _stage[stage] += seconds;

    /// <summary>Stage totals, text seconds per family (sorted), ten slowest items.</summary>
    public (Dictionary<string, double> Stage, Dictionary<string, double> TextByFamily,
        List<Dictionary<string, object?>> Slowest) Timings()
    {
        var text = new Dictionary<string, double>();
        foreach (var k in Sorted(_textByFamily.Keys)) text[k] = _textByFamily[k];
        return (new Dictionary<string, double>(_stage), text, _slowest.Items());
    }

    private static List<string> Sorted(IEnumerable<string> keys)
    {
        var list = keys.ToList();
        list.Sort(CodePointComparer.Instance);
        return list;
    }

    private int SnippetN => (int)Math.Min(_config.SnippetChars, int.MaxValue);

    private object? Cut(object? value)
    {
        var n = SnippetN;
        if (value is string s) return Text.Prefix(s, n);
        var text = PyText.Repr(value);
        return Text.ToCodePoints(text).Length <= n ? value : Text.Prefix(text, n);
    }

    private string CutStr(string value) => Text.Prefix(value, SnippetN);

    private void RecordError(ErrorRecord err, Example example)
    {
        var code = err.Code.ToString();
        if (err.Severity == "warning")
        {
            _warnings[code] = _warnings.GetValueOrDefault(code) + 1;
            return;
        }
        _errorTotal++;
        if (!_errors.TryGetValue(code, out var c)) _errors[code] = c = new ExampleCounter(_cap);
        c.Add(example);
    }

    private Example DiffExample(Example b, FieldDiff d) => b with
    {
        OldValue = Cut(d.OldValue),
        NewValue = Cut(d.NewValue),
        Note = d.OldType != d.NewType ? $"{d.OldType ?? "None"} -> {d.NewType ?? "None"}" : null,
    };

    /// <summary>Fold one file pair into its family's counters.</summary>
    public void AddPair(PairFacts pf)
    {
        if (!_families.TryGetValue(pf.Family, out var fam)) _families[pf.Family] = fam = new FamilyState(_cap);
        if (pf.Text is { } tx && Stages.TextStage.ContainsKey(tx.Method))
        {
            _textByFamily[pf.Family] = _textByFamily.GetValueOrDefault(pf.Family) + tx.Seconds;
        }
        var ex = new Example { ItemId = pf.ItemId, PathOld = pf.PathOld, PathNew = pf.PathNew };
        if (pf.Kind == "only-in-old")
        {
            fam.OnlyOld.Add(ex);
        }
        else if (pf.Kind == "only-in-new")
        {
            fam.OnlyNew.Add(ex);
        }
        else
        {
            fam.Pairs++;
            if (pf.Identical) fam.Identical.Add(ex);
            if (pf.MimeEqual == false)
            {
                fam.MimeChanged.Add(ex with { OldValue = Cut(pf.MimeOld), NewValue = Cut(pf.MimeNew) });
            }
            if (pf.FamilyEqual == false) fam.FamilyChanged.Add(ex);
            if (pf.Tier is not null) fam.Bump(fam.Tiers, pf.Tier, ex);
            if (pf.Text is not null) AddText(fam, pf, ex);
            foreach (var h in pf.Hints) fam.Bump(fam.Hints, h, ex);
            foreach (var d in pf.FieldDiffs)
            {
                if (d.Status != "same") fam.Bump(fam.Fields, $"{d.Path}:{d.Status}", DiffExample(ex, d));
            }
        }
        var nDec = (pf.DecodeErrorsOld ?? 0) + (pf.DecodeErrorsNew ?? 0);
        if (nDec != 0) fam.DecodeErrors.Add(ex, nDec);
        foreach (var err in pf.Errors)
        {
            if (err.Severity != "warning") fam.Bump(fam.FileErrors, err.Code.ToString(), ex);
            RecordError(err, ex with { Note = CutStr(err.Message) });
        }
    }

    private void AddText(FamilyState fam, PairFacts pf, Example ex)
    {
        var t = pf.Text!;
        fam.Methods[t.Method] = fam.Methods.GetValueOrDefault(t.Method) + 1;
        fam.LinesTotal += t.LinesTotal;
        fam.LinesCut += t.LinesCut;
        if (t.EditSimilarity is { } e) fam.Edit.Add(e);
        if (t.Jaccard is { } j) fam.Jaccard.Add(j);
        if (t.Relation is { } rel)
        {
            if (!fam.Relations.TryGetValue(rel, out var c)) fam.Relations[rel] = c = new ExampleCounter(0);
            c.Count++;
            if (t.EditSimilarity is { } sim)
            {
                if (!fam.RelationLow.TryGetValue(rel, out var low)) fam.RelationLow[rel] = low = new Lowest(_cap);
                low.Add(sim, ex with { Similarity = sim });
            }
        }
    }

    /// <summary>Fold one item result into the item-level counters.</summary>
    public void AddItem(ItemResult item)
    {
        _items[item.Status] = _items.GetValueOrDefault(item.Status) + 1;
        foreach (var (k, v) in item.StageSeconds)
        {
            if (_stage.ContainsKey(k)) _stage[k] += v;
        }
        _slowest.Add(item.ItemId, item.Seconds, item.Status);
        var ex = new Example { ItemId = item.ItemId };
        if (item.Status == "only-in-old") _itemsOnlyOld.Add(ex);
        else if (item.Status == "only-in-new") _itemsOnlyNew.Add(ex);
        if (item.Status == "compared")
        {
            foreach (var d in item.MetadataDiffs)
            {
                if (d.Status == "same") continue;
                var key = $"{d.Path}:{d.Status}";
                if (!_itemFields.TryGetValue(key, out var c)) _itemFields[key] = c = new ExampleCounter(_cap);
                c.Add(DiffExample(ex, d));
            }
        }
        foreach (var err in item.Errors) RecordError(err, ex with { Note = CutStr(err.Message) });
    }

    private static MetricResult Relation(string key, FamilyState f, long n)
    {
        var res = f.Relations[key].Result(key, n);
        res.Examples = f.RelationLow.TryGetValue(key, out var low) ? low.Examples() : new List<Example>();
        return res;
    }

    private static List<MetricResult> Results(Dictionary<string, ExampleCounter> table, long n) =>
        Sorted(table.Keys).Select(k => table[k].Result(k, n)).ToList();

    private FamilyAggregate Family(string name, FamilyState f)
    {
        var n = f.Pairs;
        var total = n + f.OnlyOld.Count + f.OnlyNew.Count;
        var cutPct = f.LinesTotal != 0 ? (double)(100 * f.LinesCut) / f.LinesTotal : 0.0;
        var methods = new Dictionary<string, long>();
        foreach (var k in Sorted(f.Methods.Keys)) methods[k] = f.Methods[k];
        return new FamilyAggregate
        {
            Family = name,
            Pairs = n,
            OnlyInOld = f.OnlyOld.Result("only-in-old", total),
            OnlyInNew = f.OnlyNew.Result("only-in-new", total),
            Identical = f.Identical.Result("identical", n),
            MimeChanged = f.MimeChanged.Result("mime-changed", n),
            FamilyChanged = f.FamilyChanged.Result("family-changed", n),
            Fields = Results(f.Fields, n),
            EditSimilarity = AggStats.Stats(f.Edit),
            Jaccard = AggStats.Stats(f.Jaccard),
            Relations = Sorted(f.Relations.Keys).Select(k => Relation(k, f, n)).ToList(),
            Methods = methods,
            LinesCutPct = cutPct,
            CutLinesWarning = cutPct > _config.CutLinesWarnPct,
            Tiers = Results(f.Tiers, n),
            Hints = Results(f.Hints, n),
            DecodeErrors = f.DecodeErrors.Result("decode-errors", n),
            FileErrors = Results(f.FileErrors, n),
        };
    }

    // model_dump(): unset float defaults (20, 98) stay ints in pydantic.
    private Dictionary<string, object?> ConfigDump()
    {
        var d = PyJson.Deserialize<Dictionary<string, object?>>(PyJson.Serialize(_config));
        if (!_config.CutLinesWarnPctSet) d["cut_lines_warn_pct"] = (long)_config.CutLinesWarnPct;
        if (!_config.ContainmentPctSet) d["containment_pct"] = (long)_config.ContainmentPct;
        return d;
    }

    /// <summary>Build the Report from everything added so far.</summary>
    public Report Finish(
        string toolVersion = "0",
        DateTimeOffset? startedAt = null,
        Dictionary<string, object?>? inputs = null,
        RunStats? runStats = null)
    {
        var nItems = _items.Values.Sum();
        var compared = _items.GetValueOrDefault("compared");
        return new Report
        {
            SchemaVersion = 1,
            ToolVersion = toolVersion,
            StartedAt = startedAt ?? DateTimeOffset.UnixEpoch,
            Inputs = inputs ?? new Dictionary<string, object?>(),
            Config = ConfigDump(),
            Totals = new Dictionary<string, long>
            {
                ["items"] = nItems,
                ["compared"] = compared,
                ["only_in_old"] = _items.GetValueOrDefault("only-in-old"),
                ["only_in_new"] = _items.GetValueOrDefault("only-in-new"),
                ["error"] = _items.GetValueOrDefault("error"),
                ["pairs"] = _families.Values.Sum(f => f.Pairs),
            },
            ItemsOnlyOld = _itemsOnlyOld.Result("items-only-in-old", nItems),
            ItemsOnlyNew = _itemsOnlyNew.Result("items-only-in-new", nItems),
            ItemFields = Results(_itemFields, compared),
            Families = Sorted(_families.Keys).Select(k => Family(k, _families[k])).ToList(),
            Errors = Sorted(_errors.Keys).Select(k => _errors[k].Result(k, _errorTotal)).ToList(),
            Warnings = Sorted(_warnings.Keys).Select(k => $"{k}: {_warnings[k]} occurrence(s)").ToList(),
            RunStats = runStats ?? new RunStats
            {
                WallSeconds = 0.0,
                Workers = 1,
                StageSeconds = new Dictionary<string, double>(),
                TextSecondsByFamily = new Dictionary<string, double>(),
                SlowestItems = new List<Dictionary<string, object?>>(),
                WorkersReplaced = 0,
            },
        };
    }
}
