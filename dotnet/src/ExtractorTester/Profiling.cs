using System.Diagnostics;

namespace ExtractorTester.Profiling;

/// <summary>
/// Stage-timer accumulators from profiling.py (clock/add/take, TEXT_STAGE). The rest of
/// profiling.py (STAGES, Slowest) is ported with the runner (ADR-009).
/// </summary>
public static class Stages
{
    /// <summary>Text method to its stage key.</summary>
    public static readonly IReadOnlyDictionary<string, string> TextStage = new Dictionary<string, string>
    {
        ["fast-path"] = "text_fast",
        ["full"] = "text_full",
        ["buffered"] = "text_buffered",
    };

    // Per-thread accumulators for stages timed deep inside a call; Python keeps one per process.
    [ThreadStatic]
    private static Dictionary<string, double>? _acc;

    /// <summary>One clock read in seconds (time.perf_counter).</summary>
    public static double Clock() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    /// <summary>Accumulate seconds for a stage timed inside the current item.</summary>
    public static void Add(string stage, double seconds)
    {
        _acc ??= new Dictionary<string, double>();
        _acc[stage] = _acc.GetValueOrDefault(stage) + seconds;
    }

    /// <summary>Return and reset a stage's accumulated seconds.</summary>
    public static double Take(string stage) =>
        _acc != null && _acc.Remove(stage, out var v) ? v : 0.0;
}
