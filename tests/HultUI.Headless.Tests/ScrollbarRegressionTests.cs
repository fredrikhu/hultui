using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The bar ScrollView, TextArea and the rich text editor share paints through
/// <c>Scrollbar.PaintAt</c> since ScrollbarElement needed a thumb placed by its own arithmetic.
/// The baselines below were captured before that extraction, so the move is proved to draw
/// exactly what it drew: the same rects, in the same order, at the same floats.
/// </summary>
public class ScrollbarRegressionTests
{
    private static string Frame(Element root, Action<HeadlessSession>? before = null)
    {
        using var session = HeadlessSession.Create(width: 200, height: 150,
            options: HeadlessOptions.DeterministicRecording());
        session.Window.SetRoot(root);
        session.PumpFrames(2);
        before?.Invoke(session);
        session.Window.RenderLog.Clear();
        session.PumpFrame(session.DefaultFrameDelta, forceRender: true);
        return string.Join("\n", session.Window.RenderLog.Named("DrawRoundedRect").Select(o => o.ToString().Trim()));
    }

    private static ScrollViewElement TwoWayScroll()
    {
        var scroll = ScrollView.New().Height(150f).Width(200f).Children(
            Box.New().Width(700f).Height(900f).FlexShrink(0));
        scroll.ShowScrollbarX = true;
        return scroll;
    }

    [Fact]
    public void AScrollView_ScrolledBothWays_DrawsItsBarsAsBefore()
    {
        var scroll = TwoWayScroll();

        string log = Frame(scroll, _ =>
        {
            scroll.ScrollOffsetY = 300f;
            scroll.ScrollOffsetX = 120f;
        });

        Assert.Equal(Baselines.ScrollViewScrolled.ReplaceLineEndings("\n"), log);
    }

    [Fact]
    public void AScrollView_WithItsBarExpanded_DrawsItAsBefore()
    {
        var scroll = TwoWayScroll();

        string log = Frame(scroll, session =>
        {
            session.Window.MoveMouse(194, 60);
            session.PumpFrames(2);              // part-way through the widening
        });

        Assert.Equal(Baselines.ScrollViewExpanding.ReplaceLineEndings("\n"), log);
    }

    [Fact]
    public void ATextArea_DrawsItsBarAsBefore()
    {
        var lines = string.Join("\n", Enumerable.Range(1, 40).Select(i => $"line {i}"));
        var area = TextArea.New(lines).Width(200f).Height(150f);

        string log = Frame(area);

        Assert.Equal(Baselines.TextArea.ReplaceLineEndings("\n"), log);
    }

    /// <summary>Captured at upstream fe8f4ecf, before the extraction.</summary>
    private static class Baselines
    {
        public const string ScrollViewScrolled = """
            DrawRoundedRect (194,0 6x150) r=3 fill=#80808064
            DrawRoundedRect (194,50 6x25) r=3 fill=#808080B4
            DrawRoundedRect (0,144 200x6) r=3 fill=#80808064
            DrawRoundedRect (34.29,144 57.14x6) r=3 fill=#808080B4
            """;

        public const string ScrollViewExpanding = """
            DrawRoundedRect (189.19,0 10.81x150) r=5.4 fill=#80808064
            DrawRoundedRect (191.6,0 6x25) r=3 fill=#808080B4
            DrawRoundedRect (0,144 200x6) r=3 fill=#80808064
            DrawRoundedRect (0,144 57.14x6) r=3 fill=#808080B4
            """;

        public const string TextArea = """
            DrawRoundedRect (0,0 200x150) r=4 fill=#FFFFFF stroke=#E2E8F0 width=1
            DrawRoundedRect (185,5 6x140) r=3 fill=#80808064
            DrawRoundedRect (185,5 6x25.7) r=3 fill=#808080B4
            """;
    }
}
