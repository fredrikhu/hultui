using System.IO.Compression;
using System.Reflection;
using System.Text;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Reactivity;
using SkiaSharp;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The title bar, the rail, the command line and each pane's tab strip, path trail, column header
/// and footer are parts — child components the app keeps while it rebuilds around them
/// (CommanderApp.Parts.cs). A cursor key builds none of them, and leaves the other pane as it was.
/// The risk is a value a part shows changing while it is kept: a trail still lit for the pane you
/// left, a mark count or a filter's counts that did not follow, a tab strip without its new tab.
///
/// <para>The oracle is a full build. Every check here compares the parts on screen with the parts
/// a forced rebuild of all of them makes, after the keys and clicks that keep them (and says
/// which parts each may build) and after each value they show changes the way the app changes
/// it.</para>
/// </summary>
public class PartTests
{
    private const int Files = 100;

    private static string Name(int i) => $"f{i:00}.txt";

    private static DiffViewTests.Fixture Open(bool images = false)
    {
        var t = new DiffViewTests.Fixture();
        Directory.CreateDirectory(Path.Combine(t.Left, "docs"));
        for (int i = 0; i < 5; i++) File.WriteAllText(Path.Combine(t.Left, "docs", $"note{i}.md"), $"note {i}\n");
        for (int i = 0; i < Files; i++)
        {
            t.WriteLeft(Name(i), new string('x', 10 + i));
            t.WriteRight($"other{i:00}.txt", $"right {i}\n");
        }
        using (var zip = ZipFile.Open(Path.Combine(t.Left, "pack.zip"), ZipArchiveMode.Create))
            for (int i = 0; i < 3; i++)
            {
                using var w = new StreamWriter(zip.CreateEntry($"member{i}.txt").Open());
                w.Write(string.Concat(Enumerable.Repeat("member text ", 40 + i * 20)));
            }
        if (images)
            for (int i = 0; i < 6; i++)
            {
                using var bmp = new SKBitmap(40 + 8 * i, 30);
                using (var canvas = new SKCanvas(bmp)) canvas.Clear(new SKColor((byte)(30 * i), 120, 200));
                using var image = SKImage.FromBitmap(bmp);
                using var data = image.Encode(SKEncodedImageFormat.Png, 90);
                File.WriteAllBytes(Path.Combine(t.Left, $"a-img{i}.png"), data.ToArray());
            }
        t.Open();
        t.Session.PumpFrames(2);
        return t;
    }

    // ── What a cursor key builds ─────────────────────────────────────────

    /// <summary>A cursor key in Detail, Brief or Thumbnails builds the window and no part, and the
    /// other pane — its parts, its list, every element in it — is the one it was.</summary>
    [Theory]
    [InlineData(KeyCode.F1, KeyCode.Down, KeyCode.Up)]
    [InlineData(KeyCode.F2, KeyCode.Down, KeyCode.Right)]
    [InlineData(KeyCode.F3, KeyCode.Right, KeyCode.Down)]
    public void ACursorKey_BuildsNoPart_AndLeavesTheOtherPaneAsItWas(KeyCode view, KeyCode key, KeyCode other)
    {
        using var t = Open();
        t.Press(view, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);
        var right = Descendants(Pane(t, 'R')).ToList();
        int builds = Builds(t), parts = t.App.PartBuilds;

        var keys = new[] { key, key, key, other, key, KeyCode.End, KeyCode.Home, KeyCode.PageDown, other, key };
        foreach (var k in keys) t.Press(k);
        t.Session.PumpFrames(2);

        Assert.Equal(keys.Length, Builds(t) - builds);
        Assert.Equal(0, t.App.PartBuilds - parts);
        Assert.Equal(right, Descendants(Pane(t, 'R')).ToList());
        AssertPartsAreAFullBuild(t);
    }

    // ── The view-only paths ──────────────────────────────────────────────

