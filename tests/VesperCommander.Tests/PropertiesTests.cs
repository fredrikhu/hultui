using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Alt+Enter and the context-menu key — the two ways into the shell. Neither the Properties
/// sheet nor the context menu is Vesper's to inspect, so what is tested is everything around
/// them: that the keys reach the commands (proved through the one path that stops short of
/// the shell), what the commands are scoped to, that adding the bindings didn't quietly
/// displace a neighbour, and that the path handed over is in the shape the shell accepts.
/// </summary>
public class PropertiesTests
{
    /// <summary>An archive member has no shell identity, so the command has to stop and say
    /// so. This is also the only end-to-end route available: it drives the real chord through
    /// the real key handling and dispatch, and comes back with an observable answer instead
    /// of a dialog on the developer's desktop.</summary>
    [Fact]
    public void AltEnterInsideAnArchiveSaysSoInsteadOfCallingTheShell()
    {
        using var t = new DiffViewTests.Fixture();
        InArchive(t);

        t.Press(KeyCode.Enter, KeyModifiers.Alt);

        Assert.Equal("No properties for archive members", t.Field<string?>("_notice"));
    }

    /// <summary>The context-menu key takes the same route to the same scope, so an archive
    /// member stops it in the same place. Proves the key reaches the command at all — the menu
    /// itself is the shell's and can't be asserted on.</summary>
    [Fact]
    public void TheContextMenuKeyInsideAnArchiveSaysSoToo()
    {
        using var t = new DiffViewTests.Fixture();
        InArchive(t);

        t.Press(KeyCode.Menu);

        Assert.Equal("No context menu for archive members", t.Field<string?>("_notice"));
    }

    /// <summary>The headless harness injects a <see cref="KeyCode"/> directly, so the test
    /// above would pass even if no physical key ever produced one. This covers the other half:
    /// the scancode the context-menu key actually sends. SDL calls it APPLICATION and keeps
    /// MENU for a HID usage no PC keyboard has, so the obvious-looking mapping is the wrong
    /// one and the key arrived as Unknown before this.</summary>
    [Fact]
    public void TheContextMenuKeysScancodeArrivesAsTheMenuKey()
    {
        var translate = typeof(HultUI.Platform.SDL3.EventTranslator)
            .GetMethod("TranslateScancode", BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.Equal(KeyCode.Menu, translate.Invoke(null, [SDL.SDL_Scancode.SDL_SCANCODE_APPLICATION]));
    }

    /// <summary>The stated priority: a marked selection is the scope, wherever the cursor
    /// happens to be sitting. Same rule F5/F6/F8 already follow.</summary>
    [Fact]
    public void AMarkedSelectionIsTheScopeNotTheRowUnderTheCursor()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "a");
        t.WriteLeft("b.txt", "b");
        t.WriteLeft("c.txt", "c");
        t.Open();

        t.PutCursorOn('L', "a.txt");
        Mark(t, "b.txt", "c.txt");

