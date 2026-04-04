using HultUI.Core.Primitives;

namespace GitClient.Theme;

/// <summary>
/// App-specific color palette for the Git client.
/// </summary>
public static class GitClientColors
{
    // ── Surface layers (dark, ordered by depth) ──
    public static readonly Color TitleBar = Color.FromHex("#1a1a2e");
    public static readonly Color Sidebar = Color.FromHex("#16162a");
    public static readonly Color Background = Color.FromHex("#1e1e32");
    public static readonly Color Surface = Color.FromHex("#252540");
    public static readonly Color SurfaceHover = Color.FromHex("#2e2e4a");
    public static readonly Color SurfaceActive = Color.FromHex("#383860");
    public static readonly Color Panel = Color.FromHex("#1c1c30");
    public static readonly Color StatusBar = Color.FromHex("#141428");

    // ── Borders ──
    public static readonly Color Border = Color.FromHex("#2a2a45");
    public static readonly Color BorderLight = Color.FromHex("#353555");

    // ── Text ──
    public static readonly Color TextPrimary = Color.FromHex("#e0e0f0");
    public static readonly Color TextSecondary = Color.FromHex("#8888aa");
    public static readonly Color TextMuted = Color.FromHex("#5a5a78");

    // ── Diff colors ──
    public static readonly Color DiffAddBg = new(30, 80, 50, 80);
    public static readonly Color DiffAddText = Color.FromHex("#4ade80");
    public static readonly Color DiffDelBg = new(120, 30, 30, 80);
    public static readonly Color DiffDelText = Color.FromHex("#f87171");
    public static readonly Color DiffHunkBg = new(60, 60, 120, 60);
    public static readonly Color DiffHunkText = Color.FromHex("#818cf8");
    public static readonly Color DiffLineNumber = Color.FromHex("#4a4a68");

    // ── Accents ──
    public static readonly Color BranchLocal = Color.FromHex("#60a5fa");
    public static readonly Color BranchRemote = Color.FromHex("#a78bfa");
    public static readonly Color BranchCurrent = Color.FromHex("#34d399");
    public static readonly Color TagColor = Color.FromHex("#fbbf24");
    public static readonly Color StashColor = Color.FromHex("#f472b6");

    // ── Status badges ──
    public static readonly Color StatusAdded = Color.FromHex("#4ade80");
    public static readonly Color StatusModified = Color.FromHex("#fbbf24");
    public static readonly Color StatusDeleted = Color.FromHex("#f87171");
    public static readonly Color StatusRenamed = Color.FromHex("#60a5fa");
    public static readonly Color StatusUntracked = Color.FromHex("#94a3b8");

    // ── Selection ──
    public static readonly Color Selection = new(99, 102, 241, 60);
    public static readonly Color SelectionBorder = Color.FromHex("#6366f1");

    // ── Buttons ──
    public static readonly Color ButtonPrimary = Color.FromHex("#6366f1");
    public static readonly Color ButtonPrimaryHover = Color.FromHex("#4f46e5");
    public static readonly Color ButtonSecondary = Color.FromHex("#374151");
    public static readonly Color ButtonSecondaryHover = Color.FromHex("#4b5563");
}
