using HultUI.Core.Rendering;
using Xunit;

namespace HultUI.Core.Tests.Rendering;

/// <summary>
/// <see cref="PixelFormat.Gray8"/> through the frame ring: one byte a pixel end to end, so a
/// monochrome producer writes, and the renderer borrows, a quarter of the bytes.
/// </summary>
public class PixelFormatGray8Tests
{
    [Fact]
    public void BytesPerPixel_IsOneForGrayAndFourOtherwise()
    {
        Assert.Equal(1, PixelFormat.Gray8.BytesPerPixel());
        Assert.Equal(4, PixelFormat.Rgba8888.BytesPerPixel());
        Assert.Equal(4, PixelFormat.Bgrx8888.BytesPerPixel());
        Assert.True(PixelFormat.Gray8.IsOpaque());
        Assert.True(PixelFormat.Rgbx8888.IsOpaque());
        Assert.False(PixelFormat.Bgra8888.IsOpaque());
    }

    [Fact]
    public void AGrayFrame_HasAOneBytePerPixelStride()
    {
        using var sink = new PixelFrameSink();
        using (var writer = sink.BeginWrite(5, 3, PixelFormat.Gray8))
        {
            Assert.Equal(5, writer.Stride);
            Assert.Equal(15, writer.Pixels.Length);
            for (int i = 0; i < writer.Pixels.Length; i++) writer.Pixels[i] = (byte)(i * 10);
            writer.Publish();
        }

        Assert.True(sink.TryAcquire(out var frame));
        Assert.Equal(PixelFormat.Gray8, frame.Format);
        Assert.Equal(5, frame.Stride);
        var bytes = new byte[15];
        System.Runtime.InteropServices.Marshal.Copy(frame.Pixels, bytes, 0, bytes.Length);
        Assert.Equal(140, bytes[14]);
        sink.Release(frame);
    }

    [Fact]
    public void Write_CopiesGrayRowsFromAPaddedSource()
    {
        using var sink = new PixelFrameSink();
        // Two rows of three pixels, each row padded to eight bytes.
        byte[] source = [1, 2, 3, 0, 0, 0, 0, 0, 4, 5, 6, 0, 0, 0, 0, 0];

        sink.Write(3, 2, PixelFormat.Gray8, source, stride: 8);

        Assert.True(sink.TryAcquire(out var frame));
        var bytes = new byte[6];
        System.Runtime.InteropServices.Marshal.Copy(frame.Pixels, bytes, 0, 6);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, bytes);
        sink.Release(frame);
    }

    [Fact]
    public void Write_RejectsAStrideShorterThanAGrayRow()
    {
        using var sink = new PixelFrameSink();
        Assert.Throws<ArgumentOutOfRangeException>(() => sink.Write(4, 1, PixelFormat.Gray8, new byte[4], stride: 3));
    }
}
