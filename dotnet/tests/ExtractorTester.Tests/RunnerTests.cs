using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ExtractorTester.Cli;
using ExtractorTester.Loader;
using ExtractorTester.Pool;
using ExtractorTester.Render;
using ExtractorTester.Runner;

namespace ExtractorTester.Tests;

/// <summary>Port of tests/test_cli.py and tests/test_runner*.py (--profile cases dropped, ADR-009).</summary>
[Collection("Pool")]
public class RunnerTests : IDisposable
{
    private const string Config = "name_field: name\nmime_primary_field: mp\nmime_secondary_field: ms\n";

    private readonly string _tmp = Directory.CreateTempSubdirectory("et-run-").FullName;
    private readonly string _cfg;

    public RunnerTests()
    {
        _cfg = Path.Combine(_tmp, "c.yaml");
        File.WriteAllText(_cfg, Config);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmp, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ---- helpers (tests/runner_common.py) ----

    private static Dictionary<string, object?> F(string fid, string parent, string name, params (string, object?)[] extra)
    {
        var d = new Dictionary<string, object?>
        {
            ["fileId"] = fid, ["parentId"] = parent, ["name"] = name, ["mp"] = "application", ["ms"] = "pdf", ["text"] = "hello",
        };
        foreach (var (k, v) in extra)
        {
            d[k] = v;
        }
        return d;
    }

    private static List<Dictionary<string, object?>> ItemFiles(string itemId, string author = "x") =>
    [
        F(itemId, itemId, "root", ("rootId", itemId)),
        F($"{itemId}-a", itemId, "a.pdf", ("rootId", itemId), ("author", author)),
    ];

    private static (string Old, string New) Folders(string root)
    {
        var (old, @new) = (Path.Combine(root, "old"), Path.Combine(root, "new"));
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(@new);
        return (old, @new);
    }

    private static void ThreeItems(string old, string @new)
    {
        foreach (var i in new[] { "a", "b", "c" })
        {
            TestItems.WriteItem(old, i, ItemFiles(i));
            TestItems.WriteItem(@new, i, ItemFiles(i, author: "y"));
        }
    }

    /// <summary>Run the CLI in-process; return exit code plus combined output.</summary>
    private static (int Code, string Output) Invoke(params object[] args)
    {
        var sw = new StringWriter();
        var code = CliApp.Run(args.Select(a => a.ToString()!).ToArray(), sw, sw);
        return (code, sw.ToString());
    }

    private static List<JsonElement> ReadJsonl(string path) =>
        File.ReadAllLines(path).Where(l => l.Length > 0).Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList();

    private static JsonElement Report(string outDir) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "report.json"))).RootElement.Clone();

    private static Dictionary<string, string[]> ItemErrors(string outDir) =>
        ReadJsonl(Path.Combine(outDir, "items.jsonl")).ToDictionary(
            r => r.GetProperty("item_id").GetString()!,
            r => r.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("code").GetString()!).ToArray());

    private string RunWithWorkers(string cfg, string old, string @new, int workers, PoolOptions? pool = null)
    {
        var outDir = Path.Combine(_tmp, $"out{workers}");
        var sw = new StringWriter();
        var code = CompareRunner.Run(old, @new, cfg, outDir, workers: workers, stderr: sw, options: new RunOptions { Pool = pool });
        Assert.True(code == 0, sw.ToString());
        return outDir;
    }

    private static string ExePath() =>
        Path.ChangeExtension(typeof(CliApp).Assembly.Location, OperatingSystem.IsWindows() ? ".exe" : null);

    private static PoolOptions Env(string key, string value) =>
        new() { WorkerEnvironment = new Dictionary<string, string?> { [key] = value } };

    // ---- tests/test_cli.py ----

    [Fact]
    public void MissingInputExits1()
    {
        var (old, _) = Folders(_tmp);
        var (code, output) = Invoke("compare", old, Path.Combine(_tmp, "nope"), "--config", _cfg, "--out", Path.Combine(_tmp, "o"));
        Assert.Equal(1, code);
        Assert.Contains("INPUT_NOT_FOUND", output);
    }

    [Fact]
    public void UnknownOptionExits2()
    {
        var (old, @new) = Folders(_tmp);
        var (code, output) = Invoke("compare", old, @new, "--config", _cfg, "--bogus");
        Assert.Equal(2, code);
        Assert.StartsWith("Usage: extractor-tester compare [OPTIONS] OLD NEW", output);
    }

    // ---- tests/test_runner.py ----

    [Fact]
    public void FoldersOfThreeItemsWriteAllOutputsAndExit0()
    {
        var (old, @new) = Folders(_tmp);
        foreach (var i in new[] { "c", "a", "b" })
        {
            TestItems.WriteItem(old, i, ItemFiles(i));
            TestItems.WriteItem(@new, i, ItemFiles(i, author: i == "b" ? "y" : "x"));
        }
        var outDir = Path.Combine(_tmp, "out");
        var (code, output) = Invoke("compare", old, @new, "--config", _cfg, "--out", outDir);
        Assert.True(code == 0, output);
        var items = ReadJsonl(Path.Combine(outDir, "items.jsonl"));
        Assert.Equal(["a", "b", "c"], items.Select(i => i.GetProperty("item_id").GetString()));
        Assert.All(items, i => Assert.Equal("compared", i.GetProperty("status").GetString()));
        Assert.Equal(6, ReadJsonl(Path.Combine(outDir, "pairs.jsonl")).Count);
        var report = Report(outDir);
        var totals = report.GetProperty("totals");
        Assert.Equal(3, totals.GetProperty("items").GetInt32());
        Assert.Equal(3, totals.GetProperty("compared").GetInt32());
        Assert.Equal(6, totals.GetProperty("pairs").GetInt32());
        Assert.Equal(old, report.GetProperty("inputs").GetProperty("old").GetString());
        Assert.Equal("folder", report.GetProperty("inputs").GetProperty("mode").GetString());
        Assert.Equal(CompareRunner.ToolVersion, report.GetProperty("tool_version").GetString());
    }

    [Fact]
    public void MalformedItemIsReportedAndOthersCompared()
    {
        var (old, @new) = Folders(_tmp);
        foreach (var i in new[] { "a", "b", "c" })
        {
            TestItems.WriteItem(old, i, ItemFiles(i));
            TestItems.WriteItem(@new, i, ItemFiles(i));
        }
        File.WriteAllText(Path.Combine(@new, "b.json"), "{not json");
        var outDir = Path.Combine(_tmp, "out");
        var (code, output) = Invoke("compare", old, @new, "--config", _cfg, "--out", outDir);
        Assert.True(code == 0, output);
        var items = ReadJsonl(Path.Combine(outDir, "items.jsonl")).ToDictionary(i => i.GetProperty("item_id").GetString()!);
        Assert.Equal("error", items["b"].GetProperty("status").GetString());
        Assert.Equal("INVALID_JSON", items["b"].GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.Equal("compared", items["a"].GetProperty("status").GetString());
        Assert.Equal("compared", items["c"].GetProperty("status").GetString());
        Assert.Equal(["INVALID_JSON"], Report(outDir).GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("key").GetString()));
    }

    [Fact]
    public void OneSidedItemsAndIgnoredEntries()
    {
        var (old, @new) = Folders(_tmp);
        TestItems.WriteItem(old, "a", ItemFiles("a"));
        TestItems.WriteItem(old, "gone", ItemFiles("gone"));
        TestItems.WriteItem(@new, "a", ItemFiles("a"));
        TestItems.WriteItem(@new, "fresh", ItemFiles("fresh"));
        File.WriteAllText(Path.Combine(old, "notes.txt"), "x");
        Directory.CreateDirectory(Path.Combine(@new, "sub"));
        var outDir = Path.Combine(_tmp, "out");
        var (code, output) = Invoke("compare", old, @new, "--config", _cfg, "--out", outDir);
        Assert.True(code == 0, output);
        var status = ReadJsonl(Path.Combine(outDir, "items.jsonl")).ToDictionary(
            i => i.GetProperty("item_id").GetString()!, i => i.GetProperty("status").GetString());
        Assert.Equal(new Dictionary<string, string?> { ["a"] = "compared", ["fresh"] = "only-in-new", ["gone"] = "only-in-old" }, status);
        var report = Report(outDir);
        Assert.Equal(1, report.GetProperty("totals").GetProperty("only_in_old").GetInt32());
        Assert.Equal(1, report.GetProperty("totals").GetProperty("only_in_new").GetInt32());
        Assert.Contains(
            "2 folder entries ignored (not top-level *.json files)",
            report.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()));
    }

    [Fact]
    public void EmptyFoldersAreAValidRun()
    {
        var (old, @new) = Folders(_tmp);
        var outDir = Path.Combine(_tmp, "out");
        var (code, output) = Invoke("compare", old, @new, "--config", _cfg, "--out", outDir);
        Assert.True(code == 0, output);
        Assert.Contains(Report(outDir).GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("EMPTY_INPUT"));
        Assert.Equal("\rcompared 0/0 items, ", output[..21]);
    }

    [Fact]
    public void InvalidConfigExits1()
    {
        var (old, @new) = Folders(_tmp);
        var bad = Path.Combine(_tmp, "bad.yaml");
        File.WriteAllText(bad, "x: 1\n");
        var (code, output) = Invoke("compare", old, @new, "--config", bad, "--out", Path.Combine(_tmp, "o"));
        Assert.Equal(1, code);
        Assert.StartsWith($"INVALID_CONFIG: Invalid config {bad}: ", output);
    }

    [Fact]
    public void NonEmptyOutExits1AndOverwriteReusesIt()
    {
        var (old, @new) = Folders(_tmp);
        var outDir = Path.Combine(_tmp, "out");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "keep"), "x");
        var (code, output) = Invoke("compare", old, @new, "--config", _cfg, "--out", outDir);
        Assert.Equal(1, code);
        Assert.Equal($"OUTPUT_EXISTS: output folder {outDir} is not empty; use --overwrite to reuse it{Environment.NewLine}", output);
        (code, output) = Invoke("compare", old, @new, "--config", _cfg, "--out", outDir, "--overwrite");
        Assert.True(code == 0, output);
        Assert.True(File.Exists(Path.Combine(outDir, "report.json")));
        Assert.True(File.Exists(Path.Combine(outDir, "keep")));
    }

    [Fact]
    public void OutDefaultsToCompareOutInTheWorkingDirectory()
    {
        var cli = CliApp.Build();
        var parsed = cli.Parse(["compare", "a", "b", "--config", "c"]);
        var outOption = parsed.CommandResult.Command.Options.Single(o => o.Name == "--out");
        Assert.Equal("./compare-out", parsed.GetValue<string>(outOption.Name));
        Assert.Equal("compare-out", PyPath.Str("./compare-out"));
    }

    [Fact]
    public void FileModeReportsOneItem()
    {
        var (old, @new) = Folders(_tmp);
        var o = TestItems.WriteItem(old, "I", ItemFiles("I"));
        var n = TestItems.WriteItem(@new, "I", ItemFiles("I"));
        var outDir = Path.Combine(_tmp, "out");
        var (code, output) = Invoke("compare", o, n, "--config", _cfg, "--out", outDir);
        Assert.True(code == 0, output);
        var items = ReadJsonl(Path.Combine(outDir, "items.jsonl"));
        Assert.Equal([("I", "compared")], items.Select(i => (i.GetProperty("item_id").GetString(), i.GetProperty("status").GetString())));
        var report = Report(outDir);
        Assert.Equal(1, report.GetProperty("totals").GetProperty("items").GetInt32());
        Assert.Equal("file", report.GetProperty("inputs").GetProperty("mode").GetString());
    }

    [Fact]
    public void UnexpectedFailureIsInternalFailure()
    {
        var (old, @new) = Folders(_tmp);
        TestItems.WriteItem(old, "a", ItemFiles("a"));
        TestItems.WriteItem(@new, "a", ItemFiles("a"));
        var outDir = Path.Combine(_tmp, "out");
        var sw = new StringWriter();
        var code = CompareRunner.Run(old, @new, _cfg, outDir, stderr: sw, options: new RunOptions
        {
            BeforeFinish = () => throw new InvalidOperationException("kaput"),
        });
        Assert.Equal(1, code);
        Assert.Contains("INTERNAL_FAILURE: Internal failure: RuntimeError: kaput", sw.ToString());
        Assert.False(File.Exists(Path.Combine(outDir, "report.json")));
        Assert.Single(ReadJsonl(Path.Combine(outDir, "items.jsonl")));
    }

    [Fact]
    public void WorkersDyingBeforeTakingItemsEndTheRunWithInternalFailure()
    {
        var (old, @new) = Folders(_tmp);
        ThreeItems(old, @new);
        var outDir = Path.Combine(_tmp, "out");
        var sw = new StringWriter();
        var code = CompareRunner.Run(old, @new, _cfg, outDir, workers: 1, stderr: sw, options: new RunOptions
        {
            Pool = Env(WorkerPool.DieBeforeTakeEnv, "1"),
        });
        Assert.Equal(1, code);
        Assert.Contains(
            "INTERNAL_FAILURE: Internal failure: RuntimeError: worker processes keep failing before taking any item",
            sw.ToString());
    }

    private string HtmlRun(string itemId, string name, string text)
    {
        var (old, @new) = Folders(_tmp);
        foreach (var d in new[] { old, @new })
        {
            var files = ItemFiles("a");
            files[1]["name"] = name;
            files[1]["text"] = text;
            TestItems.WriteItem(d, itemId, files);
        }
        var outDir = Path.Combine(_tmp, "out");
        var (code, output) = Invoke("compare", old, @new, "--config", _cfg, "--out", outDir);
        Assert.True(code == 0, output);
        return outDir;
    }

    [Fact]
    public void ReportHtmlWrittenAndRerenderIsByteIdentical()
    {
        var outDir = HtmlRun("a", "a.pdf", "hello");
        var htmlPath = Path.Combine(outDir, "report.html");
        var html = File.ReadAllBytes(htmlPath);
        File.Delete(htmlPath);
        HtmlRenderer.Render(Path.Combine(outDir, "report.json"), htmlPath);
        Assert.Equal(html, File.ReadAllBytes(htmlPath));
    }

    [Fact]
    public void ScriptTagsInInputAreEscaped()
    {
        var (old, @new) = Folders(_tmp);
        const string evil = "<script>alert(1)</script>";
        foreach (var (d, author) in new[] { (old, evil), (@new, evil + "2") })
        {
            var files = ItemFiles("a", author: author);
            files[1]["name"] = evil;
            files[1]["text"] = evil;
            TestItems.WriteItem(d, "a", files);
        }
        var outDir = Path.Combine(_tmp, "out");
        var (code, output) = Invoke("compare", old, @new, "--config", _cfg, "--out", outDir);
        Assert.True(code == 0, output);
        var html = File.ReadAllText(Path.Combine(outDir, "report.html"));
        Assert.DoesNotContain(evil, html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
    }

    [Fact]
    public void PathShapedItemIdCreatesNothingOutsideOut()
    {
        var box = Path.Combine(_tmp, "box");
        Directory.CreateDirectory(box);
        var (old, @new) = Folders(box);
        foreach (var d in new[] { old, @new })
        {
            TestItems.WriteItem(d, "a", ItemFiles("../x"));
        }
        var before = Directory.EnumerateFileSystemEntries(_tmp, "*", SearchOption.AllDirectories).ToHashSet();
        var outDir = Path.Combine(box, "out");
        var (code, _) = Invoke("compare", old, @new, "--config", _cfg, "--out", outDir);
        Assert.Contains(code, new[] { 0, 1 });
        var created = Directory.EnumerateFileSystemEntries(_tmp, "*", SearchOption.AllDirectories).Where(p => !before.Contains(p));
        Assert.All(created, p => Assert.True(p == outDir || p.StartsWith(outDir + Path.DirectorySeparatorChar), p));
        Assert.False(File.Exists(Path.Combine(box, "x")));
    }

    // ---- tests/test_runner_faults.py ----

    [Fact]
    public void SlowItemTimesOutAndRunStatsRecordIt()
    {
        var cfg = Path.Combine(_tmp, "slow.yaml");
        File.WriteAllText(cfg, Config + "item_timeout_seconds: 8\nfull_compare_max_chars: 2000000\n");
        var (old, @new) = Folders(_tmp);
        ThreeItems(old, @new);
        var rng = new Random(0);
        const string alphabet = "abcdefghij \n";
        foreach (var d in new[] { old, @new })
        {
            var files = ItemFiles("slow");
            // As PoolTests: far past the 8 s timeout, which cold workers need for a, b, c.
            files[1]["text"] = new string(Enumerable.Range(0, 1_000_000).Select(_ => alphabet[rng.Next(alphabet.Length)]).ToArray());
            TestItems.WriteItem(d, "slow", files);
        }
        var outDir = RunWithWorkers(cfg, old, @new, 2);
        var errors = ItemErrors(outDir);
        Assert.Equal(["ITEM_TIMEOUT"], errors["slow"]);
        Assert.All(new[] { "a", "b", "c" }, i => Assert.Empty(errors[i]));
        var rs = Report(outDir).GetProperty("run_stats");
        Assert.Equal(1, rs.GetProperty("workers_replaced").GetInt32());
        var stages = rs.GetProperty("stage_seconds").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble());
        Assert.Superset(new HashSet<string> { "load", "decode", "tree", "match", "aggregate", "render" }, stages.Keys.ToHashSet());
        Assert.All(stages.Values, v => Assert.True(v >= 0));
        var slow = rs.GetProperty("slowest_items").EnumerateArray().ToList();
        var seconds = slow.Select(x => x.GetProperty("seconds").GetDouble()).ToList();
        Assert.Equal(seconds.OrderByDescending(s => s), seconds);
        Assert.Contains("slow", slow.Select(x => x.GetProperty("item_id").GetString()));
    }

    [Fact]
    public void CrashedWorkerMarksOnlyItsItem()
    {
        var (old, @new) = Folders(_tmp);
        ThreeItems(old, @new);
        var outDir = RunWithWorkers(_cfg, old, @new, 2, Env(WorkerPool.CrashItemEnv, "b"));
        var errors = ItemErrors(outDir);
        Assert.Empty(errors["a"]);
        Assert.Equal(["WORKER_CRASHED"], errors["b"]);
        Assert.Empty(errors["c"]);
        Assert.Equal(1, Report(outDir).GetProperty("run_stats").GetProperty("workers_replaced").GetInt32());
    }

    // ---- tests/test_runner_workers.py ----

    private static readonly HashSet<string> TimingKeys = ["seconds", "stage_seconds"];

    private static string Untimed(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", v.EnumerateObject().Where(p => !TimingKeys.Contains(p.Name))
            .Select(p => JsonSerializer.Serialize(p.Name) + ":" + Untimed(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", v.EnumerateArray().Select(Untimed)) + "]",
        _ => v.GetRawText(),
    };

    [Fact]
    public void OutputsIdenticalForOneAndFourWorkers()
    {
        var (old, @new) = Folders(_tmp);
        for (var n = 0; n < 12; n++)
        {
            var i = $"item{n:D2}";
            TestItems.WriteItem(old, i, ItemFiles(i));
            TestItems.WriteItem(@new, i, ItemFiles(i, author: n % 3 == 0 ? "y" : "x"));
        }
        File.WriteAllText(Path.Combine(@new, "item05.json"), "{not json");
        TestItems.WriteItem(old, "gone", ItemFiles("gone"));
        var o1 = RunWithWorkers(_cfg, old, @new, 1);
        var o4 = RunWithWorkers(_cfg, old, @new, 4);
        foreach (var name in new[] { "pairs.jsonl", "items.jsonl" })
        {
            Assert.Equal(
                ReadJsonl(Path.Combine(o1, name)).Select(Untimed),
                ReadJsonl(Path.Combine(o4, name)).Select(Untimed));
        }
        static string Stable(string outDir)
        {
            var report = Report(outDir);
            return string.Join(",", report.EnumerateObject().Where(p => p.Name is not ("started_at" or "run_stats"))
                .Select(p => p.Name + ":" + p.Value.GetRawText()));
        }
        Assert.Equal(Stable(o1), Stable(o4));
        Assert.Equal(4, Report(o4).GetProperty("run_stats").GetProperty("workers").GetInt32());
    }

    [Fact]
    public void WorkersZeroExits2()
    {
        var (old, @new) = Folders(_tmp);
        var (code, output) = Invoke("compare", old, @new, "--config", _cfg, "--out", Path.Combine(_tmp, "o"), "--workers", 0);
        Assert.Equal(2, code);
        Assert.Contains("Error: Invalid value for '--workers': 0 is not in the range x>=1.", output);
        Assert.False(Directory.Exists(Path.Combine(_tmp, "o")));
    }

    [Fact]
    public void WorkersDefaultToCpuCountAndRunStatsHasPythonKeys()
    {
        var (old, @new) = Folders(_tmp);
        TestItems.WriteItem(old, "a", ItemFiles("a"));
        TestItems.WriteItem(@new, "a", ItemFiles("a"));
        var outDir = Path.Combine(_tmp, "out");
        var (code, output) = Invoke("compare", old, @new, "--config", _cfg, "--out", outDir);
        Assert.True(code == 0, output);
        Assert.False(Directory.Exists(Path.Combine(outDir, "profile")));
        var rs = Report(outDir).GetProperty("run_stats");
        Assert.Equal(Environment.ProcessorCount, rs.GetProperty("workers").GetInt32());
        Assert.Equal(
            ["wall_seconds", "workers", "stage_seconds", "text_seconds_by_family", "slowest_items", "workers_replaced", "profile_dir"],
            rs.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.Null, rs.GetProperty("profile_dir").ValueKind);
        Assert.Equal(
            ["load", "decode", "tree", "match", "field_compare", "text_fast", "text_full", "text_buffered", "aggregate", "render"],
            rs.GetProperty("stage_seconds").EnumerateObject().Select(p => p.Name));
    }

    // ---- real process: tests/test_runner_progress.py and the big-stack main path ----

    private static ProcessStartInfo ExeStart(params string[] args)
    {
        var psi = new ProcessStartInfo(ExePath()) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        return psi;
    }

    private static (int Code, string Stderr) RunExe(params string[] args)
    {
        using var proc = Process.Start(ExeStart(args))!;
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEnd();
        Assert.True(proc.WaitForExit(300_000), "exe did not finish");
        _ = stdout.Result;
        return (proc.ExitCode, stderr);
    }

    [Fact]
    public void ProgressLineOnStderr()
    {
        var (old, @new) = Folders(_tmp);
        for (var i = 0; i < 3; i++)
        {
            foreach (var d in new[] { old, @new })
            {
                TestItems.WriteItem(d, $"i{i}", ItemFiles($"i{i}"));
            }
        }
        var (code, stderr) = RunExe("compare", old, @new, "--config", _cfg, "--out", Path.Combine(_tmp, "out"), "--workers", "1");
        Assert.True(code == 0, stderr);
        Assert.Contains("3/3 items", stderr);
        Assert.Matches(@"^\rcompared 0/3 items, \d+\.\ds elapsed(\rcompared [1-3]/3 items, \d+\.\ds elapsed)+" + Environment.NewLine + "$", stderr);
    }

    [Fact]
    public void ProcessExitCode2OnUsageError()
    {
        var (code, stderr) = RunExe("compare", "a", "b");
        Assert.Equal(2, code);
        Assert.Contains("Usage: extractor-tester compare [OPTIONS] OLD NEW", stderr);
    }

    /// <summary>Results nested as deep as the loader allows go through the real exe's main path:
    /// writing them on a default 1 MB stack would overflow and kill the process.</summary>
    [Fact]
    public void LoaderLimitNestingRunsThroughTheExe()
    {
        var (old, @new) = Folders(_tmp);
        var depth = PyJsonDecoder.MaxDepth - 1; // the item object is one level
        foreach (var (dir, leaf) in new[] { (old, "1"), (@new, "2") })
        {
            var path = TestItems.WriteItem(dir, "deep", ItemFiles("deep"));
            var text = File.ReadAllText(path);
            File.WriteAllText(path, text[..^1] + ", \"deep\": " + new string('[', depth) + leaf + new string(']', depth) + "}");
        }
        var outDir = Path.Combine(_tmp, "out");
        var (code, stderr) = RunExe("compare", old, @new, "--config", _cfg, "--out", outDir, "--workers", "1");
        Assert.True(code == 0, stderr);
        var line = Assert.Single(File.ReadAllLines(Path.Combine(outDir, "items.jsonl")));
        Assert.StartsWith("{\"item_id\":\"deep\"", line);
        Assert.Contains(new string('[', 1000), line); // the deep value itself was written
        Assert.True(File.Exists(Path.Combine(outDir, "report.json")));
        Assert.True(File.Exists(Path.Combine(outDir, "report.html")));
    }

    [Fact]
    public void CtrlCExits130WithoutReport()
    {
        var (old, @new) = Folders(_tmp);
        var big = string.Concat(Enumerable.Repeat("word ", 100_000));
        for (var i = 0; i < 60; i++)
        {
            foreach (var (d, text) in new[] { (old, big + "a"), (@new, big + "b") })
            {
                var files = ItemFiles($"i{i}");
                files[1]["text"] = text;
                TestItems.WriteItem(d, $"i{i}", files);
            }
        }
        var outDir = Path.Combine(_tmp, "out");
        string[] args = ["compare", old, @new, "--config", _cfg, "--out", outDir, "--workers", "1"];
        var (code, err) = OperatingSystem.IsWindows()
            ? Interrupt.RunWindows(ExePath(), args, Path.Combine(_tmp, "stderr.txt"))
            : Interrupt.RunPosix(ExeStart(args));
        Assert.True(code == 130, err);
        Assert.EndsWith(Environment.NewLine + "Interrupted" +Environment.NewLine, err);
        Assert.False(File.Exists(Path.Combine(outDir, "report.json")));
        Assert.False(File.Exists(Path.Combine(outDir, "report.html")));
        Assert.DoesNotContain("Exception", err);
    }
}
