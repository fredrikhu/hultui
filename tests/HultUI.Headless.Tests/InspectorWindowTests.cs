using HultUI.Core.Builders;
using HultUI.Core.DevTools;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// End-to-end coverage for the DevTools inspector: it runs in its own window, so the
/// interesting behaviour is between two windows — the picker consuming input in the
/// app while the panel renders elsewhere. Everything here drives the real input
/// pipeline rather than calling the model directly.
/// </summary>
public class InspectorWindowTests
{
    private const float RowH = 40f;

    /// <summary>
    /// Three stacked rows, each 40 tall, the middle one carrying a click handler so
    /// tests can prove the picker swallowed the click instead of the app running it.
    /// </summary>
    private static (Element root, Element[] rows, Func<int> clicks) BuildApp(HeadlessSession session)
    {
        int clicks = 0;
        var rows = new Element[3];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = Box.New().Height(RowH).FlexShrink(0).Background(Color.Gray);
            rows[i].Name = $"row{i}";
        }
        rows[1].OnClick(_ => clicks++);

        var root = Flex.Column().Children(rows);
        root.Name = "app-root";
        session.Window.SetRoot(root);
        session.PumpFrame();
        return (root, rows, () => clicks);
    }

    private static ElementInspector Inspector(HeadlessSession session) =>
        session.Window.Window.DevTools.Inspector
        ?? throw new Xunit.Sdk.XunitException("The window has no inspector attached.");

    // Centre of a row, in window coordinates.
    private static Point Centre(Element element)
    {
        var box = element.Layout.AbsoluteBorderBox;
        return new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    [Fact]
    public void OpenInspector_AddsAWindowThatIsNotItselfInspectable()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        BuildApp(session);

        session.Application.OpenInspector();
        session.PumpFrame();

        Assert.True(session.Application.IsInspectorOpen);
        var panel = Assert.Single(session.Application.Windows, w => w.IsDevToolsWindow);
        Assert.True(panel.IsOpen);
        // The app window is the one being inspected, and it stays inspectable.
        Assert.False(session.Window.Window.IsDevToolsWindow);
    }

    [Fact]
    public void OpenInspector_TwiceReusesTheSamePanel()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        BuildApp(session);

        session.Application.OpenInspector();
        session.Application.OpenInspector();
        session.PumpFrame();

        Assert.Single(session.Application.Windows, w => w.IsDevToolsWindow);
    }

    [Fact]
    public void CtrlShiftI_OpensAndClosesThePanel()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        BuildApp(session);

        session.Window.PressKey(KeyCode.I, KeyModifiers.Ctrl | KeyModifiers.Shift);
        session.PumpFrame();
        Assert.True(session.Application.IsInspectorOpen);

        session.Window.PressKey(KeyCode.I, KeyModifiers.Ctrl | KeyModifiers.Shift);
        session.PumpFrame();
        Assert.False(session.Application.IsInspectorOpen);
    }

    [Fact]
    public void CtrlShiftC_IsLeftToTheApp()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        BuildApp(session);

        session.Window.PressKey(KeyCode.C, KeyModifiers.Ctrl | KeyModifiers.Shift);
        session.PumpFrame();

        // Browsers bind this to "inspect", but a browser is the devtools host. A
        // framework claiming it takes the chord from the app underneath, before
        // dispatch and without recourse — which is exactly what it did to Vesper's
        // "copy as path". OpenInspector(startPicking: true) and the panel's Pick
        // button arm the picker instead.
        Assert.False(session.Application.IsInspectorOpen);
    }

    [Fact]
    public void Hotkeys_CanBeTurnedOffForAppsThatBindThoseChords()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        BuildApp(session);
        session.Application.DevToolsHotkeysEnabled = false;

        session.Window.PressKey(KeyCode.I, KeyModifiers.Ctrl | KeyModifiers.Shift);
        session.PumpFrame();

        Assert.False(session.Application.IsInspectorOpen);
    }

    [Fact]
    public void TurningTheHotkeysOff_AlsoSilencesTheF12HudAndF11Tracing()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        BuildApp(session);
        var window = session.Window.Window;
        session.Application.DevToolsHotkeysEnabled = false;

        session.Window.PressKey(KeyCode.F12);
        session.Window.PressKey(KeyCode.F11);
        session.PumpFrame();

        // The whole developer surface goes dark together. Shipped builds set this,
        // and a HUD or a trace file appearing on a stray function key is exactly
        // what they are switching off.
        Assert.False(window.DevTools.IsEnabled);
        Assert.False(window.Tracer.IsRecording);
    }

    [Fact]
    public void F12StillWorksWhenTheHotkeysAreLeftOn()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        BuildApp(session);
        var window = session.Window.Window;

        session.Window.PressKey(KeyCode.F12);
        session.PumpFrame();

        Assert.True(window.DevTools.IsEnabled);
    }

    [Fact]
    public void Picking_PreviewsTheElementUnderThePointer()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        var (_, rows, _) = BuildApp(session);
        session.Application.OpenInspector(startPicking: true);
        session.PumpFrame();

        var target = Centre(rows[2]);
        session.Window.MoveMouse(target.X, target.Y);
        session.PumpFrame();

        Assert.Same(rows[2], Inspector(session).Hovered);
        // The preview is what the app's highlight outlines while picking.
        Assert.Same(rows[2], Inspector(session).Highlighted);
    }

    [Fact]
    public void Picking_SelectsOnClickWithoutRunningTheAppSHandler()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        var (_, rows, clicks) = BuildApp(session);
        session.Application.OpenInspector(startPicking: true);
        session.PumpFrame();

        var target = Centre(rows[1]);
        session.Window.Click(target.X, target.Y);
        session.PumpFrame();

        var inspector = Inspector(session);
        Assert.Same(rows[1], inspector.Selected);
        // The whole point of a picker: clicking a button selects it, never presses it.
        Assert.Equal(0, clicks());
        Assert.False(inspector.IsPicking);
    }

    [Fact]
    public void ClickingAfterPicking_ReachesTheAppAgain()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        var (_, rows, clicks) = BuildApp(session);
        session.Application.OpenInspector(startPicking: true);
        session.PumpFrame();

        var target = Centre(rows[1]);
        session.Window.Click(target.X, target.Y);   // picks, swallowed
        session.PumpFrame();
        session.Window.Click(target.X, target.Y);   // picker disarmed — the app gets this one
        session.PumpFrame();

        Assert.Equal(1, clicks());
    }

    [Fact]
    public void EscapeInTheAppWindow_CancelsPickingButKeepsTheSelection()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        var (_, rows, _) = BuildApp(session);
        session.Application.OpenInspector(startPicking: true);
        session.PumpFrame();

        var target = Centre(rows[0]);
        session.Window.Click(target.X, target.Y);
        session.PumpFrame();

        Inspector(session).SetPicking(true);   // re-arm
        session.PumpFrame();
        Assert.True(Inspector(session).IsPicking);

        session.Window.PressKey(KeyCode.Escape);
        session.PumpFrame();

        Assert.False(Inspector(session).IsPicking);
        Assert.Same(rows[0], Inspector(session).Selected);
    }

    [Fact]
    public void SelectionSurvivesAppFrames_AndThePanelKeepsRendering()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        var (root, rows, _) = BuildApp(session);
        session.Application.OpenInspector(startPicking: true);
        session.PumpFrame();

        var target = Centre(rows[1]);
        session.Window.Click(target.X, target.Y);
        session.PumpFrames(10);

        Assert.Same(rows[1], Inspector(session).Selected);
        Assert.False(Inspector(session).IsSelectionStale);
        Assert.Contains(root, Inspector(session).Roots);
    }

    [Fact]
    public void ClosingTheAppWindow_ClosesThePanelToo()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        BuildApp(session);
        session.Application.OpenInspector();
        session.PumpFrame();

        session.Window.Window.Close();
        session.PumpFrame();

        // Otherwise a closed app would keep running behind an orphaned inspector.
        Assert.False(session.Application.IsInspectorOpen);
    }

    [Fact]
    public void ClosingThePanel_DetachesTheHighlightFromTheApp()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        BuildApp(session);
        session.Application.OpenInspector();
        session.PumpFrame();
        Assert.NotNull(session.Window.Window.DevTools.Inspector);

        session.Application.CloseInspector();
        session.PumpFrame();

        Assert.Null(session.Window.Window.DevTools.Inspector);
        Assert.False(session.Window.Window.DevTools.HasHighlight);
    }

    [Fact]
    public void ThePanelRendersTheTreeAndThePropertiesOfTheSelection()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        var (_, rows, _) = BuildApp(session);
        session.Application.OpenInspector(startPicking: true);
        session.PumpFrame();

        var target = Centre(rows[1]);
        session.Window.Click(target.X, target.Y);
        session.PumpFrames(2);

        var panel = session.Attach(session.Application.Windows.Single(w => w.IsDevToolsWindow));

        // The tree pane names the picked element, and the property pane shows its
        // measured geometry — proof the panel actually laid out rather than
        // collapsing to nothing while the model quietly held the right answer.
        // The tree rows are virtualized, so they exist in the accessibility tree
        // without backing elements the query API can resolve; assert on the dump.
        var tree = panel.DumpAccessibilityTree();
        Assert.Contains("ListItem \"Box #app-root\"", tree);
        Assert.Contains("ListItem \"Box #row1\"", tree);
        Assert.NotNull(panel.FindByText("Layout"));
        Assert.NotNull(panel.FindByText($"300 × {RowH:0.#}"));
    }

    [Fact]
    public void ThePropertyPane_DrawsTheBoxModelFromResolvedEdges()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);

        // Distinct values per edge, so a diagram that transposed or reused one
        // would not still look right.
        var target = Box.New()
            .Height(RowH).FlexShrink(0)
            .Margin(new Thickness(3, 4, 5, 6))
            .Border(new Thickness(1, 2, 11, 12), Color.Red)
            .Padding(new Thickness(7, 8, 9, 10));
        target.Name = "boxy";
        session.Window.SetRoot(Flex.Column().Children(target));
        session.PumpFrame();

        session.Application.OpenInspector(startPicking: true);
        session.PumpFrame();
        var centre = Centre(target);
        session.Window.Click(centre.X, centre.Y);
        session.PumpFrames(2);

        var panel = session.Attach(session.Application.Windows.Single(w => w.IsDevToolsWindow));
        var tree = panel.DumpAccessibilityTree();

        Assert.Contains("Label \"margin\"", tree);
        Assert.Contains("Label \"border\"", tree);
        Assert.Contains("Label \"padding\"", tree);

        // Every edge of every band, plus the content size — read back from the
        // element's own resolved layout, so this pins that the diagram shows what
        // layout produced rather than what the style asked for.
        var layout = target.Layout;
        foreach (var edge in new[]
                 {
                     layout.Margin.Top, layout.Margin.Right, layout.Margin.Bottom, layout.Margin.Left,
                     layout.Border.Top, layout.Border.Right, layout.Border.Bottom, layout.Border.Left,
                     layout.Padding.Top, layout.Padding.Right, layout.Padding.Bottom, layout.Padding.Left,
                 })
        {
            Assert.Contains($"Label \"{edge:0.###}\"", tree);
        }

        var content = layout.AbsoluteContentBox;
        Assert.Contains($"Label \"{content.Width:0.###}×{content.Height:0.###}\"", tree);

        // The labels existing is not the same as the picture being right: each band
        // has to sit inside the one before it. Their left edges step inward in order.
        float MarginLeftOf(string text) => panel.GetByText(text).Bounds.X;
        float marginBand = MarginLeftOf("margin");
        float borderBand = MarginLeftOf("border");
        float paddingBand = MarginLeftOf("padding");
        float contentBand = MarginLeftOf($"{content.Width:0.###}×{content.Height:0.###}");

        Assert.True(marginBand < borderBand, $"margin {marginBand} should be outside border {borderBand}");
        Assert.True(borderBand < paddingBand, $"border {borderBand} should be outside padding {paddingBand}");
        Assert.True(paddingBand < contentBand, $"padding {paddingBand} should be outside content {contentBand}");
    }

    [Fact]
    public void ThePanelTracksTheAppTree_AsItRebuilds()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        var (root, _, _) = BuildApp(session);
        session.Application.OpenInspector();
        session.PumpFrame();

        var panel = session.Attach(session.Application.Windows.Single(w => w.IsDevToolsWindow));
        Inspector(session).SetExpanded(root, true);
        session.PumpFrames(2);
        Assert.DoesNotContain("late-arrival", panel.DumpAccessibilityTree());

        var added = Box.New().Height(RowH).FlexShrink(0);
        added.Name = "late-arrival";
        root.AddChild(added);

        // The panel re-reads the tree on a timer rather than every frame; advance the
        // (virtual) clock past the interval so the deferred refresh comes due.
        session.AdvanceTime(TimeSpan.FromMilliseconds(200));
        session.PumpFrames(3);

        Assert.Contains("ListItem \"Box #late-arrival\"", panel.DumpAccessibilityTree());
    }
}
