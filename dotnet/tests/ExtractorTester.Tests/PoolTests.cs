using ExtractorTester.Channel;
using ExtractorTester.Config;
using ExtractorTester.Loader;
using ExtractorTester.Models;
using ExtractorTester.Pool;

namespace ExtractorTester.Tests;

/// <summary>Worker-process tests run alone: CPU contention from other tests would only slow them.</summary>
[CollectionDefinition("Pool", DisableParallelization = true)]
public class PoolCollection
{
}

/// <summary>Port of tests/test_pool.py plus the pool parts of test_runner_faults.py / test_runner_workers.py.</summary>
[Collection("Pool")]
public class PoolTests : IDisposable
{
    private const string Config = "name_field: name\nmime_primary_field: mp\nmime_secondary_field: ms\n";

    private readonly string _tmp = Directory.CreateTempSubdirectory("et-pool-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmp, true);
        }
        catch (IOException)
        {
        }
    }

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

    private (string Old, string New) ThreeItems()
    {
        var (old, @new) = (Path.Combine(_tmp, "old"), Path.Combine(_tmp, "new"));
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(@new);
        foreach (var i in new[] { "a", "b", "c" })
        {
            TestItems.WriteItem(old, i, ItemFiles(i));
            TestItems.WriteItem(@new, i, ItemFiles(i, author: "y"));
        }
        return (old, @new);
    }

    private (List<(ItemResult Result, List<PairFacts> Facts, int Pid)> Results, int Replaced) CompareAll(
        string extraConfig = "", int workers = 2, PoolOptions? options = null, (string Old, string New)? dirs = null)
    {
        var (old, @new) = dirs ?? ThreeItems();
        var cfgPath = Path.Combine(_tmp, "c.yaml");
        File.WriteAllText(cfgPath, Config + extraConfig);
        var stats = new WorkerStats();
        var items = ItemLoader.Discover(old, @new).Items;
        var results = WorkerPool.CompareAll(items, ConfigLoader.Load(cfgPath), workers, stats, options).ToList();
        return (results, stats.Replaced);
    }

    private static PoolOptions Env(string key, string value) =>
        new() { WorkerEnvironment = new Dictionary<string, string?> { [key] = value } };

    private static Dictionary<string, string[]> Codes(List<(ItemResult Result, List<PairFacts> Facts, int Pid)> results) =>
        results.ToDictionary(r => r.Result.ItemId, r => r.Result.Errors.Select(e => e.Code.ToString()).ToArray());

    [Fact]
    public void WorkerHungAtStartupIsReplaced()
    {
        var options = new PoolOptions
        {
            StartupTimeoutSeconds = 15.0,
            WorkerEnvironment = new Dictionary<string, string?> { [WorkerPool.HangStartupEnv] = Path.Combine(_tmp, "hang.flag") },
        };
        var (results, replaced) = CompareAll(workers: 1, options: options); // one worker: nothing runs until replaced
        Assert.Equal(
            [("a", "compared"), ("b", "compared"), ("c", "compared")],
            results.Select(r => (r.Result.ItemId, r.Result.Status)));
        Assert.Equal(1, replaced);
    }

    /// <summary>A result received before the timeout check is kept, not ITEM_TIMEOUT.</summary>
    /// <remarks>No wall-clock race: queueing a result and jumping the pool's clock 1000 s happen under
    /// one lock that every clock read also takes, so a check sees either the old clock or the
    /// queued result with the item long expired. One worker keeps a single item in flight.</remarks>
    [Fact]
    public void ResultAlreadyReceivedBeatsTimeout()
    {
        var gate = new object();
        var jumps = 0;
        var hooks = new ChannelHooks
        {
            Clock = () =>
            {
                lock (gate)
                {
                    return WorkerChannel.MonotonicClock() + 1000 * jumps;
                }
            },
            Deliver = (s, msg, wake) =>
            {
                lock (gate)
                {
                    WorkerChannel.DefaultDeliver(s, msg, wake);
                    if (msg.Index >= 0 && msg.Result is not null)
                    {
                        jumps++;
                    }
                }
            },
        };
        var (results, replaced) = CompareAll("item_timeout_seconds: 30\n", workers: 1, options: new PoolOptions { Hooks = hooks });
        Assert.Equal(
            [("a", "compared", 0), ("b", "compared", 0), ("c", "compared", 0)],
            results.Select(r => (r.Result.ItemId, r.Result.Status, r.Result.Errors.Count)));
        Assert.Equal(3, jumps);
        Assert.Equal(0, replaced);
    }

    [Fact]
    public void WorkerKilledMidWriteMarksOnlyItsItem()
    {
        var (results, replaced) = CompareAll(options: Env(WorkerPool.GarbageItemEnv, "b"));
        var codes = Codes(results);
        Assert.Equal(["a", "b", "c"], codes.Keys);
        Assert.Empty(codes["a"]);
        Assert.Equal(["WORKER_CRASHED"], codes["b"]);
        Assert.Empty(codes["c"]);
        Assert.Equal(1, replaced);
    }

    /// <summary>A worker whose result never finishes arriving cannot block the main loop: the other
    /// items are compared and the stalled item ends as ITEM_TIMEOUT.</summary>
    [Fact]
    public void StalledSenderDoesNotBlockOtherWorkers()
    {
        using var release = new ManualResetEventSlim(false);
        var hooks = new ChannelHooks
        {
            SendGraceSeconds = 0.5,
            Deliver = (s, msg, wake) =>
            {
                if (msg.Result?.ItemId == "b")
                {
                    release.Wait(); // stuck mid-receive, as with a sender that stalls mid-write
                }
                WorkerChannel.DefaultDeliver(s, msg, wake);
            },
        };
        List<(ItemResult Result, List<PairFacts> Facts, int Pid)> results;
        int replaced;
        try
        {
            (results, replaced) = CompareAll("item_timeout_seconds: 10\n", options: new PoolOptions { Hooks = hooks });
        }
        finally
        {
            release.Set();
        }
        var codes = Codes(results);
        Assert.Empty(codes["a"]);
        Assert.Equal(["ITEM_TIMEOUT"], codes["b"]);
        Assert.Empty(codes["c"]);
        Assert.Equal(1, replaced);
    }

    /// <summary>Workers that die right after startup are respawned only a bounded number of times;
    /// the run then fails (Python RuntimeError, reported by the runner as INTERNAL_FAILURE).</summary>
    [Fact]
    public void WorkersDyingBeforeTakingItemsEndTheRun()
    {
        var e = Assert.Throws<InvalidOperationException>(() => CompareAll(workers: 1, options: Env(WorkerPool.DieBeforeTakeEnv, "1")));
        Assert.Equal("worker processes keep failing before taking any item", e.Message);
    }

    /// <summary>A hung item is ITEM_TIMEOUT and its worker replaced; results stay in item-id order
    /// although the slow item finishes (times out) after the items that follow it.</summary>
    [Fact]
    [CoversError("ITEM_TIMEOUT")]
    public void SlowItemTimesOutAndOthersAreCompared()
    {
        var dirs = ThreeItems();
        var rng = new Random(0);
        const string alphabet = "abcdefghij \n";
        foreach (var d in new[] { dirs.Old, dirs.New })
        {
            var files = ItemFiles("a-slow");
            // ~20 s to compare in Release on a desktop CPU: well past the 8 s timeout, which cold workers need for a, b, c.
            files[1]["text"] = new string(Enumerable.Range(0, 1_000_000).Select(_ => alphabet[rng.Next(alphabet.Length)]).ToArray());
            TestItems.WriteItem(d, "a-slow", files);
        }
        var (results, replaced) = CompareAll(
            "item_timeout_seconds: 8\nfull_compare_max_chars: 2000000\n", workers: 2, dirs: dirs);
        Assert.Equal(["a", "a-slow", "b", "c"], results.Select(r => r.Result.ItemId));
        var codes = Codes(results);
        Assert.Equal(["ITEM_TIMEOUT"], codes["a-slow"]);
        Assert.Equal("item a-slow: not finished after 8 s", results[1].Result.Errors[0].Message);
        Assert.Equal("error", results[1].Result.Status);
        Assert.All(new[] { results[0], results[2], results[3] }, r => Assert.Equal(("compared", 0), (r.Result.Status, r.Result.Errors.Count)));
        Assert.Equal(1, replaced);
    }

    /// <summary>A replaced worker's reader thread has exited by the time its process is disposed.</summary>
    [Fact]
    public void ReplacedWorkerReaderIsJoinedBeforeDispose()
    {
        var seen = new List<(bool ReaderAlive, bool Exited)>();
        var hooks = new ChannelHooks
        {
            BeforeDispose = s => { lock (seen) { seen.Add((s.Thread!.IsAlive, s.Proc.HasExited)); } },
        };
        var options = new PoolOptions
        {
            Hooks = hooks,
            WorkerEnvironment = new Dictionary<string, string?> { [WorkerPool.CrashItemEnv] = "b" },
        };
        var (_, replaced) = CompareAll(options: options);
        Assert.Equal(1, replaced);
        Assert.Equal([(false, true)], seen);
    }

    /// <summary>A reader stuck in delivery does not hang the pool: the join is bounded.</summary>
    [Fact]
    public void StuckReaderDoesNotHangReplacement()
    {
        using var release = new ManualResetEventSlim(false);
        var stuck = 0;
        var hooks = new ChannelHooks
        {
            ReaderJoinMilliseconds = 50,
            Deliver = (s, m, w) =>
            {
                if (m.Index == 1 && Interlocked.Exchange(ref stuck, 1) == 0)
                {
                    release.Wait(TimeSpan.FromSeconds(60)); // reader stays alive past the join bound
                }
                WorkerChannel.DefaultDeliver(s, m, w);
            },
        };
        try
        {
            var options = new PoolOptions { Hooks = hooks, WorkerEnvironment = new Dictionary<string, string?> { [WorkerPool.CrashItemEnv] = "b" } };
            var (results, replaced) = CompareAll(options: options);
            Assert.Equal(3, results.Count);
            Assert.True(replaced >= 1);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    [CoversError("WORKER_CRASHED")]
    public void CrashedWorkerMarksOnlyItsItem()
    {
        var (results, replaced) = CompareAll(options: Env(WorkerPool.CrashItemEnv, "b"));
        var codes = Codes(results);
        Assert.Empty(codes["a"]);
        Assert.Equal(["WORKER_CRASHED"], codes["b"]);
        Assert.Equal("item b: worker process crashed", results[1].Result.Errors[0].Message);
        Assert.Empty(codes["c"]);
        Assert.Equal(1, replaced);
    }

    [Fact]
    public void ManyItemsComeBackInInputOrderFromSeparateProcesses()
    {
        var (old, @new) = (Path.Combine(_tmp, "old"), Path.Combine(_tmp, "new"));
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(@new);
        var ids = Enumerable.Range(0, 40).Select(i => $"i{i:D2}").ToList();
        foreach (var i in ids)
        {
            TestItems.WriteItem(old, i, ItemFiles(i));
            TestItems.WriteItem(@new, i, ItemFiles(i, author: "y"));
        }
        var (results, replaced) = CompareAll(workers: 4, dirs: (old, @new));
        Assert.Equal(ids, results.Select(r => r.Result.ItemId));
        Assert.All(results, r => Assert.Equal("compared", r.Result.Status));
        Assert.DoesNotContain(Environment.ProcessId, results.Select(r => r.Pid));
        Assert.Equal(0, replaced);
    }

    [Fact]
    public void SingleWorkerRunsInSeparateProcess()
    {
        var (results, _) = CompareAll(workers: 1);
        Assert.Equal(3, results.Count);
        Assert.Single(results.Select(r => r.Pid).Distinct());
        Assert.NotEqual(Environment.ProcessId, results[0].Pid);
    }

    [Fact]
    public void WorkersBelowOneIsAnError()
    {
        Assert.Throws<ArgumentException>(() => CompareAll(workers: 0));
    }

    /// <summary>Nesting at the loader limit runs on the real worker path (not a big-stack test thread)
    /// without a process-killing stack overflow; one level more is the loader's RecursionError.</summary>
    /// <remarks>On a default 1 MB thread in Release, serializing this result overflows the stack.</remarks>
    [Fact]
    public void LoaderLimitNestingDoesNotCrashTheWorker()
    {
        var (old, @new) = (Path.Combine(_tmp, "old"), Path.Combine(_tmp, "new"));
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(@new);
        // The item object is one level, so a list nested MaxDepth - 1 deep is exactly at the limit.
        foreach (var (id, depth) in new[] { ("at", PyJsonDecoder.MaxDepth - 1), ("over", PyJsonDecoder.MaxDepth) })
        {
            foreach (var (dir, leaf) in new[] { (old, "1"), (@new, "2") })
            {
                var path = TestItems.WriteItem(dir, id, ItemFiles(id));
                var text = File.ReadAllText(path);
                File.WriteAllText(path, text[..^1] + ", \"deep\": " + new string('[', depth) + leaf + new string(']', depth) + "}");
            }
        }
        var (results, replaced) = CompareAll(workers: 1, dirs: (old, @new));
        Assert.Equal(["at", "over"], results.Select(r => r.Result.ItemId));
        Assert.DoesNotContain(results, r => r.Result.Errors.Any(e => e.Code == ErrorCode.WORKER_CRASHED));
        Assert.Equal(
            "RecursionError: maximum recursion depth exceeded while decoding a JSON array from a unicode string",
            Assert.Single(results[1].Result.Errors).Message);
        Assert.Equal(0, replaced);
    }
}