    /// <summary>Every path that rebuilds the view and keeps the parts, and the parts each one may
    /// build because it changes what they show: a sweep or a mark the footer's count, the filter
    /// the footer's counts and the command line, Tab and a click in the other pane the trails (the
    /// active pane's root crumb is lit). Each rebuilds the window, builds exactly those parts, and
    /// leaves every part as a full build would make it.</summary>
    [Fact]
    public void ViewOnlyPaths_BuildOnlyThePartsThatShowWhatChanged_AsAFullBuildWould()
    {
        using var t = Open();
        var s = t.Session;
        void ViewOnly(string name, Action act, params string[] built) => Step(t, name, act, window: true, built);

        ViewOnly("Down", () => t.Press(KeyCode.Down));
        ViewOnly("PageDown", () => t.Press(KeyCode.PageDown));
        ViewOnly("Home", () => t.Press(KeyCode.Home));
        t.PutCursorOn('L', Name(4));
        ViewOnly("Insert", () => t.Press(KeyCode.Insert), "left footer");
        ViewOnly("Space on a file", () => t.Press(KeyCode.Space), "left footer");
        ViewOnly("Space again", () => t.Press(KeyCode.Space), "left footer");
        ViewOnly("Shift+Down", () => t.Press(KeyCode.Down, KeyModifiers.Shift), "left footer");
        ViewOnly("Shift+Down again", () => t.Press(KeyCode.Down, KeyModifiers.Shift), "left footer");
        ViewOnly("Up after a sweep", () => t.Press(KeyCode.Up));
        ViewOnly("Ctrl+A", () => t.Press(KeyCode.A, KeyModifiers.Ctrl), "left footer");
        ViewOnly("Ctrl+A again", () => t.Press(KeyCode.A, KeyModifiers.Ctrl), "left footer");
        ViewOnly("a row clicked", () => ClickRow(t, 'L', Name(9)));
        ViewOnly("Tab", () => t.Press(KeyCode.Tab), "left trail", "right trail");
        ViewOnly("Down in the right pane", () => t.Press(KeyCode.Down));
        ViewOnly("a row clicked in the other pane", () => ClickRow(t, 'L', Name(2)), "left trail", "right trail");
        ViewOnly("a filter typed", () => t.Press(KeyCode.Digit1), "left footer", "command line");
        ViewOnly("the filter extended", () => t.Press(KeyCode.Digit2), "left footer", "command line");
        ViewOnly("Down in the filter", () => t.Press(KeyCode.Down));
        ViewOnly("Backspace in the filter", () => t.Press(KeyCode.Backspace), "left footer", "command line");
        ViewOnly("Escape clears the filter", () => t.Press(KeyCode.Escape), "left footer", "command line");
        ViewOnly("Tab with a filter", () => { t.Press(KeyCode.Digit3); t.Press(KeyCode.Tab); },
            "left trail", "right trail", "left footer", "command line");

        // The grids: a key, and Brief scrolled sideways (its columns are realized per scroll).
        t.Press(KeyCode.Tab);
        t.Press(KeyCode.F2, KeyModifiers.Ctrl);
        s.PumpFrames(2);
        ViewOnly("Brief Right", () => t.Press(KeyCode.Right));
        ViewOnly("Brief scrolled", () =>
        {
            var scroll = t.Field<Ref<ScrollViewElement>>("_briefScrollL").Current!;
            var b = scroll.Layout.AbsoluteBorderBox;
            s.Window.Wheel(b.X + b.Width / 2, b.Y + b.Height / 2, 0, -3);
        });
        t.Press(KeyCode.F3, KeyModifiers.Ctrl);
        s.PumpFrames(2);
        ViewOnly("Thumbnails Down", () => t.Press(KeyCode.Down));
        ViewOnly("Thumbnails Insert", () => t.Press(KeyCode.Insert), "left footer");
    }

    /// <summary>The pointer over a sort column lights it in its pane's column header alone: no
    /// build of the window, no other part.</summary>
    [Fact]
    public void TheSortColumnUnderThePointer_BuildsTheColumnHeaderAlone()
    {
        using var t = Open();
        var size = Text(Part(t, "left column header"), "SIZE")!;
        var date = Text(Part(t, "right column header"), "MODIFIED")!;
        Step(t, "the pointer onto a sort column", () => Hover(t, size), window: false, "left column header");
        Step(t, "the pointer onto the other pane's", () => Hover(t, date), window: false, "left column header", "right column header");
        Step(t, "the pointer off it", () => { t.Session.Window.MoveMouse(5, 400); t.Session.PumpFrames(1); }, window: false, "right column header");
    }

