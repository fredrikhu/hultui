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
}
