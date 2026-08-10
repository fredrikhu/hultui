using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using HultUI.Core.Events;
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

    // ── helpers ──────────────────────────────────────────────────────────
    private static List<string>? Scope(DiffViewTests.Fixture t) =>
        (List<string>?)t.Invoke("ShellScope", 'L', "Properties");

    private static HashSet<string> Marks(DiffViewTests.Fixture t) =>
        (HashSet<string>)t.Invoke("Marks", 'L')!;

    private static void Mark(DiffViewTests.Fixture t, params string[] names)
    {
        var marks = Marks(t);
        foreach (var n in names) marks.Add(n);
        t.MarkDirty();
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
