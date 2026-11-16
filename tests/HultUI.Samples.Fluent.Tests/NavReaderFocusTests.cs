using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Headless;
using Xunit;

namespace HultUI.Samples.Fluent.Tests;

/// <summary>
/// What a Windows reader is told on the sample, through the real UI Automation bridge
/// (<see cref="HeadlessUiaReader"/>), for the steps of the listening run of 2026-11-16: a click on a
/// navigation item, and F10, Right, Right and Escape after it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NavReaderFocusTests : IDisposable
{
    private readonly HeadlessSession _session = HeadlessSession.Create(1280, 860);
    private readonly HeadlessUiaReader? _reader;

    public NavReaderFocusTests()
    {
        var app = new FluentApp(_session.Application, _session.Window.Window);
        _session.Application.SetRoot(app.Mount());
        _session.PumpFrames(3);
        if (OperatingSystem.IsWindows()) _reader = HeadlessUiaReader.Install(_session.Window);
        _session.PumpFrames(3);
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _session.Dispose();
    }

    private Element Nav(string name) => _session.Window.FindByRole(AccessibilityRole.ListItem, name)!.Element;

    private static Point Middle(Element element)
    {
        var box = element.Layout.AbsoluteBorderBox;
        return new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    /// <summary>A press and its release in frames of their own, as a hand makes them.</summary>
    private (List<ElementId?> AtPress, List<ElementId?> AtRelease) Click(Point at)
    {
        _reader!.Clear();
        _session.Window.MouseDown(at.X, at.Y);
        _session.PumpFrame();
        _session.PumpFrame(TimeSpan.FromMilliseconds(60));
        var atPress = _reader.FocusEvents();
        _reader.Clear();
        _session.Window.MouseUp(at.X, at.Y);
        _session.PumpFrame();
        var atRelease = _reader.FocusEvents();
        // Told in the release's frame, and nothing after it.
        for (int i = 0; i < 6; i++) _session.PumpFrame(TimeSpan.FromMilliseconds(50));
        Assert.Equal(atRelease, _reader.FocusEvents());
        return (atPress, atRelease);
    }

    private List<ElementId?> Press(KeyCode key)
    {
        _reader!.Clear();
        _session.Window.PressKey(key);
        for (int i = 0; i < 4; i++) _session.PumpFrame(TimeSpan.FromMilliseconds(50));
        return _reader.FocusEvents();
    }

    /// <summary>
    /// NVDA read "Personalization, not selected, 2 of 5" and nothing more as the click switched the page:
    /// the press's frame told the focus, before the click selected the item. Told at the release now, once,
    /// on the item, which answers selected; nothing at the press.
    /// </summary>
    [Fact]
    public void AClickOnANavItem_IsReadAtItsRelease_Selected()
    {
        if (!OperatingSystem.IsWindows()) return;
        var personalization = Nav("Personalization");
        var (atPress, atRelease) = Click(Middle(personalization));
        Assert.Empty(atPress);
        Assert.Equal([personalization.Id], atRelease);
        Assert.Equal(personalization.Id, _reader!.GetFocus());
        Assert.Equal(true, _reader.IsSelected(personalization.Id));
        Assert.Equal((2, 5), _reader.Place(personalization.Id));
        Assert.Equal("Personalization", _reader.Name(personalization.Id));
        Assert.Equal(false, _reader.IsSelected(Nav("Home").Id));
    }

    /// <summary>
    /// F10, Right, Right and Escape after a click on Home: each step one focus event, the titles, then
    /// Home again, selected; never the bar.
    /// </summary>
    [Fact]
    public void F10_RightRight_Escape_GivesTheReadersFocusBackToTheNavItemTheClickFocused()
    {
        if (!OperatingSystem.IsWindows()) return;
        Click(Middle(Nav("Personalization")));
        var home = Nav("Home");
        Assert.Equal([home.Id], Click(Middle(home)).AtRelease);

        var bar = Walk(_reader!.Tree!).Single(n => n.Role == AccessibilityRole.MenuBar);
        Assert.Equal([bar.Children[0].ElementId], Press(KeyCode.F10));
        Assert.Equal([bar.Children[1].ElementId], Press(KeyCode.Right));
        Assert.Equal([bar.Children[2].ElementId], Press(KeyCode.Right));
        Assert.Equal([home.Id], Press(KeyCode.Escape));
        Assert.Same(home, _session.Window.Window.FocusedElement);
        Assert.Equal(home.Id, _reader.GetFocus());
        Assert.Equal(true, _reader.IsSelected(home.Id));
        Assert.False(_reader.HasKeyboardFocus(bar.ElementId));
    }

    /// <summary>A click on the page's text clears the focus; F10, Right, Right and Escape then give the
    /// keyboard back to nothing, and a reader is told no bar ("Menu bar menu bar" before).</summary>
    [Fact]
    public void F10_RightRight_Escape_WithNothingFocused_TellsNoBar()
    {
        if (!OperatingSystem.IsWindows()) return;
        Click(Middle(Nav("Personalization")));
        var text = _session.Window.FindByText("Choose your mode")!.Element;
        Click(Middle(text));
        Assert.Null(_session.Window.Window.FocusedElement);

        Assert.Single(Press(KeyCode.F10));
        Press(KeyCode.Right);
        Press(KeyCode.Right);
        Assert.Empty(Press(KeyCode.Escape));
        Assert.Null(_session.Window.Window.FocusedElement);
        Assert.Null(_reader!.GetFocus());
    }

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node) =>
        node.Children.SelectMany(Walk).Prepend(node);
}