    // ── Paths that build some parts ──────────────────────────────────────

    /// <summary>Another folder is that pane's tab strip, trail, column header and footer (and the
    /// footer again when the listing lands), and nothing else's.</summary>
    [Fact]
    public void APaneInAnotherFolder_BuildsItsOwnPartsAndNoOthers()
    {
        using var t = Open();
        t.PutCursorOn('L', "docs");
        Step(t, "into docs", () =>
        {
            t.Press(KeyCode.Enter);
            t.Session.PumpUntil(() => t.Dir('L').EndsWith("docs") && t.Rows('L').Count == 6);
        }, window: true, "left tab strip", "left trail", "left column header", "left footer");
        Step(t, "back out", () =>
        {
            t.Press(KeyCode.Backspace);
            t.Session.PumpUntil(() => !t.Dir('L').EndsWith("docs") && t.Rows('L').Count > Files);
        }, window: true, "left tab strip", "left trail", "left column header", "left footer");
        Step(t, "Alt+Left (back)", () =>
        {
            t.Press(KeyCode.Left, KeyModifiers.Alt);
            t.Session.PumpUntil(() => t.Dir('L').EndsWith("docs") && t.Rows('L').Count == 6);
        }, window: true, "left tab strip", "left trail", "left column header", "left footer");
    }

    /// <summary>The command line's field takes the keyboard as it opens — the app focuses it after
    /// its build, which places the part as it is now — and the line shows what it should while
    /// open, closed, and with a notice.</summary>
    [Fact]
    public void TheCommandLine_TakesTheKeyboard_AndShowsWhatItShould()
    {
        using var t = Open();
        t.Press(KeyCode.Left);
        var input = t.Field<Ref<TextInputElement>>("_cmdRef").Current;
        Assert.NotNull(input);
        Assert.False(input!.IsDetached);
        Assert.Same(input, t.Focused);
        t.Session.Window.TypeText("dir /b");
        t.Session.PumpFrames(2);
        Assert.Equal("dir /b", input.Text);
        AssertPartsAreAFullBuild(t, "open");
        // The prompt names the active pane's folder: a crumb clicked while it is open moves it.
        Step(t, "the pane moved under the open prompt", () =>
        {
            t.Invoke("NavigateTo", 'L', Path.Combine(t.Left, "docs"));
            t.Session.PumpUntil(() => t.Rows('L').Count == 6);
        }, window: true, "left tab strip", "left trail", "left column header", "left footer", "command line");
        Assert.NotNull(Text(Part(t, "command line"), "docs ›"));
        t.Press(KeyCode.Escape);
        Assert.Null(Text(Part(t, "command line"), "Enter", required: false));
        AssertPartsAreAFullBuild(t, "closed");

        // A notice set the way the archive paths set one, with a view-only build after it.
        Step(t, "a notice", () =>
        {
            t.SetField("_notice", "A notice for the command line");
            t.Invoke("MarkViewDirty");
        }, window: true, "command line");
        Assert.NotNull(Text(Part(t, "command line"), "A notice for the command line"));
        // Any key clears it, one that does nothing else too (Pause is bound to nothing).
        Step(t, "the next key clears it", () => t.Press(KeyCode.Pause), window: true, "command line");
        Assert.Null(Text(Part(t, "command line"), "A notice for the command line", required: false));
    }

    // ── What the parts show ──────────────────────────────────────────────

