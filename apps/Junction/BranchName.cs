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

    /// <summary>
    /// Checks a name against the rules <c>git check-ref-format --branch</c> enforces,
    /// returning a sentence explaining the first problem, or null when the name is
    /// usable. Used wherever a branch name is chosen — typed into the new-branch,
    /// create-from and rename prompts, or *derived*, as the new-worktree prompt names the
    /// branch after the folder — so <c>my work</c> or <c>.tmp</c> is refused up front
    /// instead of reaching git and coming back as "fatal: '.tmp' is not a valid branch
    /// name", which says nothing about which character was the problem.
    /// </summary>
    public static string? Validate(string? name)
    {
        var n = name ?? "";
        if (n.Length == 0) return "Enter a branch name";
        if (n.Trim().Length != n.Length) return "A branch name can't start or end with a space";

        foreach (var c in n)
        {
            if (char.IsControl(c) || c == ' ') return "A branch name can't contain spaces or control characters";
            if (c is '~' or '^' or ':' or '?' or '*' or '[' or '\\')
                return $"A branch name can't contain '{c}'";
        }

        if (n.Contains("..")) return "A branch name can't contain '..'";
        if (n.Contains("@{")) return "A branch name can't contain '@{'";
        if (n.Contains("//")) return "A branch name can't contain an empty path component";
        if (n == "@") return "'@' is reserved by git";
        if (n.StartsWith('/') || n.EndsWith('/')) return "A branch name can't start or end with '/'";
        if (n.StartsWith('-')) return "A branch name can't start with '-'";
        if (n.EndsWith('.')) return "A branch name can't end with '.'";
        if (n.EndsWith(".lock", StringComparison.Ordinal)) return "A branch name can't end with '.lock'";

        // Per-component rules: each slash-separated piece is its own ref path element.
        foreach (var part in n.Split('/'))
        {
            if (part.StartsWith('.')) return "No part of a branch name can start with '.'";
            if (part.EndsWith(".lock", StringComparison.Ordinal)) return "No part of a branch name can end with '.lock'";
        }

        return null;
    }

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
        // The segments *between* the prefix and the leaf — never the leaf itself. A
        // two-part name (feature/login) has none: its second segment is the title, and
        // a subtitle restating the line above it is a row saying one thing twice.
        var sub = parts.Length >= 4 ? $"{parts[1]}  ·  {parts[2]}"
                : parts.Length == 3 ? parts[1]
                : "";

        return new BranchName(label, bg, fg, title, sub, name);
    }
}
