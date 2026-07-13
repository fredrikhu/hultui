namespace HultUI.Core.Elements;

public sealed class ContextMenuItem
{
    public string Label { get; init; } = "";
    public Action? Action { get; init; }
    public bool IsSeparator { get; init; }
    public bool IsDisabled { get; init; }
    public string? ShortcutHint { get; init; }

    /// <summary>Optional leading glyph (rendered as text; the app maps its own
    /// icon set to characters). When any item in a menu sets one, every item
    /// reserves an icon column so labels stay aligned.</summary>
    public string? Glyph { get; init; }

    /// <summary>Destructive items paint their glyph and label in the theme's
    /// danger colour.</summary>
    public bool IsDanger { get; init; }

    public static ContextMenuItem Separator() => new() { IsSeparator = true };
}
