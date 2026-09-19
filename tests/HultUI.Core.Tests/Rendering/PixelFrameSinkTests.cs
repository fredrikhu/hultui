using System.Runtime.InteropServices;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using Xunit;

namespace HultUI.Core.Tests.Rendering;

/// <summary>
/// The frame ring behind <see cref="PixelFrameSink"/>: what the producer writes is what
/// the renderer borrows, a borrowed frame is never written over, and a sink survives
/// being disposed while a frame is out.
/// </summary>
public class PixelFrameSinkTests
{
    private static byte[] ReadBorrowed(in PixelFrame frame)
    {
        var bytes = new byte[frame.Stride * frame.Height];
        Marshal.Copy(frame.Pixels, bytes, 0, bytes.Length);
        return bytes;
    }

    private static void Fill(PixelFrameSink sink, int width, int height, byte value)
    {
        using var writer = sink.BeginWrite(width, height, PixelFormat.Rgba8888);
        writer.Pixels.Fill(value);
        writer.Publish();
    }

    [Fact]
    public void BeforeTheFirstFrame_NothingToDraw()
    {
        using var sink = new PixelFrameSink();

        Assert.False(sink.IsLoaded);
        Assert.Equal(Size.Zero, sink.IntrinsicSize);
        Assert.Equal(0, sink.Version);
        Assert.False(sink.TryAcquire(out _));
    }

    [Fact]
    public void APublishedFrame_IsWhatTheRendererBorrows()
    {
        using var sink = new PixelFrameSink();

        Fill(sink, 3, 2, 0x7F);

        Assert.True(sink.IsLoaded);
        Assert.Equal(new Size(3, 2), sink.IntrinsicSize);
        Assert.Equal(1, sink.Version);
        Assert.True(sink.TryAcquire(out var frame));
        Assert.Equal((3, 2, 12, PixelFormat.Rgba8888), (frame.Width, frame.Height, frame.Stride, frame.Format));
        Assert.All(ReadBorrowed(frame), b => Assert.Equal(0x7F, b));
        sink.Release(frame);
    }

