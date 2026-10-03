using HultUI.Core.Events;

namespace HultUI.Terminal.Emulation;

/// <summary>
/// The screen as the renderer reads it: copied out of the emulator under the controller's lock,
/// drawn from with the lock released. Engine-neutral, so a second engine fills the same thing.
/// Reused frame to frame; <see cref="Resize"/> only allocates when the grid grows.
/// </summary>
internal sealed class TerminalSnapshot
{
    public int Columns { get; private set; }
    public int Rows { get; private set; }

    /// <summary>Row-major, <see cref="Columns"/> per row.</summary>
    public TerminalCell[] Cells { get; private set; } = [];

    /// <summary>The cursor's column and row on the screen, already clamped into the grid (an
    /// engine's deferred wrap leaves it one past the last column).</summary>
    public int CursorColumn { get; set; }
    public int CursorRow { get; set; }

    /// <summary>The program wants the cursor shown and the viewport is where the cursor is.</summary>
    public bool CursorVisible { get; set; }
    public CursorShape CursorShape { get; set; }
    public bool CursorBlink { get; set; }

    /// <summary>0xRRGGBB, as the program left them (OSC 10, 11, 12 change them).</summary>
    public int DefaultForeground { get; set; }
    public int DefaultBackground { get; set; }
    public int CursorColor { get; set; }

    /// <summary>Lines above the screen; the viewport's first line, in the same count (0 is the
    /// oldest line kept); whether the viewport is at the bottom.</summary>
    public int ScrollbackLines { get; set; }
    public int ViewportTop { get; set; }
    public bool AtBottom { get; set; }

    /// <summary>Lines dropped off the top of the scrollback since the terminal started.
    /// <see cref="TerminalPosition.Line"/> is counted from the first line ever, so a selection
    /// keeps pointing at its text as the scrollback scrolls.</summary>
    public long LinesDropped { get; set; }

    public TerminalModes Modes { get; set; }

    /// <summary>Bumped by every <c>Feed</c> and resize: equal generations, equal screens.</summary>
    public long Generation { get; set; }

    public void Resize(int columns, int rows)
    {
        Columns = columns;
        Rows = rows;
        int n = columns * rows;
        if (Cells.Length < n) Cells = new TerminalCell[n];
    }

    public ref TerminalCell At(int column, int row) => ref Cells[row * Columns + column];

    public ReadOnlySpan<TerminalCell> Row(int row) => Cells.AsSpan(row * Columns, Columns);
}

/// <summary>
/// One cell, colours already resolved to RGB: a palette index, a default and an RGB colour all
/// arrive as 0xRRGGBB, with inverse, invisible and bold-as-bright applied, so the renderer never
/// needs the palette.
/// </summary>
internal struct TerminalCell
{
    /// <summary>The cell's single code point; 0 for an empty cell.</summary>
    public int CodePoint;

    /// <summary>The cell's text when it is more than one code point (a combining sequence, a ZWJ
    /// emoji); null otherwise.</summary>
    public string? Cluster;

    /// <summary>1, 2 for the first cell of a wide character, 0 for the cell a wide character
    /// covers.</summary>
    public byte Width;

    public CellFlags Flags;
    public TerminalUnderline Underline;

    /// <summary>0xRRGGBB.</summary>
    public int Foreground;

    /// <summary>0xRRGGBB; meaningless when <see cref="CellFlags.DefaultBackground"/> is set,
    /// where the element's own background shows.</summary>
    public int Background;

    /// <summary>0xRRGGBB, or -1 for the text colour.</summary>
    public int UnderlineColor;

    public readonly bool IsBlank => CodePoint is 0 or 32 && Cluster is null;
}

[Flags]
internal enum CellFlags : ushort
{
    None = 0,
    Bold = 1 << 0,
    Dim = 1 << 1,
    Italic = 1 << 2,
    Strikethrough = 1 << 3,
    Overline = 1 << 4,
    Blink = 1 << 5,
    /// <summary>No background of its own: the element's background shows through.</summary>
    DefaultBackground = 1 << 6,
    /// <summary>Part of an OSC 8 hyperlink.</summary>
    Link = 1 << 7,
}

internal enum TerminalUnderline : byte { None, Single, Double, Curly, Dotted, Dashed }

internal enum CursorShape : byte { Block, Underline, Bar }

internal enum MouseTracking : byte { None, Click, Drag, Any }

/// <summary>The program's modes the element acts on.</summary>
[Flags]
internal enum TerminalModes : ushort
{
    None = 0,
    BracketedPaste = 1 << 0,
    FocusEvents = 1 << 1,
    AlternateScreen = 1 << 2,
    SynchronizedOutput = 1 << 3,
    Win32Input = 1 << 4,
    ApplicationCursorKeys = 1 << 5,
    /// <summary>The program tracks the mouse at all (any of <see cref="MouseTracking"/>).</summary>
    MouseTracking = 1 << 6,
}

