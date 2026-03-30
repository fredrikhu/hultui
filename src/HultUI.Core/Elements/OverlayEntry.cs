namespace HultUI.Core.Elements;

internal sealed class OverlayEntry
{
    public required int Id { get; init; }
    public required Element Content { get; init; }
    public float X { get; set; }
    public float Y { get; set; }
    public bool DismissOnOutsideClick { get; init; }
    public bool IsHitTestable { get; init; } = true;
    public bool StyleResolved { get; set; }
    public bool LayoutDone { get; set; }
}
