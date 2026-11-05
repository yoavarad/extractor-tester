using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ExtractorTester.Models;

namespace ExtractorTester.Channel;

/// <summary>One worker message: ready (index -1), ack (result null) or result (port of the Python tuple).</summary>
public sealed record WorkerMessage(int Index, ItemResult? Result, List<PairFacts>? Facts, int Pid);

/// <summary>Replaceable clock, delivery and grace period; tests swap them as Python monkeypatches channel.</summary>
public sealed class ChannelHooks
{
    /// <summary>A message being received for longer than this is a stalled sender: judge the worker anyway.</summary>
    public double SendGraceSeconds { get; init; } = 5.0;

    /// <summary>Monotonic seconds.</summary>
    public Func<double> Clock { get; init; } = WorkerChannel.MonotonicClock;

    /// <summary>Queue a received message on its slot and wake the pool.</summary>
    public Action<Slot, WorkerMessage, ManualResetEventSlim> Deliver { get; init; } = WorkerChannel.DefaultDeliver;
}

/// <summary>One worker process, its pipes and what the pool knows about it.</summary>
public sealed class Slot
{
    private long _busyBits = BitConverter.DoubleToInt64Bits(double.NaN);
    private volatile bool _broken;

    public Slot(Process proc, Stream inbox, Stream conn, double started, int startupFailures)
    {
        Proc = proc;
        Inbox = inbox;
        Conn = conn;
        Started = started;
        StartupFailures = startupFailures;
    }

    public Process Proc { get; }

    /// <summary>Parent to worker items.</summary>
    public Stream Inbox { get; }

    /// <summary>Worker to parent messages; only the reader thread uses and closes it.</summary>
    public Stream Conn { get; }

    public double Started { get; }
    public int StartupFailures { get; set; }
    public bool Ready { get; set; }

    /// <summary>The worker acked the item it holds.</summary>
    public bool Taken { get; set; }

    /// <summary>The worker has taken at least one item.</summary>
    public bool Proven { get; set; }

    /// <summary>The message channel failed (EOF, corrupt payload).</summary>
    public bool Broken { get => _broken; set => _broken = value; }

    /// <summary>A message is being received since then; null when none is.</summary>
    public double? BusySince
    {
        get
        {
            var v = BitConverter.Int64BitsToDouble(Interlocked.Read(ref _busyBits));
            return double.IsNaN(v) ? null : v;
        }
        set => Interlocked.Exchange(ref _busyBits, BitConverter.DoubleToInt64Bits(value ?? double.NaN));
    }

    /// <summary>First seen dead while its reader still ran.</summary>
    public double? DeadSince { get; set; }

    public ConcurrentQueue<WorkerMessage> Msgs { get; } = new();
    public Thread? Thread { get; set; }

    /// <summary>Close the item pipe; the reader closes the message pipe when it reaches EOF.</summary>
    public void Close()
    {
        try
        {
            Inbox.Dispose();
        }
        catch (IOException) // flushing bytes a dead worker never read; the handle is closed anyway
        {
        }
    }
}

/// <summary>Worker message channel: length-prefixed JSON frames, one reader thread per worker pipe.</summary>
public static class WorkerChannel
{
    /// <summary>Stack for threads that build or parse results: item values nest up to the loader's
    /// ~3000-level limit, which overflows a default 1 MB stack (a .NET stack overflow kills the process).</summary>
    public const int BigStackBytes = 256 * 1024 * 1024;

    private static readonly JsonSerializerOptions ReadOptions = new(PyJson.Options) { MaxDepth = 8192 };

    /// <summary>Larger frames are corrupt: results are far smaller than the 50 MB item cap allows.</summary>
    private const int MaxFrameBytes = 1 << 30;

    public static double MonotonicClock() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    public static void DefaultDeliver(Slot s, WorkerMessage msg, ManualResetEventSlim wake)
    {
        s.Msgs.Enqueue(msg);
        wake.Set();
    }

    /// <summary>Write one frame: 4-byte little-endian length, then UTF-8 payload.</summary>
    public static void WriteFrame(Stream stream, byte[] payload)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        stream.Write(header);
        stream.Write(payload);
        stream.Flush();
    }

    /// <summary>Read one frame; null on a clean EOF before any byte. Throws on a truncated frame.</summary>
    public static byte[]? ReadFrame(Stream stream, Action? onFirstByte = null)
    {
        var first = stream.ReadByte();
        if (first < 0)
        {
            return null;
        }
        onFirstByte?.Invoke();
        var header = new byte[4];
        header[0] = (byte)first;
        stream.ReadExactly(header, 1, 3);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > MaxFrameBytes) // e.g. stray text on the worker's stdout
        {
            throw new InvalidDataException($"bad frame length {length}");
        }
        var payload = new byte[length];
        stream.ReadExactly(payload);
        return payload;
    }

    /// <summary>Encode a worker message.</summary>
    public static byte[] Encode(WorkerMessage m)
    {
        var sb = new StringBuilder();
        sb.Append("{\"index\":").Append(m.Index)
            .Append(",\"result\":").Append(PyJson.Serialize(m.Result))
            .Append(",\"facts\":").Append(PyJson.Serialize(m.Facts))
            .Append(",\"pid\":").Append(m.Pid).Append('}');
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>Decode a worker message; throws on a corrupt payload.</summary>
    public static WorkerMessage Decode(byte[] payload) =>
        JsonSerializer.Deserialize<WorkerMessage>(payload, ReadOptions) ?? throw new JsonException("null message");

    /// <summary>Move messages from the worker's pipe to s.Msgs; on EOF or failure mark s broken.</summary>
    private static void Reader(Slot s, ManualResetEventSlim wake, ChannelHooks hooks)
    {
        try
        {
            while (ReadFrame(s.Conn, () => s.BusySince = hooks.Clock()) is { } payload)
            {
                hooks.Deliver(s, Decode(payload), wake);
                s.BusySince = null;
            }
        }
        catch (Exception) // EOF mid-frame, broken pipe or any corrupt payload
        {
        }
        s.BusySince = null; // nothing more is arriving
        s.Conn.Dispose();
        s.Broken = true;
        wake.Set();
    }

    /// <summary>Build the slot for a started worker and start its reader thread.</summary>
    public static Slot OpenSlot(
        Process proc, Stream inbox, Stream conn, int startupFailures, ManualResetEventSlim wake, ChannelHooks hooks)
    {
        var s = new Slot(proc, inbox, conn, hooks.Clock(), startupFailures);
        s.Thread = new Thread(() => Reader(s, wake, hooks), BigStackBytes) { IsBackground = true, Name = "worker-reader" };
        s.Thread.Start();
        return s;
    }
}
