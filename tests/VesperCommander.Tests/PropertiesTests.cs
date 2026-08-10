using System.IO;
using System.IO.Compression;
using System.Linq;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Alt+Enter — Explorer's Properties chord. The sheet itself is the Windows shell's, so
/// nothing here can assert on the dialog; what is testable is everything around it: that
/// the chord reaches the command at all (proved through the one path that stops short of
/// the shell), that adding it didn't quietly displace a neighbouring binding, and that the
/// path handed to the shell is in the shape it accepts.
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
        var zip = Path.Combine(t.Left, "assets.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("readme.txt").Open()))
                w.Write("hello");
        t.Open();

        t.PutCursorOn('L', "assets.zip");
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "readme.txt"));
        t.PutCursorOn('L', "readme.txt");

        t.Press(KeyCode.Enter, KeyModifiers.Alt);

        Assert.Equal("No properties for an archive member", t.Field<string?>("_notice"));
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
