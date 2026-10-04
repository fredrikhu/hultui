using System.IO.Compression;
using XTerm.Graphics;

namespace XTerm.Tests.Graphics;

/// <summary>
/// Decoding PNG, which is the format Kitty-speaking tools reach for by default.
///
/// <para>Written by hand rather than taken from a package: XTerm.NET is a headless emulator with two
/// small dependencies and no imaging stack. So the decoder is ours, and so is the risk.</para>
///
/// <para>Two kinds of fixture, deliberately. <see cref="RealPng"/> is bytes produced by somebody
/// else's encoder, pasted in — it is the guard against the decoder and a home-made fixture sharing
/// a misunderstanding. <see cref="Encode"/> is a minimal encoder written here, which exists because
/// no ordinary encoder lets you demand a particular scanline filter, and the five filters are the
/// part of PNG most worth testing one at a time. It is not used to check the decoder against
/// itself: its output was verified readable by an independent decoder before being relied on.</para>
///
/// <para>System.Drawing is deliberately not used at runtime — it is Windows-only since .NET 6 and
/// CI runs on Linux.</para>
/// </summary>
public class PngDecoderTests
{
    private const long NoLimit = 1_000_000;

    private static bool TryDecode(byte[] data, out byte[] pixels, out int width, out int height,
                                  long maxPixels = NoLimit)
        => PngDecoder.TryDecode(data, maxPixels, out pixels, out width, out height);

    private static (byte R, byte G, byte B, byte A) Pixel(byte[] bgra, int width, int x, int y)
    {
        var at = (y * width + x) * 4;
        return (bgra[at + 2], bgra[at + 1], bgra[at], bgra[at + 3]);
    }

    // ---- a PNG from somebody else's encoder ----------------------------------------------------

