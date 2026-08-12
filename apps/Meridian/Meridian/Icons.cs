namespace Meridian;

/// <summary>
/// Phosphor icon names for the Meridian UI, rendered as real vector artwork by
/// <see cref="HultUI.Icons.Phosphor"/>. The mocks use placeholder glyphs
/// (▤ ▶ ◎ ≣ ⬡) with the icon choice left to implementation; these are those choices.
/// Every name is asserted to exist in the bundled set by MeridianIconTests, so a typo
/// fails the build rather than silently rendering nothing.
/// </summary>
public static class Icons
{
    // View rail (keys 1–5).
    public const string Requests = "paper-plane-tilt";
    public const string Runner = "play";
    public const string Discovery = "compass";           // also the front door's Discover card
    public const string History = "clock-counter-clockwise";
    public const string Environments = "stack";

    // Front door cards.
    public const string FolderOpen = "folder-open";
    public const string Plus = "plus";
    public const string ImportSpec = "download-simple";

    // Window controls (Windows/Linux style; bold so the marks survive caption size).
    public const string WinMinimize = "minus-bold";
    public const string WinMaximize = "square-bold";
    public const string WinClose = "x-bold";
}
