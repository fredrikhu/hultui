namespace VesperCommander;

/// <summary>
/// Phosphor icon names, one constant per thing Vesper draws.
///
/// <para>Named rather than written inline because an unknown name renders an empty box and
/// reports nothing — <see cref="HultUI.Icons.Phosphor"/> deliberately survives a typo rather
/// than crashing a menu. <c>VesperGlyphNameTests</c> reflects over this class and fails on a
/// name that is not in the bundled set, which is what actually catches the typo.</para>
///
/// <para>Adding one: put the Phosphor name in scripts/phosphor-icons.txt, run
/// scripts/generate-phosphor-icons.ps1, then add the constant here.</para>
/// </summary>
public static class Glyphs
{
    // Window controls. Bold: at caption size a regular stroke is thinner than a device
    // pixel, and the minimize bar washes out to nothing.
    public const string WinMinimize = "minus-bold";
    public const string WinMaximize = "square-bold";
    public const string WinClose = "x-bold";

    // Navigation and direction
    public const string Back = "arrow-left";
    public const string ArrowUp = "arrow-up";
    public const string ArrowDown = "arrow-down";
    public const string ArrowRight = "arrow-right";
    public const string CaretUp = "caret-up";
    public const string CaretDown = "caret-down";
    public const string CaretRight = "caret-right";
    public const string Swap = "arrows-left-right";

    // Actions
    public const string Search = "magnifying-glass";
    public const string Check = "check";
    public const string Close = "x";
    public const string Plus = "plus";
    public const string Minus = "minus";
    public const string Copy = "copy";
    public const string Play = "play";

    // Status
    public const string Warning = "warning";
    public const string Prohibit = "prohibit";
    public const string Dot = "circle-fill";

    // Archives
    public const string Key = "key";           // password prompt
    public const string Lock = "lock-simple";  // a read-only (locked) tab
    public const string Pack = "package";

    // Dialog marks
    public const string Rename = "pencil-simple";
    public const string Bookmarks = "star";
    public const string Drives = "hard-drives";
    public const string Settings = "gear";
    public const string BinaryFile = "file-x";
    public const string TextContent = "text-aa";
    public const string ResizeGrip = "dots-six";

    // Options pages
    public const string PageGeneral = "sliders-horizontal";
    public const string PagePanes = "columns";
    public const string PageKeyboard = "keyboard";
    public const string PageTools = "wrench";
    public const string PageColors = "palette";
}
