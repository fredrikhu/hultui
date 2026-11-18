using System.Globalization;
using HultUI.Core.Builders;
using HultUI.Core.Dialogs;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A dialog fades in by its parts: the scrim's colour from clear, and the panel at its own
/// opacity. It used to fade the full-window scrim's opacity, so every fade frame drew the whole
/// window through an offscreen group the window's size, and on GL Skia kept that texture for the
/// window's life (18 MB at 2160×1350). The panel's group is its own size.
/// </summary>
public sealed class DialogFadeTests
{
    private const int Width = 800, Height = 600;
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1 / 120.0);

    private static (HeadlessSession Session, Element Root) Page(bool record = false)
    {
        var session = HeadlessSession.Create(Width, Height,
            options: record ? new HeadlessOptions { RecordDrawCalls = true } : null);
        var root = Box.New().Width(Dimension.Percent(100)).Height(Dimension.Percent(100)).Background(Color.White);
        session.Window.SetRoot(root);
        session.PumpFrame();
        return (session, root);
    }

    private static BoxElement Scrim(HeadlessSession session) => (BoxElement)session.Window.OverlayContents[^1];

    [Fact]
    public void NoFadeFrameDrawsThroughAGroupTheWindowsSize()
    {
        var (session, root) = Page(record: true);
        using var owned = session;
        session.Window.RenderLog.Clear();
        _ = Dialog.Confirm(root.OverlayHost!, "Delete branch", "The branch has commits no other branch has.");

        var groups = new List<Rect>();
        for (int i = 0; i < 20; i++)
        {
            session.PumpFrame(Frame);
            groups.AddRange(session.Window.RenderLog.Named("PushOpacity").Select(op => Bounds(op.Args)));
            session.Window.RenderLog.Clear();
        }

        // The panel fades in a group around it and its shadow; nothing else is faded as a group.
        var panel = Scrim(session).Children[0].Layout.AbsoluteBorderBox;
        Assert.NotEmpty(groups);
        foreach (var g in groups)
        {
            Assert.True(g.Width < Width && g.Height < Height, $"a group of {g} in an {Width}x{Height} window");
            Assert.True(g.X <= panel.X && g.Y <= panel.Y && g.Right >= panel.Right && g.Bottom >= panel.Bottom,
                $"the group {g} cuts the panel {panel}");
        }
    }

    [Fact]
    public void TheScrimFadesItsColourAndThePanelItsOpacity_ToTheSameLastFrame()
    {
        var (session, root) = Page();
        using var owned = session;
        var scrimColor = new Color(10, 20, 30, 150);
        Element? panel = null;
        _ = Dialog.Show<bool>(root.OverlayHost!, _ => panel = Box.New().Width(300).Height(200)
            .Background(Color.White).Opacity(0.9f), new DialogOptions { ScrimColor = scrimColor });
        var scrim = Scrim(session);

        var alphas = new List<int>();
        var opacities = new List<float>();
        for (int i = 0; i < 20; i++)
        {
            session.PumpFrame(Frame);
            Assert.Equal(1f, scrim.Opacity);
            alphas.Add(scrim.BackgroundColor.A);
            opacities.Add(panel!.Opacity);
        }

        Assert.True(alphas[0] > 0 && alphas[0] < 100, $"the scrim's first frame is at alpha {alphas[0]}");
        Assert.True(opacities[0] > 0f && opacities[0] < 0.5f, $"the panel's first frame is at {opacities[0]}");
        Assert.Equal(alphas.Order(), alphas);
        Assert.Equal(opacities.Order(), opacities);
        // The last frame is the dialog as it was: the scrim's own colour, the panel's own opacity.
        Assert.Equal(scrimColor, scrim.BackgroundColor);
        Assert.Equal(0.9f, panel!.Opacity);
    }

    [Fact]
    public void WithoutAScrim_OnlyThePanelFades()
    {
        var (session, root) = Page();
        using var owned = session;
        Element? panel = null;
        _ = Dialog.Show<bool>(root.OverlayHost!, _ => panel = Box.New().Width(300).Height(200).Background(Color.White),
            new DialogOptions { ShowScrim = false });
        var scrim = Scrim(session);

        session.PumpFrame(Frame);
        Assert.Equal(0, scrim.BackgroundColor.A);
        Assert.InRange(panel!.Opacity, 0.01f, 0.99f);
        for (int i = 0; i < 20; i++) session.PumpFrame(Frame);
        Assert.Equal(0, scrim.BackgroundColor.A);
        Assert.Equal(1f, panel.Opacity);
    }

    /// <summary>The panel is the app's, and its hover style keeps the look it had to go back to.
    /// Had the fade set its opacity directly, a hover that began mid-fade would take the half-faded
    /// value as that look, and the panel would stay half faded once the pointer left. (The button
    /// takes the focus, so no focus style of the panel's took that look before the fade.)</summary>
    [Fact]
    public void AHoverThatBeginsMidFade_LeavesThePanelAtItsOwnOpacity()
    {
        var (session, root) = Page();
        using var owned = session;
        BoxElement? panel = null;
        _ = Dialog.Show<bool>(root.OverlayHost!, _ => panel = Box.New().Width(300).Height(200)
            .Background(Color.White).BorderWidth(1f).BorderColor(Color.White)
            .OnHover(p => p.BorderColor = new Color(0, 0, 255, 255))
            .Children(Button.New("OK")));

        for (int i = 0; i < 3; i++) session.PumpFrame(Frame);
        Assert.IsType<ButtonElement>(session.Window.Window.FocusedElement);
        Assert.InRange(panel!.Opacity, 0.01f, 0.99f);
        var box = panel.Layout.AbsoluteBorderBox;
        session.Window.MoveMouse(box.X + box.Width / 2, box.Y + box.Height / 2);
        for (int i = 0; i < 20; i++) session.PumpFrame(Frame);
        Assert.Equal(new Color(0, 0, 255, 255), panel.BorderColor);
        session.Window.MoveMouse(2, 2);
        session.PumpFrame(Frame);

        Assert.Equal(Color.White, panel.BorderColor);
        Assert.Equal(1f, panel.Opacity);
    }

    private static Rect Bounds(string args)
    {
        int at = args.IndexOf("bounds=(", StringComparison.Ordinal);
        Assert.True(at >= 0, $"a group with no bounds: {args}");
        var inner = args[(at + 8)..args.LastIndexOf(')')];
        var parts = inner.Split(' ');
        var xy = parts[0].Split(',');
        var wh = parts[1].Split('x');
        static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        return new Rect(P(xy[0]), P(xy[1]), P(wh[0]), P(wh[1]));
    }
}
