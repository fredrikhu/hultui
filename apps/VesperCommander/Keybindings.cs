using System.Text;
using HultUI.Core.Events;

namespace VesperCommander;

/// <summary>The rebindable commands (the ones listed on the Options Keyboard page).
/// Structural keys — arrows, Tab, Space, Enter, Backspace, the clipboard and history
/// combos, the pane drive pickers — stay fixed and aren't part of this set.</summary>
public enum Cmd
{
    Copy, Move, Rename, Delete, NewFolder, Edit, Diff, Sync, Find, MeasureAll,
    Palette, Bookmarks, Options, NewTab, CloseTab, GoUpFolder, IntoFolder,
    Preview, Refresh, ToggleHidden, Swap, OpenInOther, Pack, Extract,
    ViewDetail, ViewBrief, ViewThumbnails, MultiRename, GotoPath,
}

/// <summary>A key plus its modifiers — one per command.</summary>
public readonly record struct Chord(KeyCode Key, bool Ctrl, bool Alt, bool Shift);

/// <summary>Default chords, display metadata, and chord ⇄ string conversion for the
/// keymap. User overrides live in <c>Settings.KeyBindings</c> (command name → chord
/// string) and are merged over these defaults at load time.</summary>
public static class Keybindings
{
    public static readonly Dictionary<Cmd, Chord> Defaults = new()
    {
        [Cmd.Copy] = new(KeyCode.F5, false, false, false),
        [Cmd.Move] = new(KeyCode.F6, false, false, false),
        [Cmd.Rename] = new(KeyCode.F6, false, false, true),           // Shift+F6
        [Cmd.Delete] = new(KeyCode.F8, false, false, false),
        [Cmd.NewFolder] = new(KeyCode.F7, false, false, false),
        [Cmd.Edit] = new(KeyCode.F4, false, false, false),
        [Cmd.Diff] = new(KeyCode.F3, false, false, false),
        [Cmd.Sync] = new(KeyCode.F9, false, false, false),
        [Cmd.Find] = new(KeyCode.F7, false, true, false),             // Alt+F7
        [Cmd.MeasureAll] = new(KeyCode.Enter, false, true, true),     // Alt+Shift+Enter
        [Cmd.Palette] = new(KeyCode.P, true, false, false),
        [Cmd.Bookmarks] = new(KeyCode.D, true, false, false),
        [Cmd.Options] = new(KeyCode.Comma, true, false, false),
        [Cmd.NewTab] = new(KeyCode.T, true, false, false),
        [Cmd.CloseTab] = new(KeyCode.W, true, false, false),
        [Cmd.GoUpFolder] = new(KeyCode.PageUp, true, false, false),
        [Cmd.IntoFolder] = new(KeyCode.PageDown, true, false, false),
        [Cmd.Preview] = new(KeyCode.Q, true, false, false),
        [Cmd.Refresh] = new(KeyCode.R, true, false, false),
        [Cmd.ToggleHidden] = new(KeyCode.H, true, false, false),
        [Cmd.Swap] = new(KeyCode.U, true, false, false),
        [Cmd.OpenInOther] = new(KeyCode.Equals, false, false, false),
        [Cmd.Pack] = new(KeyCode.F5, false, true, false),       // Alt+F5
        [Cmd.Extract] = new(KeyCode.F9, false, true, false),    // Alt+F9
        [Cmd.ViewDetail] = new(KeyCode.F1, true, false, false),      // Ctrl+F1
        [Cmd.ViewBrief] = new(KeyCode.F2, true, false, false),       // Ctrl+F2
        [Cmd.ViewThumbnails] = new(KeyCode.F3, true, false, false),  // Ctrl+F3
        [Cmd.MultiRename] = new(KeyCode.M, true, false, false),      // Ctrl+M
        [Cmd.GotoPath] = new(KeyCode.G, true, false, false),         // Ctrl+G
    };

    /// <summary>Commands in display order, with their Keyboard-page labels.</summary>
    public static readonly (Cmd cmd, string label)[] Order =
    [
        (Cmd.Copy, "Copy"), (Cmd.Move, "Move"), (Cmd.Rename, "Rename"), (Cmd.Delete, "Delete"),
        (Cmd.NewFolder, "New folder"), (Cmd.Edit, "Edit file"), (Cmd.Diff, "Diff"), (Cmd.Sync, "Synchronize"),
        (Cmd.Find, "Find files"), (Cmd.MeasureAll, "Measure all folders"),
        (Cmd.Palette, "Command palette"), (Cmd.Bookmarks, "Bookmark manager"), (Cmd.Options, "Options"),
        (Cmd.NewTab, "New tab"), (Cmd.CloseTab, "Close tab"),
        (Cmd.GoUpFolder, "Up one folder"), (Cmd.IntoFolder, "Into folder"),
        (Cmd.GotoPath, "Go to path"),
        (Cmd.Preview, "Preview panel"), (Cmd.Refresh, "Refresh"),
        (Cmd.ToggleHidden, "Toggle hidden files"), (Cmd.Swap, "Swap panes"),
        (Cmd.OpenInOther, "Open in other pane"),
        (Cmd.Pack, "Pack (create archive)"), (Cmd.Extract, "Extract archive"),
        (Cmd.ViewDetail, "View: Detail"), (Cmd.ViewBrief, "View: Brief"), (Cmd.ViewThumbnails, "View: Thumbnails"),
        (Cmd.MultiRename, "Multi-rename"),
    ];

    public static string Format(Chord c)
    {
        var sb = new StringBuilder();
        if (c.Ctrl) sb.Append("Ctrl+");
        if (c.Alt) sb.Append("Alt+");
        if (c.Shift) sb.Append("Shift+");
        sb.Append(KeyName(c.Key));
        return sb.ToString();
    }

    public static Chord? Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        bool ctrl = false, alt = false, shift = false;
        var parts = s.Split('+');
        for (int i = 0; i < parts.Length - 1; i++)
            switch (parts[i].Trim())
            {
                case "Ctrl": ctrl = true; break;
                case "Alt": alt = true; break;
                case "Shift": shift = true; break;
                default: return null;
            }
        return KeyFromName(parts[^1].Trim()) is { } k ? new Chord(k, ctrl, alt, shift) : null;
    }

    /// <summary>A bare modifier key — ignored while capturing a rebind so the user can
    /// hold a modifier before pressing the real key.</summary>
    public static bool IsModifier(KeyCode k) => k is
        KeyCode.ShiftLeft or KeyCode.ShiftRight or KeyCode.CtrlLeft or KeyCode.CtrlRight
        or KeyCode.AltLeft or KeyCode.AltRight or KeyCode.SuperLeft or KeyCode.SuperRight;

    private static string KeyName(KeyCode k) => k switch
    {
        KeyCode.Comma => ",",
        KeyCode.Equals => "=",
        KeyCode.PageUp => "PgUp",
        KeyCode.PageDown => "PgDn",
        _ => k.ToString(),
    };

    private static KeyCode? KeyFromName(string s) => s switch
    {
        "," => KeyCode.Comma,
        "=" => KeyCode.Equals,
        "PgUp" => KeyCode.PageUp,
        "PgDn" => KeyCode.PageDown,
        _ => Enum.TryParse<KeyCode>(s, out var k) ? k : null,
    };
}
