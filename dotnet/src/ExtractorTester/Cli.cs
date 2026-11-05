using System.CommandLine;
using ExtractorTester.Pool;
using ExtractorTester.Runner;

namespace ExtractorTester.Cli;

public static class CliApp
{
    /// <summary>Program name in usage lines (the Python console script).</summary>
    public const string ProgramName = "extractor-tester";

    /// <summary>Exit code of a usage error (click's UsageError).</summary>
    public const int UsageErrorExit = 2;

    /// <summary>The command tree; the compare action writes its messages to <paramref name="stderr"/>.</summary>
    public static RootCommand Build(TextWriter? stderr = null)
    {
        var old = new Argument<string>("OLD") { Description = "Old output: file or folder" };
        var @new = new Argument<string>("NEW") { Description = "New output: same kind as OLD" };
        var config = new Option<string>("--config") { Description = "Path to compare-config YAML", Required = true };
        var @out = new Option<string>("--out") { Description = "Output folder", DefaultValueFactory = _ => "./compare-out" };
        var overwrite = new Option<bool>("--overwrite") { Description = "Allow writing into a non-empty output folder" };
        var workers = new Option<int?>("--workers") { Description = "Worker processes (default: CPU count)" };
        workers.Validators.Add(r =>
        {
            if (r.GetValueOrDefault<int?>() is { } n && n < 1)
            {
                r.AddError($"Invalid value for '--workers': {n} is not in the range x>=1.");
            }
        });

        var compare = new Command("compare", "Compare the outputs of the old and new pipelines and write the report.")
        {
            old, @new, config, @out, overwrite, workers,
        };
        compare.SetAction(r => CompareRunner.Run(
            r.GetValue(old)!,
            r.GetValue(@new)!,
            r.GetValue(config)!,
            r.GetValue(@out)!,
            r.GetValue(overwrite),
            r.GetValue(workers),
            stderr));

        // Internal: the pool re-launches this exe as a worker that talks over stdin/stdout.
        var workerConfig = new Argument<string>("CONFIG_JSON");
        var worker = new Command(WorkerPool.WorkerCommandName, "Run one compare worker (internal).")
        {
            workerConfig,
        };
        worker.Hidden = true;
        worker.SetAction(r => WorkerPool.RunWorker(r.GetValue(workerConfig)!));

        return new RootCommand("Compare JSON outputs of two extraction pipelines.") { compare, worker };
    }

    /// <summary>Parse and run; usage errors print click-style usage plus the error and exit 2.</summary>
    public static int Run(string[] args, TextWriter? stdout = null, TextWriter? stderr = null)
    {
        var err = stderr ?? Console.Error;
        var parsed = Build(err).Parse(args);
        if (parsed.Errors.Count > 0)
        {
            var command = parsed.CommandResult.Command;
            var (usage, help) = command is RootCommand
                ? ($"{ProgramName} [OPTIONS] COMMAND [ARGS]...", $"{ProgramName} --help")
                : ($"{ProgramName} {command.Name} [OPTIONS] OLD NEW", $"{ProgramName} {command.Name} --help");
            err.WriteLine($"Usage: {usage}");
            err.WriteLine($"Try '{help}' for help.");
            err.WriteLine();
            err.WriteLine($"Error: {parsed.Errors[0].Message}");
            return UsageErrorExit;
        }
        // The runner handles Ctrl-C itself (exit 130), so the parser installs no handler.
        return parsed.Invoke(new InvocationConfiguration
        {
            Output = stdout ?? Console.Out,
            Error = err,
            ProcessTerminationTimeout = null,
        });
    }
}
