namespace Junction;

/// <summary>
/// Phosphor icon names for the Junction UI. The design references Phosphor Icons by name
/// (<c>ph ph-&lt;name&gt;</c>) and these are those names verbatim, rendered as real vector
/// artwork by <see cref="HultUI.Icons.Phosphor"/>.
///
/// <para>These used to be the closest Unicode lookalike per glyph — the one deliberate
/// fidelity compromise in the redesign, made because the framework had no icon-font
/// support. Bundling a font was never the only way out: the icons are single-path SVGs,
/// so rasterizing their path data gives exact artwork with per-call size and colour, and
/// needs nothing from the text pipeline.</para>
///
/// <para>Nine constants name a different Phosphor icon than their C# name implies, either
/// because Phosphor spells it differently (<c>CounterClockwise</c> is
/// <c>arrow-counter-clockwise</c>) or because it has no such glyph at all; those carry a
/// note. Every name here is asserted to exist in the bundled set by IconNameTests, so a
/// typo fails the build rather than silently rendering nothing.</para>
/// </summary>
public static class Icons
{
    // Brand / structure
    public const string GitFork = "git-fork";
    public const string GitBranch = "git-branch";
    public const string GitRepository = "hard-drives";   // Phosphor has no repository glyph; storage reads distinctly from Folders
    public const string GitCommit = "git-commit";
    public const string GitDiff = "git-diff";
    public const string GitMerge = "git-merge";

    // Nav / sidebar
    public const string Sidebar = "sidebar";
    public const string Stack = "stack";
    public const string Folders = "folders";
    public const string Shapes = "shapes";
    public const string Browsers = "browsers";
    public const string Sparkle = "sparkle";
    public const string SquaresFour = "squares-four";
    public const string Pencil = "pencil";

    // Arrows / actions
    public const string ArrowUp = "arrow-up";
    public const string ArrowDown = "arrow-down";
    public const string ArrowLeft = "arrow-left";
    public const string ArrowRight = "arrow-right";
    public const string ArrowsClockwise = "arrows-clockwise";
    // Phosphor 1.x called this "archive-tray"; 2.x renamed it. The artwork is unchanged.
    public const string TrayArrowDown = "tray-arrow-down";
    public const string Plus = "plus";
    public const string Minus = "minus";
    public const string MagnifyingGlass = "magnifying-glass";
    public const string FileMagnifier = "file-magnifying-glass";
    public const string X = "x";
    public const string Funnel = "funnel";
    public const string CheckCircle = "check-circle";
    public const string CaretUp = "caret-up";
    public const string CaretDown = "caret-down";
    public const string CaretRight = "caret-right";

    // Ref chips
    public const string ArrowFatRight = "arrow-fat-right";
    public const string Flask = "flask";
    public const string Cloud = "cloud";
    public const string Handshake = "handshake";
    public const string Tag = "tag";

    // Diff pane
    public const string ArrowsOut = "arrows-out";
    public const string ArrowsIn = "arrows-in";
    public const string Rows = "rows";
    public const string Columns = "columns";
    public const string FileCode = "file-code";
    public const string FileImage = "file-image";
    // Phosphor has no binary-file glyph; the crossed file is its "nothing to show here" mark.
    public const string FileBinary = "file-x";
    public const string CheckSquare = "check-square";
    public const string Square = "square";

    // Branches / include
    public const string Eye = "eye";
    public const string EyeSlash = "eye-slash";
    public const string Trash = "trash";
    public const string Broom = "broom";

    // Worktree locks
    public const string Lock = "lock-simple";
    // Phosphor's bundled set has no open-padlock; the key is its "this opens the lock" mark.
    public const string Unlock = "key";

    // Window controls (Windows/Linux style)
    public const string WinMinimize = "minus-bold";   // bold: the regular bar is thinner than a device pixel at caption size
    public const string WinMaximize = "square-bold";
    public const string WinClose = "x-bold";

    // Interactive rebase actions
    public const string Check = "check";
    public const string Wrench = "wrench";
    public const string ArrowsInLineVertical = "arrows-in-line-vertical";
    public const string ArrowLineUp = "arrow-line-up";
    public const string Play = "play";
    public const string DotsSixVertical = "dots-six-vertical";

    // Repo / workspace management
    public const string FolderPlus = "folder-plus";
    public const string DownloadSimple = "download-simple";
    public const string StackPlus = "stack-plus";
    public const string Swatches = "swatches";
    public const string PlusCircle = "plus-circle";
    public const string MinusCircle = "minus-circle";
    public const string FolderOpen = "folder-open";
    public const string CheckBold = "check-fat";

    // Workspace icon picker glyphs (Phosphor stand-ins; keyed by name)
    public const string RocketLaunch = "rocket-launch";
    public const string Package = "package";
    public const string Cube = "cube";
    public const string Database = "database";
    public const string TreeStructure = "tree-structure";
    public const string Lightning = "lightning";
    public const string Palette = "palette";
    public const string Bug = "bug";

    /// <summary>The 16 workspace icon keys (order = picker order), as stored in
    /// <c>WorkspaceData.Icon</c>. Map a key to its glyph via <c>WorkspaceGlyph</c>.</summary>
    public static readonly string[] WorkspaceKeys =
    [
        "stack", "browsers", "shapes", "rocket-launch", "package", "cube",
        "database", "cloud", "flask", "tree-structure", "git-fork", "lightning",
        "palette", "wrench", "bug", "squares-four",
    ];

    // Context-menu verbs
    public const string FileArrowDown = "file-arrow-down";
    public const string BendDownRight = "arrow-bend-down-right";
    public const string Cherries = "cherries";
    public const string ArrowsDownUp = "arrows-down-up";
    public const string ListChecks = "list-checks";
    public const string CounterClockwise = "arrow-counter-clockwise";
    // Phosphor has no blame glyph; a clock winding back is what the view shows — who
    // last touched each line, and when.
    public const string Blame = "clock-counter-clockwise";
    public const string LineLeft = "arrow-line-left";
    public const string Warning = "warning";
    public const string Copy = "copy";
    public const string PencilSimple = "pencil-simple";
    // Phosphor's set here carries no terminal or console glyph; a keyboard is the nearest
    // thing to "you type the rest of this yourself".
    public const string Terminal = "keyboard";

    // GitLab integration: accounts, merge requests, pipelines, review, todos, options.
    public const string GitLabLogo = "gitlab-logo";
    public const string GitPullRequest = "git-pull-request";
    public const string SealCheck = "seal-check";
    public const string ChatCircle = "chat-circle";
    public const string ChatCircleFill = "chat-circle-fill";
    public const string Bell = "bell";
    public const string Eyes = "eyes";
    public const string NotePencil = "note-pencil";
    public const string CircleNotch = "circle-notch";
    public const string XCircle = "x-circle";
    public const string Clock = "clock";
    public const string Prohibit = "prohibit";
    public const string SkipForwardCircle = "skip-forward-circle";
    public const string PlayCircle = "play-circle";
    public const string SignOut = "sign-out";
    public const string ArrowSquareOut = "arrow-square-out";
    public const string LinkSimple = "link-simple";
    public const string UserCircle = "user-circle";
    public const string Gear = "gear";
    public const string At = "at";
    public const string UserPlus = "user-plus";
    public const string SlidersHorizontal = "sliders-horizontal";
    public const string PlugsConnected = "plugs-connected";
    public const string ShieldCheck = "shield-check";
    public const string TerminalWindow = "terminal-window";
    public const string Code = "code";
    public const string Table = "table";
    public const string HardDrives = "hard-drives";
}
