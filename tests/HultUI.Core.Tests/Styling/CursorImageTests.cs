using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Styling;

/// <summary>
/// A <see cref="CursorImage"/> is handed to native code as-is, so it must be impossible to make one
/// whose pixels are short or whose hotspot is off the picture.
/// </summary>
public class CursorImageTests
{
    [Fact]
    public void Keeps_ItsOwnCopy_OfThePixels()
    {
        var pixels = new byte[2 * 2 * 4];
        pixels[0] = 7;
        var image = new CursorImage(2, 2, pixels, 1, 1);

        pixels[0] = 99;

        Assert.Equal(7, image.Pixels[0]);
        Assert.Equal(16, image.Pixels.Length);
    }

    [Fact]
    public void Ignores_BytesBeyondThePicture()
    {
        var image = new CursorImage(1, 1, new byte[10], 0, 0);

        Assert.Equal(4, image.Pixels.Length);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-3, 4)]
    public void Refuses_AnEmptySize(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CursorImage(width, height, new byte[64], 0, 0));
    }

    [Fact]
    public void Refuses_TooFewPixels()
    {
        Assert.Throws<ArgumentException>(() => new CursorImage(2, 2, new byte[15], 0, 0));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    [InlineData(-1, 0)]
    public void Refuses_AHotspotOffThePicture(int x, int y)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CursorImage(2, 2, new byte[16], x, y));
    }
}
