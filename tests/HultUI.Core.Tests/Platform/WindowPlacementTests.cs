using System.Text.Json;
using System.Text.Json.Serialization;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Platform;

public sealed class PlacementHolder
{
    public WindowPlacement? Placement { get; set; }
}

[JsonSerializable(typeof(PlacementHolder))]
internal partial class PlacementTestJsonContext : JsonSerializerContext;

/// <summary>
/// <see cref="WindowPlacement.FitTo"/>: saved bounds coming back on a desktop that changed. The
/// layout is this development machine's — a 3840×2160 primary at the origin with the task bar
/// along the bottom, and a 2560×1440 monitor to its left, 314 px lower.
/// </summary>
public sealed class WindowPlacementTests
{
    private static readonly Rect Primary = new(0, 0, 3840, 2088);
    private static readonly Rect Left = new(-2560, 314, 2560, 1392);
    private static readonly Rect[] Both = [Primary, Left];

    [Fact]
    public void AWindowOnAMonitor_StaysExactlyWhereItWas()
    {
        var p = new WindowPlacement(200, 150, 1000, 700, false);
        Assert.Same(p, p.FitTo(Both));
    }

    [Fact]
    public void AWindowAcrossTwoMonitors_StaysWhileItsTopIsOnEither()
    {
        // Mostly on the left monitor, but its top edge (y 100) is above that monitor and on the
        // primary — still grabbable, so left alone.
        var p = new WindowPlacement(-1500, 100, 1800, 900, false);
        Assert.Same(p, p.FitTo(Both));
    }

    [Fact]
    public void AMonitorThatIsGone_CentresTheWindowOnTheNearestOne()
    {
        // Saved on a third monitor to the right of the primary, unplugged since.
        var fit = new WindowPlacement(5000, 400, 1000, 700, false).FitTo(Both);
        Assert.Equal(new WindowPlacement(1420, 694, 1000, 700, false), fit);
    }

    [Fact]
    public void ATitleBarAboveTheMonitor_SlidesDownOntoIt()
    {
        // On the left monitor only, with its top above that monitor's: the caption is off the
        // screen and the window could never be dragged back by it.
        var fit = new WindowPlacement(-2000, 100, 1000, 700, false).FitTo(Both);
        Assert.Equal(new WindowPlacement(-2000, 314, 1000, 700, false), fit);
    }

    [Fact]
    public void ATopEdgeWithinTheResizeBorder_CountsAsOnScreen()
    {
        var inReach = new WindowPlacement(400, -WindowPlacement.TopTolerance, 1000, 700, false);
        Assert.Same(inReach, inReach.FitTo(Both));

        var above = inReach with { Y = -WindowPlacement.TopTolerance - 1 };
        Assert.Equal(above with { Y = 0 }, above.FitTo(Both));
    }

    [Fact]
    public void AWindowHangingOffTheBottom_ByItsCaption_SlidesUp()
    {
        // Only the top 20 px are on screen: less than the caption band.
        var fit = new WindowPlacement(400, 2068, 1000, 700, false).FitTo(Both);
        Assert.Equal(new WindowPlacement(400, 1388, 1000, 700, false), fit);
    }

    [Fact]
    public void ASliverOnScreen_IsNotEnoughToGrab()
    {
        // 40 px of the top edge on the primary's right side: under the 100 px minimum.
        var fit = new WindowPlacement(3800, 300, 1000, 700, false).FitTo(Both);
        Assert.Equal(new WindowPlacement(2840, 300, 1000, 700, false), fit);
    }

    [Fact]
    public void ANarrowWindow_NeedsAllOfItsWidthOnScreen()
    {
        var whole = new WindowPlacement(3780, 300, 60, 200, false);
        Assert.Same(whole, whole.FitTo(Both));

        var cut = whole with { X = 3790 };
        Assert.Equal(whole, cut.FitTo(Both));
    }

    [Fact]
    public void AWindowLargerThanItsMonitor_ShrinksToTheWorkArea()
    {
        // Saved on a bigger screen, or before the resolution went down.
        var fit = new WindowPlacement(100, 100, 4000, 3000, false).FitTo(Both);
        Assert.Equal(new WindowPlacement(0, 0, 3840, 2088, false), fit);
    }

    [Fact]
    public void Maximized_SurvivesTheFit()
    {
        var fit = new WindowPlacement(9000, 9000, 1000, 700, true).FitTo(Both);
        Assert.True(fit.Maximized);
        Assert.Equal(1420, fit.X);
    }

    [Fact]
    public void NoWorkAreas_LeavesTheBoundsAlone()
    {
        var p = new WindowPlacement(-90000, 5, 10, 10, false);
        Assert.Same(p, p.FitTo([]));
    }

    [Fact]
    public void ZeroSizedBounds_AreNotUsable()
    {
        Assert.True(new WindowPlacement(0, 0, 1, 1, false).IsUsable);
        Assert.False(new WindowPlacement(0, 0, 0, 700, false).IsUsable);
        Assert.False(new WindowPlacement(0, 0, 1000, -1, false).IsUsable);
    }

    [Fact]
    public void ItRoundTripsThroughSourceGeneratedJson_WithoutItsDerivedFlag()
    {
        var holder = new PlacementHolder { Placement = new WindowPlacement(-2000, 500, 1000, 700, true) };
        var json = JsonSerializer.Serialize(holder, PlacementTestJsonContext.Default.PlacementHolder);

        Assert.DoesNotContain("IsUsable", json);
        var back = JsonSerializer.Deserialize(json, PlacementTestJsonContext.Default.PlacementHolder);
        Assert.Equal(holder.Placement, back!.Placement);
    }
}
