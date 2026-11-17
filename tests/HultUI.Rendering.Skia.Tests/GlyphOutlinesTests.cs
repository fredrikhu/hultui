using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SkiaSharp;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Glyph outlines read by HarfBuzz from the font file (<see cref="GlyphOutlines"/>), which
/// <see cref="SkiaRenderContext.TextAsOutlines"/> draws so that golden images match on every OS.
/// These hold the draw API's exports, the outlines to the ones Skia's own scaler reads, and the
/// outlines' exact floats to one hash, which both CI operating systems have to reproduce.
/// </summary>
public class GlyphOutlinesTests
{
    // Two of the pinned faces HultUI.Headless embeds, linked into this project's output.
    private static readonly string FontDir = Path.Combine(AppContext.BaseDirectory, "Fonts");

    private static SKTypeface Face(string file) =>
        SKTypeface.FromFile(Path.Combine(FontDir, file)) ?? throw new FileNotFoundException(file);

    [Fact]
    public void TheDrawApi_IsInTheLibraryHarfBuzzSharpLoaded()
    {
        Assert.Null(GlyphOutlines.Missing);
        Assert.True(NativeLibrary.TryLoad(GlyphOutlines.Library, typeof(HarfBuzzSharp.Font).Assembly, null,
            out nint library));
        Assert.Empty(NativeTextBlob.Absent(library, GlyphOutlines.Exports));
    }

    /// <summary>The same outline Skia reads, wherever the glyph is put: for every glyph of a
    /// line, upright and oblique, the bounds agree with <see cref="SKFont.GetGlyphPath"/>'s to a
    /// fraction of a pixel. (The two OS backends disagree with each other by up to 0.02 px.)</summary>
    [Theory]
    [InlineData("DejaVuSans.ttf")]
    [InlineData("DejaVuSans-Oblique.ttf")]
    public void AnOutline_IsTheOneSkiaReads_PutWhereItWasAsked(string file)
    {
        using var typeface = Face(file);
        using var font = new SKFont(typeface, 14) { Hinting = SKFontHinting.None, Subpixel = true, LinearMetrics = true };
        using var outlines = new GlyphOutlines();
        var glyphs = font.GetGlyphs("Hamburgefonstiv 0123456789 the slant Åäö ÆØ fiffl “quotes” ½");
        foreach (var glyph in glyphs.Distinct())
        {
            using var skia = font.GetGlyphPath(glyph);
            using var builder = new SKPathBuilder();
            outlines.Add(builder, typeface, glyph, 14, 10.25f, 20.5f);
            using var harfBuzz = builder.Detach();

            if (skia is null || skia.IsEmpty)
            {
                Assert.True(harfBuzz.IsEmpty, $"glyph {glyph} has no outline in Skia but one from HarfBuzz");
                continue;
            }
            var expected = skia.Bounds;
            expected.Offset(10.25f, 20.5f);
            var actual = harfBuzz.Bounds;
            const float Close = 0.03f;
            Assert.True(Math.Abs(expected.Left - actual.Left) < Close && Math.Abs(expected.Top - actual.Top) < Close
                && Math.Abs(expected.Right - actual.Right) < Close && Math.Abs(expected.Bottom - actual.Bottom) < Close,
                $"glyph {glyph} of {file}: Skia's bounds {expected}, HarfBuzz's {actual}");
        }
    }

    [Fact]
    public void ASpace_AddsNothing()
    {
        using var typeface = Face("DejaVuSans.ttf");
        using var font = new SKFont(typeface, 14);
        using var outlines = new GlyphOutlines();
        using var builder = new SKPathBuilder();
        outlines.Add(builder, typeface, font.GetGlyphs(" ")[0], 14, 0, 0);
        using var path = builder.Detach();
        Assert.True(path.IsEmpty);
    }

    [Fact]
    public void OneHarfBuzzFont_IsKeptPerTypeface()
    {
        using var regular = Face("DejaVuSans.ttf");
        using var oblique = Face("DejaVuSans-Oblique.ttf");
        using var outlines = new GlyphOutlines();
        using var builder = new SKPathBuilder();
        foreach (var size in new[] { 11f, 14f, 24f })
        {
            outlines.Add(builder, regular, 36, size, 0, 0);
            outlines.Add(builder, oblique, 36, size, 0, 0);
        }
        Assert.Equal(2, outlines.FontCount);
    }

    /// <summary>
    /// The point of reading outlines through HarfBuzz: the exact floats of these glyphs, which
    /// DirectWrite and FreeType give differently (the oblique t, the Å and the ä, among others),
    /// hash the same on Windows and Linux, as CI runs this on both. A new HarfBuzz may change the
    /// hash, the same on both OSes; check the golden images, then update it.
    /// </summary>
    [Fact]
    public void TheOutlines_AreTheSameFloatsOnEveryOs()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var outlines = new GlyphOutlines();
        foreach (var file in new[] { "DejaVuSans.ttf", "DejaVuSans-Oblique.ttf" })
        {
            using var typeface = Face(file);
            using var font = new SKFont(typeface, 14);
            foreach (var size in new[] { 11f, 14f, 24f })
                foreach (var glyph in font.GetGlyphs("Italic 14 — the slant Åäö ÆØ fiffl “quotes” ½"))
                {
                    using var builder = new SKPathBuilder();
                    outlines.Add(builder, typeface, glyph, size, 16.5f, 100.25f);
                    using var path = builder.Detach();
                    hash.AppendData(BitConverter.GetBytes(path.VerbCount));
                    foreach (var point in path.Points)
                    {
                        hash.AppendData(BitConverter.GetBytes(point.X));
                        hash.AppendData(BitConverter.GetBytes(point.Y));
                    }
                }
        }
        const string Expected = "48B42D2F648F41D5D1A3A2F8D8DE5D733468D0EA3C67945DF7E72DC299019D62";
        string actual = Convert.ToHexString(hash.GetHashAndReset());
        Assert.True(actual == Expected, $"The outlines hash to {actual} on {RuntimeInformation.OSDescription}, not {Expected}.");
    }
}
