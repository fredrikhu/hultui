namespace HultUI.Core.Elements;

[Flags]
public enum DirtyFlags : byte
{
    None     = 0,
    Layout   = 1 << 0,
    Paint    = 1 << 1,
    Children = 1 << 2,
    Style    = 1 << 3,
    All      = Layout | Paint | Children | Style,

    /// <summary>
    /// Every element under this one is marked <see cref="Style"/> too, as this one is: marking
    /// the subtree for style again has nothing to add. Set where a mark walks down, and dropped
    /// with <see cref="Style"/> or when a child that is not marked joins (Element.MarkStyleDirty).
    /// </summary>
    StyleSubtree = 1 << 4,
}
