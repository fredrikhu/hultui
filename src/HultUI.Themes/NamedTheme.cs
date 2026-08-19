using HultUI.Core.Primitives;

namespace HultUI.Themes;

/// <summary>
/// One named colour scheme, as the handful of seed colours a scheme is actually published
/// with — a ground, a raised surface, a text colour, an accent or two, and the red / green /
/// amber every editor needs for diffs, errors and search hits. The tonal ramps an app styles
/// against are generated from those (see <see cref="Ramp"/>), so adding a scheme means
/// transcribing roughly a dozen values rather than forty.
/// </summary>
/// <remarks>
/// An app's own design system is not one of these. Nocturne's ramps were drawn by hand and
/// the apps keep them as literals, so the default look is pixel-for-pixel what it was; this
/// type is how the <i>borrowed</i> schemes are described, and they come out of the curve.
/// </remarks>
public sealed record NamedTheme
{
    /// <summary>Stable id, persisted in settings. Never rename one — a renamed id reads back
    /// as "unknown theme" and silently drops the user to the default.</summary>
    public required string Id { get; init; }

    /// <summary>What the picker shows.</summary>
    public required string Name { get; init; }

    /// <summary>One line under the name in the picker. Says what the scheme is like to look
    /// at, not who made it.</summary>
    public required string Blurb { get; init; }

    /// <summary>Whether the ground is darker than the text. Decides which way every ramp
    /// runs, so it is not cosmetic.</summary>
    public required bool IsDark { get; init; }

    // ── Seeds ────────────────────────────────────────────────────────────
    public required Color Bg { get; init; }          // the app ground
    public required Color Surface { get; init; }     // raised bars, menus, dialogs
    public required Color Text { get; init; }        // primary text
    public required Color Neutral { get; init; }     // mid-tone the grey ramp is spun from
    public required Color Accent { get; init; }      // primary accent
    public required Color Accent2 { get; init; }     // secondary accent (renames, remote refs)
    public required Color Red { get; init; }         // deletions, danger, marked files
    public required Color Green { get; init; }       // additions
    public required Color Amber { get; init; }       // search hits, tags, warnings

    /// <summary>Which of TextMateSharp's bundled themes colours code in this scheme. Chrome
    /// and code are separate palettes, and a warm scheme wrapped around VS Code Dark+ reads
    /// as two apps stacked on top of each other.</summary>
    public required string SyntaxTheme { get; init; }

    /// <summary>This theme's nine-step neutral scale. See <see cref="Ramp"/> for what the
    /// numbering means — which end is 100 depends on <see cref="IsDark"/>.</summary>
    public Color[] Neutrals => Ramp.GenerateNeutral(Neutral, Bg, IsDark);

    public Color[] Accents => Ramp.Generate(Accent, IsDark);

    public Color[] Accent2s => Ramp.Generate(Accent2, IsDark);

    /// <summary>The primary text colour at <paramref name="opacity"/> — the muted steps
    /// every label in both apps is written against.</summary>
    public Color On(double opacity) => ColorMath.Alpha(Text, opacity);
}
