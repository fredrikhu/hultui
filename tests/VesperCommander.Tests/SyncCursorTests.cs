using System.IO;
using System.Reflection;
using System.Text;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Reactivity;
using SkiaSharp;
using VesperCommander.Theme;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The cursor in Synchronize (F9): what a build of the rows costs, and what a cursor key does (plan
/// 17 round 10 step 7). The rows are kept between builds, a cursor key moves the ring on the live
/// rows with no build (CommanderApp.MoveSyncCursor), and the keys that change the rows still build. Each test holds the window to what a build that keeps no
/// row makes: the elements, their colours and rings, the reader's tree and the pixels.
/// </summary>
public class SyncCursorTests
{
    private static string Name(int i) => $"f{i:000}.txt";

    /// <summary>Two folders of <paramref name="files"/> files, every third one the same on both sides
    /// (Equal, hidden while "Show equal" is off) and the rest newer on the left, in Sync with the
    /// comparison done.</summary>
    internal static DiffViewTests.Fixture OpenSync(int files) => OpenSync(files, reader: false, out _);

    internal static DiffViewTests.Fixture OpenSync(int files, bool reader, out HultUI.Headless.HeadlessAccessibilityBridge? bridge)
    {
        var t = new DiffViewTests.Fixture();
        var then = System.DateTime.Now.AddHours(-2);
        for (int i = 0; i < files; i++)
        {
            bool equal = i % 3 == 1;
            t.WriteLeft(Name(i), equal ? "same" : "left, newer");
            t.WriteRight(Name(i), "same");
            File.SetLastWriteTime(Path.Combine(t.Right, Name(i)), then);
            if (equal) File.SetLastWriteTime(Path.Combine(t.Left, Name(i)), then);
        }
        t.Open();
        bridge = reader ? t.Session.Window.InstallAccessibilityBridge() : null;
        t.Invoke("OpenSync");
        t.Session.PumpUntil(() => !t.Field<bool>("_syncComparing") && t.Field<System.Collections.IList>("_syncRows").Count == files,
            System.TimeSpan.FromSeconds(20));
        t.Session.PumpFrames(2);
        return t;
    }

    private static int Builds(DiffViewTests.Fixture t) => t.Field<int>("_builds");

    /// <summary>Sync's scroll view of rows: the view's third part, under its header and the column
    /// header.</summary>
    private static ScrollViewElement Scroll(DiffViewTests.Fixture t)
    {
        static Element? Find(Element e, string key)
        {
            if (e.Key == key) return e;
            foreach (var c in e.Children) if (Find(c, key) is { } hit) return hit;
            return null;
        }
        return Find(t.Root, "sync")?.Children[2] as ScrollViewElement ?? throw new Xunit.Sdk.XunitException("Sync shows no rows");
    }

    /// <summary>The rows on screen, by the name on their left.</summary>
    private static Dictionary<string, BoxElement> Rows(DiffViewTests.Fixture t)
    {
        var rows = new Dictionary<string, BoxElement>();
        foreach (var row in Scroll(t).Children[0].Children) rows[((TextElement)row.Children[0]).Content] = (BoxElement)row;
        return rows;
    }

    private static string Rail(DiffViewTests.Fixture t, string name) => ((TextElement)Rows(t)[name].Children[3].Children[0]).Content;

    private static string[] Ringed(DiffViewTests.Fixture t) =>
        [.. Rows(t).Where(r => r.Value.BorderColor == Nocturne.Accent600).Select(r => r.Key).Order()];

    private static void Press(DiffViewTests.Fixture t, KeyCode key, int times = 1)
    {
        for (int i = 0; i < times; i++) t.Press(key);
        t.Session.PumpFrames(1);
    }

    private static void ShowEqual(DiffViewTests.Fixture t, bool on)
    {
        t.SetField("_syncShowEqual", on);
        t.MarkDirty();
        t.Session.PumpFrames(2);
    }

    /// <summary>A build of the rows asks for the rows shown once, not once a row: each row looked
    /// itself up in a fresh filtered list (O(rows²) a build).</summary>
    [Fact]
    public void ABuildOfTheRows_AsksForTheRowsShownOnce()
    {
        using var t = OpenSync(60);
        CommanderApp.VisibleSyncRowsCalls = 0;
        t.MarkDirty();
        t.Session.PumpFrames(1);
        Assert.InRange(CommanderApp.VisibleSyncRowsCalls, 1, 2);
    }

    /// <summary>A cursor key builds nothing, no part either, and touches no row but the two the ring
    /// moved between, which it restyles in place; with "Show equal" on, an Equal row the cursor
    /// reaches draws at full colour and fades again when it leaves. The window is what a build that
    /// keeps no row makes.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACursorKey_BuildsNothing_AndMovesTheRingOnTheLiveRows(bool showEqual)
    {
        using var t = OpenSync(30);
        if (showEqual) ShowEqual(t, true);
        var before = Rows(t);
        foreach (var row in before.Values) row.AutomationId = "untouched";
        int builds = Builds(t), parts = t.App.PartBuilds;

        Press(t, KeyCode.Down);
        Press(t, KeyCode.Down);
        Press(t, KeyCode.Up);

        Assert.Equal(0, Builds(t) - builds);
        Assert.Equal(0, t.App.PartBuilds - parts);
        var after = Rows(t);
        Assert.All(after, r => Assert.Same(before[r.Key], r.Value));
        Assert.All(after.Values, r => Assert.Equal("untouched", r.AutomationId));
        // Show equal off: f000, f002, f003, …; on: f000, f001 (Equal), f002, …
        string second = showEqual ? Name(1) : Name(2);
        Assert.Equal([second], Ringed(t));
        Assert.Equal(1, t.Field<int>("_syncSel"));
        if (showEqual)
        {
            // The Equal row the cursor is on draws at full colour; the next Equal one (f004) is faded.
            Assert.Equal(Nocturne.Text, ((TextElement)after[Name(1)].Children[0]).ResolvedInherited.TextColor);
            Assert.NotEqual(Nocturne.Text, ((TextElement)after[Name(4)].Children[0]).ResolvedInherited.TextColor);
        }
        AssertIsAFullBuild(t, "after Down, Down, Up");

        // Off the Equal row again, which fades as a build makes it.
        Press(t, KeyCode.Down, 2);
        AssertIsAFullBuild(t, "after two more Downs");
    }

    /// <summary>A cursor key that has to build (as if the latest build had not made the rows) builds,
    /// after keys that built nothing, and shows the cursor as a build that keeps no row does.</summary>
    [Fact]
    public void ACursorKeyThatHasToBuild_ShowsTheCursor()
    {
        using var t = OpenSync(90);
        Press(t, KeyCode.Down, 30);
        t.SetField("_syncRowsBuild", -1);
        int builds = Builds(t);
        Press(t, KeyCode.Down);
        Assert.Equal(1, Builds(t) - builds);
        Assert.Equal(31, t.Field<int>("_syncSel"));
        Assert.Single(Ringed(t));
        AssertIsAFullBuild(t, "after a Down that built");
    }

    /// <summary>The keys and clicks that change the rows still build, and the window is what a build
    /// that keeps no row makes after each: Space (the direction), the rail, "Show equal" both ways,
    /// a rescan, and a click on a row after keys that built nothing.</summary>
    [Fact]
    public void AKeyThatChangesTheRows_StillBuilds()
    {
        using var t = OpenSync(30);
        Press(t, KeyCode.Down, 3);
        int builds = Builds(t);
        Press(t, KeyCode.Space);
        Assert.Equal(1, Builds(t) - builds);
        Assert.Equal("←", Rail(t, Name(5)));
        AssertIsAFullBuild(t, "after Space");
        // Again, on the row the last build made with the cursor on it.
        Press(t, KeyCode.Space);
        Assert.Equal("∅", Rail(t, Name(5)));
        // The rail of a row the cursor is not on.
        var rail = Rows(t)[Name(8)].Children[3].Layout.AbsoluteBorderBox;
        t.Session.Window.Click(rail.X + rail.Width / 2, rail.Y - Scroll(t).ScrollOffsetY + rail.Height / 2);
        t.Session.PumpFrames(2);
        Assert.Equal("←", Rail(t, Name(8)));
        Assert.Equal([Name(5)], Ringed(t));
        AssertIsAFullBuild(t, "after Space again and a rail click");

        // "Show equal" moves the cursor's place onto another row with a build: the row the keys
        // ringed must not keep its ring.
        builds = Builds(t);
        ShowEqual(t, true);
        Assert.True(Builds(t) > builds, "Show equal did not build");
        Assert.Single(Ringed(t));
        AssertIsAFullBuild(t, "after Show equal on");
        Press(t, KeyCode.Down, 2);
        ShowEqual(t, false);
        Assert.Single(Ringed(t));
        AssertIsAFullBuild(t, "after Show equal off");

        // A click on a row after keys that built nothing rings it, and only it.
        Press(t, KeyCode.Down, 2);
        var target = Rows(t)[Name(12)];
        var b = target.Layout.AbsoluteBorderBox;
        float scroll = Scroll(t).ScrollOffsetY;
        t.Session.Window.Click(b.X + 40, b.Y - scroll + b.Height / 2);
        t.Session.PumpFrames(2);
        Assert.Equal([Name(12)], Ringed(t));
        AssertIsAFullBuild(t, "after the click");

        // A rescan makes new rows: the cursor goes back to the top.
        builds = Builds(t);
        t.Invoke("RunSyncCompare");
        t.Session.PumpUntil(() => !t.Field<bool>("_syncComparing") && t.Field<System.Collections.IList>("_syncRows").Count == 30,
            System.TimeSpan.FromSeconds(20));
        t.Session.PumpFrames(2);
        Assert.True(Builds(t) > builds, "the rescan did not build");
        Assert.Equal([Name(0)], Ringed(t));
        AssertIsAFullBuild(t, "after a rescan");
    }

    /// <summary>With a reader, a cursor key tells it the row left is not selected and the row reached
    /// is, on their nodes, and builds no tree and no window. The rows are the buttons they were, named
    /// by what they show.</summary>
    [Fact]
    public void ACursorKey_TellsAReaderTheRowItReached_AndBuildsNoTree()
    {
        using var t = OpenSync(30, reader: true, out var bridge);
        t.Session.PumpFrames(3);
        var tree = t.Session.Window.AccessibilityTree;
        var selected = Selected(tree).ToArray();
        Assert.Single(selected);
        Assert.Equal(AccessibilityRole.Button, selected[0].Role);
        Assert.StartsWith(Name(0) + " ", selected[0].Name);
        int trees = t.Session.Window.AccessibilityTreesBuilt, builds = Builds(t);

        bridge!.ClearDiffs();
        t.Press(KeyCode.Down);
        t.Session.PumpFrames(2);
        var told = bridge.Diffs.SelectMany(d => d.PropertyChanged)
            .Where(c => c.PropertyName == AccessibilityProperties.IsSelected)
            .Select(c => $"{c.Node.Name!.Split(' ')[0]}={c.Node.IsSelected}").ToArray();
        Assert.Equal([$"{Name(0)}=False", $"{Name(2)}=True"], told);
        Assert.Equal(builds, Builds(t));
        Assert.Equal(trees, t.Session.Window.AccessibilityTreesBuilt);
        AssertIsAFullBuild(t, "after a Down with a reader", reader: true);
    }

    private static IEnumerable<AccessibilityNode> Selected(AccessibilityNode node) =>
        node.Children.SelectMany(Selected).Prepend(node).Where(n => n.IsSelected == true);

    // ── what the window shows ────────────────────────────────────────────

    /// <summary>What the window shows now, then after a build of the window and every part that keeps
    /// no row, which must match: the elements (kinds, texts, colours as resolved, rings, where each
    /// lies, the scroll, the reader's selected state), the reader's tree when one listens, and the
    /// pixels.</summary>
    internal static void AssertIsAFullBuild(DiffViewTests.Fixture t, string when, bool reader = false)
    {
        string kept = Sig(t.Root);
        string? keptTree = reader ? Tree(t.Session.Window.AccessibilityTree) : null;
        byte[] keptPx = Pixels(t);
        var cells = t.Field<object>("_syncCells");
        cells.GetType().GetMethod("Clear")!.Invoke(cells, null);
        typeof(CommanderApp).GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly, System.Type.EmptyTypes)!
            .Invoke(t.App, null);
        t.Session.PumpFrames(2);
        Same(kept, Sig(t.Root), $"the window {when}");
        if (keptTree is not null) Same(keptTree, Tree(t.Session.Window.AccessibilityTree), $"the reader's tree {when}");
        Assert.True(keptPx.AsSpan().SequenceEqual(Pixels(t)), $"the pixels {when} are not what a full build draws");
    }

    private static void Same(string kept, string full, string what)
    {
        if (kept == full) return;
        var xl = kept.Split('\n'); var yl = full.Split('\n');
        for (int l = 0; l < System.Math.Max(xl.Length, yl.Length); l++)
            if (l >= xl.Length || l >= yl.Length || xl[l] != yl[l])
                throw new Xunit.Sdk.XunitException($"{what} is not what a full build makes, line {l}:\n"
                    + $"  kept: {(l < xl.Length ? xl[l] : "<none>")}\n  full: {(l < yl.Length ? yl[l] : "<none>")}");
    }

    private static byte[] Pixels(DiffViewTests.Fixture t)
    {
        using var image = t.Session.Window.CapturePixels();
        using var pixmap = image.PeekPixels();
        return pixmap.GetPixelSpan().ToArray();
    }

    private static string Sig(Element root)
    {
        var sb = new StringBuilder();
        void Walk(Element el, int depth)
        {
            sb.Append(' ', depth).Append(el.GetType().Name);
            if (el is TextElement text) sb.Append(" text=").Append(text.Content).Append(" ink=").Append(text.ResolvedInherited.TextColor);
            if (el is BoxElement b) sb.Append(" bg=").Append(b.BackgroundColor).Append(" border=").Append(b.BorderColor)
                .Append(" bw=").Append(b.Box.BorderWidth);
            if (el.Role is { } role) sb.Append(" role=").Append(role);
            if (el.AccessibleSelected is { } sel) sb.Append(" selected=").Append(sel);
            if (el.IsFocused) sb.Append(" focused");
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

    private static string Tree(AccessibilityNode root)
    {
        var sb = new StringBuilder();
        void Walk(AccessibilityNode n, int depth)
        {
            sb.Append(' ', depth).Append(n.Role).Append(" name=").Append(n.Name).Append(" selected=").Append(n.IsSelected)
                .Append(System.FormattableString.Invariant($" @{n.BoundingRect.X:0.##},{n.BoundingRect.Y:0.##} {n.BoundingRect.Width:0.##}x{n.BoundingRect.Height:0.##}"))
                .Append('\n');
            foreach (var c in n.Children) Walk(c, depth + 1);
        }
        Walk(root, 0);
        return sb.ToString();
    }
}
