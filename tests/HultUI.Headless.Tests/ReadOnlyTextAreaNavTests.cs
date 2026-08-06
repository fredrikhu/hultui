using System.Linq;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A read-only TextArea used as a content viewer scrolls on plain navigation keys
/// (there's no visible caret to chase). The regression: caret-movement semantics made
/// the first PageUp/Down only nudge the hidden caret to the viewport edge without
/// scrolling, and arrows/Home/End appeared to do nothing.
/// </summary>
public class ReadOnlyTextAreaNavTests
{
    private static TextAreaElement Build(HeadlessSession session)
    {
        var text = string.Join("\n", Enumerable.Range(0, 100).Select(i => $"line {i}"));
        var area = TextArea.New(text).ReadOnly().Width(180).Height(100)
            .FontFamily("monospace").FontSize(12);
        area.IsFocusable = true;
        session.Window.SetRoot(area);
        session.PumpFrame();
        session.Window.Click(area);   // focus the viewer
        session.PumpFrame();
        return area;
    }

    [Fact]
    public void FirstPageDown_Scrolls_ThenArrowsAndEndHome()
    {
        using var session = HeadlessSession.Create(width: 220, height: 160);
        var area = Build(session);
        Assert.Equal(0f, area.ScrollOffsetY, 2);

        // The bug: the first PageDown scrolled nothing. It must move a page now.
        session.Window.PressKey(KeyCode.PageDown);
        session.PumpFrame();
        float afterPage = area.ScrollOffsetY;
        Assert.True(afterPage > 0f, "first PageDown should scroll the viewport");

        // Down nudges one line further.
        session.Window.PressKey(KeyCode.Down);
        session.PumpFrame();
        Assert.True(area.ScrollOffsetY > afterPage, "Down should scroll one more line");

        // End jumps to the bottom, Home back to the top.
        session.Window.PressKey(KeyCode.End);
        session.PumpFrame();
        float bottom = area.ScrollOffsetY;
        Assert.True(bottom > afterPage);

        session.Window.PressKey(KeyCode.Home);
        session.PumpFrame();
        Assert.Equal(0f, area.ScrollOffsetY, 2);
    }

    [Fact]
    public void PageUpAtTop_StaysAtTop()
    {
        using var session = HeadlessSession.Create(width: 220, height: 160);
        var area = Build(session);

        session.Window.PressKey(KeyCode.PageUp);
        session.PumpFrame();

        Assert.Equal(0f, area.ScrollOffsetY, 2);
    }
}
