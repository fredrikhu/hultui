using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace GitClient.Theme;

/// <summary>
/// App-specific color palette for the Git client.
/// </summary>
public static class GitClientColors
{
    private static GitClientPalette _active = GitClientPalette.Dark;

    public static void Use(GitClient.ThemeMode mode)
    {
        _active = mode == GitClient.ThemeMode.Light
            ? GitClientPalette.Light
            : GitClientPalette.Dark;
    }

    public static HultUI.Core.Styling.Theme ToHultTheme() => new()
    {
        Background = Background,
        TextColor = TextPrimary,
        Surface = Surface,
        Primary = ButtonPrimary,
        Secondary = TextSecondary,
        ContextMenu = new ContextMenuTheme
        {
            Background = Surface,
            BorderColor = BorderLight,
            TextColor = TextPrimary,
            DisabledTextColor = TextMuted,
            HoverBackground = SurfaceHover,
            SeparatorColor = Border,
            ShortcutTextColor = TextSecondary,
        },
    };

    // Surface layers, ordered by depth.
    public static Color TitleBar => _active.TitleBar;
    public static Color Sidebar => _active.Sidebar;
    public static Color Background => _active.Background;
    public static Color Surface => _active.Surface;
    public static Color SurfaceHover => _active.SurfaceHover;
    public static Color SurfaceActive => _active.SurfaceActive;
    public static Color Panel => _active.Panel;
    public static Color StatusBar => _active.StatusBar;

    // Borders.
    public static Color Border => _active.Border;
    public static Color BorderLight => _active.BorderLight;

    // Text.
    public static Color TextPrimary => _active.TextPrimary;
    public static Color TextSecondary => _active.TextSecondary;
    public static Color TextMuted => _active.TextMuted;

    // Diff colors.
    public static Color DiffAddBg => _active.DiffAddBg;
    public static Color DiffAddText => _active.DiffAddText;
    public static Color DiffDelBg => _active.DiffDelBg;
    public static Color DiffDelText => _active.DiffDelText;
    public static Color DiffHunkBg => _active.DiffHunkBg;
    public static Color DiffHunkText => _active.DiffHunkText;
    public static Color DiffLineNumber => _active.DiffLineNumber;
    public static Color DiffEmptyBg => _active.DiffEmptyBg;

    // Accents.
    public static Color BranchLocal => _active.BranchLocal;
    public static Color BranchRemote => _active.BranchRemote;
    public static Color BranchCurrent => _active.BranchCurrent;
    public static Color TagColor => _active.TagColor;
    public static Color StashColor => _active.StashColor;

    // Status badges.
    public static Color StatusAdded => _active.StatusAdded;
    public static Color StatusModified => _active.StatusModified;
    public static Color StatusDeleted => _active.StatusDeleted;
    public static Color StatusRenamed => _active.StatusRenamed;
    public static Color StatusUntracked => _active.StatusUntracked;
    public static Color StatusConflicted => _active.StatusConflicted;

    // Selection.
    public static Color Selection => _active.Selection;
    public static Color SelectionBorder => _active.SelectionBorder;

    // Buttons.
    public static Color ButtonPrimary => _active.ButtonPrimary;
    public static Color ButtonPrimaryHover => _active.ButtonPrimaryHover;
    public static Color ButtonPrimaryText => _active.ButtonPrimaryText;
    public static Color ButtonSecondary => _active.ButtonSecondary;
    public static Color ButtonSecondaryHover => _active.ButtonSecondaryHover;

    // Dialogs.
    public static Color DialogBackdrop => _active.DialogBackdrop;
    public static Color DialogBackground => _active.DialogBackground;
    public static Color DialogBorder => _active.DialogBorder;
}

