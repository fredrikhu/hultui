namespace HultUI.Core.Elements;

internal sealed class OverlayEntry
{
    public required int Id { get; init; }
    public required Element Content { get; init; }
    public float X { get; set; }
    public float Y { get; set; }
    public bool DismissOnOutsideClick { get; init; }
    /// <summary>Runs when the host dismisses the overlay on an outside click — the one
    /// close the owner did not initiate, so the one it has to be told about. An explicit
    /// <c>CloseOverlay</c> does not raise it; the caller already knows.</summary>
    public Action? OnDismissed { get; init; }
    public bool IsHitTestable { get; init; } = true;
    /// <summary>Holds the keyboard while open — see <c>IOverlayHost.ShowOverlay</c>.</summary>
    public bool IsModal { get; init; }
    /// <summary>What had the focus when the overlay opened: where it goes back to when the
    /// overlay closes holding it.</summary>
    public Element? FocusBefore { get; init; }
    /// <summary>The element an anchored overlay belongs to: a press on it while the overlay is
    /// open closes the overlay and goes no further.</summary>
    public Element? Anchor { get; init; }
    /// <summary>Where an anchored overlay goes, from its laid-out size and the window's; asked
    /// again every frame, so the overlay follows its anchor.</summary>
    public Func<Primitives.Size, Primitives.Size, Primitives.Point>? Place { get; init; }
    public bool StyleResolved { get; set; }
    public bool LayoutDone { get; set; }
}
