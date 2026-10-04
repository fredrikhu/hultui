using HultUI.Core.Events;

namespace VesperCommander;

/// <summary>The rebindable commands (the ones listed on the Options Keyboard page).</summary>
public enum Cmd
{
    Copy, Move, Rename, Delete, NewFolder, Edit, View, Diff, Sync, Find, MeasureAll,
    Palette, Bookmarks, Options, NewTab, CloseTab, GoUpFolder, IntoFolder,
    Preview, Refresh, ToggleHidden, Swap, OpenInOther, Pack, Extract,
    ViewDetail, ViewBrief, ViewThumbnails, MultiRename, GotoPath, Properties, ContextMenu,
    MarkPattern, Holders,
}

/// <summary>
/// Every key the main window answers to, as one <see cref="ShortcutMap"/>: the rebindable
/// commands first, in Keyboard-page order, then the keys Vesper keeps for itself. Those are in
/// the map too, so a command can never be rebound onto one — it used to be possible, and a
/// command on Ctrl+C either never fired or silently replaced copying, depending on which check
/// ran first. User overrides live in <c>Settings.KeyBindings</c> (command name → chord text, ""
/// for none), written and read by the map.
/// </summary>
public static class Keybindings
{
    /// <summary>The one group: the main window, where every key means one thing.</summary>
    public const string Group = "Commands";

