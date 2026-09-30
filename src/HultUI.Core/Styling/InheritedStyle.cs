using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

public sealed class InheritedStyle
{
    /// <summary>Shared all-defaults instance for elements that never set inherited
    /// text properties. Read-only by contract — never mutate.</summary>
    internal static readonly InheritedStyle Default = new();

    public string? FontFamily { get; set; }
    public float? FontSize { get; set; }
    public FontWeight? FontWeight { get; set; }
    public FontSlant? FontSlant { get; set; }
    public Color? TextColor { get; set; }
    public TextDirection? TextDirection { get; set; }
    public TextAlign? TextAlign { get; set; }
    public TextDecoration? Decoration { get; set; }
    /// <summary>Absolute line box height in pixels; 0 or null means use font metrics.</summary>
    public float? LineHeight { get; set; }
    /// <summary>Extra spacing in pixels inserted after each glyph.</summary>
    public float? LetterSpacing { get; set; }
    public CursorType? Cursor { get; set; }

    internal void CopyFrom(InheritedStyle source)
    {
        FontFamily = source.FontFamily;
        FontSize = source.FontSize;
        FontWeight = source.FontWeight;
        FontSlant = source.FontSlant;
        TextColor = source.TextColor;
        TextDirection = source.TextDirection;
        TextAlign = source.TextAlign;
        Decoration = source.Decoration;
        LineHeight = source.LineHeight;
        LetterSpacing = source.LetterSpacing;
        Cursor = source.Cursor;
    }
}

// New members are trailing with defaults so existing 6-argument construction
// (DevTools HUD, tests) keeps compiling unchanged.
public readonly record struct ResolvedInheritedStyle(
    string FontFamily,
    float FontSize,
    FontWeight FontWeight,
    Color TextColor,
    TextDirection TextDirection,
    CursorType Cursor,
    FontSlant FontSlant = FontSlant.Normal,
    TextAlign TextAlign = TextAlign.Start,
    TextDecoration Decoration = TextDecoration.None,
    float LineHeight = 0f,
    float LetterSpacing = 0f);

public enum FontWeight : ushort
{
    Thin = 100,
    ExtraLight = 200,
    Light = 300,
    Regular = 400,
    Medium = 500,
    SemiBold = 600,
    Bold = 700,
    ExtraBold = 800,
    Black = 900
}

public enum FontSlant : byte
{
    Normal,
    Italic,
    Oblique
}

public enum TextDirection : byte
{
    Ltr,
    Rtl,

    /// <summary>
    /// Take the paragraph's direction from its first strong character (UAX #9
    /// rules P2/P3) — what a chat message or a commit subject wants, where the
    /// content decides and the UI cannot know in advance. Text with no strong
    /// character at all reads left to right.
    /// </summary>
    Auto
}

public enum TextAlign : byte
{
    /// <summary>
    /// Align to where reading starts: left in a left-to-right paragraph, right in
    /// a right-to-left one. The default, and the only value that follows the text.
    /// </summary>
    Start,
    Left,
    Center,
    Right,

    /// <summary>Align to where reading ends — right in Ltr, left in Rtl.</summary>
    End
}

public static class TextAlignment
{
    /// <summary>
    /// How much of a line's leftover width goes before it: 0 flush left, 0.5
    /// centred, 1 flush right. <see cref="TextAlign.Start"/> and
    /// <see cref="TextAlign.End"/> follow the paragraph's resolved direction —
    /// which for <see cref="TextDirection.Auto"/> is only known once the text has
    /// been analysed — while Left and Right stay absolute.
    /// </summary>
    public static float Factor(TextAlign align, bool rtlParagraph) => align switch
    {
        TextAlign.Left => 0f,
        TextAlign.Center => 0.5f,
        TextAlign.Right => 1f,
        TextAlign.End => rtlParagraph ? 0f : 1f,
        _ => rtlParagraph ? 1f : 0f,
    };
}

[Flags]
public enum TextDecoration : byte
{
    None = 0,
    Underline = 1,
    LineThrough = 2
}

public enum CursorType : byte
{
    Default,
    Pointer,
    Text,
    Crosshair,
    Move,
    ResizeNS,
    ResizeEW,
    ResizeNESW,
    ResizeNWSE,
    NotAllowed,
    Wait,
    /// <summary>No cursor: hidden while the pointer is over the element.</summary>
    None,
    ColResize,
    RowResize,
    /// <summary>Busy in the background: the arrow with a spinner, still clickable.</summary>
    Progress
}