/// <summary>A place in the terminal's text: a line counted from the first line the terminal ever
/// had (see <see cref="TerminalSnapshot.LinesDropped"/>) and a column.</summary>
internal readonly record struct TerminalPosition(long Line, int Column) : IComparable<TerminalPosition>
{
    public int CompareTo(TerminalPosition other) =>
        Line != other.Line ? Line.CompareTo(other.Line) : Column.CompareTo(other.Column);

    public static bool operator <(TerminalPosition a, TerminalPosition b) => a.CompareTo(b) < 0;
    public static bool operator >(TerminalPosition a, TerminalPosition b) => a.CompareTo(b) > 0;
    public static bool operator <=(TerminalPosition a, TerminalPosition b) => a.CompareTo(b) <= 0;
    public static bool operator >=(TerminalPosition a, TerminalPosition b) => a.CompareTo(b) >= 0;
}

/// <summary>A key as the element hands it over: HultUI's key event, unchanged.</summary>
internal readonly record struct TerminalKey(KeyCode Key, KeyModifiers Modifiers, bool IsRepeat = false, ushort ScanCode = 0);

internal enum TerminalMouseAction : byte { Down, Up, Move, WheelUp, WheelDown }

internal enum TerminalMouseButton : byte { None, Left, Middle, Right }

/// <summary>A pointer event in cells, 0-based from the screen's top-left.</summary>
internal readonly record struct TerminalMouse(
    TerminalMouseAction Action, TerminalMouseButton Button, int Column, int Row, KeyModifiers Modifiers);

/// <summary>What the emulator reports while it parses: raised inside <c>Feed</c>, on the reader
/// thread, under the controller's lock.</summary>
internal abstract record TerminalEvent
{
    /// <summary>Bytes the program asked for (DA, DSR, kitty replies) or a paste: to the program.</summary>
    public sealed record Reply(string Text) : TerminalEvent;
    public sealed record Title(string Text) : TerminalEvent;
    public sealed record Bell : TerminalEvent;
    public sealed record Directory(string Path) : TerminalEvent;
    public sealed record ClipboardWrite(string Text) : TerminalEvent;
    public sealed record ScreenSwitched(bool Alternate) : TerminalEvent;
}

/// <summary>
/// The emulator seam: what the controller and element need from an engine. Internal until a
/// second engine exists; its shape is checked against libghostty-vt's render-state API before it
/// is called stable (docs/design/TERMINAL.md).
/// </summary>
/// <remarks>Not thread-safe. Every member is called under the controller's lock.</remarks>
internal interface ITerminalEmulator : IDisposable
{
    int Columns { get; }
    int Rows { get; }

    void Feed(ReadOnlySpan<byte> output);
    void Resize(int columns, int rows);
    void CopySnapshot(TerminalSnapshot into);

    /// <summary>The bytes a key sends, or null for none: printable keys without Ctrl or Alt send
    /// their text through <c>TextInput</c> instead.</summary>
    string? EncodeKey(in TerminalKey key);

    /// <summary>The bytes a pointer event sends when the program tracks the mouse; null otherwise.</summary>
    string? EncodeMouse(in TerminalMouse mouse);

    /// <summary>The focus report when the program asked for them (DECSET 1004); null otherwise.</summary>
    string? EncodeFocus(bool focused);

    /// <summary>Pastes <paramref name="text"/>: bracketed or announced as the program asked, ESC
    /// stripped. The bytes arrive as a <see cref="TerminalEvent.Reply"/>.</summary>
    void Paste(string text);

    /// <summary>Lines kept above the screen. Applied to the normal screen; set while the
    /// alternate screen is up, it is applied when the program returns.</summary>
    void SetScrollback(int lines);

    /// <summary>Moves the viewport by <paramref name="lines"/> (negative is up, into the
    /// scrollback); 0 moves nothing.</summary>
    void ScrollViewport(int lines);
    void ScrollToBottom();

    /// <summary>The text from <paramref name="start"/> to <paramref name="end"/> (exclusive), wrapped
    /// rows joined, other lines ending in "\n", trailing blanks dropped.</summary>
    string GetText(TerminalPosition start, TerminalPosition end);

    /// <summary>The word around <paramref name="at"/>, for a double click.</summary>
    (TerminalPosition Start, TerminalPosition End) WordAt(TerminalPosition at);

    /// <summary>The logical line around <paramref name="at"/> (wrapped rows included), for a triple click.</summary>
    (TerminalPosition Start, TerminalPosition End) LineAt(TerminalPosition at);

    event Action<TerminalEvent>? Event;
}