public sealed record GitClientPalette(
    Color TitleBar,
    Color Sidebar,
    Color Background,
    Color Surface,
    Color SurfaceHover,
    Color SurfaceActive,
    Color Panel,
    Color StatusBar,
    Color Border,
    Color BorderLight,
    Color TextPrimary,
    Color TextSecondary,
    Color TextMuted,
    Color DiffAddBg,
    Color DiffAddText,
    Color DiffDelBg,
    Color DiffDelText,
    Color DiffHunkBg,
    Color DiffHunkText,
    Color DiffLineNumber,
    Color DiffEmptyBg,
    Color BranchLocal,
    Color BranchRemote,
    Color BranchCurrent,
    Color TagColor,
    Color StashColor,
    Color StatusAdded,
    Color StatusModified,
    Color StatusDeleted,
    Color StatusRenamed,
    Color StatusUntracked,
    Color StatusConflicted,
    Color Selection,
    Color SelectionBorder,
    Color ButtonPrimary,
    Color ButtonPrimaryHover,
    Color ButtonPrimaryText,
    Color ButtonSecondary,
    Color ButtonSecondaryHover,
    Color DialogBackdrop,
    Color DialogBackground,
    Color DialogBorder)
{
    public static GitClientPalette Dark { get; } = new(
        TitleBar: Color.FromHex("#1a1a2e"),
        Sidebar: Color.FromHex("#16162a"),
        Background: Color.FromHex("#1e1e32"),
        Surface: Color.FromHex("#252540"),
        SurfaceHover: Color.FromHex("#2e2e4a"),
        SurfaceActive: Color.FromHex("#383860"),
        Panel: Color.FromHex("#1c1c30"),
        StatusBar: Color.FromHex("#141428"),
        Border: Color.FromHex("#2a2a45"),
        BorderLight: Color.FromHex("#353555"),
        TextPrimary: Color.FromHex("#e0e0f0"),
        TextSecondary: Color.FromHex("#8888aa"),
        TextMuted: Color.FromHex("#5a5a78"),
        DiffAddBg: new Color(30, 80, 50, 80),
        DiffAddText: Color.FromHex("#4ade80"),
        DiffDelBg: new Color(120, 30, 30, 80),
        DiffDelText: Color.FromHex("#f87171"),
        DiffHunkBg: new Color(60, 60, 120, 60),
        DiffHunkText: Color.FromHex("#818cf8"),
        DiffLineNumber: Color.FromHex("#4a4a68"),
        DiffEmptyBg: new Color(40, 40, 60, 60),
        BranchLocal: Color.FromHex("#60a5fa"),
        BranchRemote: Color.FromHex("#a78bfa"),
        BranchCurrent: Color.FromHex("#34d399"),
        TagColor: Color.FromHex("#fbbf24"),
        StashColor: Color.FromHex("#f472b6"),
        StatusAdded: Color.FromHex("#4ade80"),
        StatusModified: Color.FromHex("#fbbf24"),
        StatusDeleted: Color.FromHex("#f87171"),
        StatusRenamed: Color.FromHex("#60a5fa"),
        StatusUntracked: Color.FromHex("#94a3b8"),
        StatusConflicted: Color.FromHex("#ef4444"),
        Selection: new Color(99, 102, 241, 60),
        SelectionBorder: Color.FromHex("#6366f1"),
        ButtonPrimary: Color.FromHex("#6366f1"),
        ButtonPrimaryHover: Color.FromHex("#4f46e5"),
        ButtonPrimaryText: Color.FromHex("#ffffff"),
        ButtonSecondary: Color.FromHex("#374151"),
        ButtonSecondaryHover: Color.FromHex("#4b5563"),
        DialogBackdrop: new Color(0, 0, 0, 140),
        DialogBackground: Color.FromHex("#2a2a48"),
        DialogBorder: Color.FromHex("#3a3a5a"));

    public static GitClientPalette Light { get; } = new(
        TitleBar: Color.FromHex("#f3f4f6"),
        Sidebar: Color.FromHex("#f8fafc"),
        Background: Color.FromHex("#ffffff"),
        Surface: Color.FromHex("#f1f5f9"),
        SurfaceHover: Color.FromHex("#e2e8f0"),
        SurfaceActive: Color.FromHex("#dbeafe"),
        Panel: Color.FromHex("#ffffff"),
        StatusBar: Color.FromHex("#e5e7eb"),
        Border: Color.FromHex("#cbd5e1"),
        BorderLight: Color.FromHex("#94a3b8"),
        TextPrimary: Color.FromHex("#111827"),
        TextSecondary: Color.FromHex("#475569"),
        TextMuted: Color.FromHex("#94a3b8"),
        DiffAddBg: new Color(187, 247, 208, 120),
        DiffAddText: Color.FromHex("#166534"),
        DiffDelBg: new Color(254, 202, 202, 130),
        DiffDelText: Color.FromHex("#991b1b"),
        DiffHunkBg: new Color(199, 210, 254, 120),
        DiffHunkText: Color.FromHex("#3730a3"),
        DiffLineNumber: Color.FromHex("#64748b"),
        DiffEmptyBg: new Color(226, 232, 240, 130),
        BranchLocal: Color.FromHex("#2563eb"),
        BranchRemote: Color.FromHex("#7c3aed"),
        BranchCurrent: Color.FromHex("#059669"),
        TagColor: Color.FromHex("#b45309"),
        StashColor: Color.FromHex("#be185d"),
        StatusAdded: Color.FromHex("#15803d"),
        StatusModified: Color.FromHex("#b45309"),
        StatusDeleted: Color.FromHex("#dc2626"),
        StatusRenamed: Color.FromHex("#2563eb"),
        StatusUntracked: Color.FromHex("#64748b"),
        StatusConflicted: Color.FromHex("#dc2626"),
        Selection: new Color(37, 99, 235, 45),
        SelectionBorder: Color.FromHex("#2563eb"),
        ButtonPrimary: Color.FromHex("#2563eb"),
        ButtonPrimaryHover: Color.FromHex("#1d4ed8"),
        ButtonPrimaryText: Color.FromHex("#ffffff"),
        ButtonSecondary: Color.FromHex("#e2e8f0"),
        ButtonSecondaryHover: Color.FromHex("#cbd5e1"),
        DialogBackdrop: new Color(15, 23, 42, 80),
        DialogBackground: Color.FromHex("#ffffff"),
        DialogBorder: Color.FromHex("#cbd5e1"));
}
