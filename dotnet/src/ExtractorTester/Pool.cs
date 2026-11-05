using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ExtractorTester.Channel;
using ExtractorTester.Compare;
using ExtractorTester.Config;
using ExtractorTester.Loader;
using ExtractorTester.Models;

namespace ExtractorTester.Pool;

/// <summary>Counters CompareAll fills in while it runs.</summary>
public sealed class WorkerStats
{
    public int Replaced { get; set; }
}

/// <summary>Knobs tests change (Python monkeypatches module globals and os.environ instead).</summary>
public sealed class PoolOptions
{
    public ChannelHooks Hooks { get; init; } = new();

    /// <summary>A worker that has not signalled ready this long after spawn is killed and replaced.</summary>
    public double StartupTimeoutSeconds { get; init; } = WorkerPool.StartupTimeoutSeconds;

    /// <summary>Extra environment for worker processes (a null value removes the variable).</summary>
    public IReadOnlyDictionary<string, string?>? WorkerEnvironment { get; init; }
}

/// <summary>Compare items in worker processes with per-item timeout and crash recovery (port of pool.py).</summary>
public static class WorkerPool
{
    public const int WindowPerWorker = 32;
    public const double StartupTimeoutSeconds = 120.0;

    /// <summary>Hidden CLI subcommand the exe re-launches itself with.</summary>
    public const string WorkerCommandName = "worker";

    // Test hooks: a worker given the item with this id exits abruptly (crash) or sends a
    // corrupt payload and exits (killed mid-write); the worker that first creates the file at
    // the hang path hangs during startup; with DIE_BEFORE_TAKE set, every worker exits on
    // receiving its first item, before taking it.
    public const string CrashItemEnv = "EXTRACTOR_TESTER_TEST_CRASH_ITEM";
    public const string GarbageItemEnv = "EXTRACTOR_TESTER_TEST_GARBAGE_ITEM";
    public const string HangStartupEnv = "EXTRACTOR_TESTER_TEST_HANG_STARTUP";
    public const string DieBeforeTakeEnv = "EXTRACTOR_TESTER_TEST_DIE_BEFORE_TAKE";

    private const double PollSeconds = 0.5;

    // Consecutive workers on one slot that die or hang before taking any item; at this count
    // the run gives up (a broken environment) instead of respawning forever.
    private const int MaxStartupFailures = 3;

    private sealed record ItemMessage(int Index, DiscoveredItem Item);

    private sealed record WorkerConfig(CompareConfig Config, Dictionary<string, string> FamilyMap);

    private static ItemResult OneSided(string itemId, string status) => new()
    {
        ItemId = itemId,
        Status = status,
        MetadataDiffs = [],
        FilesIdentical = 0,
        FilesChanged = 0,
        FilesOnlyOld = 0,
        FilesOnlyNew = 0,
        Errors = [],
        Seconds = 0.0,
        StageSeconds = [],
    };

    /// <summary>Compare one discovered item; the single per-item call a parallel runner can swap out.</summary>
    public static (ItemResult Result, List<PairFacts> Facts) CompareOne(DiscoveredItem item, CompareConfig config)
    {
        if (item.NewPath is null)
        {
            return (OneSided(item.ItemId, "only-in-old"), []);
        }
        if (item.OldPath is null)
        {
            return (OneSided(item.ItemId, "only-in-new"), []);
        }
        return ItemComparer.CompareItem(item.OldPath, item.NewPath, config);
    }

    private static ItemResult ErrorResult(string itemId, ErrorCode code, string message, double seconds)
    {
        var err = new ErrorRecord { Code = code, Scope = "item", Severity = "error", ItemId = itemId, Message = message };
        return OneSided(itemId, "error") with { Errors = [err], Seconds = seconds };
    }

