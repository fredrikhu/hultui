namespace HultUI.Core.Elements;

[Flags]
public enum DirtyFlags : byte
{
    None     = 0,
    Layout   = 1 << 0,
    Paint    = 1 << 1,
    Children = 1 << 2,
    Style    = 1 << 3,
    All      = Layout | Paint | Children | Style
}
