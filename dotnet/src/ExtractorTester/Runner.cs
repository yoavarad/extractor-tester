using System.Diagnostics;
using ExtractorTester.Aggregate;
using ExtractorTester.Channel;
using ExtractorTester.Config;
using ExtractorTester.Loader;
using ExtractorTester.Models;
using ExtractorTester.Output;
using ExtractorTester.Pool;
using ExtractorTester.Render;
using Stages = ExtractorTester.Profiling.Stages;

namespace ExtractorTester.Runner;

/// <summary>Test seams for <see cref="CompareRunner.Run"/> (Python tests monkeypatch instead).</summary>
public sealed class RunOptions
{
    /// <summary>Passed to the worker pool.</summary>
    public PoolOptions? Pool { get; init; }

    /// <summary>Called right before the report is built (Aggregator.finish).</summary>
    public Action? BeforeFinish { get; init; }

    /// <summary>Cancelled to interrupt the run, like KeyboardInterrupt.</summary>
    public CancellationToken Interrupt { get; init; }
}

/// <summary>Top-level compare run: config, discovery, per-item compare, writers, report.json (runner.py).</summary>
public static class CompareRunner
{
    /// <summary>importlib.metadata.version("extractor-tester"); keep equal to pyproject.toml.</summary>
    public const string ToolVersion = "0.1.0";

    private const double ProgressSeconds = 0.1;

    // How long an interrupted run may take to stop its workers before the process gives up on it.
    private const int InterruptGraceMs = 10_000;

    private static void Progress(TextWriter err, int done, int total, double t0)
    {
        var elapsed = Stages.Clock() - t0;
        err.Write($"\rcompared {done}/{total} items, {PyText.FixedPoint(elapsed, 1)}s elapsed");
        err.Flush();
    }

    private static double Monotonic() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private static void Execute(
        string old, string @new, string configPath, string @out, bool overwrite, int workers,
        TextWriter err, RunOptions options)
    {
        var interrupt = options.Interrupt;
        var t0 = Stages.Clock();
        var startedAt = DateTimeOffset.UtcNow;
        var config = ConfigLoader.Load(configPath);
        var discovery = ItemLoader.Discover(old, @new);
        ReportWriter.PrepareOut(@out, overwrite);
        interrupt.ThrowIfCancellationRequested();
        var agg = new Aggregator(config);
        var workerStats = new WorkerStats();
        var total = discovery.Items.Count;
        var lastShown = 0.0;
        Progress(err, 0, total, t0);
        using (var pw = ReportWriter.PairsWriter(@out))
        using (var iw = ReportWriter.ItemsWriter(@out))
        {
            var done = 0;
            foreach (var (result, facts, _) in WorkerPool.CompareAll(discovery.Items, config, workers, workerStats, options.Pool))
            {
                interrupt.ThrowIfCancellationRequested();
                done++;
                var tAgg = Stages.Clock();
                foreach (var pf in facts)
                {
                    pw.Write(pf);
                    agg.AddPair(pf);
                }
                iw.Write(result);
                agg.AddItem(result);
                agg.AddStage("aggregate", Stages.Clock() - tAgg);
                if (done == total || Monotonic() - lastShown >= ProgressSeconds)
                {
                    Progress(err, done, total, t0);
                    lastShown = Monotonic();
                }
            }
        }
        interrupt.ThrowIfCancellationRequested();
        if (total > 0)
        {
            err.Write(Environment.NewLine); // Python's text-mode stderr writes \r\n on Windows
        }
        var tFin = Stages.Clock();
        var (stageSeconds, textByFamily, slowest) = agg.Timings();
        options.BeforeFinish?.Invoke();
        var report = agg.Finish(
            toolVersion: ToolVersion,
            startedAt: startedAt,
            inputs: new Dictionary<string, object?>
            {
                ["old"] = old,
                ["new"] = @new,
                ["mode"] = File.Exists(old) ? "file" : "folder",
                ["config"] = configPath,
            },
            runStats: new RunStats
            {
                WallSeconds = Stages.Clock() - t0,
                Workers = workers,
                StageSeconds = stageSeconds,
                TextSecondsByFamily = textByFamily,
                SlowestItems = slowest,
                WorkersReplaced = workerStats.Replaced,
                ProfileDir = null, // --profile is dropped (ADR-009)
            });
        report.Warnings.AddRange(discovery.Warnings.Select(w => $"{w.Code}: {w.Message}"));
        if (discovery.IgnoredCount > 0)
        {
            report.Warnings.Add($"{discovery.IgnoredCount} folder entries ignored (not top-level *.json files)");
        }
        agg.AddStage("aggregate", Stages.Clock() - tFin);
        interrupt.ThrowIfCancellationRequested();
        // Render time is only known after rendering: render once to measure it, then write the
        // final report.json (with the render stage) and render again so the HTML shows it too.
        var tRender = Stages.Clock();
        var html = Path.Combine(@out, "report.html");
        HtmlRenderer.Render(ReportWriter.WriteReport(@out, report), html);
        agg.AddStage("render", Stages.Clock() - tRender);
        report.RunStats.StageSeconds = agg.Timings().Stage;
        report.RunStats.WallSeconds = Stages.Clock() - t0;
        HtmlRenderer.Render(ReportWriter.WriteReport(@out, report), html);
    }

