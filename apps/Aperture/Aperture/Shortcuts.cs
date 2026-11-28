using HultUI.Core.Events;

namespace Aperture;

/// <summary>
/// Aperture's own keys — over a playing film, and while browsing — beside the transport keys
/// <see cref="HultUI.Media.VideoPlayer.Shortcuts"/> owns. Matched exactly: they used to answer
/// to any modifier, so Ctrl+F made the film full screen and Ctrl+1 went home.
/// </summary>
internal static class Shortcuts
{
    public static readonly ShortcutMap Map = Build();

    public static Shortcut? Find(KeyDownEvent e, string group) => Map.Find(e, group);

    public static bool Pressed(string id, KeyDownEvent e) => Map.Matches(id, e);

    public static string Keys(string id) => Map.Keys(id);

    private static ShortcutMap Build()
    {
        static KeyChord K(KeyCode key) => new(key);
        var map = new ShortcutMap();

        const string player = "Player";
        // A remote's and a keyboard's media keys too: stop leaves the film, as Escape does; next and
        // previous step the queue, or the chapters when there is no queue (VideoPlayer has the rest).
        map.Add("player.stop", player, "Close the report, else stop", K(KeyCode.Escape), K(KeyCode.MediaStop));
        map.Add("player.play", player, "Play or pause", K(KeyCode.Space), K(KeyCode.K), K(KeyCode.MediaPlayPause));
        map.Add("player.next", player, "Next in the queue, else the next chapter", K(KeyCode.MediaNext));
        map.Add("player.previous", player, "Previous in the queue, else the previous chapter", K(KeyCode.MediaPrevious));
        map.Add("player.fullscreen", player, "Full screen", K(KeyCode.F));
        map.Add("player.chapters", player, "Chapters", K(KeyCode.C));
        map.Add("player.queue", player, "Up next", K(KeyCode.Q));
        map.Add("player.nextChapter", player, "Next chapter", K(KeyCode.PageDown));
        map.Add("player.previousChapter", player, "Previous chapter", K(KeyCode.PageUp));

        const string browse = "Browse";
        // Back and Forward are a browser's: Alt+← and Alt+→ (and the mouse's side buttons,
        // which are not keys — ApertureApp.History.cs), with Escape and Backspace for Back.
        map.Add("browse.back", browse, "Back", K(KeyCode.Escape), K(KeyCode.Backspace),
            new KeyChord(KeyCode.Left, KeyModifiers.Alt));
        map.Add("browse.forward", browse, "Forward", new KeyChord(KeyCode.Right, KeyModifiers.Alt));
        map.Add("browse.search", browse, "Search", K(KeyCode.Slash));
        map.Add("browse.favourites", browse, "Favourites", K(KeyCode.Digit0));
        map.Add("browse.refresh", browse, "Refresh", K(KeyCode.F5));
        map.Add("browse.library", browse, "Home, then the libraries in rail order",
            Enumerable.Range(1, 9).Select(n => new KeyChord(KeyCode.Digit0 + (ushort)n)).ToArray()).Shown("1 – 9");
        return map;
    }
}
