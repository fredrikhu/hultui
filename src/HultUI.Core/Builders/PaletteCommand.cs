using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

/// <summary>One row of a <see cref="CommandPalette"/>: what it says, and what it does.</summary>
/// <param name="Title">The row's text, and what the query is matched against first.</param>
/// <param name="Run">What choosing it does. The palette is closed before it runs, so a command
/// that opens a dialog of its own gets the keyboard.</param>
public sealed record PaletteCommand(string Title, Action Run)
{
    /// <summary>Its kind or category — "Branch", "Request" — shown as a tag or a column (see
    /// <see cref="CommandPalette.GroupStyle"/>) and searched.</summary>
    public string? Group { get; init; }

    /// <summary>A second line under the title: a path, a count, where it leads. Searched.</summary>
    public string? Detail { get; init; }

    /// <summary>The keys that do the same without the palette, shown in a key chip — read
    /// them from the app's <see cref="Events.ShortcutMap"/> so the row cannot go stale.</summary>
    public string? Keys { get; init; }

    /// <summary>A leading picture, built afresh for each row it appears in.</summary>
    public Func<Element>? Icon { get; init; }

    /// <summary>Words it should be found by that it does not show.</summary>
    public string? Keywords { get; init; }
}

/// <summary>Where a <see cref="PaletteCommand.Group"/> is shown.</summary>
public enum PaletteGroupStyle
{
    /// <summary>Not at all; still searched.</summary>
    None,
    /// <summary>A column before the title.</summary>
    Leading,
    /// <summary>A tag after the title.</summary>
    Trailing,
}