    [Fact]
    public void Write_CopiesRowsHonouringTheSourceStride()
    {
        using var sink = new PixelFrameSink();
        // Two 1-pixel rows in a buffer whose rows are 8 bytes apart: the padding
        // must not end up in the frame.
        byte[] padded = [1, 2, 3, 4, 0xEE, 0xEE, 0xEE, 0xEE, 5, 6, 7, 8, 0xEE, 0xEE, 0xEE, 0xEE];

        sink.Write(1, 2, PixelFormat.Bgra8888, padded, stride: 8);

        Assert.True(sink.TryAcquire(out var frame));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, ReadBorrowed(frame));
        Assert.Equal(PixelFormat.Bgra8888, frame.Format);
        sink.Release(frame);
    }

    [Fact]
    public void AnAbandonedWriter_PublishesNothing()
    {
        using var sink = new PixelFrameSink();
        Fill(sink, 1, 1, 1);

        using (var writer = sink.BeginWrite(1, 1, PixelFormat.Rgba8888))
            writer.Pixels.Fill(2);   // no Publish

        Assert.Equal(1, sink.Version);
        Assert.True(sink.TryAcquire(out var frame));
        Assert.All(ReadBorrowed(frame), b => Assert.Equal(1, b));
        sink.Release(frame);

        // And the slot it held is free again: three more frames fit without complaint.
        Fill(sink, 1, 1, 3);
        Fill(sink, 1, 1, 4);
        Fill(sink, 1, 1, 5);
    }

    [Fact]
    public void ABorrowedFrame_IsNeverWrittenOver()
    {
        using var sink = new PixelFrameSink();
        Fill(sink, 2, 2, 1);
        Assert.True(sink.TryAcquire(out var held));

        // Two publishes while the renderer holds frame 1 exhaust every slot but the
        // held one; a third must reuse an unheld slot, not the borrowed buffer.
        Fill(sink, 2, 2, 2);
        Fill(sink, 2, 2, 3);
        Fill(sink, 2, 2, 4);

        Assert.All(ReadBorrowed(held), b => Assert.Equal(1, b));
        sink.Release(held);

        Assert.True(sink.TryAcquire(out var latest));
        Assert.All(ReadBorrowed(latest), b => Assert.Equal(4, b));
        Assert.Equal(4, sink.Version);
        sink.Release(latest);
    }

    [Fact]
    public void ASecondBorrow_ReturnsTheSameFrame_UntilBothAreReleased()
    {
        // Two elements drawing one sink in the same render pass.
        using var sink = new PixelFrameSink();
        Fill(sink, 1, 1, 1);

        Assert.True(sink.TryAcquire(out var first));
        Fill(sink, 1, 1, 2);                        // newer frame arrives mid-pass
        Assert.True(sink.TryAcquire(out var second));

        Assert.Equal(first.Slot, second.Slot);      // same version for the whole pass
        sink.Release(first);
        Fill(sink, 1, 1, 3);
        Assert.All(ReadBorrowed(second), b => Assert.Equal(1, b));   // still held
        sink.Release(second);

        Assert.True(sink.TryAcquire(out var next));
        Assert.All(ReadBorrowed(next), b => Assert.Equal(3, b));
        sink.Release(next);
    }

    [Fact]
    public void ConcurrentWrites_AreRefused()
    {
        using var sink = new PixelFrameSink();
        using var open = sink.BeginWrite(1, 1, PixelFormat.Rgba8888);

        Assert.Throws<InvalidOperationException>(() => sink.BeginWrite(1, 1, PixelFormat.Rgba8888));
    }

    [Fact]
    public void ALargerFrame_GrowsTheSlot_AndASmallerOneReusesIt()
    {
        using var sink = new PixelFrameSink();
        Fill(sink, 1, 1, 1);
        Fill(sink, 1, 1, 1);
        Fill(sink, 1, 1, 1);

        Fill(sink, 8, 8, 2);
        Assert.Equal(new Size(8, 8), sink.IntrinsicSize);
        Assert.True(sink.TryAcquire(out var big));
        Assert.Equal(8 * 4 * 8, ReadBorrowed(big).Length);
        sink.Release(big);

        Fill(sink, 2, 2, 3);
        Assert.True(sink.TryAcquire(out var small));
        Assert.Equal((2, 2, 8), (small.Width, small.Height, small.Stride));
        Assert.Equal(16, ReadBorrowed(small).Length);
        sink.Release(small);
    }

    [Fact]
    public void DisposedWhileBorrowed_ReleaseIsStillSafe_AndWritesStop()
    {
        var sink = new PixelFrameSink();
        Fill(sink, 1, 1, 1);
        Assert.True(sink.TryAcquire(out var frame));

        sink.Dispose();

        Assert.All(ReadBorrowed(frame), b => Assert.Equal(1, b));
        sink.Release(frame);
        Assert.Throws<ObjectDisposedException>(() => sink.BeginWrite(1, 1, PixelFormat.Rgba8888));
    }

    [Fact]
    public void FramePublished_IsRaisedOnTheProducersThread_OncePerFrame()
    {
        using var sink = new PixelFrameSink();
        int raised = 0, thread = -1;
        sink.FramePublished += s => { raised++; thread = Environment.CurrentManagedThreadId; };

        var producer = new Thread(() => Fill(sink, 1, 1, 1));
        producer.Start();
        Assert.True(producer.Join(TimeSpan.FromSeconds(10)));

        Assert.Equal(1, raised);
        Assert.Equal(producer.ManagedThreadId, thread);
    }

    [Fact]
    public void AProducerOutrunningTheRenderer_NeverTearsABorrowedFrame()
    {
        // Every frame is a solid value; a borrowed frame that reads as anything but
        // one value was written over while held.
        using var sink = new PixelFrameSink();
        const int frames = 2000;
        Exception? producerError = null;
        var producer = new Thread(() =>
        {
            try
            {
                for (int i = 1; i <= frames; i++)
                    Fill(sink, 16, 16, (byte)(i % 251 + 1));
            }
            catch (Exception ex) { producerError = ex; }
        });
        producer.Start();

        int borrowed = 0;
        while (producer.IsAlive || borrowed == 0)
        {
            if (!sink.TryAcquire(out var frame)) continue;
            borrowed++;
            var bytes = ReadBorrowed(frame);
            byte first = bytes[0];
            Assert.All(bytes, b => Assert.Equal(first, b));
            sink.Release(frame);
        }

        Assert.True(producer.Join(TimeSpan.FromSeconds(30)));
        Assert.Null(producerError);
        Assert.Equal(frames, sink.Version);
        Assert.True(borrowed > 0);
    }
}