    /// <summary>Execute a compare run in worker processes and return the exit code.</summary>
    /// <remarks>
    /// workers defaults to the CPU count. The run executes on a big-stack thread: writing results
    /// nested as deep as the loader allows overflows a default 1 MB stack. Ctrl-C (or
    /// <see cref="RunOptions.Interrupt"/>) exits 130 without writing report.json.
    /// </remarks>
    public static int Run(
        string old, string @new, string configPath, string @out, bool overwrite = false, int? workers = null,
        TextWriter? stderr = null, RunOptions? options = null)
    {
        var err = stderr ?? Console.Error;
        options ??= new RunOptions();
        using var interrupt = CancellationTokenSource.CreateLinkedTokenSource(options.Interrupt);
        var run = new RunOptions { Pool = options.Pool, BeforeFinish = options.BeforeFinish, Interrupt = interrupt.Token };
        void OnCancel(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            interrupt.Cancel();
        }
        old = PyPath.Str(old);
        @new = PyPath.Str(@new);
        configPath = PyPath.Str(configPath);
        @out = PyPath.Str(@out);
        var n = workers ?? Environment.ProcessorCount;
        Exception? failure = null;
        var finished = new ManualResetEventSlim(false); // not disposed: an interrupted run may still set it
        var thread = new Thread(
            () =>
            {
                try
                {
                    Execute(old, @new, configPath, @out, overwrite, n, err, run);
                }
                catch (Exception e)
                {
                    failure = e;
                }
                finally
                {
                    finished.Set();
                }
            },
            WorkerChannel.BigStackBytes) { IsBackground = true, Name = "compare-run" };
        Console.CancelKeyPress += OnCancel;
        try
        {
            thread.Start();
            if (WaitHandle.WaitAny([interrupt.Token.WaitHandle, finished.WaitHandle]) == 0)
            {
                // Interrupted: let the run stop its workers, but do not wait on it forever.
                thread.Join(InterruptGraceMs);
                failure = new OperationCanceledException();
            }
        }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
        }
        return Exit(failure, err);
    }

    private static int Exit(Exception? failure, TextWriter err)
    {
        switch (failure)
        {
            case null:
                return 0;
            case OperationCanceledException:
                err.WriteLine(Environment.NewLine + "Interrupted");
                return 130;
            case ConfigException e:
                err.WriteLine($"{ErrorCode.INVALID_CONFIG}: {e.Message}");
                return 1;
            case LoaderException e:
                err.WriteLine($"{e.Code}: {e.Message}");
                return 1;
            case OutputError e:
                err.WriteLine($"{e.Code}: {e.Message}");
                return 1;
            default:
                err.WriteLine(failure.ToString());
                // The pool's InvalidOperationException mirrors Python's RuntimeError.
                var name = failure is InvalidOperationException ? "RuntimeError" : failure.GetType().Name;
                err.WriteLine($"{ErrorCode.INTERNAL_FAILURE}: Internal failure: {name}: {failure.Message}");
                return 1;
        }
    }
}

/// <summary>str(pathlib.Path(p)): the path as Python prints it.</summary>
public static class PyPath
{
    /// <summary>Drop "." and empty segments and trailing separators; Windows uses backslashes.</summary>
    public static string Str(string path)
    {
        var windows = OperatingSystem.IsWindows();
        var p = windows ? path.Replace('/', '\\') : path;
        var sep = windows ? '\\' : '/';
        var drive = "";
        if (windows && p.Length >= 2 && p[1] == ':')
        {
            drive = p[..2];
            p = p[2..];
        }
        var lead = p.Length - p.TrimStart(sep).Length;
        // POSIX keeps exactly two leading slashes; Windows keeps a UNC prefix (\\server\share).
        var root = lead == 0 ? "" : windows ? new string(sep, lead >= 2 ? 2 : 1) : lead == 2 ? "//" : "/";
        var parts = p.Split(sep).Where(s => s.Length > 0 && s != ".");
        var body = string.Join(sep, parts);
        var result = drive + root + body;
        return result.Length == 0 ? "." : result;
    }
}
