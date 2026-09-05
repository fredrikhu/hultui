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
            if (_settings.KeyBindings.TryGetValue(cmd.ToString(), out var s))
            {
                if (s.Length == 0) continue;                     // explicitly unbound (a conflict steal)
                if (Keybindings.Parse(s) is { } c) chord = c;
            }
            _keyReverse[chord] = cmd;   // conflicts are prevented at rebind time (RebindTo steals)
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
            case Cmd.View: OpenViewer(side); break;
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
            case Cmd.Preview: TogglePreview(); break;
            case Cmd.Refresh: RefreshPane(side); break;
            case Cmd.ToggleHidden: ToggleHidden(); break;
            case Cmd.Swap: SwapPanes(); break;
            case Cmd.OpenInOther: NavigateTo(side == 'L' ? 'R' : 'L', Dir(side)); break;
            case Cmd.Extract: ExtractCursorArchive(); break;
            case Cmd.Pack: OpenPack(); break;
            case Cmd.ViewDetail: SetView(side, ViewMode.Detail); break;
            case Cmd.ViewBrief: SetView(side, ViewMode.Brief); break;
            case Cmd.ViewThumbnails: SetView(side, ViewMode.Thumbnails); break;
            case Cmd.MultiRename: OpenRename(); break;
            case Cmd.GotoPath: OpenGotoPrompt(side); break;
            case Cmd.Properties: ShowProperties(side); break;
            case Cmd.ContextMenu: ShowContextMenuAtCursor(side); break;
            case Cmd.MarkPattern: OpenMarkPatternPrompt(side); break;
            case Cmd.Holders: ShowHolders(side); break;
        }
    }

    /// <summary>Record a rebind in the draft: store the chord as an override (or drop it when
    /// it matches the default, so only real changes persist), then <b>steal</b> the chord from
    /// any other command that held it — that command reverts to its default, or becomes
    /// explicitly unbound ("") when its default is the very chord we took. This keeps the map
    /// unambiguous instead of a nondeterministic last-write-wins.</summary>
    private void RebindTo(Cmd cmd, Chord chord)
    {
        if (chord.Equals(Keybindings.Defaults[cmd]))
            _draft.KeyBindings.Remove(cmd.ToString());
        else
            _draft.KeyBindings[cmd.ToString()] = Keybindings.Format(chord);

        foreach (var (other, _) in Keybindings.Order)
        {
            if (other == cmd) continue;
            if (CurrentChord(other, _draft) is { } oc && oc.Equals(chord))
            {
                if (Keybindings.Defaults[other].Equals(chord))
                    _draft.KeyBindings[other.ToString()] = "";   // its default IS this chord → unbind
                else
                    _draft.KeyBindings.Remove(other.ToString());  // revert to its own (free) default
            }
        }
    }

    /// <summary>The chord bound to a command in the given settings (draft while editing),
    /// falling back to the default. Null when the command is explicitly unbound (a stolen
    /// chord's former owner).</summary>
    private static Chord? CurrentChord(Cmd cmd, Settings s)
    {
        if (s.KeyBindings.TryGetValue(cmd.ToString(), out var str))
        {
            if (str.Length == 0) return null;                       // explicitly unbound
            if (Keybindings.Parse(str) is { } c) return c;
        }
        return Keybindings.Defaults[cmd];
    }
}
