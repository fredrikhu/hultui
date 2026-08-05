using HultUI.Core.Events;

namespace VesperCommander;

public partial class CommanderApp
{
    // ── Keymap (rebindable commands) ─────────────────────────────────────
    private readonly Dictionary<Chord, Cmd> _keyReverse = new();
    private Cmd? _rebindCmd;   // set while the Keyboard page is capturing a new chord

    /// <summary>Rebuild the pressed-chord → command lookup from defaults + user overrides.
    /// Call after settings load / save.</summary>
    private void RebuildKeymap()
    {
        _keyReverse.Clear();
        foreach (var (cmd, def) in Keybindings.Defaults)
        {
            var chord = def;
            if (_settings.KeyBindings.TryGetValue(cmd.ToString(), out var s) && Keybindings.Parse(s) is { } c)
                chord = c;
            _keyReverse[chord] = cmd;   // last write wins if two commands collide
        }
    }

    private static Chord ChordOf(KeyDownEvent e) => new(e.Key,
        e.Modifiers.HasFlag(KeyModifiers.Ctrl) || e.Modifiers.HasFlag(KeyModifiers.Super),
        e.Modifiers.HasFlag(KeyModifiers.Alt),
        e.Modifiers.HasFlag(KeyModifiers.Shift));

    /// <summary>Match a pressed chord to a rebindable command. <paramref name="wantGlobal"/>
    /// selects the Ctrl/Alt-modified commands (which fire even from a focused field) vs the
    /// bare ones (which fire only in a pane), preserving the original two-tier dispatch.</summary>
    private bool MatchHotkey(KeyDownEvent e, bool wantGlobal, out Cmd cmd)
    {
        var chord = ChordOf(e);
        if ((chord.Ctrl || chord.Alt) != wantGlobal) { cmd = default; return false; }
        return _keyReverse.TryGetValue(chord, out cmd);
    }

    private void Dispatch(Cmd cmd)
    {
        char side = _active;
        switch (cmd)
        {
            case Cmd.Copy: StartCopy(); break;
            case Cmd.Move: StartMove(); break;
            case Cmd.Rename: OpenRenamePrompt(side); break;
            case Cmd.Delete: StartDelete(); break;
            case Cmd.NewFolder: OpenNewFolderPrompt(side); break;
            case Cmd.Edit: EditFocused(side); break;
            case Cmd.Diff: OpenDiff(side); break;
            case Cmd.Sync: OpenSync(); break;
            case Cmd.Find: OpenFind(side); break;
            case Cmd.MeasureAll: MeasureAllDirs(side); break;
            case Cmd.Palette: TogglePalette(); break;
            case Cmd.Bookmarks: OpenBookmarks(); break;
            case Cmd.Options: OpenOptions(); break;
            case Cmd.NewTab: NewTab(side); break;
            case Cmd.CloseTab: CloseTab(side, TabIndex(side)); break;
            case Cmd.GoUpFolder: GoUp(side); break;
            case Cmd.IntoFolder: DescendCursor(side); break;
            case Cmd.Preview: CyclePreview(); break;
            case Cmd.Refresh: RefreshPane(side); break;
            case Cmd.ToggleHidden: ToggleHidden(); break;
            case Cmd.Swap: SwapPanes(); break;
            case Cmd.OpenInOther: NavigateTo(side == 'L' ? 'R' : 'L', Dir(side)); break;
            case Cmd.Extract: ExtractCursorArchive(); break;
            case Cmd.Pack: OpenPack(); break;
        }
    }

    /// <summary>Record a rebind in the draft: store the chord as an override, or drop the
    /// override when it matches the default (so only real changes persist).</summary>
    private void RebindTo(Cmd cmd, Chord chord)
    {
        if (chord.Equals(Keybindings.Defaults[cmd]))
            _draft.KeyBindings.Remove(cmd.ToString());
        else
            _draft.KeyBindings[cmd.ToString()] = Keybindings.Format(chord);
    }

    /// <summary>The chord bound to a command in the given settings (draft while editing),
    /// falling back to the default.</summary>
    private static Chord CurrentChord(Cmd cmd, Settings s) =>
        s.KeyBindings.TryGetValue(cmd.ToString(), out var str) && Keybindings.Parse(str) is { } c
            ? c : Keybindings.Defaults[cmd];
}
