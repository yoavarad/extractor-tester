using System.Diagnostics;
using System.IO.Pipes;
using ExtractorTester.Channel;

namespace ExtractorTester.Tests;

/// <summary>Port of tests/test_channel.py: reader thread queues messages and marks the slot broken on EOF.</summary>
public class ChannelTests
{
    private static (Slot Slot, AnonymousPipeServerStream Writer, ManualResetEventSlim Wake) OpenSlot()
    {
        var writer = new AnonymousPipeServerStream(PipeDirection.Out);
        var reader = new AnonymousPipeClientStream(PipeDirection.In, writer.ClientSafePipeHandle);
        var wake = new ManualResetEventSlim(false);
        var slot = WorkerChannel.OpenSlot(new Process(), new MemoryStream(), reader, 0, wake, new ChannelHooks());
        return (slot, writer, wake);
    }

    [Fact]
    public void ReaderQueuesMessagesThenMarksBrokenOnEof()
    {
        var (s, w, wake) = OpenSlot();
        WorkerChannel.WriteFrame(w, WorkerChannel.Encode(new WorkerMessage(-1, null, null, 1)));
        WorkerChannel.WriteFrame(w, WorkerChannel.Encode(new WorkerMessage(0, null, null, 1)));
        w.Dispose();
        Assert.True(s.Thread!.Join(5000));
        Assert.Equal([new WorkerMessage(-1, null, null, 1), new WorkerMessage(0, null, null, 1)], s.Msgs.ToArray());
        Assert.True(s.Broken);
        Assert.True(wake.IsSet);
        Assert.Throws<ObjectDisposedException>(() => s.Conn.ReadByte()); // closed by the reader
        s.Close();
    }

    [Fact]
    public void CorruptPayloadMarksBroken()
    {
        var (s, w, _) = OpenSlot();
        WorkerChannel.WriteFrame(w, [0x80, 0x05, .. "not a pickle"u8]);
        Assert.True(s.Thread!.Join(5000));
        Assert.True(s.Broken);
        Assert.Empty(s.Msgs);
        w.Dispose();
        s.Close();
    }
}
