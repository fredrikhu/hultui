namespace HultUI.Core.Elements;

internal sealed class OverlayEntry
{
    public required int Id { get; init; }
    public required Element Content { get; init; }
    public float X { get; set; }
    public float Y { get; set; }
    /// <summary>Where the overlay was asked to be, which <see cref="X"/> and <see cref="Y"/> are
    /// unless the window held it back: what a window of another size keeps on screen, so one that
    /// grows again gives the overlay its place back.</summary>
    public float WantedX { get; set; }
    /// <summary>The vertical half of <see cref="WantedX"/>.</summary>
    public float WantedY { get; set; }
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
    /// <summary>Set when the window changes size: its next frame, once the overlay is laid out at
    /// that size, keeps it on screen, as an opening does.</summary>
    public bool WindowResized { get; set; }
    /// <summary>Set when the overlay is put somewhere else; the window's next frame takes it as a
    /// move of everything in it, for the accessibility tree's bounds, and clears it.</summary>
    public bool Moved { get; set; }
    /// <summary>Set when something in the overlay changed that its part of the accessibility tree
    /// does not have yet (its children, its layout, its place); cleared when that part is built.</summary>
    public bool A11yStale { get; set; }
    /// <summary>Set when an overlay a reader sees opens: the applied accessibility tree has no part for
    /// it yet. Cleared when its part is built, on its own or with the tree.</summary>
    public bool A11yOpened { get; set; }
    /// <summary>Set when one of a set in the overlay was selected (a palette's chosen row): the
    /// window's next frame builds its part of the accessibility tree at once, as for a change of
    /// its children, and clears it.</summary>
    public bool A11ySelected { get; set; }
}