    public static ShortcutMap Create()
    {
        var map = new ShortcutMap();
        void Bind(Cmd cmd, string label, KeyCode key, KeyModifiers modifiers = KeyModifiers.None) =>
            map.Add(cmd.ToString(), Group, label, new KeyChord(key, modifiers)).CanRebind();
        Shortcut Fixed(string id, string label, params KeyChord[] keys) => map.Add(id, Group, label, keys);

        const KeyModifiers ctrl = KeyModifiers.Ctrl, alt = KeyModifiers.Alt, shift = KeyModifiers.Shift;

        Bind(Cmd.Copy, "Copy", KeyCode.F5);
        Bind(Cmd.Move, "Move", KeyCode.F6);
        Bind(Cmd.Rename, "Rename", KeyCode.F6, shift);
        Bind(Cmd.Delete, "Delete", KeyCode.F8);
        Bind(Cmd.NewFolder, "New folder", KeyCode.F7);
        Bind(Cmd.Edit, "Edit file", KeyCode.F4);
        Bind(Cmd.View, "View file", KeyCode.F3, shift);
        Bind(Cmd.Diff, "Diff", KeyCode.F3);
        Bind(Cmd.Sync, "Synchronize", KeyCode.F9);
        Bind(Cmd.Properties, "Properties", KeyCode.Enter, alt);           // Explorer's
        Bind(Cmd.ContextMenu, "Context menu", KeyCode.Menu);               // the context-menu key
        Bind(Cmd.Holders, "Who has this file open", KeyCode.L, ctrl | shift);   // "locks"
        Bind(Cmd.Find, "Find files", KeyCode.F7, alt);
        Bind(Cmd.MarkPattern, "Mark files by pattern", KeyCode.A, ctrl | shift);
        Bind(Cmd.MeasureAll, "Measure all folders", KeyCode.Enter, alt | shift);
        Bind(Cmd.Palette, "Command palette", KeyCode.P, ctrl);
        Bind(Cmd.Bookmarks, "Bookmark manager", KeyCode.D, ctrl);
        Bind(Cmd.Options, "Options", KeyCode.Comma, ctrl);
        Bind(Cmd.NewTab, "New tab", KeyCode.T, ctrl);
        Bind(Cmd.CloseTab, "Close tab", KeyCode.W, ctrl);
        Bind(Cmd.GoUpFolder, "Up one folder", KeyCode.PageUp, ctrl);
        Bind(Cmd.IntoFolder, "Into folder", KeyCode.PageDown, ctrl);
        Bind(Cmd.GotoPath, "Go to path", KeyCode.G, ctrl);
        Bind(Cmd.Preview, "Preview panel", KeyCode.Q, ctrl);
        Bind(Cmd.Refresh, "Refresh", KeyCode.R, ctrl);
        Bind(Cmd.ToggleHidden, "Toggle hidden files", KeyCode.H, ctrl);
        Bind(Cmd.Swap, "Swap panes", KeyCode.U, ctrl);
        Bind(Cmd.OpenInOther, "Open in other pane", KeyCode.Equals);
        Bind(Cmd.Pack, "Pack (create archive)", KeyCode.F5, alt);
        Bind(Cmd.Extract, "Extract archive", KeyCode.F9, alt);
        Bind(Cmd.ViewDetail, "View: Detail", KeyCode.F1, ctrl);
        Bind(Cmd.ViewBrief, "View: Brief", KeyCode.F2, ctrl);
        Bind(Cmd.ViewThumbnails, "View: Thumbnails", KeyCode.F3, ctrl);
        Bind(Cmd.MultiRename, "Multi-rename", KeyCode.M, ctrl);

        // Kept for Vesper: the clipboard and history chords, the tab and drive keys, and the
        // structural keys a pane is driven with.
        Fixed("CopyPath", "Copy the folder's path", new KeyChord(KeyCode.C, ctrl | shift));
        Fixed("ClipboardCopy", "Copy to the clipboard", new KeyChord(KeyCode.C, ctrl));
        Fixed("ClipboardCut", "Cut to the clipboard", new KeyChord(KeyCode.X, ctrl));
        Fixed("ClipboardPaste", "Paste from the clipboard", new KeyChord(KeyCode.V, ctrl));
        Fixed("MarkAll", "Mark everything", new KeyChord(KeyCode.A, ctrl));
        Fixed("JumpBookmark", "Jump to the Nth bookmark",
            Enumerable.Range(1, 9).Select(n => new KeyChord(KeyCode.Digit0 + (ushort)n, ctrl)).ToArray()).Shown("Ctrl+1 – 9");
        Fixed("NextTab", "Next tab", new KeyChord(KeyCode.Tab, ctrl));
        Fixed("PreviousTab", "Previous tab", new KeyChord(KeyCode.Tab, ctrl | shift));
        Fixed("Back", "Back", new KeyChord(KeyCode.Left, alt));
        Fixed("Forward", "Forward", new KeyChord(KeyCode.Right, alt));
        Fixed("LeftDrive", "Drive for the left pane", new KeyChord(KeyCode.F1, alt));
        Fixed("RightDrive", "Drive for the right pane", new KeyChord(KeyCode.F2, alt));
        Fixed("ReviewEmpty", "Review: the empty pane", new KeyChord(KeyCode.E, ctrl | shift));
        Fixed("ReviewOffline", "Review: the offline pane", new KeyChord(KeyCode.O, ctrl | shift));
        Fixed("ReviewDenied", "Review: the denied pane", new KeyChord(KeyCode.D, ctrl | shift));
        Fixed("ReviewConnecting", "Review: the connecting pane", new KeyChord(KeyCode.N, ctrl | shift));
        Fixed("ReviewClear", "Review: the real pane", new KeyChord(KeyCode.K, ctrl | shift));
        Fixed("SwitchPane", "Switch pane", new KeyChord(KeyCode.Tab));
        Fixed("Cursor", "Move the cursor",
            new KeyChord(KeyCode.Up), new KeyChord(KeyCode.Down), new KeyChord(KeyCode.Left), new KeyChord(KeyCode.Right),
            new KeyChord(KeyCode.PageUp), new KeyChord(KeyCode.PageDown), new KeyChord(KeyCode.Home), new KeyChord(KeyCode.End));
        Fixed("ExtendMarks", "Extend the marks",
            new KeyChord(KeyCode.Up, shift), new KeyChord(KeyCode.Down, shift), new KeyChord(KeyCode.Left, shift), new KeyChord(KeyCode.Right, shift),
            new KeyChord(KeyCode.PageUp, shift), new KeyChord(KeyCode.PageDown, shift), new KeyChord(KeyCode.Home, shift), new KeyChord(KeyCode.End, shift));
        Fixed("Open", "Open", new KeyChord(KeyCode.Enter));
        Fixed("Mark", "Mark", new KeyChord(KeyCode.Space), new KeyChord(KeyCode.Insert));
        Fixed("UpFolderKey", "Up a folder", new KeyChord(KeyCode.Backspace));
        Fixed("DeleteKey", "Delete", new KeyChord(KeyCode.Delete));
        Fixed("Clear", "Clear the search or the cut", new KeyChord(KeyCode.Escape));
        return map;
    }

    /// <summary>The rebindable commands, in Keyboard-page order.</summary>
    public static IEnumerable<(Cmd Cmd, Shortcut Shortcut)> Commands(ShortcutMap map) =>
        map.All.Where(s => s.Rebindable).Select(s => (Enum.Parse<Cmd>(s.Id), s));
}
