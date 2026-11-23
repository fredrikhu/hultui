using System.IO;
using System.Reflection;
using System.Text;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using VesperCommander.Theme;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// A cursor key that moves nothing but the cursor is shown on the live elements and builds
/// nothing (CommanderApp.MoveCursorLive). Each test holds what it shows to what a full build of
/// the window makes, and the keys that still build to building.
/// </summary>
public class CursorNoBuildTests
{
    private const int Files = 40;

    private static string Name(int i) => $"f{i:00}.txt";

    /// <summary>Two folders of 40 files and a folder; with <paramref name="sameNames"/> the right
    /// one holds the left one's names, so the F3 partner follows the cursor across.</summary>
    private static DiffViewTests.Fixture Open(bool sameNames = false)
    {
        var t = new DiffViewTests.Fixture();
        Directory.CreateDirectory(Path.Combine(t.Left, "docs"));
        Directory.CreateDirectory(Path.Combine(t.Right, "sub"));
        for (int i = 0; i < Files; i++)
        {
            t.WriteLeft(Name(i), $"left {i}\n");
            t.WriteRight(sameNames ? Name(i) : $"other{i:00}.txt", $"right {i}\n");
        }
        t.Open();
        t.Session.PumpFrames(2);
        return t;
    }

    private static int Builds(DiffViewTests.Fixture t) => t.Field<int>("_builds");

    private static VirtualListElement<CommanderApp.PaneRowItem> List(DiffViewTests.Fixture t, char side) =>
        t.Field<Ref<VirtualListElement<CommanderApp.PaneRowItem>>>(side == 'L' ? "_vlistL" : "_vlistR").Current
        ?? throw new Xunit.Sdk.XunitException($"the {side} pane has no list");

    private static void Press(DiffViewTests.Fixture t, params KeyCode[] keys)
    {
        foreach (var key in keys) t.Press(key);
        t.Session.PumpFrames(1);
    }

