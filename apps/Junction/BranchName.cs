using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Junction.Theme;

namespace Junction;

/// <summary>
/// Parses a branch name into the display pieces the Junction UI shows: a short
/// prefix chip (FEAT / REL / WIP / MAIN / DEV), a leaf title, and a
/// <c>version · team</c> subtitle. Mirrors <c>parseBranch</c> in the design.
///
/// <para>The naming scheme the design assumes:
/// <c>feature/&lt;version&gt;/&lt;team&gt;/&lt;pc-id-and-title&gt;</c>,
/// <c>release/&lt;version&gt;</c>, <c>wip/&lt;version&gt;/&lt;team&gt;/&lt;…&gt;</c>.
/// Names that don't follow it degrade gracefully — the whole name becomes the
/// title and the prefix is upper-cased as-is.</para>
/// </summary>
public readonly record struct BranchName(
    string ChipLabel,
    Color ChipBg,
    Color ChipFg,
    string Title,
    string Sub,
    string Short)
{
    public bool HasSub => Sub.Length > 0;

    public static BranchName Parse(string name)
    {
        name ??= "";
        var parts = name.Split('/');
        var prefix = parts.Length > 0 ? parts[0] : name;

        var (label, bg, fg) = prefix switch
        {
            "feature" => ("FEAT", Nocturne.Accent800, Nocturne.Accent100),
            "wip" => ("WIP", Nocturne.Accent2_800, Nocturne.Accent2_100),
            "release" => ("REL", Nocturne.Neutral800, Nocturne.Neutral100),
            "main" or "master" => ("MAIN", Nocturne.Neutral800, Nocturne.Neutral200),
            "develop" => ("DEV", Nocturne.Neutral800, Nocturne.Neutral200),
            _ => (prefix.ToUpperInvariant(), Nocturne.Neutral800, Nocturne.Neutral100),
        };

        var title = parts.Length > 1 ? parts[^1] : prefix;
        var sub = parts.Length >= 3 ? $"{parts[1]}  ·  {parts[2]}"
                : parts.Length == 2 ? parts[1]
                : "";

        return new BranchName(label, bg, fg, title, sub, name);
    }
}