    /// <summary>Each value a part shows, changed the way the app changes it: the parts on screen are
    /// what a full build makes.</summary>
    [Fact]
    public void EveryValueAPartShows_ReachesIt()
    {
        using var t = Open();
        var s = t.Session;
        void After(string name, Action act)
        {
            act();
            s.PumpFrames(3);
            AssertPartsAreAFullBuild(t, name);
        }

        // Tabs: a new one, locked, a duplicate of a Brief tab sorted by size, cycled, closed.
        After("a new tab", () => t.Press(KeyCode.T, KeyModifiers.Ctrl));
        After("the tab locked", () => t.Invoke("ToggleTabLock", 'L', 1));
        After("Brief", () => t.Press(KeyCode.F2, KeyModifiers.Ctrl));
        After("sorted by size", () => t.Invoke("SetSort", 'L', SortMode.Size));
        After("the tab duplicated", () => t.Invoke("DuplicateTab", 'L', 1));
        Assert.Equal(ViewMode.Brief, (ViewMode)t.Invoke("ViewOf", 'L')!);
        After("the next tab", () => t.Press(KeyCode.Tab, KeyModifiers.Ctrl));
        After("a tab closed", () => t.Press(KeyCode.W, KeyModifiers.Ctrl));
        After("another closed", () => t.Press(KeyCode.W, KeyModifiers.Ctrl));
        After("Detail", () => t.Press(KeyCode.F1, KeyModifiers.Ctrl));
        After("sorted by date", () => ClickOn(t, Text(Part(t, "left column header"), "MODIFIED")!));
        After("sorted by name", () => t.Invoke("SetSort", 'L', SortMode.Name));
        After("Thumbnails", () => t.Press(KeyCode.F3, KeyModifiers.Ctrl));
        After("Detail again", () => t.Press(KeyCode.F1, KeyModifiers.Ctrl));

        // An archive (its ratio column and amber footer), a result set (its pill), and back.
        t.PutCursorOn('L', "pack.zip");
        After("into the archive", () =>
        {
            t.Press(KeyCode.Enter);
            s.PumpUntil(() => t.Dir('L').EndsWith("pack.zip") && t.Rows('L').Count == 4);
        });
        After("out of it", () =>
        {
            t.Press(KeyCode.Backspace);
            s.PumpUntil(() => !t.Dir('L').EndsWith("pack.zip") && t.Rows('L').Count > Files);
        });
        After("a result set fed in", () =>
        {
            t.Press(KeyCode.F7, KeyModifiers.Alt);
            t.SetField("_findRecursive", true);
            t.SetField("_findPattern", ".md");
            t.Invoke("RunFind");
            s.PumpUntil(() => !t.Field<bool>("_findSearching"), TimeSpan.FromSeconds(30));
            s.PumpFrames(2);
            t.Press(KeyCode.L, KeyModifiers.Ctrl);
        });
        Assert.NotNull(t.Invoke("ResultsOf", 'L'));
        After("Down in the results", () => t.Press(KeyCode.Down));
        After("the results left", () =>
        {
            t.Press(KeyCode.Backspace);
            s.PumpUntil(() => t.Rows('L').Count > Files);
        });

        // The footers: files cut for a move (set and taken back by hand, then a key: Ctrl+X and
        // Esc go through the system clipboard), a folder measured, a file added and the pane
        // refreshed.
        After("a file cut", () =>
        {
            t.Field<HashSet<string>>("_cutPending").Add(Path.Combine(t.Left, Name(3)));
            t.Press(KeyCode.Down);
        });
        After("the cut taken back", () =>
        {
            t.Field<HashSet<string>>("_cutPending").Clear();
            t.Press(KeyCode.Up);
        });
        t.PutCursorOn('L', "docs");
        After("a folder measured", () =>
        {
            t.Press(KeyCode.Space);
            s.PumpUntil(() => t.Field<System.Collections.IDictionary>("_dirSizes").Count == 1);
            s.PumpFrames(30);
        });
        After("a file added and refreshed", () =>
        {
            t.WriteLeft("zz-new.txt", "new");
            t.Press(KeyCode.R, KeyModifiers.Ctrl);
            s.PumpUntil(() => t.Rows('L').Any(e => e.Name == "zz-new.txt"));
        });

        // The rail, the trails' fit, the scale.
        // A bookmark (persisted: taken out again, so the next test's rail is the usual one).
        int bookmarks = t.Field<System.Collections.IList>("_bookmarks").Count;
        After("a bookmark added", () => t.Invoke("AddBookmark"));
        After("the bookmark removed", () => t.Invoke("RemoveBookmark", bookmarks));
        After("a narrow window", () => s.Window.SetClientSize(760, 700));
        After("a wide one", () => s.Window.SetClientSize(1240, 820));
        After("the display scale", () => s.Window.SetScale(1.5f));
    }

