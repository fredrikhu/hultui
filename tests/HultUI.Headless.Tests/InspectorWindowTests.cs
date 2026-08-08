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
    public void CtrlShiftC_OpensThePanelWithThePickerArmed()
    {
        using var session = HeadlessSession.Create(width: 300, height: 200);
        BuildApp(session);

        session.Window.PressKey(KeyCode.C, KeyModifiers.Ctrl | KeyModifiers.Shift);
        session.PumpFrame();

        Assert.True(Inspector(session).IsPicking);
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

        session.Window.PressKey(KeyCode.C, KeyModifiers.Ctrl | KeyModifiers.Shift);   // re-arm
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
