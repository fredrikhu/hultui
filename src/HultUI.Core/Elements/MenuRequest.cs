namespace HultUI.Core.Elements;

/// <summary>Why a menu opened with <see cref="IOverlayHost.OpenMenu"/> closed.</summary>
public enum MenuCloseReason
{
    /// <summary>An item was chosen; its action has run.</summary>
    Chosen,
    /// <summary>Escape — a step back, not a dismissal: a menu bar returns to its titles.</summary>
    Escape,
    /// <summary>A click outside, or the owner closed it.</summary>
    Dismissed,
    /// <summary>Another menu opened in its place.</summary>
    Replaced,
}

/// <summary>
/// What an owner of menus — a menu bar — asks of <see cref="IOverlayHost.OpenMenu"/> beyond the
/// items: to be told when the menu closes and why, and to be handed ← and → when the menu has no
/// use for them, so the keys can move to the neighbouring menu.
/// </summary>
public sealed class MenuRequest
{
    /// <summary>Runs once when the menu closes, after a chosen item's action.</summary>
    public Action<MenuCloseReason>? Closed { get; init; }

    /// <summary>← with no submenu to close (-1), → with no submenu to open (+1).</summary>
    public Action<int>? Navigate { get; init; }

    /// <summary>Starts with the first item highlighted — a menu opened from the keyboard.</summary>
    public bool HighlightFirst { get; init; }
}