    /// <summary>Thumbnails landing count the footer's "decoding" down to the free space: the
    /// footer follows them, and no other part builds for them.</summary>
    [Fact]
    public void ThumbnailsLanding_BuildTheFooterAlone()
    {
        using var t = Open(images: true);
        t.Session.Window.PressKey(KeyCode.F3, KeyModifiers.Ctrl);
        var before = PartBuildCounts(t);
        var cache = () => t.Field<ThumbnailCache?>("_thumbsCache");
        t.Session.PumpUntil(() => cache() is { PendingCount: 0, Landed: > 0 }, TimeSpan.FromSeconds(20));
        t.Session.PumpFrames(3);
        foreach (var (part, n) in PartBuildCounts(t))
            if (part != "left footer")
                Assert.True(n == before.GetValueOrDefault(part), $"a thumbnail landing built the {part}");
        AssertPartsAreAFullBuild(t, "decoded");
        Assert.Null(Text(Part(t, "left footer"), "decoding", required: false, within: true));

        // A decode still running when a key rebuilds the view shows in the footer, and its landing
        // takes it off. The grid realizes its tiles (and starts their decodes) after the build that
        // made the footer, and the decodes race the frames, so one is put in flight by hand.
        var inFlight = (HashSet<string>)typeof(ThumbnailCache)
            .GetField("_inFlight", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cache())!;
        Step(t, "a decode in flight", () => { inFlight.Add("held"); t.Press(KeyCode.Right); }, window: true, "left footer");
        Assert.NotNull(Text(Part(t, "left footer"), "decoding 1", within: true));
        Step(t, "and landed", () => { inFlight.Remove("held"); t.Press(KeyCode.Left); }, window: true, "left footer");
        Assert.Null(Text(Part(t, "left footer"), "decoding", required: false, within: true));
    }

    /// <summary>Walking an overlay's list — the drive picker, the bookmark manager — rebuilds the
    /// overlay and no part.</summary>
    [Fact]
    public void AnOverlaysWalk_BuildsNoPart()
    {
        using var t = Open();
        t.Press(KeyCode.F1, KeyModifiers.Alt);
        Step(t, "the drive picker's Down", () => t.Press(KeyCode.Down), window: true);
        Step(t, "the drive picker's Home", () => t.Press(KeyCode.Home), window: true);
        t.Press(KeyCode.Escape);
        // Two bookmarks at least, so the walk moves (persisted: taken out again after).
        int bookmarks = t.Field<System.Collections.IList>("_bookmarks").Count;
        t.Invoke("AddBookmark");
        t.Invoke("NavigateTo", 'L', Path.Combine(t.Left, "docs"));
        t.Session.PumpUntil(() => t.Rows('L').Count == 6);
        t.Invoke("AddBookmark");
        t.Press(KeyCode.D, KeyModifiers.Ctrl);
        Step(t, "the bookmark manager's Down", () => t.Press(KeyCode.Down), window: true);
        Step(t, "the bookmark manager's Up", () => t.Press(KeyCode.Up), window: true);
        t.Press(KeyCode.Escape);
        while (t.Field<System.Collections.IList>("_bookmarks").Count > bookmarks) t.Invoke("RemoveBookmark", bookmarks);
    }

    // ── Where the parts lie ──────────────────────────────────────────────

    /// <summary>A part's host takes its root's place in the layout: the rail fills the body's
    /// height, and in a window too short for the lists the tab strips, trails, column headers and
    /// footers keep their heights while the lists give way.</summary>
    [Fact]
    public void ThePartsLieWhereTheirRootsDid()
    {
        using var t = Open();
        var rail = Part(t, "rail");
        Assert.Equal(rail.Parent!.Layout.AbsoluteContentBox.Height, rail.Layout.AbsoluteBorderBox.Height, 1);
        Assert.Equal(rail.Layout.AbsoluteBorderBox.Height, rail.Children[0].Layout.AbsoluteBorderBox.Height, 1);

        string[] bars = ["left tab strip", "left trail", "left column header", "left footer", "right tab strip",
            "right trail", "right column header", "right footer", "title bar", "command line"];
        var tall = bars.ToDictionary(n => n, n => Part(t, n).Layout.AbsoluteBorderBox.Height);
        t.Session.Window.SetClientSize(1240, 260);
        t.Session.PumpFrames(3);
        foreach (var n in bars)
            Assert.True(Math.Abs(tall[n] - Part(t, n).Layout.AbsoluteBorderBox.Height) < 0.5f,
                $"the {n} gave way in a short window: {tall[n]} → {Part(t, n).Layout.AbsoluteBorderBox.Height}");
        Assert.Equal(rail.Parent!.Layout.AbsoluteContentBox.Height, rail.Layout.AbsoluteBorderBox.Height, 1);
        AssertPartsAreAFullBuild(t, "short");
    }

