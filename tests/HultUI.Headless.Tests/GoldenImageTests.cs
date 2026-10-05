using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Rendering.Skia;
using SkiaSharp;
using Xunit;

namespace HultUI.Headless.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// The golden-image harness itself: pinned text, the comparison and its tolerance, and what
/// <see cref="GoldenImage.Match(SKImage, string, GoldenTolerance?, string)"/> does with
/// baselines and failures on disk. The control goldens are in <see cref="ControlGoldenTests"/>.
/// </summary>
public sealed class GoldenImageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hultui-golden-" + Guid.NewGuid().ToString("N"));
    private readonly string? _update = Environment.GetEnvironmentVariable(GoldenImage.UpdateVariable);
    private readonly string? _failures = Environment.GetEnvironmentVariable(GoldenImage.FailureDirectoryVariable);

    public GoldenImageTests()
    {
        // Each test decides for itself whether it is updating; a run of the whole suite with
        // the variable set must not turn the failures below into writes.
        Environment.SetEnvironmentVariable(GoldenImage.UpdateVariable, null);
        Environment.SetEnvironmentVariable(GoldenImage.FailureDirectoryVariable, Path.Combine(_dir, "failures"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(GoldenImage.UpdateVariable, _update);
        Environment.SetEnvironmentVariable(GoldenImage.FailureDirectoryVariable, _failures);
        try { Directory.Delete(_dir, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    // A file in the temp folder stands in for the test's source file: baselines go beside it.
    private string Caller => Path.Combine(_dir, "FakeTests.cs");
    private string Baseline(string name) => Path.Combine(_dir, GoldenImage.BaselineFolder, name + ".png");
    private string Failure(string name, string kind) => Path.Combine(_dir, "failures", $"{name}.{kind}.png");

    private static SKImage Image(int width = 8, int height = 6, (int X, int Y, SKColor Color)[]? pixels = null)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(Grey);
        foreach (var (x, y, color) in pixels ?? []) bitmap.SetPixel(x, y, color);
        using var pixmap = bitmap.PeekPixels();
        return SKImage.FromPixelCopy(pixmap);
    }

    private static readonly SKColor Grey = new(240, 240, 240);

    // ── Pinned text ────────────────────────────────────────────────

    [Fact]
    public void PinnedText_ResolvesEveryFamily_ToTheBundledFaces()
    {
        using var session = HeadlessSession.Create(100, 100, options: HeadlessOptions.Golden());
        session.PumpFrame();

        foreach (var family in new[] { "Segoe UI", "Arial", "Helvetica Neue", FontFamilies.SansSerif, "nonexistent" })
            Assert.Equal("DejaVu Sans", TextMeasure.MeasureFont(TextMeasure.StyleFor(14f, family))!.Value.FamilyName);
        foreach (var family in new[] { FontFamilies.Monospace, "Consolas, monospace" })
        {
            var mono = TextMeasure.MeasureFont(TextMeasure.StyleFor(14f, family))!.Value;
            Assert.Equal("DejaVu Sans Mono", mono.FamilyName);
            Assert.True(mono.IsFixedPitch);
        }

        // Nothing falls back to the machine's fonts: a character DejaVu lacks stays missing,
        // where an unpinned session finds it in whatever CJK font is installed.
        Assert.False(TextMeasure.HasGlyph(TextMeasure.StyleFor(14f), '東'));
        Assert.True(TextMeasure.HasGlyph(TextMeasure.StyleFor(14f), 'Å'));
    }

    private static SKTypeface Bundled(string file)
    {
        using var stream = typeof(HeadlessOptions).Assembly.GetManifestResourceStream("HultUI.Headless.Fonts." + file)!;
        using var data = SKData.Create(stream);
        return SKTypeface.FromData(data)!;
    }

    [Fact]
    public void Pinned_PicksTheSlantBeforeTheWeight()
    {
        SKTypeface regular = Bundled("DejaVuSans.ttf"), bold = Bundled("DejaVuSans-Bold.ttf"),
            oblique = Bundled("DejaVuSans-Oblique.ttf"), mono = Bundled("DejaVuSansMono.ttf");
        var fonts = SkiaFontCollection.Pinned(regular, bold, oblique, mono);
        object Face(string family, FontWeight weight, FontSlant slant) => fonts.Resolve(family, weight, slant).NativeHandle;

        Assert.Same(regular, Face("Segoe UI", FontWeight.Regular, FontSlant.Normal));
        Assert.Same(bold, Face("Segoe UI", FontWeight.SemiBold, FontSlant.Normal));
        Assert.Same(oblique, Face("Segoe UI", FontWeight.Regular, FontSlant.Italic));
        // No bold oblique here: the oblique, since a reader sees the slant before the weight.
        Assert.Same(oblique, Face("Segoe UI", FontWeight.Bold, FontSlant.Oblique));
        // Only one fixed-pitch face, so every monospace style lands on it.
        Assert.Same(mono, Face(FontFamilies.Monospace, FontWeight.Bold, FontSlant.Italic));
        // A name a registered face carries wins over the generic fallback order.
        Assert.Same(mono, Face("DejaVu Sans Mono", FontWeight.Regular, FontSlant.Normal));
        Assert.True(fonts.IsPinned);
    }

    [Fact]
    public void Register_FindsAShippedFaceByName_AndLeavesOtherNamesToTheOS()
    {
        var shipped = Bundled("DejaVuSansMono.ttf");
        var fonts = new SkiaFontCollection();
        fonts.Register(shipped);

        Assert.False(fonts.IsPinned);
        // The same instance, not an installed copy of the same family (Linux CI has DejaVu).
        Assert.Same(shipped, fonts.Resolve("DejaVu Sans Mono", FontWeight.Regular, FontSlant.Normal).NativeHandle);
        Assert.Same(shipped, fonts.Resolve("Nope, DejaVu Sans Mono", FontWeight.Regular, FontSlant.Normal).NativeHandle);
        Assert.NotSame(shipped, fonts.Resolve("sans-serif", FontWeight.Regular, FontSlant.Normal).NativeHandle);
        Assert.NotNull(fonts.MatchCharacter('A', FontWeight.Regular, FontSlant.Normal));
    }

    [Fact]
    public void PinnedText_DrawsTheTextAsOutlines()
    {
        using var session = HeadlessSession.Create(120, 40, options: HeadlessOptions.Golden());
        session.Application.Theme = Theme.Light;
        session.Window.SetRoot(Box.New().Padding(new Thickness(8)).Children(Text.New("MMMM").FontSize(20)));
        session.PumpFrame();

        using var image = session.Window.CapturePixels();
        using var bitmap = SKBitmap.FromImage(image);
        int ink = 0;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                if (bitmap.GetPixel(x, y).Red < 100) ink++;
        // Four 20 px Ms are a few hundred dark pixels; an outline path that came out empty is none.
        Assert.InRange(ink, 150, 2000);
    }

    [Fact]
    public void Match_RefusesAWindowWhoseTextIsNotPinned()
    {
        using var session = HeadlessSession.Create(40, 40);
        session.PumpFrame();
        var e = Assert.Throws<InvalidOperationException>(() => GoldenImage.Match(session.Window, "x", callerFilePath: Caller));
        Assert.Contains("HeadlessOptions.Golden()", e.Message);
    }

    // ── Comparison ────────────────────────────────────────────────

    [Fact]
    public void Compare_IdenticalImages_Pass()
    {
        using var a = Image();
        using var b = Image();
        var result = GoldenImage.Compare(a, b, GoldenTolerance.Exact);
        Assert.True(result.Passed);
        Assert.Equal(0, result.DifferentPixels);
        Assert.Equal(0, result.MaxChannelDelta);
    }

    [Fact]
    public void Compare_AChannelWithinTolerance_Passes_OneBeyondFails()
    {
        using var expected = Image();
        using var near = Image(pixels: [(3, 2, new SKColor(242, 240, 240))]);
        using var far = Image(pixels: [(3, 2, new SKColor(243, 240, 240))]);

        var within = GoldenImage.Compare(expected, near);
        Assert.True(within.Passed);
        Assert.Equal(2, within.MaxChannelDelta);

        var beyond = GoldenImage.Compare(expected, far);
        Assert.False(beyond.Passed);
        Assert.Equal(1, beyond.DifferentPixels);
        Assert.Equal(3, beyond.MaxChannelDelta);
        Assert.Equal(SKRectI.Create(3, 2, 1, 1), beyond.Bounds);

        Assert.False(GoldenImage.Compare(expected, near, GoldenTolerance.Exact).Passed);
    }

    [Fact]
    public void Compare_AlphaCounts()
    {
        using var expected = Image();
        using var faded = Image(pixels: [(0, 0, Grey.WithAlpha(200))]);
        Assert.Equal(55, GoldenImage.Compare(expected, faded).MaxChannelDelta);
    }

    [Fact]
    public void Compare_PixelAllowance_PassesThatManyAndNoMore()
    {
        using var expected = Image();
        using var three = Image(pixels: [(0, 0, SKColors.Red), (7, 0, SKColors.Red), (2, 5, SKColors.Red)]);

        Assert.True(GoldenImage.Compare(expected, three, new GoldenTolerance(Pixels: 3)).Passed);
        var two = GoldenImage.Compare(expected, three, new GoldenTolerance(Pixels: 2));
        Assert.False(two.Passed);
        Assert.Equal(3, two.DifferentPixels);
        Assert.Equal(SKRectI.Create(0, 0, 8, 6), two.Bounds);
    }

    [Fact]
    public void Compare_DifferentSizes_NeverPass()
    {
        using var expected = Image(8, 6);
        using var actual = Image(8, 7);
        var result = GoldenImage.Compare(expected, actual, new GoldenTolerance(255, int.MaxValue));
        Assert.True(result.SizeDiffers);
        Assert.False(result.Passed);
    }

    // ── Baselines and failures on disk ────────────────────────────

    [Fact]
    public void Match_WithoutABaseline_FailsAndLeavesTheFrame_UpdatingWritesIt()
    {
        using var frame = Image(pixels: [(1, 1, SKColors.Blue)]);

        var e = Assert.Throws<GoldenImageMismatchException>(() => GoldenImage.Match(frame, "fresh", callerFilePath: Caller));
        Assert.Null(e.Comparison);
        Assert.Equal(Baseline("fresh"), e.BaselinePath);
        Assert.Contains(GoldenImage.UpdateVariable, e.Message);
        Assert.True(File.Exists(e.ActualPath));
        Assert.False(File.Exists(Baseline("fresh")));

        Environment.SetEnvironmentVariable(GoldenImage.UpdateVariable, "1");
        GoldenImage.Match(frame, "fresh", callerFilePath: Caller);
        Assert.True(File.Exists(Baseline("fresh")));

        // Written exactly: the frame it was made from matches it with no tolerance at all.
        Environment.SetEnvironmentVariable(GoldenImage.UpdateVariable, null);
        GoldenImage.Match(frame, "fresh", GoldenTolerance.Exact, Caller);
    }

    [Fact]
    public void Match_AChangedFrame_WritesActualExpectedAndDiff_APassClearsThem()
    {
        using var before = Image(pixels: [(1, 1, SKColors.Blue)]);
        using var after = Image(pixels: [(1, 1, SKColors.Blue), (5, 4, SKColors.Red)]);
        Environment.SetEnvironmentVariable(GoldenImage.UpdateVariable, "1");
        GoldenImage.Match(before, "changed", callerFilePath: Caller);
        Environment.SetEnvironmentVariable(GoldenImage.UpdateVariable, null);

        var e = Assert.Throws<GoldenImageMismatchException>(() => GoldenImage.Match(after, "changed", callerFilePath: Caller));
        Assert.Equal(1, e.Comparison!.DifferentPixels);
        Assert.Contains("differs in 1 pixel", e.Message);
        Assert.Equal(Failure("changed", "actual"), e.ActualPath);
        Assert.Equal(Failure("changed", "expected"), e.ExpectedPath);
        Assert.Equal(Failure("changed", "diff"), e.DiffPath);

        // The diff marks the changed pixel red and greys out the rest.
        using (var diff = SKBitmap.Decode(e.DiffPath))
        {
            Assert.Equal(new SKColor(255, 0, 0), diff.GetPixel(5, 4));
            var other = diff.GetPixel(0, 0);
            Assert.Equal(other.Red, other.Green);
            Assert.Equal(other.Green, other.Blue);
        }

        GoldenImage.Match(before, "changed", callerFilePath: Caller);
        Assert.False(File.Exists(Failure("changed", "actual")));
        Assert.False(File.Exists(Failure("changed", "diff")));
    }

    [Fact]
    public void Match_Updating_RewritesADifferentBaseline_ButNotOneWithinTolerance()
    {
        using var before = Image(pixels: [(1, 1, SKColors.Blue)]);
        using var noise = Image(pixels: [(1, 1, SKColors.Blue), (6, 3, new SKColor(241, 240, 240))]);
        using var after = Image(pixels: [(1, 1, SKColors.Green)]);
        Environment.SetEnvironmentVariable(GoldenImage.UpdateVariable, "1");
        GoldenImage.Match(before, "rewrite", callerFilePath: Caller);
        byte[] original = File.ReadAllBytes(Baseline("rewrite"));

        GoldenImage.Match(noise, "rewrite", callerFilePath: Caller);
        Assert.Equal(original, File.ReadAllBytes(Baseline("rewrite")));

        GoldenImage.Match(after, "rewrite", callerFilePath: Caller);
        Assert.NotEqual(original, File.ReadAllBytes(Baseline("rewrite")));
        Environment.SetEnvironmentVariable(GoldenImage.UpdateVariable, null);
        GoldenImage.Match(after, "rewrite", GoldenTolerance.Exact, Caller);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("sub/name")]
    [InlineData("C:name")]
    [InlineData(".hidden")]
    [InlineData("space name")]
    public void Match_RejectsNamesThatAreNotOnePortableFile(string name)
    {
        using var frame = Image();
        Assert.Throws<ArgumentException>(() => GoldenImage.Match(frame, name, callerFilePath: Caller));
    }

    [Fact]
    public void Match_FromASourcePathThatIsNotHere_SaysWhy()
    {
        using var frame = Image();
        var e = Assert.Throws<InvalidOperationException>(() =>
            GoldenImage.Match(frame, "x", callerFilePath: "/_/tests/Somewhere/FakeTests.cs"));
        Assert.Contains("PathMap", e.Message);
    }
}
