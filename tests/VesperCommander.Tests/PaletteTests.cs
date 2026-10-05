using System.Reflection;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The command palette (Ctrl+P) as a complete second route to the app's commands. It was a
/// hand-written shortlist: twelve of the rebindable commands — Pack and Extract among them —
/// could only be reached by knowing their chord, and the chords it did print were literals that
/// went on claiming the default after the user had rebound them.
/// </summary>
public class PaletteTests
{
    /// <summary>Every rebindable command is reachable from the palette. The list is matched by
    /// chord rather than by label, so renaming a row doesn't quietly drop its command.</summary>
    [Fact]
    public void EveryRebindableCommandIsInThePalette()
    {
        using var t = new Harness();
        t.Open();

        var shown = t.Commands().Select(c => c.Shortcut).Where(s => s != null).ToHashSet();
        var missing = Keybindings.Commands(Keybindings.Create())
            .Where(o => o.Cmd != Cmd.Palette)          // the palette does not list itself
            .Select(o => (label: o.Shortcut.Title, chord: o.Shortcut.KeysText))
            .Where(o => !shown.Contains(o.chord))
            .Select(o => $"{o.label} ({o.chord})")
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void PackAndExtractAreThere()
    {
        using var t = new Harness();
        t.Open();

        var labels = t.Commands().Select(c => c.Label).ToList();
        Assert.Contains(labels, l => l.StartsWith("Pack into archive"));
        Assert.Contains(labels, l => l.StartsWith("Extract archive"));
    }

    /// <summary>Choosing Pack from the palette opens the Pack dialog — the row runs the command
    /// rather than merely naming it.</summary>
    [Fact]
    public void ChoosingPackOpensTheDialog()
    {
        using var t = new Harness();
        t.WriteLeft("notes.txt", "x");
        t.Open();
        t.Mark("notes.txt");

        t.Run(l => l.StartsWith("Pack into archive"));

        Assert.True(t.Field<bool>("_packOpen"), "the palette's Pack row did not open the dialog");
    }

    /// <summary>A rebound command advertises its new chord. The captions used to be literals, so
    /// the palette kept teaching the default the user had just replaced.</summary>
    [Fact]
    public void ARowFollowsARebindInsteadOfAdvertisingTheDefault()
    {
        using var t = new Harness();
        t.Open();

        Assert.Equal("Alt+F5", t.ShortcutOf(l => l.StartsWith("Pack into archive")));

        var settings = t.Field<Settings>("_settings");
        settings.KeyBindings["Pack"] = "Ctrl+Shift+P";
        t.ApplySettings();

        Assert.Equal("Ctrl+Shift+P", t.ShortcutOf(l => l.StartsWith("Pack into archive")));
    }

    /// <summary>An unbound command — one whose chord was taken by another — shows no chord
    /// rather than a stale one, and stays reachable by name.</summary>
    [Fact]
    public void AnUnboundCommandShowsNoChordButIsStillListed()
    {
        using var t = new Harness();
        t.Open();

        t.Field<Settings>("_settings").KeyBindings["Pack"] = "";
        t.ApplySettings();

        var row = t.Commands().Single(c => c.Label.StartsWith("Pack into archive"));
        Assert.Null(row.Shortcut);
    }

    // ── harness ──────────────────────────────────────────────────────────
    private sealed class Harness : IDisposable
    {
        private readonly DiffViewTests.Fixture _t = new();

        public void Open() => _t.Open();
        public void Dispose() => _t.Dispose();
        public void WriteLeft(string name, string text) => _t.WriteLeft(name, text);
        public void Mark(params string[] names) => _t.Mark('L', names);
        public T Field<T>(string name) => _t.Field<T>(name);

        /// <summary>What saving the Options does with the bindings: load them into the keymap.</summary>
        public void ApplySettings() => _t.Invoke("RebuildKeymap");

        private object App => typeof(DiffViewTests.Fixture)
            .GetField("_app", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(_t)!;

        /// <summary>The palette's command list, flattened to the two things these tests care about.</summary>
        public List<(string Label, string? Shortcut)> Commands() =>
            Rows().Select(c => (c.Title, c.Keys)).ToList();

        private IReadOnlyList<HultUI.Core.Builders.PaletteCommand> Rows() =>
            (IReadOnlyList<HultUI.Core.Builders.PaletteCommand>)typeof(CommanderApp)
                .GetMethod("Commands", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(App, null)!;

        public string? ShortcutOf(Func<string, bool> label) =>
            Commands().Single(c => label(c.Label)).Shortcut;

        /// <summary>Invoke a palette row the way selecting it does.</summary>
        public void Run(Func<string, bool> label)
        {
            Rows().First(c => label(c.Title)).Run();
            _t.Session.PumpFrame();
        }
    }
}
