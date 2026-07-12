using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

public sealed class InheritedStyle
{
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
    TextAlign TextAlign = TextAlign.Left,
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
    Rtl
}

public enum TextAlign : byte
{
    Left,
    Center,
    Right
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
    None,
    ColResize,
    RowResize
}
