using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Headless;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Where the ring actually lands, in pixels.
///
/// <para>The band maths is the part that can be quietly wrong: a uniform stroke is centred on
/// the rect it is given, so the rect has to be inflated to the <i>middle</i> of the ring rather
/// than to its outer edge. Getting that wrong is off by half a width — invisible at 1px, and
/// wrong everywhere. These use a 4px ring so a half-width error is two whole pixels.</para>
/// </summary>
public class OutlineRenderTests
{
    private const int Size = 60;

    /// <summary>A 20x20 box centred in a 60x60 frame: border box spans 20..40 on both axes.</summary>
    private static SkiaSharp.SKBitmap Render(Outline outline, float radius = 0f)
    {
        var session = HeadlessSession.Create(Size, Size);
        var inner = Box.New().Width(20).Height(20).Background(Color.White).CornerRadius(radius);
        inner.Outline = outline;

        session.Window.SetRoot(
            Box.New().Background(Color.Black)
                .Padding(new Thickness(20))
                .Children(inner));
        session.PumpFrame();

        using var image = session.Window.CapturePixels();
        var bitmap = SkiaSharp.SKBitmap.FromImage(image);
        session.Dispose();
        return bitmap;
    }

    private static bool IsRed(SkiaSharp.SKBitmap b, int x, int y)
    {
        var p = b.GetPixel(x, y);
        return p.Red > 180 && p.Green < 80 && p.Blue < 80;
    }

    /// <summary>
    /// At offset 0 the ring sits immediately outside the border box, entirely outside it.
    ///
    /// <para>A 4px ring on a box spanning 20..40 occupies 16..20 — so 16 through 19 are red, and
    /// 20 is still the box's own white. That last assertion is the one that says "outside": a
    /// ring drawn inside the box, or centred on its edge, would have painted over it.</para>
    /// </summary>
    [Fact]
    public void TheRingSitsOutsideTheBorderBox()
    {
        using var b = Render(new Outline(4f, Color.Red));

        Assert.True(IsRed(b, 17, 30), "the band, left side");
        Assert.True(IsRed(b, 19, 30), "up to the box edge");
        Assert.False(IsRed(b, 20, 30), "but not over the box itself");
        Assert.False(IsRed(b, 15, 30), "and not beyond the band");
        Assert.True(IsRed(b, 42, 30), "and the same on the right");
        Assert.True(IsRed(b, 30, 17), "and above");
        Assert.True(IsRed(b, 30, 42), "and below");
    }

    /// <summary>
    /// A negative offset pulls the ring inward — <c>-width</c> puts it exactly where a border of
    /// that width would have been, which is what makes it a drop-in for one.
    /// </summary>
    [Fact]
    public void ANegativeOffsetPutsTheRingWhereABorderWouldBe()
    {
        using var b = Render(new Outline(4f, Color.Red, Offset: -4f));

        Assert.False(IsRed(b, 19, 30), "nothing outside the box");
        Assert.True(IsRed(b, 21, 30), "the band is the outermost 4px inside it");
        Assert.True(IsRed(b, 23, 30), "still inside the band");
        Assert.False(IsRed(b, 25, 30), "and the middle of the box is untouched");
    }

    /// <summary>
    /// The ring follows a rounded box's curve instead of squaring off around it.
    ///
    /// <para>The radius has to grow with the ring, and forgetting that is invisible in every
    /// other assertion here — they all sample straight runs, where the two are identical. It
    /// shows only at the corner, and only clearly with a thick ring: a 12px ring on an 8px box
    /// leaves the ring's arc 4px further out than the box's, while an un-grown one keeps the
    /// box's tight radius on a rect 6px larger, which throws the whole arc outward.</para>
    ///
    /// <para>Measured on the top-left diagonal rather than derived — the first version of this
    /// test was reasoned out and asserted the wrong pixels, and failed against correct code.
    /// Grown: red from 14 to 21. Un-grown: red from 12 to 19. So both ends discriminate, and
    /// both are asserted, because an implementation can only be wrong in one direction and
    /// checking one end alone would not say which.</para>
    /// </summary>
    [Fact]
    public void TheRingFollowsARoundedCorner()
    {
        using var b = Render(new Outline(12f, Color.Red), radius: 8f);

        Assert.True(IsRed(b, 20, 20), "the ring's inner arc reaches here");
        Assert.True(IsRed(b, 21, 21), "and here, right up against the box's own corner");
        Assert.False(IsRed(b, 13, 13), "and does not reach out to here");
        Assert.False(IsRed(b, 12, 12), "nor here — an un-grown radius paints both");
    }

    /// <summary>An offset opens a gap: the ring moves out, and what it moves off is the box's
    /// own colour rather than the ring's — a ring that merely got thicker would fill the gap.</summary>
    [Fact]
    public void APositiveOffsetLeavesAGap()
    {
        using var b = Render(new Outline(2f, Color.Red, Offset: 4f));

        Assert.False(IsRed(b, 19, 30), "the gap, next to the box");
        Assert.False(IsRed(b, 17, 30), "still the gap");
        Assert.True(IsRed(b, 15, 30), "the band, 4px out");
        Assert.False(IsRed(b, 12, 30), "and nothing past it");
    }
}