        Assert.Equal(
            [Path.Combine(t.Left, "b.txt"), Path.Combine(t.Left, "c.txt")],
            Scope(t));
    }

    [Fact]
    public void WithNothingMarkedTheScopeIsTheRowUnderTheCursor()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "a");
        t.WriteLeft("b.txt", "b");
        t.Open();

        t.PutCursorOn('L', "b.txt");

        Assert.Equal([Path.Combine(t.Left, "b.txt")], Scope(t));
    }

    /// <summary>".." is a way back, not a file. Neither the shell sheet nor the menu should
    /// open on it — the same reason copy and delete skip it.</summary>
    [Fact]
    public void TheParentRowIsNeverTheScope()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "a");
        t.Open();

        t.PutCursorOn('L', "..");

        Assert.Null(Scope(t));
    }

    /// <summary>A right press marks straight away so a quick right-click feels instant; when
    /// it turns out to be a hold, that mark has to come back off. Getting this wrong leaves a
    /// stray mark behind every context menu.</summary>
    [Fact]
    public void AHoldTakesBackTheMarkThePressApplied()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "a");
        t.WriteLeft("b.txt", "b");
        t.Open();
        Mark(t, "b.txt");

        var pressed = t.Rows('L').First(r => r.Name == "a.txt");
        t.Invoke("BeginRightMark", 'L', pressed);
        Assert.Contains("a.txt", Marks(t));      // the press marked it

        t.Invoke("RevertRightMark");

        Assert.Equal(["b.txt"], Marks(t).OrderBy(s => s).ToArray());
    }

    /// <summary>The bug that made hold-right-click stop opening anything: the menu was scoped
    /// through <c>OperationTargets</c>, which reads the <em>cursor</em> row. A right press
    /// deliberately doesn't move the cursor — it starts a mark sweep — so the menu was about
    /// whatever row the cursor happened to be on, and on ".." that is nothing at all, which
    /// <c>ShellScope</c> returns as "no menu" without a word about why.</summary>
    [Fact]
    public void AHoldIsAboutTheRowThePointerIsOnNotTheCursorRow()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "a");
        t.WriteLeft("b.txt", "b");
        t.Open();
        t.PutCursorOn('L', "..");                // where the cursor sits on entering a folder

        var pressed = t.Rows('L').First(r => r.Name == "b.txt");

        Assert.Equal([Path.Combine(t.Left, "b.txt")], ContextTargets(t, pressed));
    }

    /// <summary>Pressing on a row that is part of the marked set means the set — the same rule
    /// Explorer follows, and what makes "mark five, right-hold one" open one menu for five.</summary>
    [Fact]
    public void AHoldOnAMarkedRowIsAboutTheWholeMarkedSet()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "a");
        t.WriteLeft("b.txt", "b");
        t.WriteLeft("c.txt", "c");
        t.Open();
        Mark(t, "a.txt", "c.txt");

        var pressed = t.Rows('L').First(r => r.Name == "a.txt");

        Assert.Equal(
            [Path.Combine(t.Left, "a.txt"), Path.Combine(t.Left, "c.txt")],
            ContextTargets(t, pressed).OrderBy(s => s, StringComparer.Ordinal).ToArray());
    }

    /// <summary>An unmarked row is itself even when other rows are marked: the pointer named
    /// one thing, and it isn't one of them.</summary>
    [Fact]
    public void AHoldOnAnUnmarkedRowIgnoresMarksElsewhere()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "a");
        t.WriteLeft("b.txt", "b");
        t.Open();
        Mark(t, "a.txt");

        var pressed = t.Rows('L').First(r => r.Name == "b.txt");

        Assert.Equal([Path.Combine(t.Left, "b.txt")], ContextTargets(t, pressed));
    }

    [Fact]
    public void AHoldOnTheParentRowIsAboutNothing()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "a");
        t.Open();

        var pressed = t.Rows('L').First(r => r.IsParent);

        Assert.Empty(ContextTargets(t, pressed));
    }

    /// <summary>The plumbing under those rules, driven with a real right press on a real row:
    /// the hold arms, and it remembers the row the pointer is on rather than reading the cursor
    /// when it fires. (Firing itself can't be tested — it hands off to the shell's own modal
    /// menu, which would open on whoever is running the suite.)</summary>
    [Fact]
    public void ARightPressArmsTheHoldAgainstTheRowUnderThePointer()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "a");
        t.WriteLeft("b.txt", "b");
        t.Open();

        var row = RowRect(t, 'L', "b.txt");
        t.PutCursorOn('L', "..");                // the cursor is somewhere else entirely

        t.Session.Window.MouseDown(row.X + 20, row.Y + row.Height / 2, MouseButton.Right);
        t.Session.PumpFrame();

        Assert.True(t.Field<bool>("_holdArmed"));
        Assert.Equal("b.txt", t.Field<FileEntry?>("_holdEntry")?.Name);
    }

    /// <summary>Releasing before the hold elapses is a plain right-click: it keeps its mark and
    /// leaves no armed timer behind to fire a menu after the fact.</summary>
    [Fact]
    public void ReleasingBeforeTheHoldElapsesDisarmsIt()
    {
        using var t = new DiffViewTests.Fixture();
        t.WriteLeft("a.txt", "a");
        t.Open();

        var row = RowRect(t, 'L', "a.txt");
        float x = row.X + 20, y = row.Y + row.Height / 2;
        t.Session.Window.MouseDown(x, y, MouseButton.Right);
        t.Session.PumpFrame();
        t.Session.Window.MouseUp(x, y, MouseButton.Right);
        t.Session.PumpFrame();

        Assert.False(t.Field<bool>("_holdArmed"));
        Assert.Contains("a.txt", Marks(t));      // the quick right-click's mark stays
    }

    // ── helpers ──────────────────────────────────────────────────────────
    private static List<string>? Scope(DiffViewTests.Fixture t) =>
        (List<string>?)t.Invoke("ShellScope", 'L', "Properties");

    /// <summary>Where a named row is on screen. Read off the cursor row's own Ref rather than
    /// computed from a row height — putting the cursor on it is what makes the app hand out
    /// that rectangle, and rows don't move when the cursor does.</summary>
    private static HultUI.Core.Primitives.Rect RowRect(DiffViewTests.Fixture t, char side, string name)
    {
        t.PutCursorOn(side, name);
        var reference = t.Field<HultUI.Core.Reactivity.Ref<HultUI.Core.Elements.BoxElement>>(
            side == 'L' ? "_cursorRowL" : "_cursorRowR");
        return reference.Current!.Layout.AbsoluteBorderBox;
    }

    /// <summary>What a menu opened on <paramref name="row"/> acts on, as full paths.</summary>
    private static string[] ContextTargets(DiffViewTests.Fixture t, FileEntry row) =>
        ((IReadOnlyList<FileEntry>)t.Invoke("ContextTargets", 'L', row)!)
            .Select(e => e.FullPath).ToArray();

    /// <summary>Leaf names of the left pane's marks — the set itself is keyed by full
    /// path (CommanderApp.MarkKey), which these single-folder tests don't need to spell out.</summary>
    private static HashSet<string> Marks(DiffViewTests.Fixture t) => t.MarkNames('L');

    private static void Mark(DiffViewTests.Fixture t, params string[] names)
    {
        t.Mark('L', names);
        t.Session.PumpFrame();
    }

    /// <summary>Put the left pane's cursor on a file inside a zip.</summary>
    private static void InArchive(DiffViewTests.Fixture t)
    {
        var zip = Path.Combine(t.Left, "assets.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("readme.txt").Open()))
                w.Write("hello");
        t.Open();

        t.PutCursorOn('L', "assets.zip");
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "readme.txt"));
        t.PutCursorOn('L', "readme.txt");
    }

    /// <summary>Alt+Enter lands one Shift away from Alt+Shift+Enter (measure all folders).
    /// The keymap is a dictionary keyed by chord and built with a plain indexer assignment,
    /// so a collision doesn't fail — it silently drops whichever command was written first.</summary>
    [Fact]
    public void NoTwoCommandsShareADefaultChord()
    {
        var clashes = Keybindings.Defaults
            .GroupBy(kv => kv.Value)
            .Where(g => g.Count() > 1)
            .Select(g => $"{Keybindings.Format(g.Key)} → {string.Join(", ", g.Select(kv => kv.Key))}")
            .ToList();

        Assert.True(clashes.Count == 0, "chords bound to more than one command: " + string.Join("; ", clashes));
    }

    /// <summary>Every command needs a row on the Options Keyboard page, or it can't be
    /// rebound — and <see cref="Keybindings.Order"/> is a second list that is easy to forget
    /// when adding one.</summary>
    [Fact]
    public void EveryCommandIsListedOnTheKeyboardPage()
    {
        var listed = Keybindings.Order.Select(o => o.cmd).ToHashSet();
        var missing = Keybindings.Defaults.Keys.Where(c => !listed.Contains(c)).ToList();

        Assert.True(missing.Count == 0, "not on the Keyboard page: " + string.Join(", ", missing));
    }

    [Theory]
    // A volume root keeps its separator: "C:" alone means the drive's current directory.
    [InlineData(@"C:\", @"C:\")]
    // Everything else loses it — "C:\dir\" doesn't bind to a shell item.
    [InlineData(@"C:\dir\", @"C:\dir")]
    [InlineData(@"C:\dir\file.txt", @"C:\dir\file.txt")]
    public void ThePathIsHandedToTheShellInTheShapeItAccepts(string given, string expected)
        => Assert.Equal(expected, ShellProperties.ShellTarget(given));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NothingIsShownForAnEmptyPath(string? given)
        => Assert.Null(ShellProperties.ShellTarget(given));
}