    /// <summary>The window as a full build makes it: what both panes show now, then again after a
    /// build of the window and every part, which must match.</summary>
    internal static void AssertIsAFullBuild(DiffViewTests.Fixture t, string when)
    {
        string kept = Sig(t.Root);
        typeof(CommanderApp).GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly, Type.EmptyTypes)!
            .Invoke(t.App, null);
        t.Session.PumpFrames(2);
        string full = Sig(t.Root);
        if (kept == full) return;
        var xl = kept.Split('\n'); var yl = full.Split('\n');
        for (int l = 0; l < Math.Max(xl.Length, yl.Length); l++)
            if (l >= xl.Length || l >= yl.Length || xl[l] != yl[l])
                throw new Xunit.Sdk.XunitException($"the window {when} is not what a full build makes, line {l}:\n"
                    + $"  live: {(l < xl.Length ? xl[l] : "<none>")}\n  full: {(l < yl.Length ? yl[l] : "<none>")}");
    }

    /// <summary>What a subtree shows: element kinds, text, colours, where each lies, a list's
    /// selection and scroll, and which element has the keyboard. A virtual list's first child is
    /// its spacer, whose laid-out height the list does not keep (it places its rows itself), so
    /// only its place is compared.</summary>
    internal static string Sig(Element root)
    {
        var sb = new StringBuilder();
        void Walk(Element el, int depth)
        {
            if (el.Parent is ScrollViewElement { } owner && owner.GetType().Name.StartsWith("VirtualListElement", StringComparison.Ordinal)
                && owner.Children.Count > 0 && ReferenceEquals(owner.Children[0], el))
            {
                sb.Append(' ', depth).Append("spacer\n");
                return;
            }
            sb.Append(' ', depth).Append(el.GetType().Name);
            if (el is TextElement t) sb.Append(" text=").Append(t.Content);
            if (el is BoxElement b) sb.Append(" bg=").Append(b.BackgroundColor).Append(" border=").Append(b.BorderColor);
            if (el.IsFocused) sb.Append(" focused");
            if (el is VirtualListElement<CommanderApp.PaneRowItem> list)
                sb.Append(" selected=").Append(list.SelectedIndex).Append(" highlight=").Append(list.SelectionHighlight.HasValue);
            if (el is ScrollViewElement sv) sb.Append(System.FormattableString.Invariant($" scroll={sv.ScrollOffsetX:0.##},{sv.ScrollOffsetY:0.##}"));
            sb.Append(" op=").Append(el.Opacity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            var box = el.Layout.AbsoluteBorderBox;
            sb.Append(System.FormattableString.Invariant($" @{box.X:0.##},{box.Y:0.##} {box.Width:0.##}x{box.Height:0.##}"));
            sb.Append('\n');
            foreach (var child in el.Children) Walk(child, depth + 1);
        }
        Walk(root, 0);
        return sb.ToString();
    }

    // ── Detail ───────────────────────────────────────────────────────────

    /// <summary>The list draws the cursor, so a key tells the mounted list its new selection and
    /// scrolls it there: no build, no part, no row made, and the window is what a build makes —
    /// also after End scrolled the list, and back at the top.</summary>
    [Fact]
    public void InDetail_ACursorKey_BuildsNothing_AndTheListShowsTheCursor()
    {
        using var t = Open();
        t.PutCursorOn('L', Name(3));
        int builds = Builds(t), parts = t.App.PartBuilds;
        var rows = List(t, 'L').Children.ToList();

        Press(t, KeyCode.Down, KeyCode.Down, KeyCode.Up);
        Assert.Equal(0, Builds(t) - builds);
        Assert.Equal(0, t.App.PartBuilds - parts);
        Assert.Equal(rows, List(t, 'L').Children.ToList());
        Assert.Equal(Name(4), t.Rows('L')[List(t, 'L').SelectedIndex].Name);
        Assert.Equal(t.Cursor('L'), List(t, 'L').SelectedIndex);
        AssertIsAFullBuild(t, "after Down, Down, Up");

        builds = Builds(t);
        Press(t, KeyCode.End);
        Assert.Equal(0, Builds(t) - builds);
        Assert.True(List(t, 'L').ScrollOffsetY > 0, "End did not scroll the list");
        Assert.Equal(t.Rows('L').Count - 1, List(t, 'L').SelectedIndex);
        AssertIsAFullBuild(t, "after End");

        builds = Builds(t);
        Press(t, KeyCode.Home, KeyCode.PageDown);
        Assert.Equal(0, Builds(t) - builds);
        AssertIsAFullBuild(t, "after Home, PageDown");
    }

    /// <summary>A plain key at either end of the list moves nothing and builds nothing.</summary>
    [Fact]
    public void AKeyAtTheEndOfTheList_BuildsNothing()
    {
        using var t = Open();
        Press(t, KeyCode.End);
        int builds = Builds(t);
        Press(t, KeyCode.Down, KeyCode.End, KeyCode.PageDown);
        Assert.Equal(0, Builds(t) - builds);
        Assert.Equal(t.Rows('L').Count - 1, t.Cursor('L'));
    }

    /// <summary>The keys that change more than the cursor still build: a Shift sweep (its marks),
    /// Insert (the mark it sets), and every key while the rows draw the cursor (the zebra tint).</summary>
    [Fact]
    public void AKeyThatChangesMoreThanTheCursor_Builds()
    {
        using var t = Open();
        t.PutCursorOn('L', Name(3));

        int builds = Builds(t);
        t.Press(KeyCode.Down, KeyModifiers.Shift);
        t.Session.PumpFrames(1);
        Assert.True(Builds(t) > builds, "a Shift sweep did not build");
        Assert.Contains(Name(3), t.MarkNames('L'));

        builds = Builds(t);
        Press(t, KeyCode.Insert);
        Assert.True(Builds(t) > builds, "Insert did not build");
        AssertIsAFullBuild(t, "after Insert");

        var settings = t.Field<Settings>("_settings");
        bool zebra = settings.Zebra;
        try
        {
            settings.Zebra = true;
            t.MarkDirty();
            t.Session.PumpFrames(2);
            builds = Builds(t);
            Press(t, KeyCode.Down);
            Assert.Equal(1, Builds(t) - builds);
            AssertIsAFullBuild(t, "with the zebra tint");
        }
        finally { settings.Zebra = zebra; }
    }

    /// <summary>The F3 partner is drawn in the other pane: a same-named row there moves with each
    /// key, which builds. The other pane's cursor, when it is on a file and nothing over there has
    /// the cursor's name, stays the partner while the cursor moves between files — no build — and
    /// is let go when the cursor reaches a folder, which builds.</summary>
    [Fact]
    public void TheF3Partner_BuildsWhenItMoves_AndNotWhenItStays()
    {
        using (var same = Open(sameNames: true))
        {
            same.PutCursorOn('L', Name(2));
            int builds = Builds(same);
            Press(same, KeyCode.Down);
            Assert.Equal(1, Builds(same) - builds);
            Assert.Equal(Nocturne.Accent700, PartnerBorder(same, Name(3)));
            AssertIsAFullBuild(same, "with same names");

            // From a folder, which pairs with nothing, onto a file the other pane has too.
            same.PutCursorOn('L', "docs");
            builds = Builds(same);
            Press(same, KeyCode.Down);
            Assert.Equal(1, Builds(same) - builds);
            Assert.Equal(Nocturne.Accent700, PartnerBorder(same, Name(0)));
            AssertIsAFullBuild(same, "onto a file named as one over there");
        }

        using var t = Open();
        t.PutCursorOn('R', "other05.txt");
        t.PutCursorOn('L', Name(2));
        Assert.Equal(Nocturne.Accent700, PartnerBorder(t, "other05.txt"));
        int b = Builds(t);
        Press(t, KeyCode.Down, KeyCode.Down, KeyCode.Up);
        Assert.Equal(0, Builds(t) - b);
        Assert.Equal(Nocturne.Accent700, PartnerBorder(t, "other05.txt"));
        AssertIsAFullBuild(t, "with the cursors paired");

        t.PutCursorOn('L', Name(0));
        b = Builds(t);
        Press(t, KeyCode.Up);   // onto "docs", a folder: F3 pairs nothing
        Assert.Equal(1, Builds(t) - b);
        Assert.Equal(Color.Transparent, PartnerBorder(t, "other05.txt"));
        AssertIsAFullBuild(t, "on a folder");
    }

    /// <summary>Both panes on one folder: the same-named row over there is the cursor's own file,
    /// which F3 never pairs with, so the other pane's cursor on a file stays the partner while the
    /// cursor moves — no build, the ring kept on it. When the cursor reaches that very file its
    /// only partner would be itself: F3 does nothing there and the ring goes, which builds, as does
    /// the key that leaves it.</summary>
    [Fact]
    public void WithBothPanesOnOneFolder_TheOtherCursorsFileStaysThePartner_AndAKeyBuildsNothing()
    {
        using var t = OpenOnOneFolder();
        t.PutCursorOn('R', Name(5));
        t.PutCursorOn('L', Name(2));
        Assert.Equal(Nocturne.Accent700, PartnerBorder(t, Name(5)));
        int builds = Builds(t), parts = t.App.PartBuilds;
        Press(t, KeyCode.Down, KeyCode.Down, KeyCode.Up, KeyCode.Down);
        Assert.Equal(Name(4), t.Rows('L')[t.Cursor('L')].Name);
        Assert.Equal(0, Builds(t) - builds);
        Assert.Equal(0, t.App.PartBuilds - parts);
        Assert.Equal(Nocturne.Accent700, PartnerBorder(t, Name(5)));
        AssertIsAFullBuild(t, "with both panes on one folder");

        builds = Builds(t);
        Press(t, KeyCode.Down);   // onto f05 itself
        Assert.Equal(1, Builds(t) - builds);
        Assert.Equal(Color.Transparent, PartnerBorder(t, Name(5)));
        AssertIsAFullBuild(t, "on the other cursor's own file");

        builds = Builds(t);
        Press(t, KeyCode.Down);
        Assert.Equal(1, Builds(t) - builds);
        Assert.Equal(Nocturne.Accent700, PartnerBorder(t, Name(5)));
        AssertIsAFullBuild(t, "off the other cursor's own file");
    }

    /// <summary>Both panes on one folder with the other cursor on ".." or a folder: the cursor's
    /// only partner would be its own file, so F3 does nothing and nothing is ringed, and a key
    /// builds nothing. The file used to be ringed as its own partner, and the ring followed each
    /// key, which built the window every time (29 KB a key against 1.2).</summary>
    [Theory]
    [InlineData("..")]
    [InlineData("docs")]
    public void WithBothPanesOnOneFolder_AndTheOtherCursorOnNoFile_NothingIsRinged_AndAKeyBuildsNothing(string other)
    {
        using var t = OpenOnOneFolder();
        t.PutCursorOn('R', other);
        t.PutCursorOn('L', Name(2));
        AssertNothingRinged(t);
        int builds = Builds(t), parts = t.App.PartBuilds;
        Press(t, KeyCode.Down, KeyCode.Down, KeyCode.Up, KeyCode.Down);
        Assert.Equal(Name(4), t.Rows('L')[t.Cursor('L')].Name);
        Assert.Equal(0, Builds(t) - builds);
        Assert.Equal(0, t.App.PartBuilds - parts);
        AssertNothingRinged(t);
        AssertIsAFullBuild(t, $"with the other cursor on {other}");
    }

    /// <summary><see cref="Open"/>, then the right pane moved to the left one's folder.</summary>
    private static DiffViewTests.Fixture OpenOnOneFolder()
    {
        var t = Open();
        t.Invoke("NavigateTo", 'R', t.Left, null, false);
        t.Session.PumpUntil(() => t.Rows('R').Count == t.Rows('L').Count);
        t.Session.PumpFrames(2);
        return t;
    }

    private static void AssertNothingRinged(DiffViewTests.Fixture t)
    {
        int rows = 0;
        foreach (var row in List(t, 'R').Children.Skip(1))
            if (row.Children.Count > 1 && row.Children[1] is TextElement { Content: var name })
            {
                rows++;
                if (((BoxElement)row).BorderColor != Color.Transparent)
                    throw new Xunit.Sdk.XunitException($"{name} is ringed in the right pane");
            }
        Assert.True(rows > 10, $"only {rows} rows realized in the right pane");
    }

    private static Color PartnerBorder(DiffViewTests.Fixture t, string name)
    {
        foreach (var row in List(t, 'R').Children.Skip(1))
            if (row.Children[1] is TextElement { Content: var c } && c == name) return ((BoxElement)row).BorderColor;
        throw new Xunit.Sdk.XunitException($"{name} is not realized in the right pane");
    }

    /// <summary>With the preview open it shows the cursor's file, so a key builds.</summary>
    [Fact]
    public void WithThePreviewOpen_ACursorKey_Builds()
    {
        using var t = Open();
        var settings = t.Field<Settings>("_settings");
        bool preview = settings.PreviewPane;
        try
        {
            settings.PreviewPane = true;
            t.MarkDirty();
            t.Session.PumpFrames(2);
            int builds = Builds(t);
            Press(t, KeyCode.Down);
            Assert.Equal(1, Builds(t) - builds);
        }
        finally { settings.PreviewPane = preview; }
    }
}
