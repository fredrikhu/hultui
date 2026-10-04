using HultUI.Core.Events;

namespace Meridian;

/// <summary>
/// The keys Meridian answers to, as one framework <see cref="ShortcutMap"/>: the key handler asks
/// it whether a press is a shortcut, and every hint that names a key — the palette's key column,
/// the buttons, the front door's cards, the footers — reads its text from it.
/// </summary>
/// <remarks>
/// The hints were typed by hand, and three said keys that did nothing: Ctrl I (import) and
/// Ctrl N (new collection) had no handler at all, and Ctrl R ran the collection only from the
/// Runner view while the palette offered it everywhere.
/// </remarks>
internal static class Shortcuts
{
    public static readonly ShortcutMap Map = Build();

    public static bool Pressed(string id, KeyDownEvent e) => Map.Matches(id, e);

    public static string Keys(string id) => Map.Keys(id);

    private static ShortcutMap Build()
    {
        const KeyModifiers ctrl = KeyModifiers.Ctrl;
        static KeyChord K(KeyCode key, KeyModifiers modifiers = KeyModifiers.None) => new(key, modifiers);

        var map = new ShortcutMap();
        const string global = "Global";
        map.Add("palette", global, "Command palette", K(KeyCode.P, ctrl));
        map.Add("view", global, "Requests · Runner · Discovery · History · Environments",
            Enumerable.Range(1, 5).Select(n => new KeyChord(KeyCode.Digit0 + (ushort)n)).ToArray()).Shown("1 – 5");
        map.Add("open", global, "Open a collection", K(KeyCode.O, ctrl));
        map.Add("save", global, "Save the request, or the environments on their view", K(KeyCode.S, ctrl));
        map.Add("close", global, "Close the tab", K(KeyCode.W, ctrl));
        map.Add("env", global, "Switch environment", K(KeyCode.E, ctrl));
        map.Add("run", global, "Run the collection", K(KeyCode.R, ctrl));
        map.Add("import", global, "Import an OpenAPI spec", K(KeyCode.I, ctrl));
        map.Add("send", "Requests", "Send", K(KeyCode.Enter, ctrl));
        map.Add("cancel", "Requests", "Cancel the send, or abort the run", K(KeyCode.Escape));
        map.Add("flip", "Requests", "Flip the response pane right ⇄ bottom", K(KeyCode.Backslash, ctrl));
        map.Add("find", "Requests", "Find in the response", K(KeyCode.F, ctrl));
        map.Add("history.walk", "History", "Walk the rows · open", K(KeyCode.Up), K(KeyCode.Down), K(KeyCode.Enter)).Shown("↑ ↓ ↵");
        map.Add("history.restore", "History", "Restore to the editor", K(KeyCode.R));
        return map;
    }
}
