using HultUI.Core.Events;

namespace Reel;

/// <summary>
/// Reel's own keys, beside the transport keys <see cref="HultUI.Media.VideoPlayer.Shortcuts"/>
/// owns: one table the handler matches and the hints read.
/// </summary>
internal static class Shortcuts
{
    public static readonly ShortcutMap Map = Build();

    public static Shortcut? Find(KeyDownEvent e) => Map.Find(e);

    public static string Keys(string id) => Map.Keys(id);

    private static ShortcutMap Build()
    {
        static KeyChord K(KeyCode key, KeyModifiers modifiers = KeyModifiers.None) => new(key, modifiers);
        var map = new ShortcutMap();
        const string group = "Reel";
        map.Add("open", group, "Open a file", K(KeyCode.O, KeyModifiers.Ctrl));
        // The Page keys walk chapters inside a chaptered file and roll on to the next file at
        // either end; N and P are always the file.
        map.Add("nextChapter", group, "Next chapter, or the next file", K(KeyCode.PageDown));
        map.Add("previousChapter", group, "Previous chapter, or the previous file", K(KeyCode.PageUp));
        map.Add("nextFile", group, "Next file", K(KeyCode.N));
        map.Add("previousFile", group, "Previous file", K(KeyCode.P));
        map.Add("chapters", group, "Chapters", K(KeyCode.C));
        map.Add("back", group, "Close the chapters, then leave full screen", K(KeyCode.Escape));
        return map;
    }
}