    // ── Harness ──────────────────────────────────────────────────────────

    /// <summary>Runs <paramref name="act"/> and checks it built the window (or not) and exactly the
    /// named parts on screen (at least once each), and that every part is what a full build makes,
    /// before and after.</summary>
    private static void Step(DiffViewTests.Fixture t, string name, Action act, bool window, params string[] built)
    {
        AssertPartsAreAFullBuild(t, "before " + name);
        int builds = Builds(t);
        var before = PartBuildCounts(t);
        act();
        t.Session.PumpFrames(2);
        if (window) Assert.True(Builds(t) > builds, $"{name} did not rebuild the window");
        else Assert.True(Builds(t) == builds, $"{name} rebuilt the window");
        var after = PartBuildCounts(t);
        foreach (var (part, n) in after)
        {
            int was = before.GetValueOrDefault(part);
            bool expected = built.Contains(part);
            if (expected) Assert.True(n > was, $"{name} did not build the {part}");
            else Assert.True(n == was, $"{name} built the {part} ({n - was} builds)");
        }
        foreach (var part in built) Assert.True(after.ContainsKey(part), $"the {part} is not on screen");
        AssertPartsAreAFullBuild(t, "after " + name);
    }

    private static void AssertPartsAreAFullBuild(DiffViewTests.Fixture t, string? when = null)
    {
        var kept = PartSigs(t);
        int parts = t.App.PartBuilds;
        FullRebuild(t);
        t.Session.PumpFrames(2);
        Assert.True(t.App.PartBuilds - parts >= kept.Count,
            $"the forced rebuild built {t.App.PartBuilds - parts} of {kept.Count} parts");
        var full = PartSigs(t);
        Assert.Equal(full.Keys, kept.Keys);
        foreach (var (part, sig) in kept)
        {
            if (sig == full[part]) continue;
            var xl = sig.Split('\n'); var yl = full[part].Split('\n');
            for (int l = 0; l < Math.Max(xl.Length, yl.Length); l++)
                if (l >= xl.Length || l >= yl.Length || xl[l] != yl[l])
                    throw new Xunit.Sdk.XunitException(
                        $"the kept {part} is not what a full build makes{(when is null ? "" : " (" + when + ")")}, line {l}:\n"
                        + $"  kept: {(l < xl.Length ? xl[l] : "<none>")}\n  full: {(l < yl.Length ? yl[l] : "<none>")}");
        }
    }