    /// <summary>Entry of the hidden worker subcommand; returns the process exit code.</summary>
    /// <remarks>The loop runs on a big-stack thread: deeply nested item values recurse far past
    /// the default 1 MB main-thread stack, and a .NET stack overflow cannot be caught.</remarks>
    public static int RunWorker(string configPath)
    {
        var code = 0;
        var t = new Thread(() => code = WorkerMain(configPath), WorkerChannel.BigStackBytes) { Name = "worker-main" };
        t.Start();
        t.Join();
        return code;
    }

    private static void StartupHook()
    {
        var path = Environment.GetEnvironmentVariable(HangStartupEnv);
        if (path is null)
        {
            return;
        }
        try
        {
            File.Open(path, FileMode.CreateNew).Dispose();
        }
        catch (IOException)
        {
            return;
        }
        Thread.Sleep(Timeout.Infinite);
    }

    /// <summary>Worker loop on stdin/stdout: send ready (index -1), then take one (index, item) at a
    /// time until null, ack it (index, null, null, pid) and send back (index, result, facts, pid).</summary>
    private static int WorkerMain(string configPath)
    {
        StartupHook();
        // Ctrl-C reaches the whole console group; the main process alone stops the workers.
        Console.CancelKeyPress += (_, e) => e.Cancel = true;
        var pid = Environment.ProcessId;
        var crashItem = Environment.GetEnvironmentVariable(CrashItemEnv);
        var garbageItem = Environment.GetEnvironmentVariable(GarbageItemEnv);
        var wc = PyJson.Deserialize<WorkerConfig>(File.ReadAllText(configPath, Encoding.UTF8));
        var config = wc.Config;
        config.SetFamilyMap(wc.FamilyMap);
        using var inbox = Console.OpenStandardInput();
        using var conn = Console.OpenStandardOutput();
        WorkerChannel.WriteFrame(conn, WorkerChannel.Encode(new WorkerMessage(-1, null, null, pid)));
        while (WorkerChannel.ReadFrame(inbox) is { } payload
            && JsonSerializer.Deserialize<ItemMessage?>(payload, PyJson.Options) is { } msg)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DieBeforeTakeEnv)))
            {
                Environment.Exit(1);
            }
            WorkerChannel.WriteFrame(conn, WorkerChannel.Encode(new WorkerMessage(msg.Index, null, null, pid)));
            if (msg.Item.ItemId == crashItem)
            {
                Environment.Exit(1);
            }
            if (msg.Item.ItemId == garbageItem)
            {
                WorkerChannel.WriteFrame(conn, [0x80, 0x05, .. "not a pickle"u8]);
                Environment.Exit(1);
            }
            var (result, facts) = CompareOne(msg.Item, config);
            WorkerChannel.WriteFrame(conn, WorkerChannel.Encode(new WorkerMessage(msg.Index, result, facts, pid)));
        }
        return 0;
    }

    /// <summary>The command that runs this assembly's worker subcommand.</summary>
    internal static (string FileName, List<string> Prefix) WorkerCommand()
    {
        var asm = typeof(WorkerPool).Assembly;
        var processPath = Environment.ProcessPath;
        if (Assembly.GetEntryAssembly() == asm && processPath is not null)
        {
            // The exe itself, or `dotnet ExtractorTester.dll`.
            return Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? (processPath, [asm.Location])
                : (processPath, []);
        }
        // Hosted by another program (the test runner): this assembly's apphost, else the dotnet muxer.
        var appHost = Path.ChangeExtension(asm.Location, OperatingSystem.IsWindows() ? ".exe" : null);
        if (File.Exists(appHost))
        {
            return (appHost, []);
        }
        var muxer = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? Path.GetFullPath(Path.Combine(
            RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        return (muxer, [asm.Location]);
    }

    private static void Kill(Process proc)
    {
        try
        {
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            // already gone
        }
        proc.WaitForExit();
    }

    /// <summary>Compare items in worker processes; yield (result, facts, worker pid) in input order.</summary>
    /// <remarks>
    /// Each worker has its own pipes and holds at most one item. Dispatch runs at most
    /// workers x WindowPerWorker items ahead of the oldest unfinished item, which bounds the
    /// buffered results. A worker that dies, sends a corrupt message or holds an item past
    /// item_timeout_seconds is killed (with its process tree) and replaced with fresh pipes; only
    /// its item gets WORKER_CRASHED or ITEM_TIMEOUT. A result already received always wins over a
    /// timeout. A worker that dies before taking its item gives the item back; a worker not ready
    /// within the startup timeout is replaced; MaxStartupFailures such workers in a row on one
    /// slot end the run with InvalidOperationException (Python RuntimeError). Replacements are
    /// counted in stats.
    /// </remarks>
    public static IEnumerable<(ItemResult Result, List<PairFacts> Facts, int Pid)> CompareAll(
        IReadOnlyList<DiscoveredItem> items,
        CompareConfig config,
        int workers,
        WorkerStats? stats = null,
        PoolOptions? options = null)
    {
        if (workers < 1)
        {
            throw new ArgumentException($"workers must be >= 1, got {workers}", nameof(workers));
        }
        stats ??= new WorkerStats();
        options ??= new PoolOptions();
        var hooks = options.Hooks;
        using var wake = new ManualResetEventSlim(false);
        var slots = new List<Slot>();
        var (fileName, prefix) = WorkerCommand();
        var configPath = Path.Combine(Path.GetTempPath(), $"extractor-tester-worker-{Guid.NewGuid():N}.json");
        File.WriteAllText(
            configPath,
            PyJson.Serialize(new Dictionary<string, object?> { ["config"] = config, ["family_map"] = config.FamilyMap }),
            new UTF8Encoding(false));

        void Start(int i, int startupFailures = 0)
        {
            var psi = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
            };
            foreach (var a in prefix)
            {
                psi.ArgumentList.Add(a);
            }
            psi.ArgumentList.Add(WorkerCommandName);
            psi.ArgumentList.Add(configPath);
            foreach (var (k, v) in options.WorkerEnvironment ?? new Dictionary<string, string?>())
            {
                psi.Environment[k] = v;
            }
            var proc = Process.Start(psi) ?? throw new InvalidOperationException("worker process did not start");
            var slot = WorkerChannel.OpenSlot(
                proc, proc.StandardInput.BaseStream, proc.StandardOutput.BaseStream, startupFailures, wake, hooks);
            if (i < slots.Count)
            {
                slots[i] = slot;
            }
            else
            {
                slots.Add(slot);
            }
        }

        void Replace(int i, int startupFailures = 0)
        {
            var old = slots[i];
            Kill(old.Proc); // the pipe now reaches EOF, which ends the old reader thread
            old.Close();
            stats.Replaced++;
            Start(i, startupFailures);
            old.JoinReader(hooks.ReaderJoinMilliseconds); // bounded: a stuck reader must not hang the pool
            hooks.BeforeDispose?.Invoke(old);
            old.Proc.Dispose(); // after Start: if it throws, the finally block still sees a usable (exited) slot
        }

        var held = new Dictionary<int, (int Index, double Since)>(); // worker slot -> (item index, since)
        var retry = new List<int>(); // items given back by workers that died before taking them
        var buffer = new Dictionary<int, (ItemResult, List<PairFacts>, int)>();
        var window = workers * WindowPerWorker;
        var timeout = config.ItemTimeoutSeconds;

        // Handle every message received from slot i; false once its channel failed.
        bool Read(int i)
        {
            var s = slots[i];
            var broken = s.Broken; // read first: the reader queues all messages before breaking
            while (s.Msgs.TryDequeue(out var m))
            {
                if (m.Index == -1)
                {
                    s.Ready = true;
                }
                else if (!held.TryGetValue(i, out var h) || h.Index != m.Index)
                {
                    continue;
                }
                else if (m.Result is null)
                {
                    s.Taken = s.Proven = true;
                    s.StartupFailures = 0;
                }
                else
                {
                    held.Remove(i);
                    buffer[m.Index] = (m.Result, m.Facts ?? [], m.Pid);
                }
            }
            return !broken;
        }

        int nextDispatch = 0, nextWrite = 0;
        try
        {
            for (var i = 0; i < workers; i++)
            {
                Start(i);
            }
            while (nextWrite < items.Count)
            {
                var limit = Math.Min(items.Count, nextWrite + window);
                for (var i = 0; i < slots.Count; i++)
                {
                    var s = slots[i];
                    if (!s.Ready || held.ContainsKey(i) || !(retry.Count > 0 || nextDispatch < limit))
                    {
                        continue;
                    }
                    var index = retry.Count > 0 ? retry[0] : nextDispatch;
                    try
                    {
                        var frame = $"{{\"index\":{index},\"item\":{PyJson.Serialize(items[index])}}}";
                        WorkerChannel.WriteFrame(s.Inbox, Encoding.UTF8.GetBytes(frame));
                    }
                    catch (IOException) // worker died while idle; judged below
                    {
                        continue;
                    }
                    if (retry.Count > 0)
                    {
                        retry.RemoveAt(0);
                    }
                    else
                    {
                        nextDispatch++;
                    }
                    held[i] = (index, hooks.Clock());
                    s.Taken = false;
                }
                wake.Wait(TimeSpan.FromSeconds(PollSeconds));
                wake.Reset();
                // Per slot: snapshot liveness and clock, then handle everything already received,
                // so a result received before the snapshot always wins over a verdict.
                for (var i = 0; i < slots.Count; i++)
                {
                    var s = slots[i];
                    var alive = !s.Proc.HasExited;
                    var now = hooks.Clock();
                    if (!alive && s.Thread is { IsAlive: true })
                    {
                        s.DeadSince ??= now; // reader still queueing its last words
                        if (now - s.DeadSince < hooks.SendGraceSeconds)
                        {
                            continue;
                        }
                    }
                    var busy = s.BusySince;
                    alive = Read(i) && alive;
                    if (alive && busy is not null && now - busy < hooks.SendGraceSeconds)
                    {
                        continue; // a message is arriving; judge on a later pass
                    }
                    if (held.TryGetValue(i, out var h))
                    {
                        if (alive && now - h.Since <= timeout)
                        {
                            continue;
                        }
                        if (alive || s.Taken)
                        {
                            var itemId = items[h.Index].ItemId;
                            var (code, message) = alive
                                ? (ErrorCode.ITEM_TIMEOUT, $"item {itemId}: not finished after {timeout} s")
                                : (ErrorCode.WORKER_CRASHED, $"item {itemId}: worker process crashed");
                            buffer[h.Index] = (ErrorResult(itemId, code, message, now - h.Since), [], s.Proc.Id);
                            held.Remove(i);
                            Replace(i);
                            continue;
                        }
                        held.Remove(i); // died before taking it: give it back
                        retry.Add(h.Index);
                        retry.Sort();
                    }
                    else if (alive && (s.Ready || now - s.Started <= options.StartupTimeoutSeconds))
                    {
                        continue;
                    }
                    if (s.Proven)
                    {
                        Replace(i);
                        continue;
                    }
                    if (s.StartupFailures + 1 >= MaxStartupFailures)
                    {
                        throw new InvalidOperationException("worker processes keep failing before taking any item");
                    }
                    Replace(i, s.StartupFailures + 1);
                }
                while (buffer.Remove(nextWrite, out var r))
                {
                    yield return r;
                    nextWrite++;
                }
            }
        }
        finally
        {
            foreach (var s in slots)
            {
                try
                {
                    WorkerChannel.WriteFrame(s.Inbox, "null"u8.ToArray());
                }
                catch (Exception e) when (e is IOException or ObjectDisposedException)
                {
                }
            }
            var deadline = Stopwatch.StartNew();
            foreach (var s in slots)
            {
                var left = Math.Max(0, 5000 - (int)deadline.ElapsedMilliseconds);
                if (!s.Proc.WaitForExit(left))
                {
                    Kill(s.Proc);
                }
                s.Close();
                s.Proc.Dispose();
            }
            try
            {
                File.Delete(configPath);
            }
            catch (IOException)
            {
            }
        }
    }
}
