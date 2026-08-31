using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Rendering.Skia;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// A border with different widths per side follows the box's corner radius.
///
/// <para>It used to paint each edge as a plain rectangle, which is exact on a square box and
/// wrong on a rounded one: the bar runs corner to corner and sticks out past the arc, so a
/// bottom-only border under a 4px-radius text field ends in two square tabs. It is the sort of
/// thing that is invisible at 100% and obvious the moment anyone looks closely — which is how it
/// was found, in the sample's search box.</para>
///
/// <para>Both halves are asserted here, because the fix is only correct if the square-box path
/// is left exactly as it was: that path is what every single-sided divider in the shipped apps
/// draws, and moving it half a pixel would show up everywhere at once.</para>
/// </summary>
public class PerSideBorderRadiusTests
{
    private const int Size = 60;

    /// <summary>Renders a box with a bottom-only border and returns the frame.</summary>
    private static byte[] Draw(float radius, out int rowBytes)
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(Size, Size, 1f);
        var ctx = surface.RenderContext;

        ctx.BeginFrame(Size, Size, 1f, Color.White);
        ctx.DrawBorder(new Rect(10, 10, 40, 40), new CornerRadius(radius), Color.Red,
            new Thickness(0, 0, 4, 0), BorderStyle.Solid);
        ctx.EndFrame();
        surface.Present();

        return surface.ReadPixels(out _, out _, out rowBytes);
    }

    private static bool IsRed(byte[] px, int rowBytes, int x, int y)
    {
        int i = y * rowBytes + x * 4;
        return px[i] > 180 && px[i + 1] < 80 && px[i + 2] < 80;
    }

    /// <summary>
    /// The corner is the whole point. With a 10px radius the bottom-left corner of the box is
    /// well outside the arc, and a bar drawn corner to corner paints it.
    /// </summary>
    [Fact]
    public void ARoundedBoxKeepsItsCornersClear()
    {
        var px = Draw(radius: 10f, out int rowBytes);

        Assert.True(IsRed(px, rowBytes, x: 30, y: 48), "the middle of the bottom edge should be painted");
        Assert.False(IsRed(px, rowBytes, x: 11, y: 48), "the bottom-left corner is outside the arc");
        Assert.False(IsRed(px, rowBytes, x: 48, y: 48), "and so is the bottom-right");
    }

    /// <summary>
    /// The ends curve inward through the band rather than running straight.
    ///
    /// <para>Measured: the painted run is 13–46 at the top of the 4px band and 18–41 at the
    /// bottom, because the arc is carrying the edge outward and down. A bar is 10–49 on every
    /// row of the band, so this is the assertion that tells a curve from a rectangle — "the
    /// corner is clear" alone would also pass for a bar that had merely been made shorter.</para>
    /// </summary>
    [Fact]
    public void TheEndsCurveInwardThroughTheBand()
    {
        var px = Draw(radius: 10f, out int rowBytes);

        var (topFirst, topLast) = Run(px, rowBytes, y: 46);
        var (bottomFirst, bottomLast) = Run(px, rowBytes, y: 49);

        Assert.True(bottomFirst > topFirst, $"left end should draw in: {topFirst} -> {bottomFirst}");
        Assert.True(bottomLast < topLast, $"right end should draw in: {topLast} -> {bottomLast}");
        Assert.True(topFirst > 10 && topLast < 49, "and neither row reaches the box's own corners");
    }

    private static (int First, int Last) Run(byte[] px, int rowBytes, int y)
    {
        int first = -1, last = -1;
        for (int x = 5; x < Size - 5; x++)
            if (IsRed(px, rowBytes, x, y)) { if (first < 0) first = x; last = x; }
        return (first, last);
    }

    /// <summary>The square-box path is untouched: a bar corner to corner, which is exact.</summary>
    [Fact]
    public void ASquareBoxStillPaintsTheWholeEdge()
    {
        var px = Draw(radius: 0f, out int rowBytes);

        Assert.True(IsRed(px, rowBytes, x: 30, y: 48), "middle of the edge");
        Assert.True(IsRed(px, rowBytes, x: 11, y: 48), "bottom-left corner, square");
        Assert.True(IsRed(px, rowBytes, x: 48, y: 48), "bottom-right corner, square");
    }

    /// <summary>And it sits in the same place either way — the rounded path insets its stroke to
    /// land where the square path's bar does, so switching a control's radius does not also move
    /// its border.</summary>
    [Fact]
    public void TheEdgeSitsInsideTheBoxInBothPaths()
    {
        var rounded = Draw(radius: 10f, out int rowBytes);
        var square = Draw(radius: 0f, out _);

        // The box is 10..50; a 4px bottom border occupies 46..49 inside it, and nothing below.
        foreach (var px in new[] { rounded, square })
        {
            Assert.True(IsRed(px, rowBytes, x: 30, y: 47));
            Assert.True(IsRed(px, rowBytes, x: 30, y: 49));
            Assert.False(IsRed(px, rowBytes, x: 30, y: 51));
        }
    }
}
