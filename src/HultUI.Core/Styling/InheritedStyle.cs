using HultUI.Core.Primitives;

namespace HultUI.Core.Styling;

public sealed class InheritedStyle
{
    public string? FontFamily { get; set; }
    public float? FontSize { get; set; }
    public FontWeight? FontWeight { get; set; }
    public Color? TextColor { get; set; }
    public TextDirection? TextDirection { get; set; }
    public CursorType? Cursor { get; set; }
}

public readonly record struct ResolvedInheritedStyle(
    string FontFamily,
    float FontSize,
    FontWeight FontWeight,
    Color TextColor,
    TextDirection TextDirection,
    CursorType Cursor);

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
    None
}
