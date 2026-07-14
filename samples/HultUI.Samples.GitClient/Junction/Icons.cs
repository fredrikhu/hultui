namespace GitClient.Junction;

/// <summary>
/// Icon glyphs for the Junction UI. The design references Phosphor Icons by
/// name (<c>ph ph-&lt;name&gt;</c>), but the framework has no icon-font support
/// yet, so each Phosphor glyph is mapped to the closest Unicode symbol here and
/// rendered as text (missing glyphs resolve through the system font fallback).
///
/// <para>This is the one deliberate fidelity compromise in the redesign. To
/// restore pixel-exact icons, bundle the Phosphor font and swap these constants
/// for its private-use codepoints — every call site already goes through this
/// table, so nothing else has to change.</para>
/// </summary>
public static class Icons
{
    // Brand / structure
    public const string GitFork = "⎇";        // ⎇  app mark
    public const string GitBranch = "⎇";      // ⎇
    public const string GitRepository = "◫";  // ◫
    public const string GitCommit = "◉";      // ◉
    public const string GitDiff = "⇄";        // ⇄
    public const string GitMerge = "⋔";       // ⋔

    // Nav / sidebar
    public const string Sidebar = "☰";        // ☰
    public const string Stack = "☰";          // ☰  workspaces
    public const string Folders = "▦";        // ▦
    public const string Shapes = "◈";         // ◈
    public const string Browsers = "▤";       // ▤
    public const string Sparkle = "✦";        // ✦
    public const string SquaresFour = "▦";    // ▦  overview
    public const string Pencil = "✎";         // ✎  changes

    // Arrows / actions
    public const string ArrowUp = "↑";        // ↑
    public const string ArrowDown = "↓";      // ↓
    public const string ArrowLeft = "←";      // ←
    public const string ArrowRight = "→";     // →
    public const string ArrowsClockwise = "⟳";// ⟳  fetch
    public const string ArchiveTray = "⇩";    // ⇩  stash
    public const string Plus = "+";
    public const string MagnifyingGlass = "⌕";// ⌕
    public const string X = "✕";              // ✕
    public const string Funnel = "▽";         // ▽  include filter
    public const string CheckCircle = "✓";    // ✓
    public const string CaretUp = "▴";        // ▴
    public const string CaretDown = "▾";      // ▾

    // Ref chips
    public const string ArrowFatRight = "➤";  // ➤  HEAD
    public const string Flask = "⚗";          // ⚗  wip
    public const string Cloud = "☁";          // ☁  remote
    public const string Tag = "◇";            // ◇

    // Diff pane
    public const string ArrowsOut = "⤢";      // ⤢  fullscreen
    public const string ArrowsIn = "⤡";       // ⤡  exit fullscreen
    public const string Rows = "≡";           // ≡  unified
    public const string Columns = "▥";        // ▥  split
    public const string FileCode = "▤";       // ▤
    public const string CheckSquare = "☑";    // ☑
    public const string Square = "☐";         // ☐

    // Branches / include
    public const string Eye = "◎";            // ◎
    public const string EyeSlash = "⊘";       // ⊘
    public const string Trash = "⌫";          // ⌫

    // Window controls (Windows/Linux style)
    public const string WinMinimize = "—";    // —
    public const string WinMaximize = "▢";    // ▢
    public const string WinClose = "✕";       // ✕

    // Interactive rebase actions
    public const string Check = "✓";                  // ✓  pick
    public const string Wrench = "⚙";                 // ⚙  edit
    public const string ArrowsInLineVertical = "⇵";   // ⇵  squash (meld into previous)
    public const string ArrowLineUp = "⤒";            // ⤒  fixup
    public const string Play = "▶";                   // ▶  start rebase
    public const string DotsSixVertical = "⠿";        // ⠿  drag handle

    // Context-menu verbs
    public const string BendDownRight = "↳";  // ↳  checkout detached
    public const string Cherries = "◈";       // ◈  cherry-pick
    public const string ArrowsDownUp = "↕";   // ↕  rebase
    public const string ListChecks = "☰";     // ☰  interactive rebase
    public const string CounterClockwise = "↺";// ↺  revert
    public const string LineLeft = "⇤";       // ⇤  reset
    public const string Warning = "⚠";        // ⚠
    public const string Copy = "⧉";           // ⧉
    public const string PencilSimple = "✎";   // ✎  rename
}
