using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Headless;
using VesperCommander.Theme;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The inactive pane rings the file F3 would compare the cursor against.
///
/// <para>The pane used to draw a cursor only when it had the keyboard, which left the second
/// half of a comparison invisible until you pressed F3 and read the header. What is ringed is
/// the operand <see cref="CommanderApp"/> actually resolves, not simply the other pane's
/// cursor — pairing prefers a same-named file, so the two answers differ exactly when it
/// matters most.</para>
/// </summary>
public class DiffPartnerMarkTests
{
    [Fact]
    public void TheSameNamedFileIsRinged_NotTheOtherPanesCursor()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "one\n");
        t.WriteRight("a.txt", "two\n");
        t.WriteRight("b.txt", "other\n");
        t.Open();

        // The right pane is looking at b.txt; F3 still pairs a.txt with a.txt.
        t.PutCursorOn('R', "b.txt");
        t.PutCursorOn('L', "a.txt");

        Assert.Equal(Nocturne.Accent700, Border(t, 'R', "a.txt"));
        Assert.Equal(Color.Transparent, Border(t, 'R', "b.txt"));
    }

    [Fact]
    public void WithNoSameNamedFileTheOtherPanesCursorIsRinged()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("only-left.txt", "one\n");
        t.WriteRight("x.txt", "x\n");
        t.WriteRight("y.txt", "y\n");
        t.Open();

        t.PutCursorOn('R', "y.txt");
        t.PutCursorOn('L', "only-left.txt");

        Assert.Equal(Nocturne.Accent700, Border(t, 'R', "y.txt"));
        Assert.Equal(Color.Transparent, Border(t, 'R', "x.txt"));
    }

    /// <summary>Nothing is ringed when F3 would do nothing, so the mark's presence is itself
    /// the answer to "will this compare anything".</summary>
    [Fact]
    public void NothingIsRingedWhenTheCursorIsOnAFolder()
    {
        using var t = new DiffViewTests.Fixture();
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(t.Left, "sub"));
        t.WriteRight("a.txt", "two\n");
        t.Open();

        t.PutCursorOn('R', "a.txt");
        t.PutCursorOn('L', "sub");

        Assert.Equal(Color.Transparent, Border(t, 'R', "a.txt"));
    }

    /// <summary>
    /// The two marks differ in kind, not merely in brightness: the cursor is filled and the
    /// partner is a hollow ring. A dimmer copy of the same treatment reads as the same thing
    /// rendered faintly, which is exactly what the inactive pane must not look like.
    /// </summary>
    [Fact]
    public void ThePartnerRingIsDistinctFromTheActiveCursor()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "one\n");
        t.WriteRight("a.txt", "two\n");
        t.Open();
        t.PutCursorOn('L', "a.txt");

        var cursor = Row(t, 'L', "a.txt");
        var partner = Row(t, 'R', "a.txt");

        Assert.Equal(Nocturne.Accent600, cursor.BorderColor);
        Assert.Equal(Nocturne.Accent700, partner.BorderColor);
        Assert.NotEqual(cursor.BackgroundColor, partner.BackgroundColor);
    }

    private static Color Border(DiffViewTests.Fixture t, char side, string name) =>
        Row(t, side, name).BorderColor;

    /// <summary>The realized pane row showing <paramref name="name"/>. Reached through the
    /// app's own list Ref rather than by walking the window, because both panes are on screen
    /// and a search by name would find whichever came first.</summary>
    private static BoxElement Row(DiffViewTests.Fixture t, char side, string name)
    {
        var list = t.Field<Ref<VirtualListElement<CommanderApp.PaneRowItem>>>(side == 'L' ? "_vlistL" : "_vlistR").Current
            ?? throw new Xunit.Sdk.XunitException($"the {side} pane has no list");
        // The list's first child is the spacer standing in for the rows scrolled past above.
        foreach (var row in list.Children.Skip(1))
            if (row.Children.Count > 1 && row.Children[1] is TextElement label && label.Content == name)
                return (BoxElement)row;
        throw new Xunit.Sdk.XunitException($"{name} is not a realized row in the {side} pane");
    }
}
