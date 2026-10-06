using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Clickable ranges inside a label. The subtle one is a link on a label that is not
/// selectable: the drawn line layout a click is mapped through used to be cached only for
/// selectable labels, so a plain label's link could never resolve where the click landed
/// and simply did nothing.
/// </summary>
public class TextLinkTests
{
    private const string Text = "Older commit fixing ABCD-4291 today";
    private static readonly TextLink Link = new(Text.IndexOf("ABCD", System.StringComparison.Ordinal), 9, "issue://4291");

    private static (TextElement label, List<string> opened) Build(HeadlessSession session, bool selectable)
    {
        var opened = new List<string>();
        var label = TextElement.New(Text);
        label.Selectable = selectable;
        label.Links = [Link];
        label.LinkActivated = opened.Add;
        session.Window.SetRoot(Box.New().Width(600).Height(60).Children(label));
        session.PumpFrame();
        return (label, opened);
    }

    private static void ClickAt(HeadlessSession session, TextElement label, int offset)
    {
        var box = label.Layout.AbsoluteContentBox;
        float x = box.X + Core.Rendering.TextMeasure.MeasureWidth(Text[..offset], label.ResolvedInherited);
        session.Window.Click(x, box.Y + box.Height / 2f);
    }

    [Theory]
    [InlineData(false)]   // the case that was broken
    [InlineData(true)]
    public void ClickingInsideTheRangeActivatesTheLink(bool selectable)
    {
        using var session = HeadlessSession.Create(width: 600, height: 200);
        var (label, opened) = Build(session, selectable);

        ClickAt(session, label, Link.Start + Link.Length / 2);

        Assert.Equal(["issue://4291"], opened);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClickingOutsideTheRangeDoesNothing(bool selectable)
    {
        using var session = HeadlessSession.Create(width: 600, height: 200);
        var (label, opened) = Build(session, selectable);

        ClickAt(session, label, 3);                       // inside "Older"
        ClickAt(session, label, Text.Length - 2);         // inside "today"

        Assert.Empty(opened);
    }

    /// <summary>A click on a link belongs to the link, not to whatever the label sits in.</summary>
    [Fact]
    public void TheClickDoesNotReachAnAncestorHandler()
    {
        using var session = HeadlessSession.Create(width: 600, height: 200);
        var opened = new List<string>();
        int rowClicks = 0;
        var label = TextElement.New(Text);
        label.Links = [Link];
        label.LinkActivated = opened.Add;
        var row = Box.New().Width(600).Height(60).Children(label);
        row.OnClick(_ => rowClicks++);
        session.Window.SetRoot(row);
        session.PumpFrame();

        ClickAt(session, label, Link.Start + Link.Length / 2);
        Assert.Equal(1, opened.Count);
        Assert.Equal(0, rowClicks);

        ClickAt(session, label, 3);
        Assert.Equal(1, opened.Count);
        Assert.Equal(1, rowClicks);       // and the rest of the label still belongs to the row
    }

    /// <summary>Reconciliation replaces the links and the handler, like any app-supplied
    /// closure — a stale one would point at the previous build's data.</summary>
    [Fact]
    public void APatchCarriesTheLinksAndHandlerOver()
    {
        using var session = HeadlessSession.Create(width: 600, height: 200);
        var (label, _) = Build(session, selectable: false);

        var reopened = new List<string>();
        var fresh = TextElement.New(Text);
        fresh.Links = [Link with { Target = "issue://second" }];
        fresh.LinkActivated = reopened.Add;
        typeof(Element).GetMethod("PatchFrom", System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance)!.Invoke(label, [fresh]);
        session.PumpFrame();

        ClickAt(session, label, Link.Start + Link.Length / 2);
        Assert.Equal(["issue://second"], reopened);
    }

    // ── Links after styled text (docs/plans/12-rich-text-spans.md's hit-testing gap) ──
    //
    // A click used to be mapped by measuring prefixes in the label's own style. A larger or
    // bolder run ahead of a link is wider than that, so everything after it mapped to an
    // offset further on than where it was drawn — on a paragraph of Junction's markdown, a
    // click on a link after a heading-sized word landed past its end.

    private const string Styled = "Release notes here ABCD-4291 today";
    private static readonly TextLink StyledLink =
        new(Styled.IndexOf("ABCD", System.StringComparison.Ordinal), 9, "issue://4291");
    private const int BigEnd = 19;   // "Release notes here ", drawn larger than the rest

    // Twice the size moves the old mapping past the whole link; a few pixels more moves it a
    // few characters on, which from just before the link lands inside it. Each size is the one
    // its test fails under on the old code.
    private const float Twice = 28f, Slightly = 17f;

    private static (TextElement label, List<string> opened) BuildStyled(HeadlessSession session, bool selectable,
        float bigSize = Twice)
    {
        var opened = new List<string>();
        var label = TextElement.New(Styled);
        label.Selectable = selectable;
        label.Spans = [new Core.Rendering.TextSpan(0, BigEnd, default, FontSize: bigSize)];
        label.Links = [StyledLink];
        label.LinkActivated = opened.Add;
        session.Window.SetRoot(Box.New().Width(800).Height(80).Children(label));
        session.PumpFrame();
        return (label, opened);
    }

    // Where the caret before `offset` is drawn: the big run at its size, the rest at the label's.
    private static void ClickStyledAt(HeadlessSession session, TextElement label, int offset, float bigSize = Twice)
    {
        var box = label.Layout.AbsoluteContentBox;
        var style = label.ResolvedInherited;
        float x = box.X + Core.Rendering.TextMeasure.MeasureWidth(Styled[..Math.Min(offset, BigEnd)],
            style with { FontSize = bigSize });
        if (offset > BigEnd)
            x += Core.Rendering.TextMeasure.MeasureWidth(Styled[BigEnd..offset], style);
        session.Window.Click(x, box.Y + box.Height / 2f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AfterLargerText_AClickOnTheLinkActivatesIt(bool selectable)
    {
        using var session = HeadlessSession.Create(width: 800, height: 200);
        var (label, opened) = BuildStyled(session, selectable);

        ClickStyledAt(session, label, StyledLink.Start + 2);

        Assert.Equal(["issue://4291"], opened);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AfterLargerText_AClickJustBeforeTheLinkDoesNothing(bool selectable)
    {
        using var session = HeadlessSession.Create(width: 800, height: 200);
        var (label, opened) = BuildStyled(session, selectable, Slightly);

        ClickStyledAt(session, label, StyledLink.Start - 1, Slightly);

        Assert.Empty(opened);
    }

    [Fact]
    public void AfterLargerText_ADragSelectsWhatWasDrawnUnderThePointer()
    {
        using var session = HeadlessSession.Create(width: 800, height: 200);
        var (label, _) = BuildStyled(session, selectable: true);
        var box = label.Layout.AbsoluteContentBox;
        var style = label.ResolvedInherited;
        float big = Core.Rendering.TextMeasure.MeasureWidth(Styled[..BigEnd], style with { FontSize = Twice });
        float y = box.Y + box.Height / 2f;

        // From the end of the big run to the end of "ABCD-4291".
        float to = big + Core.Rendering.TextMeasure.MeasureWidth(Styled[BigEnd..(StyledLink.Start + 9)], style);
        session.Window.Drag(new Core.Primitives.Point(box.X + big, y), new Core.Primitives.Point(box.X + to, y));

        Assert.Equal("ABCD-4291", label.SelectedText);
    }
}
