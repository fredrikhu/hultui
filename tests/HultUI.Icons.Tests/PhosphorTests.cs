using System.Linq;
using HultUI.Core.Primitives;
using HultUI.Icons;
using Xunit;

namespace HultUI.IconsTests;

/// <summary>
/// The bundled Phosphor set. An unknown name renders an empty box rather than throwing —
/// deliberately, so one typo cannot crash a menu — which means nothing at runtime would
/// tell you a name is wrong. These tests are what does.
/// </summary>
public class PhosphorTests
{
    [Fact]
    public void TheBundleIsNotEmpty()
    {
        Assert.NotEmpty(Phosphor.Names);
        Assert.True(Phosphor.Has("git-branch"));
    }

    [Fact]
    public void EveryBundledIconRastersToVisiblePixels()
    {
        // A `d` string that fails to parse yields an empty path, which draws nothing and
        // reports no error. Rasterize each and require some coverage.
        foreach (var name in Phosphor.Names)
        {
            var src = Phosphor.Source(name, 24f, Color.White);
            Assert.NotNull(src);
            Assert.True(src!.IsLoaded, name);
            Assert.True(src.IntrinsicSize.Width > 0 && src.IntrinsicSize.Height > 0, name);
        }
    }

    [Fact]
    public void AnUnknownNameIsSurvivable()
    {
        Assert.False(Phosphor.Has("not-a-real-icon"));
        Assert.Null(Phosphor.Source("not-a-real-icon", 16f, Color.White));

        // Draw still returns a correctly sized element, so a bad name costs the glyph
        // and not the layout around it.
        var element = Phosphor.Draw("not-a-real-icon", 16f, Color.White);
        Assert.NotNull(element);
    }

    [Fact]
    public void RastersAreCutAtDeviceResolution()
    {
        // A bitmap produced at layout size gets resampled on the way to the screen, and
        // the visible result is a soft or aliased icon. Asking for the device size means
        // the renderer scales nothing.
        Assert.Equal(13, Phosphor.Source("git-branch", 13f, Color.White, 1f)!.IntrinsicSize.Width);
        Assert.Equal(20, Phosphor.Source("git-branch", 13f, Color.White, 1.5f)!.IntrinsicSize.Width);
        Assert.Equal(26, Phosphor.Source("git-branch", 13f, Color.White, 2f)!.IntrinsicSize.Width);
    }

    [Fact]
    public void HairlineIconsStaySolidAtWindowControlSize()
    {
        // Phosphor's regular `minus` is a bar 6% of its box — 0.6px at the size the
        // window controls were using, straddling two pixel rows at ~25% each. That is
        // why the minimize button appeared to render nothing at all. The bold weight is
        // 9.4%, and the button is drawn larger, so the bar now clears a device pixel.
        // The caption glyphs: bold weight at the 11.5 box WinButton now uses.
        var src = Phosphor.Source("minus-bold", 11.5f, Color.White, 1f)!;

        // Peak alpha is the honest measure: the bar is sub-pixel, so it should render as
        // a partially transparent line rather than nothing at all.
        int peak = PeakAlpha(src);
        Assert.True(peak > 100, $"minus rendered with peak alpha {peak}; it is invisible");
    }

    private static int PeakAlpha(HultUI.Core.Rendering.IImageSource src)
    {
        var bmp = (SkiaSharp.SKBitmap)src.GetType()
            .GetProperty("Bitmap", System.Reflection.BindingFlags.NonPublic
                                   | System.Reflection.BindingFlags.Instance)!.GetValue(src)!;
        int peak = 0;
        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
            peak = Math.Max(peak, bmp.GetPixel(x, y).Alpha);
        return peak;
    }

    [Fact]
    public void RastersAreCachedPerNameColourAndSize()
    {
        var a = Phosphor.Source("git-branch", 16f, Color.White);
        var b = Phosphor.Source("git-branch", 16f, Color.White);
        Assert.Same(a, b);

        Assert.NotSame(a, Phosphor.Source("git-branch", 16f, Color.Red));
        Assert.NotSame(a, Phosphor.Source("git-branch", 32f, Color.White));
    }
}

/// <summary>
/// Junction names icons by Phosphor name in string constants. A name that is not in the
/// bundle draws an empty box and reports nothing, so nothing at runtime would catch a
/// typo or a rename of the generated set — this does.
/// </summary>
public class JunctionIconNameTests
{
    public static TheoryData<string, string> Constants()
    {
        var data = new TheoryData<string, string>();
        foreach (var f in typeof(GitClient.Junction.Icons).GetFields(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (f.IsLiteral && f.FieldType == typeof(string) && f.GetRawConstantValue() is string v)
                data.Add(f.Name, v);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Constants))]
    public void EveryIconConstantNamesABundledIcon(string constant, string iconName)
    {
        Assert.True(Phosphor.Has(iconName), $"Icons.{constant} = \"{iconName}\" is not in the bundled Phosphor set");
    }

    [Fact]
    public void TheWorkspacePickerKeysAllResolve()
    {
        // These are stored in user settings and mapped to glyphs at render time, so a
        // missing one shows an empty square in the picker rather than failing loudly.
        foreach (var key in GitClient.Junction.Icons.WorkspaceKeys)
            Assert.True(Phosphor.Has(key), key);
    }
}

/// <summary>
/// The nominal-to-box size conversion. Junction's icon sizes were chosen when icons were
/// text glyphs, and a vector fills more of its box than a glyph fills its em — so reusing
/// the numbers unchanged drew everything oversized.
/// </summary>
public class IconSizeTests
{
    [Fact]
    public void NominalSizesShrinkToTheirVisualEquivalent()
    {
        // Measured: the Unicode stand-ins inked ~64% of the font size, Phosphor paths ink
        // ~72% of the box. Under, but close.
        Assert.True(GitClient.Junction.Ui.IconBox(16f) < 16f);
        Assert.InRange(GitClient.Junction.Ui.IconBox(16f) / 16f, 0.85f, 0.92f);
    }

    [Fact]
    public void TheRelativeSizeHierarchyIsPreserved()
    {
        // One ratio applied uniformly, so the design's relationships — a small badge
        // against a larger button glyph — survive the correction.
        float badge = GitClient.Junction.Ui.IconBox(9f);
        float row = GitClient.Junction.Ui.IconBox(12f);
        float button = GitClient.Junction.Ui.IconBox(15f);
        Assert.True(badge < row && row < button);
        Assert.Equal(12f / 9f, row / badge, 1);
    }

    [Fact]
    public void SizesLandOnHalfPixels()
    {
        // Rounded to halves so rasters land on sensible pixel counts instead of
        // producing a slightly different bitmap per call site.
        foreach (var n in new[] { 9f, 10f, 11f, 12f, 13f, 13.5f, 14f, 15f, 16f, 17f, 26f })
            Assert.Equal(0f, GitClient.Junction.Ui.IconBox(n) * 2f % 1f);
    }
}
