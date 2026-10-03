using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Terminal;

/// <summary>
/// How a <see cref="TerminalController"/> starts. <see cref="Scrollback"/> and
/// <see cref="MaxScrollbackBytes"/> are only the starting values: the controller's properties of
/// the same names change them while the terminal runs.
/// </summary>
public sealed class TerminalOptions
{
    /// <summary>The most scrollback lines either setting accepts. At 200 columns a million lines
    /// is about 4.6 GiB, and a width change over it takes seconds.</summary>
    public const int MaxScrollback = 1_000_000;

    /// <summary>The default scrollback: about 24 MiB at 200 columns, 10 MiB at 80.</summary>
    public const int DefaultScrollback = 5000;

    private readonly int _scrollback = DefaultScrollback;
    private readonly long? _maxScrollbackBytes;
    private readonly float _fontSize = 13f;

    /// <summary>Lines kept above the screen. 0 turns scrollback off; values above
    /// <see cref="MaxScrollback"/> are clamped to it; negative values throw.</summary>
    public int Scrollback
    {
        get => _scrollback;
        init => _scrollback = ValidateScrollback(value);
    }

    /// <summary>
    /// A memory budget for the scrollback, in bytes; none by default. When set, the lines kept are
    /// the smaller of <see cref="Scrollback"/> and what the budget holds at the current width
    /// (a line costs about 25 bytes per column, whatever it holds), recomputed at every width change.
    /// </summary>
    public long? MaxScrollbackBytes
    {
        get => _maxScrollbackBytes;
        init => _maxScrollbackBytes = ValidateBudget(value);
    }

    /// <summary>
    /// The font family, or a list of them (<c>"JetBrains Mono, Fira Code"</c>) of which the first
    /// installed is used. Null takes the platform's monospace face
    /// (<see cref="FontFamilies.Monospace"/>: Cascadia Mono, then Consolas, on Windows; Menlo on
    /// macOS; fontconfig's <c>monospace</c> on Linux), and so does a family that is not
    /// installed: the terminal always ends the list with it.
    /// </summary>
    public string? FontFamily { get; init; }

    /// <summary>The font size, in layout units. Must be positive.</summary>
    public float FontSize
    {
        get => _fontSize;
        init => _fontSize = value > 0 && float.IsFinite(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(FontSize), value, "The font size must be positive.");
    }

    /// <summary>The colours. Programs can change any of them while running (OSC 4, 10, 11, 12).</summary>
    public TerminalTheme Theme { get; init; } = TerminalTheme.Dark;

    /// <summary>What the terminal calls itself to programs that ask (XTGETTCAP <c>TN</c>).
    /// A connection sets <c>TERM</c> in the program's environment itself.</summary>
    public string TermName { get; init; } = "xterm-256color";

    internal static int ValidateScrollback(int lines) => lines switch
    {
        < 0 => throw new ArgumentOutOfRangeException(nameof(Scrollback), lines, "Scrollback cannot be negative."),
        > MaxScrollback => MaxScrollback,
        _ => lines,
    };

    internal static long? ValidateBudget(long? bytes) => bytes is < 0
        ? throw new ArgumentOutOfRangeException(nameof(MaxScrollbackBytes), bytes, "The scrollback budget cannot be negative.")
        : bytes;

    /// <summary>The family string the terminal draws with: a missing family would otherwise draw
    /// in the platform's default face, which is proportional.</summary>
    internal string ResolvedFontFamily => string.IsNullOrWhiteSpace(FontFamily)
        ? FontFamilies.Monospace
        : $"{FontFamily}, {FontFamilies.Monospace}";
}

/// <summary>
/// A terminal's colours: the default foreground and background, the cursor, the selection, and
/// the sixteen ANSI colours. The other 240 of the 256-colour palette are the standard cube and
/// grey ramp, as in every xterm.
/// </summary>
public sealed record TerminalTheme(
    Color Foreground,
    Color Background,
    Color Cursor,
    Color Selection,
    IReadOnlyList<Color> Ansi)
{
    /// <summary>The tint behind a search hit.</summary>
    public Color SearchHit { get; init; } = new(255, 191, 0, 61);

    /// <summary>The tint behind the current search hit.</summary>
    public Color SearchCurrent { get; init; } = new(255, 191, 0, 158);

    /// <summary>Windows Terminal's Campbell scheme: what most Windows users' consoles look like.</summary>
    public static TerminalTheme Dark { get; } = new(
        Foreground: Color.FromHex("#CCCCCC"),
        Background: Color.FromHex("#0C0C0C"),
        Cursor: Color.FromHex("#FFFFFF"),
        Selection: new Color(255, 255, 255, 64),
        Ansi:
        [
            Color.FromHex("#0C0C0C"), Color.FromHex("#C50F1F"), Color.FromHex("#13A10E"), Color.FromHex("#C19C00"),
            Color.FromHex("#0037DA"), Color.FromHex("#881798"), Color.FromHex("#3A96DD"), Color.FromHex("#CCCCCC"),
            Color.FromHex("#767676"), Color.FromHex("#E74856"), Color.FromHex("#16C60C"), Color.FromHex("#F9F1A5"),
            Color.FromHex("#3B78FF"), Color.FromHex("#B4009E"), Color.FromHex("#61D6D6"), Color.FromHex("#F2F2F2"),
        ]);

    /// <summary>A light scheme (Windows Terminal's One Half Light).</summary>
    public static TerminalTheme Light { get; } = new(
        Foreground: Color.FromHex("#383A42"),
        Background: Color.FromHex("#FAFAFA"),
        Cursor: Color.FromHex("#4F525D"),
        Selection: new Color(0, 0, 0, 48),
        Ansi:
        [
            Color.FromHex("#383A42"), Color.FromHex("#E45649"), Color.FromHex("#50A14F"), Color.FromHex("#C18301"),
            Color.FromHex("#0184BC"), Color.FromHex("#A626A4"), Color.FromHex("#0997B3"), Color.FromHex("#FAFAFA"),
            Color.FromHex("#4F525D"), Color.FromHex("#DF6C75"), Color.FromHex("#98C379"), Color.FromHex("#E4C07A"),
            Color.FromHex("#61AFEF"), Color.FromHex("#C577DD"), Color.FromHex("#56B5C1"), Color.FromHex("#FFFFFF"),
        ]);
}