    /// <summary>The parts on screen by name, each with what it shows.</summary>
    private static SortedDictionary<string, string> PartSigs(DiffViewTests.Fixture t)
    {
        var sigs = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, host) in Parts(t)) sigs[name] = Sig(host);
        return sigs;
    }

    private static Dictionary<string, int> PartBuildCounts(DiffViewTests.Fixture t) =>
        Parts(t).ToDictionary(p => p.Name, p => (int)p.Host.Component.GetType().GetProperty("Builds")!.GetValue(p.Host.Component)!);

    /// <summary>The parts' hosts in the tree, named.</summary>
    private static List<(string Name, ComponentHost Host)> Parts(DiffViewTests.Fixture t)
    {
        var names = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        void Add(object? part, string name) { if (part is not null) names[part] = name; }
        Add(t.Field<object?>("_chromePart"), "title bar");
        Add(t.Field<object?>("_railPart"), "rail");
        Add(t.Field<object?>("_cmdPart"), "command line");
        var panes = t.Field<Array>("_paneParts");
        for (int i = 0; i < 2; i++)
        {
            var p = panes.GetValue(i)!;
            string side = i == 0 ? "left" : "right";
            foreach (var (field, what) in new[] { ("Strip", "tab strip"), ("Trail", "trail"), ("ResultsTrail", "trail"),
                         ("Columns", "column header"), ("Foot", "footer") })
                Add(p.GetType().GetField(field)!.GetValue(p), $"{side} {what}");
        }
        var found = new List<(string, ComponentHost)>();
        void Walk(Element e)
        {
            if (e is ComponentHost h && names.TryGetValue(h.Component, out var n)) { found.Add((n, h)); return; }
            foreach (var c in e.Children) Walk(c);
        }
        Walk(t.Root);
        return found;
    }

    private static ComponentHost Part(DiffViewTests.Fixture t, string name) =>
        Parts(t).FirstOrDefault(p => p.Name == name).Host ?? throw new Xunit.Sdk.XunitException($"the {name} is not on screen");

    /// <summary>What a subtree shows: element kinds, keys, text, colours, tooltips, names, opacity
    /// and where each element lies, in tree order.</summary>
    private static string Sig(Element e)
    {
        var sb = new StringBuilder();
        void Walk(Element el, int depth)
        {
            sb.Append(' ', depth).Append(el.GetType().Name);
            if (el is not ComponentHost && el.Key is { } k) sb.Append(" key=").Append(k);
            if (el is TextElement t) sb.Append(" text=").Append(t.Content);
            if (el is TextInputElement input) sb.Append(" input=").Append(input.Text);
            if (el is BoxElement b) sb.Append(" bg=").Append(b.BackgroundColor).Append(" border=").Append(b.BorderColor);
            if (el.TooltipText is { } tip) sb.Append(" tip=").Append(tip);
            if (el.AccessibleName is { } n) sb.Append(" name=").Append(n);
            if (el.AutomationId is { } id) sb.Append(" id=").Append(id);
            sb.Append(" fg=").Append(el.ResolvedInherited.TextColor).Append(" w=").Append(el.ResolvedInherited.FontWeight);
            sb.Append(" op=").Append(el.Opacity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            var box = el.Layout.AbsoluteBorderBox;
            sb.Append(System.FormattableString.Invariant($" @{box.X:0.##},{box.Y:0.##} {box.Width:0.##}x{box.Height:0.##}"));
            sb.Append('\n');
            foreach (var child in el.Children) Walk(child, depth + 1);
        }
        Walk(e, 0);
        return sb.ToString();
    }

    private static void FullRebuild(DiffViewTests.Fixture t) =>
        typeof(CommanderApp).GetMethod("MarkDirty",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly, Type.EmptyTypes)!
            .Invoke(t.App, null);

    private static int Builds(DiffViewTests.Fixture t) => t.Field<int>("_builds");

    private static Element Pane(DiffViewTests.Fixture t, char side) =>
        t.Field<Ref<BoxElement>>(side == 'L' ? "_paneRefL" : "_paneRefR").Current
        ?? throw new Xunit.Sdk.XunitException($"the {side} pane is not mounted");

    private static IEnumerable<Element> Descendants(Element e)
    {
        yield return e;
        foreach (var c in e.Children)
            foreach (var d in Descendants(c)) yield return d;
    }

    private static Element? Text(Element root, string text, bool required = true, bool within = false)
    {
        var found = Descendants(root).FirstOrDefault(e => e is TextElement tx
            && (within ? tx.Content.Contains(text, StringComparison.Ordinal) : tx.Content == text));
        if (found is null && required) throw new Xunit.Sdk.XunitException($"no \"{text}\" under {root.GetType().Name}");
        return found;
    }

    /// <summary>Presses on the realized row showing <paramref name="name"/>.</summary>
    private static void ClickRow(DiffViewTests.Fixture t, char side, string name)
    {
        var list = t.Field<Ref<VirtualListElement<FileEntry>>>(side == 'L' ? "_vlistL" : "_vlistR").Current!;
        var row = list.Children.Skip(1).First(r => Descendants(r).Any(e => e is TextElement { Content: var c } && c == name));
        ClickOn(t, row);
    }

    private static void ClickOn(DiffViewTests.Fixture t, Element e)
    {
        var b = e.Layout.AbsoluteBorderBox;
        t.Session.Window.Click(b.X + b.Width / 2, b.Y + b.Height / 2);
        t.Session.PumpFrames(1);
    }

    private static void Hover(DiffViewTests.Fixture t, Element e)
    {
        var b = e.Layout.AbsoluteBorderBox;
        t.Session.Window.MoveMouse(b.X + b.Width / 2, b.Y + b.Height / 2);
        t.Session.PumpFrames(1);
    }
}