    /// <summary>
    /// 5x3 32-bit RGBA, produced by System.Drawing and pasted in. The last two pixels are fully
    /// transparent black, which is worth having: a decoder that forced everything opaque would pass
    /// every other test here.
    /// </summary>
    private static byte[] RealPng => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAUAAAADCAYAAABbNsX4AAAAAXNSR0IArs4c6QAAAARnQU1BAACx" +
        "jwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAABESURBVBhXY+ASkeOSN8+WM4naYeRZa+sWt6Ar" +
        "iqH48PW8jmcxTXN1V07bFKC36nhx9T6GO9NOXPq40/eZ2J1ZvxigAAD9BhlVn28K4gAAAABJRU5E" +
        "rkJggg==");

    /// <summary>What that file holds, read out by the same encoder that wrote it.</summary>
    private static readonly (byte R, byte G, byte B, byte A)[] RealPngPixels =
    {
        (10, 20, 30, 10),    (31, 55, 107, 30),   (52, 90, 184, 50),   (73, 125, 61, 70),   (94, 160, 138, 90),
        (115, 195, 215, 110),(136, 230, 92, 130), (157, 45, 169, 150), (178, 80, 46, 170),  (199, 115, 123, 190),
        (220, 150, 200, 210),(241, 185, 77, 230), (22, 220, 154, 250), (0, 0, 0, 0),        (0, 0, 0, 0)
    };

    [Fact]
    public void A_png_from_a_real_encoder_decodes_to_the_pixels_it_holds()
    {
        Assert.True(TryDecode(RealPng, out var pixels, out var width, out var height));
        Assert.Equal((5, 3), (width, height));
        Assert.Equal(5 * 3 * 4, pixels.Length);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var expected = RealPngPixels[y * width + x];
                var actual = Pixel(pixels, width, x, y);
                Assert.True(actual == expected,
                    $"pixel ({x},{y}) decoded as {actual}, expected {expected}");
            }
        }
    }

    /// <summary>
    /// Alpha has to survive. A Kitty image is composited over the cell behind it, so a decoder that
    /// quietly forced everything opaque would look right until something transparent arrived.
    /// </summary>
    [Fact]
    public void Transparency_survives()
    {
        Assert.True(TryDecode(RealPng, out var pixels, out var width, out _));

        Assert.Equal((byte)10, Pixel(pixels, width, 0, 0).A);   // barely there
        Assert.Equal((byte)250, Pixel(pixels, width, 2, 2).A);  // nearly solid
        Assert.Equal((byte)0, Pixel(pixels, width, 3, 2).A);    // gone entirely
    }

    // ---- every scanline filter, one at a time ---------------------------------------------------

    /// <summary>
    /// The five filters are where a PNG decoder actually goes wrong, and each is reconstructed from
    /// different neighbours — left, above, both, or the Paeth predictor of all three. An encoder
    /// picks them by heuristic, so the only way to be sure each path works is to demand it.
    /// </summary>
    [Theory]
    [InlineData(0, "None")]
    [InlineData(1, "Sub")]
    [InlineData(2, "Up")]
    [InlineData(3, "Average")]
    [InlineData(4, "Paeth")]
    public void Every_scanline_filter_reconstructs_the_original(byte filter, string name)
    {
        var (source, width, height) = Gradient(9, 7);

        var png = Encode(source, width, height, filter);

        Assert.True(TryDecode(png, out var pixels, out var decodedWidth, out var decodedHeight),
            $"filter {name} did not decode at all");
        Assert.Equal((width, height), (decodedWidth, decodedHeight));

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var at = (y * width + x) * 4;
                var expected = (source[at], source[at + 1], source[at + 2], source[at + 3]);
                var actual = Pixel(pixels, width, x, y);
                Assert.True(actual == expected,
                    $"filter {name}: pixel ({x},{y}) came back {actual}, expected {expected}");
            }
        }
    }

    /// <summary>RGBA bytes with enough variation that a wrong predictor cannot come out right.</summary>
    private static (byte[] Rgba, int Width, int Height) Gradient(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            rgba[i * 4] = (byte)(i * 37 % 251);
            rgba[i * 4 + 1] = (byte)(i * 89 % 241);
            rgba[i * 4 + 2] = (byte)(i * 151 % 233);
            rgba[i * 4 + 3] = (byte)(i * 211 % 255);
        }
        return (rgba, width, height);
    }

    // ---- the two filters nothing else exercises -------------------------------------------------

    /// <summary>
    /// A 2x2 image whose filtered bytes were worked out by hand from the specification, so the
    /// decoder is checked against the standard rather than against the encoder further down this
    /// file.
    /// </summary>
    /// <remarks>
    /// This matters for Average and Paeth specifically. Real encoders do not emit them for small
    /// images — System.Drawing writes filter 0 for everything, and the icons in this repository use
    /// only None, Sub and Up — so without this, those two paths would only ever be checked against
    /// an encoder written by the same hand, and a shared misreading of the predictor would cancel
    /// out and pass.
    /// </remarks>
    [Theory]
    [InlineData(3, "Average", new byte[] { 3, 10, 20, 30, 40, 45, 50, 55, 60,
                                           3, 85, 90, 95, 100, 60, 60, 60, 60 })]
    [InlineData(4, "Paeth", new byte[] { 4, 10, 20, 30, 40, 40, 40, 40, 40,
                                         4, 80, 80, 80, 80, 40, 40, 40, 40 })]
    public void A_filter_computed_by_hand_reconstructs_the_original(byte filter, string name, byte[] scanlines)
    {
        _ = filter;

        // What those bytes must come back as.
        var expected = new (byte R, byte G, byte B, byte A)[]
        {
            (10, 20, 30, 40),    (50, 60, 70, 80),
            (90, 100, 110, 120), (130, 140, 150, 160)
        };

        var png = WrapPng(scanlines, 2, 2);

        Assert.True(TryDecode(png, out var pixels, out var width, out var height),
            $"the hand-built {name} image did not decode");
        Assert.Equal((2, 2), (width, height));

        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                var actual = Pixel(pixels, width, x, y);
                Assert.True(actual == expected[y * 2 + x],
                    $"{name}: pixel ({x},{y}) came back {actual}, expected {expected[y * 2 + x]}");
            }
        }
    }

    /// <summary>
    /// Paeth when the distances tie, which is the one part of the predictor with a choice to make.
    /// </summary>
    /// <remarks>
    /// <para>The specification breaks a tie towards the left neighbour: <c>pa &lt;= pb &amp;&amp; pa &lt;= pc</c>,
    /// not <c>&lt;</c>. Writing it with strict comparisons is an easy slip and produces a picture that
    /// is right almost everywhere, which is the worst kind of wrong.</para>
    /// <para>The values are not arbitrary. Writing the predictor as distances over
    /// <c>d = above - aboveLeft</c> and <c>e = left - aboveLeft</c>, a tie between <c>pa</c> and
    /// <c>pc</c> needs <c>e = -2d</c> — so aboveLeft 100, above 110, left 80 gives
    /// pa 10, pb 20, pc 10, and the tie decides between returning 80 and returning 100. Every other
    /// fixture in this file, and both icons in this repository, happen to avoid it.</para>
    /// </remarks>
    [Fact]
    public void Paeth_breaks_a_tie_towards_the_left_neighbour()
    {
        // aboveLeft=100, above=110, left=80, and the pixel itself 200.
        var scanlines = new byte[]
        {
            4, 100, 100, 100, 100,  10,  10,  10,  10,
            4, 236, 236, 236, 236, 120, 120, 120, 120
        };

        var png = WrapPng(scanlines, 2, 2);

        Assert.True(TryDecode(png, out var pixels, out var width, out _));
        Assert.True(Pixel(pixels, width, 1, 1) == ((byte)200, (byte)200, (byte)200, (byte)200),
            $"the tie resolved the wrong way: got {Pixel(pixels, width, 1, 1)}, expected (200, 200, 200, 200). "
            + "The predictor must prefer the left neighbour when the distances are equal.");
    }

    // ---- refusing what it cannot read ----------------------------------------------------------

    /// <summary>
    /// The payload is untrusted output from another process. Every one of these means "no image",
    /// and none may escape as an exception.
    /// </summary>
    [Theory]
    [InlineData("empty")]
    [InlineData("not a png at all")]
    [InlineData("signature only")]
    [InlineData("truncated mid-idat")]
    [InlineData("header truncated")]
    [InlineData("chunk longer than the file")]
    [InlineData("corrupt compressed stream")]
    public void Malformed_input_is_refused_rather_than_thrown(string what)
    {
        var png = RealPng;

        var data = what switch
        {
            "empty" => Array.Empty<byte>(),
            "not a png at all" => new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            "signature only" => png[..8],
            "truncated mid-idat" => png[..(png.Length - 20)],
            "header truncated" => png[..12],
            "chunk longer than the file" => WithChunkLength(png, 0x7FFFFF00),
            _ => WithCorruptImageData(png)
        };

        var exception = Record.Exception(() => TryDecode(data, out _, out _, out _));

        Assert.True(exception is null, $"{what} threw instead of being refused: {exception}");
    }

    private static byte[] WithChunkLength(byte[] png, int length)
    {
        var copy = (byte[])png.Clone();
        copy[8] = (byte)(length >> 24);
        copy[9] = (byte)(length >> 16);
        copy[10] = (byte)(length >> 8);
        copy[11] = (byte)length;
        return copy;
    }

    private static byte[] WithCorruptImageData(byte[] png)
    {
        var copy = (byte[])png.Clone();
        for (int i = 8; i + 8 < copy.Length; i++)
        {
            if (copy[i + 4] == 'I' && copy[i + 5] == 'D' && copy[i + 6] == 'A' && copy[i + 7] == 'T')
            {
                for (int j = i + 8; j < Math.Min(i + 30, copy.Length); j++)
                    copy[j] ^= 0xFF;
                break;
            }
        }
        return copy;
    }

    /// <summary>
    /// The header declares a size before any pixel data arrives, so an absurd one is refused before
    /// anything is allocated for it.
    /// </summary>
    [Fact]
    public void An_image_larger_than_the_budget_is_refused()
    {
        Assert.False(TryDecode(RealPng, out _, out _, out _, maxPixels: 4));
        Assert.True(TryDecode(RealPng, out _, out _, out _, maxPixels: 15));
    }

    /// <summary>
    /// An Adam7 picture must come out pixel-for-pixel identical to the same picture stored plainly.
    /// </summary>
    /// <remarks>
    /// Eight by eight so that all seven passes carry data -- a smaller picture leaves some of them
    /// empty and the scatter arithmetic goes untested for those.
    /// </remarks>
    [Fact]
    public void An_interlaced_png_decodes_to_the_same_pixels_as_a_plain_one()
    {
        var (source, width, height) = Gradient(8, 8);

        Assert.True(TryDecode(Encode(source, width, height, filter: 0),
                              out var plain, out _, out _));
        Assert.True(TryDecode(EncodeInterlaced(source, width, height),
                              out var interlaced, out var w, out var h));

        Assert.Equal(8, w);
        Assert.Equal(8, h);
        Assert.Equal(plain, interlaced);
    }

    /// <summary>
    /// A picture small enough that four of the seven passes are empty. Those contribute no bytes at
    /// all, not even a filter byte -- counting one would shift every later pass and turn the rest of
    /// the image into noise.
    /// </summary>
    [Fact]
    public void An_interlaced_png_with_empty_passes_still_decodes()
    {
        var (source, width, height) = Gradient(2, 2);

        Assert.True(TryDecode(Encode(source, width, height, filter: 0), out var plain, out _, out _));
        Assert.True(TryDecode(EncodeInterlaced(source, width, height), out var interlaced, out _, out _));

        Assert.Equal(plain, interlaced);
    }

    /// <summary>
    /// The interlace flag set over scanlines that are not interlaced. The pass lengths cannot match,
    /// and a picture decoded from misread bytes would be worse than an error reply.
    /// </summary>
    [Fact]
    public void A_png_claiming_interlace_it_does_not_have_is_refused()
    {
        var (source, width, height) = Gradient(8, 8);
        var png = Encode(source, width, height, filter: 0, interlace: 1);

        Assert.False(TryDecode(png, out _, out _, out _));
    }

    /// <summary>An interlace method that does not exist is refused rather than guessed at.</summary>
    [Fact]
    public void An_unknown_interlace_method_is_refused()
    {
        var (source, width, height) = Gradient(4, 4);
        var png = Encode(source, width, height, filter: 0, interlace: 2);

        Assert.False(TryDecode(png, out _, out _, out _));
    }

    /// <summary>
    /// Writes an Adam7 picture: seven independently filtered sub-images, in pass order.
    /// </summary>
    /// <remarks>
    /// Filter 0 throughout, because what is under test here is the pass geometry and the scatter,
    /// not the filters -- those have their own tests on the straight-through path.
    /// </remarks>
    private static byte[] EncodeInterlaced(byte[] rgba, int width, int height)
    {
        var passes = new (int X, int Y, int StepX, int StepY)[]
        {
            (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4),
            (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)
        };

        var raw = new List<byte>();

        foreach (var pass in passes)
        {
            var passWidth = (width - pass.X + pass.StepX - 1) / pass.StepX;
            var passHeight = (height - pass.Y + pass.StepY - 1) / pass.StepY;
            if (passWidth <= 0 || passHeight <= 0)
                continue;

            for (int y = 0; y < passHeight; y++)
            {
                raw.Add(0);   // filter: none
                for (int x = 0; x < passWidth; x++)
                {
                    var sourceX = pass.X + x * pass.StepX;
                    var sourceY = pass.Y + y * pass.StepY;
                    var at = (sourceY * width + sourceX) * 4;
                    raw.Add(rgba[at]);
                    raw.Add(rgba[at + 1]);
                    raw.Add(rgba[at + 2]);
                    raw.Add(rgba[at + 3]);
                }
            }
        }

        return WrapPng(raw.ToArray(), width, height, interlace: 1);
    }

    // ---- a minimal encoder, so a filter can be demanded ------------------------------------------

    /// <summary>
    /// Writes 8-bit RGBA with one chosen filter on every row.
    /// </summary>
    /// <remarks>
    /// Only exists because no ordinary encoder lets a caller pick the filter, and filters are what a
    /// PNG decoder most needs testing on. Emits real CRCs, so its output is a legitimate PNG rather
    /// than something only this decoder would take.
    /// </remarks>
    private static byte[] Encode(byte[] rgba, int width, int height, byte filter, byte interlace = 0)
    {
        var bytesPerPixel = 4;
        var bytesPerRow = width * bytesPerPixel;

        var raw = new byte[(bytesPerRow + 1) * height];
        for (int y = 0; y < height; y++)
        {
            var rowStart = y * (bytesPerRow + 1);
            raw[rowStart] = filter;

            for (int i = 0; i < bytesPerRow; i++)
            {
                int current = rgba[y * bytesPerRow + i];
                int left = i >= bytesPerPixel ? rgba[y * bytesPerRow + i - bytesPerPixel] : 0;
                int above = y > 0 ? rgba[(y - 1) * bytesPerRow + i] : 0;
                int aboveLeft = (y > 0 && i >= bytesPerPixel) ? rgba[(y - 1) * bytesPerRow + i - bytesPerPixel] : 0;

                raw[rowStart + 1 + i] = filter switch
                {
                    1 => (byte)(current - left),
                    2 => (byte)(current - above),
                    3 => (byte)(current - ((left + above) >> 1)),
                    4 => (byte)(current - Predictor(left, above, aboveLeft)),
                    _ => (byte)current
                };
            }
        }

        return WrapPng(raw, width, height, interlace);
    }

    /// <summary>
    /// Wraps already-filtered scanlines in a PNG container, without touching the bytes.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Encode"/> so a test can supply scanlines it worked out itself and
    /// keep the filtering code in this file out of the answer.
    /// </remarks>
    private static byte[] WrapPng(byte[] filteredScanlines, int width, int height, byte interlace = 0)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionMode.Compress, leaveOpen: true))
            zlib.Write(filteredScanlines, 0, filteredScanlines.Length);

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var header = new byte[13];
        WriteBigEndian(header, 0, width);
        WriteBigEndian(header, 4, height);
        header[8] = 8;          // bit depth
        header[9] = 6;          // colour type: truecolour with alpha
        header[10] = 0;         // compression
        header[11] = 0;         // filter method
        header[12] = interlace;
        WriteChunk(png, "IHDR", header);

        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", Array.Empty<byte>());

        return png.ToArray();
    }

    /// <summary>The Paeth predictor, straight from the specification.</summary>
    private static int Predictor(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteBigEndian(byte[] buffer, int at, int value)
    {
        buffer[at] = (byte)(value >> 24);
        buffer[at + 1] = (byte)(value >> 16);
        buffer[at + 2] = (byte)(value >> 8);
        buffer[at + 3] = (byte)value;
    }

    private static void WriteChunk(Stream stream, string type, byte[] body)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, body.Length);
        stream.Write(length);

        var typed = new byte[4 + body.Length];
        for (int i = 0; i < 4; i++)
            typed[i] = (byte)type[i];
        body.CopyTo(typed, 4);
        stream.Write(typed);

        var crc = new byte[4];
        WriteBigEndian(crc, 0, unchecked((int)Crc32(typed)));
        stream.Write(crc);
    }

    private static uint[]? _crcTable;

    private static uint Crc32(byte[] data)
    {
        if (_crcTable is null)
        {
            _crcTable = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                var c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                _crcTable[i] = c;
            }
        }

        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = _crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
