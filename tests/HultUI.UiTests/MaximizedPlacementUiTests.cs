using HultUI.Core.Builders;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Platform.SDL3;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// A placement set on a shown maximized window lands where it was put, on both frames. On the
/// custom frame the restore was the frame's posted WM_SYSCOMMAND, which landed after the move and
/// took the window back to the bounds from before the maximize: the placement was undone.
/// </summary>
[Collection("ui")] // one Application per process, and SDL is process-global
public sealed class MaximizedPlacementUiTests
{
    public static readonly TheoryData<WindowStyle> Styles = new() { WindowStyle.Borderless, WindowStyle.Default };

    [UiTheory]
    [MemberData(nameof(Styles))]
    public void APlacementOnAShownMaximizedWindow_LandsWhereItWasPut(WindowStyle style)
    {
        using var guard = ForegroundGuard.Begin();
        var area = Monitors.WorkAreas()[0];
        using var app = new Application(new SdlWindowingPlatform(), "maximized placement", 640, 480, style);
        app.SetRoot(Box.New().FlexGrow(1).Background(new Color(40, 80, 120)));
        var window = app.PrimaryWindow;
        window.Placement = new WindowPlacement((int)area.X + 80, (int)area.Y + 80, 800, 600, true);
        for (int i = 0; i < 3; i++) app.RunFrame(TimeSpan.FromMilliseconds(16));
        Assert.True(window.IsMaximized);

        var half = new WindowPlacement((int)area.X, (int)area.Y, (int)area.Width / 2, (int)area.Height, false);
        window.Placement = half;
        for (int i = 0; i < 5; i++) app.RunFrame(TimeSpan.FromMilliseconds(16));

        Assert.False(window.IsMaximized);
        Assert.Equal(half, window.Placement);
    }
}
