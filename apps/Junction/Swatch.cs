using HultUI.Core.Primitives;
using Junction.Theme;

namespace Junction;

/// <summary>
/// A curated badge/icon colour from the Nocturne palette. Each swatch carries
/// three tuned values: <see cref="Solid"/> (the swatch dot / workspace icon
/// colour) and a <see cref="Fg"/>/<see cref="Bg"/> pair for the tinted tile
/// (bg fill, fg glyph/letter). The design deliberately offers only these six —
/// no free colour picker.
/// </summary>
public readonly record struct Swatch(string Id, string Label, Color Solid, Color Fg, Color Bg);

public static class Swatches
{
    // oklch values from the handoff converted to sRGB (see styles.css swatches()).
    public static readonly IReadOnlyList<Swatch> All =
    [
        new("blurple", "Blurple", Nocturne.Accent,      Nocturne.Accent200,  Nocturne.Accent800),
        new("teal",    "Teal",    Hex("#5db4be"),       Hex("#93d8e0"),      Hex("#093a3f")),
        new("green",   "Green",   Hex("#7ebc8b"),       Hex("#a9ddb2"),      Hex("#1a3821")),
        new("amber",   "Amber",   Hex("#ddae6c"),       Hex("#f2ca92"),      Hex("#463111")),
        new("rose",    "Rose",    Hex("#dc8b8d"),       Hex("#fab1b2"),      Hex("#4a2325")),
        new("neutral", "Slate",   Nocturne.Neutral400,  Nocturne.Neutral200, Nocturne.Neutral800),
    ];

    public static Swatch ById(string? id)
    {
        foreach (var s in All)
            if (s.Id == id) return s;
        return All[0];   // default: blurple
    }

    /// <summary>A deterministic swatch id for a repo that hasn't been assigned one,
    /// so freshly-added repos still get stable, varied colours.</summary>
    public static string SeedId(string key)
    {
        uint h = 2166136261u;
        foreach (var c in key) { h ^= c; h *= 16777619u; }
        return All[(int)(h % (uint)All.Count)].Id;
    }

    private static Color Hex(string hex) => Color.FromHex(hex);
}
