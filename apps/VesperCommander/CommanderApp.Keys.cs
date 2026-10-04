using HultUI.Core.Events;

namespace VesperCommander;

public partial class CommanderApp
{
    // ── Keymap (rebindable commands) ─────────────────────────────────────
    private readonly ShortcutMap _keys = Keybindings.Create();
    private ShortcutMap? _draftKeys;   // the Keyboard page's copy, while Options is open
    private Cmd? _rebindCmd;           // set while the Keyboard page is capturing a new chord
    private string? _rebindRefusal;    // why the last key pressed in a capture was not taken

    /// <summary>Apply the user's overrides from settings. Call after settings load / save.</summary>
    private void RebuildKeymap() => _keys.LoadOverrides(_settings.KeyBindings);

    /// <summary>The draft's keymap: the map with the draft settings' overrides, made when the
    /// Keyboard page first asks and dropped whenever the draft's bindings are replaced.</summary>
    private ShortcutMap DraftKeys
    {
        get
        {
            if (_draftKeys is null)
            {
                _draftKeys = Keybindings.Create();
                _draftKeys.LoadOverrides(_draft.KeyBindings);
            }
            return _draftKeys;
        }
    }

    /// <summary>A command's keys as shown — a caption, a tooltip, a palette row — so they move
    /// when it is rebound. Empty when it has none.</summary>
    private string KeyText(Cmd cmd) => _keys.Keys(cmd.ToString());

    /// <summary><see cref="KeyText"/>, or null when there is none: a menu's shortcut column.</summary>
    private string? KeyHint(Cmd cmd) => KeyText(cmd) is { Length: > 0 } keys ? keys : null;

    private bool Pressed(Cmd cmd, KeyDownEvent e) => _keys.Matches(cmd.ToString(), e);

    /// <summary>Match a pressed chord to a rebindable command. <paramref name="wantGlobal"/>
    /// selects the Ctrl/Alt-modified commands (which fire even from a focused field) vs the
    /// bare ones (which fire only in a pane), preserving the original two-tier dispatch.</summary>
    private bool MatchHotkey(KeyDownEvent e, bool wantGlobal, out Cmd cmd)
    {
        cmd = default;
        bool global = (e.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Super | KeyModifiers.Alt)) != 0;
        if (global != wantGlobal) return false;
        return _keys.Find(e, Keybindings.Group) is { Rebindable: true } s && Enum.TryParse(s.Id, out cmd);
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

    /// <summary>
    /// The Keyboard page's capture: bind <paramref name="cmd"/> to <paramref name="chord"/> in
    /// the draft. The map takes the chord from any command that had it (that one goes back to
    /// its default, or to none when its default is this chord), and refuses a key Vesper keeps
    /// for itself — then the capture stays open and says whose key it is.
    /// </summary>
    private void RebindTo(Cmd cmd, KeyChord chord)
    {
        var draft = DraftKeys;
        if (draft.Rebind(cmd.ToString(), chord))
        {
            _draft.KeyBindings = draft.SaveOverrides();
            _rebindCmd = null;
            _rebindRefusal = null;
        }
        else
        {
            var holder = draft.Holder(chord, draft[cmd.ToString()]);
            _rebindRefusal = $"{chord} is taken: {holder?.Title.ToLowerInvariant()}";
        }
    }
}
