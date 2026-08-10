using System.IO.Compression;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Images whose decoded size bears no relation to their size on disk. Both decoders used to
/// gate on bytes and hand the file to Skia; see <see cref="ImageDecode"/> for why that is not
/// a bound at all.
/// </summary>
public class ImageBombTests
{
    private static string Scratch([System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        var dir = Path.Combine(Path.GetTempPath(), "vesper-hostile", name);
        try { Directory.Delete(dir, true); } catch { }
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// A couple of hundred kilobytes on disk, 144 million pixels decoded — 576 MB. The byte
    /// cap the decoders used to gate on lets it straight through.
    /// <para>Sized deliberately: at 30000×30000 the allocator refuses before the budget is
    /// consulted, so the test passed even with the budget removed. It has to be an image the
    /// machine <i>could</i> decode, or it proves nothing.</para>
    /// </summary>
    [Fact]
    public void APixelBombIsRefusedRatherThanDecoded()
    {
        var dir = Scratch();
        var bomb = Path.Combine(dir, "bomb.png");
        HugePng.Write(bomb, 12000, 12000);

        Assert.True(new FileInfo(bomb).Length < 24L * 1024 * 1024,
            "the bomb has to pass the byte cap, or this proves nothing");
        Assert.Null(ImageDecode.Bounded(bomb, 256));
        Assert.Null(ImageDecode.Bounded(File.ReadAllBytes(bomb), 256));   // the archive-member path
    }

    /// <summary>The budget is on pixels, so a colour-type change doesn't sneak past it —
    /// 20000×20000 RGBA was 1.6 GB.</summary>
    [Fact]
    public void ARgbaPixelBombIsRefusedToo()
    {
        var dir = Scratch();
        var bomb = Path.Combine(dir, "bomb-rgba.png");
        HugePng.Write(bomb, 20000, 20000, colourType: 6, bytesPerPixel: 4);
        Assert.Null(ImageDecode.Bounded(bomb, 256));
    }

    /// <summary>The cap has to leave ordinary images alone, and hand back something the
    /// caller can actually draw.</summary>
    [Fact]
    public void AnOrdinaryImageStillDecodes()
    {
        var dir = Scratch();
        var png = Path.Combine(dir, "small.png");
        HugePng.Write(png, 64, 48);

        using var bmp = ImageDecode.Bounded(png, 256);
        Assert.NotNull(bmp);
        Assert.Equal(64, bmp.Width);
        Assert.Equal(48, bmp.Height);
    }

    /// <summary>
    /// A big JPEG is decoded at a fraction of its stored size rather than in full — the thing
    /// that lets the budget be a real number without refusing real photographs. Skia samples
    /// while decoding for JPEG and WebP, so this never materializes the whole frame.
    /// </summary>
    [Fact]
    public void ASampleableImageIsDecodedSmallerThanItsSource()
    {
        var dir = Scratch();
        var jpeg = Path.Combine(dir, "photo.jpg");
        WriteJpeg(jpeg, 4000, 3000);

        using var bmp = ImageDecode.Bounded(jpeg, 256);
        Assert.NotNull(bmp);
        Assert.True(bmp.Width < 4000,
            $"decoded at {bmp.Width}x{bmp.Height} — the full frame, so sampling did not happen");
        Assert.True(bmp.Width >= 256, $"sampled all the way down to {bmp.Width}px — too small to draw");
    }

    /// <summary>PNG has no sampled decode, so a large one is materialized whole. That is
    /// exactly why the budget is needed, and why an ordinary-sized PNG has to keep working.</summary>
    [Fact]
    public void ALargePngWithinTheBudgetStillDecodes()
    {
        var dir = Scratch();
        var png = Path.Combine(dir, "wide.png");
        HugePng.Write(png, 4000, 3000);

        using var bmp = ImageDecode.Bounded(png, 256);
        Assert.NotNull(bmp);
        Assert.True((long)bmp.Width * bmp.Height <= ImageDecode.MaxPixels);
    }

    /// <summary>End to end through the grid's cache: a bomb resolves to Failed, which the
    /// tile renders as the image-kind icon.</summary>
    [Fact]
    public void TheThumbnailCacheFailsABombInsteadOfDecodingIt()
    {
        var dir = Scratch();
        var bomb = Path.Combine(dir, "bomb.png");
        HugePng.Write(bomb, 12000, 12000);
        var info = new FileInfo(bomb);

        using var cache = new ThumbnailCache(() => { });
        var deadline = DateTime.UtcNow.AddSeconds(30);
        ThumbnailCache.Status status;
        while (true)
        {
            (_, status) = cache.Get(bomb, info.Length, info.LastWriteTime, 256);
            if (status != ThumbnailCache.Status.Pending) break;
            Assert.True(DateTime.UtcNow < deadline, "the decode never settled");
            Thread.Sleep(25);
        }
        Assert.Equal(ThumbnailCache.Status.Failed, status);
    }
    private static void WriteJpeg(string path, int width, int height)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height);
        bitmap.Erase(SkiaSharp.SKColors.CornflowerBlue);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 80);
        File.WriteAllBytes(path, data.ToArray());
    }

    /// <summary>Writes a valid PNG of arbitrary declared dimensions whose pixels are one long
    /// run of zeroes — kilobytes on disk, whatever you asked for once decoded.</summary>
    private static class HugePng
    {
        public static void Write(string path, int width, int height, byte colourType = 0, int bytesPerPixel = 1)
        {
            using var fs = File.Create(path);
            fs.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

            var ihdr = new byte[13];
            WriteBigEndian(ihdr, 0, width);
            WriteBigEndian(ihdr, 4, height);
            ihdr[8] = 8;              // bit depth
            ihdr[9] = colourType;     // 0 = greyscale, 6 = RGBA
            Chunk(fs, "IHDR", ihdr);

            // One zlib stream over height rows of (filter byte + row bytes), all zero.
            using var raw = new MemoryStream();
            raw.WriteByte(0x78); raw.WriteByte(0x01);
            long total = (long)height * ((long)width * bytesPerPixel + 1);
            using (var deflate = new DeflateStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            {
                var zeros = new byte[64 * 1024];
                for (long left = total; left > 0;)
                {
                    int n = (int)Math.Min(zeros.Length, left);
                    deflate.Write(zeros, 0, n);
                    left -= n;
                }
            }
            // Adler-32 over all zeroes: a stays 1, b accumulates to the byte count.
            uint adler = ((uint)(total % 65521) << 16) | 1u;
            raw.Write([(byte)(adler >> 24), (byte)(adler >> 16), (byte)(adler >> 8), (byte)adler]);
            Chunk(fs, "IDAT", raw.ToArray());
            Chunk(fs, "IEND", []);
        }

        private static void WriteBigEndian(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var length = new byte[4];
            WriteBigEndian(length, 0, data.Length);
            s.Write(length);
            var tag = System.Text.Encoding.ASCII.GetBytes(type);
            s.Write(tag);
            s.Write(data);
            var crc = new byte[4];
            WriteBigEndian(crc, 0, unchecked((int)Crc32(tag, data)));
            s.Write(crc);
        }

        private static uint Crc32(byte[] a, byte[] b)
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            uint crc = 0xFFFFFFFFu;
            foreach (var x in a) crc = table[(crc ^ x) & 0xFF] ^ (crc >> 8);
            foreach (var x in b) crc = table[(crc ^ x) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
