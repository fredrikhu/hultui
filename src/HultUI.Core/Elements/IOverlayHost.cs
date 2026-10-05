using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

public interface IOverlayHost
{
    /// <summary>Put <paramref name="content"/> on top of the window at a point in window
    /// coordinates, above everything the normal tree draws.</summary>
    /// <param name="content">What to show. Laid out at its own measured size.</param>
    /// <param name="x">Left edge, in window coordinates.</param>
    /// <param name="y">Top edge, in window coordinates.</param>
    /// <param name="dismissOnOutsideClick">Whether a click outside the overlay closes it.
    /// False for an overlay its owner closes on its own terms.</param>
    /// <param name="hitTestable">Whether the overlay takes pointer input at all. False makes
    /// it decorative — a drag preview or a tooltip that must not swallow the click under it.</param>
    /// <param name="onDismissed">Runs when the host dismisses the overlay on an outside
    /// click. An explicit <see cref="CloseOverlay"/> does not raise it — the callback
    /// exists so an owner tracking open state is told about the one close it did not
    /// initiate.</param>
    /// <param name="modal">Whether the overlay holds the keyboard while it is open: Tab and
    /// Shift+Tab stay inside it, and keys go to it even when the focus is somewhere it does not
    /// reach — so nothing behind a dialog acts on a key pressed while the dialog is up.</param>
    OverlayHandle ShowOverlay(Element content, float x, float y, bool dismissOnOutsideClick = true, bool hitTestable = true, Action? onDismissed = null, bool modal = false);
    /// <summary>
    /// Puts <paramref name="content"/> on top of the window where <paramref name="place"/> says
    /// — given the content's laid-out size and the window's, it returns the top-left corner in
    /// window coordinates — and asks again every frame, so the overlay follows
    /// <paramref name="anchor"/> when it scrolls or moves. A press on the anchor while the
    /// overlay is open closes it (raising <paramref name="onDismissed"/>) and goes no further, so
    /// the control that opened it does not open it again. A press in an overlay below this one
    /// counts as outside it.
    /// </summary>
    OverlayHandle ShowAnchoredOverlay(Element content, Element anchor, Func<Primitives.Size, Primitives.Size, Primitives.Point> place,
        bool dismissOnOutsideClick = true, Action? onDismissed = null, bool modal = false)
    {
        var box = anchor.WindowBorderBox;
        return ShowOverlay(content, box.X, box.Bottom, dismissOnOutsideClick, hitTestable: true, onDismissed, modal);
    }

    void CloseOverlay(OverlayHandle handle);
    void UpdateOverlayPosition(OverlayHandle handle, float x, float y);
    Theme? ActiveTheme { get; }

    /// <summary>Runs <paramref name="callback"/> once, after the next frame has been laid out and
    /// presented — for work that needs where things ended up, which a build cannot know yet. A
    /// host without frames runs it at once.</summary>
    void RequestPostFrameCallback(Action callback) => callback();

    /// <summary>Whether <paramref name="element"/> is part of this host's live tree. A
    /// <see cref="Reactivity.Ref{T}"/> goes on pointing at an element after a rebuild unmounts it,
    /// so a non-null ref is no proof the element is on screen.</summary>
    bool IsInTree(Element element) => element.IsHosted;

    /// <summary>
    /// Opens a menu of <paramref name="items"/> at <paramref name="position"/> (window
    /// coordinates), the way a context menu opens — for a control that owns menus of its own,
    /// such as a menu bar. Opening one closes any menu already open. A host without menus
    /// closes it at once, as <see cref="MenuCloseReason.Dismissed"/>.
    /// </summary>
    void OpenMenu(IReadOnlyList<ContextMenuItem> items, Primitives.Point position, MenuRequest? request = null) =>
        request?.Closed?.Invoke(MenuCloseReason.Dismissed);

    /// <summary>Closes the menu <see cref="OpenMenu"/> opened, if it is still open.</summary>
    void CloseMenu() { }
}
