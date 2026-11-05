using System.CommandLine;
using ExtractorTester.Pool;

namespace ExtractorTester.Cli;

public static class CliApp
{
    public static RootCommand Build()
    {
        var old = new Argument<string>("OLD") { Description = "Old output: file or folder" };
        var @new = new Argument<string>("NEW") { Description = "New output: same kind as OLD" };
        var config = new Option<string>("--config") { Description = "Path to compare-config YAML", Required = true };
        var @out = new Option<string>("--out") { Description = "Output folder", DefaultValueFactory = _ => "./compare-out" };
        var overwrite = new Option<bool>("--overwrite") { Description = "Allow writing into a non-empty output folder" };
        var workers = new Option<int?>("--workers") { Description = "Worker processes (default: CPU count)" };

        var compare = new Command("compare", "Compare JSON outputs of two extraction pipelines.")
        {
            old, @new, config, @out, overwrite, workers,
        };
        compare.SetAction(_ =>
        {
            Console.WriteLine("not implemented yet");
            return 0;
        });

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
}
