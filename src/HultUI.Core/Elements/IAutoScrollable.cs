namespace HultUI.Core.Elements;

/// <summary>
/// A surface the middle-button autoscroll gesture can drive.
///
/// <para>The gesture was written against <see cref="ScrollViewElement"/>, which is what
/// carries a list. That is not what carries a wall of text: <see cref="TextAreaElement"/>
/// scrolls itself — its own offset, its own scrollbar, its own wheel handler — because it
/// scrolls in wrapped rows it computes rather than in child elements. Nothing about that is
/// visible to a reader, who sees a tall scrollable thing and holds the middle button on it.
/// So the window looks for this rather than for a class, and both answer to it.</para>
///
/// <para>Implemented by <see cref="ScrollViewElement"/> and <see cref="TextAreaElement"/>;
/// implement it on anything else that scrolls content it owns.</para>
/// </summary>
public interface IAutoScrollable
{
    /// <summary>Whether this surface takes part in the gesture at all. False where the
    /// middle button already means something here.</summary>
    bool MiddleClickAutoScroll { get; }

    /// <summary>Whether there is anywhere left to scroll. False means a middle press is
    /// just a middle press, and falls through to the app untouched.</summary>
    bool CanAutoScroll { get; }

    /// <summary>The vertical offset, so a gesture can carry its position across a rebuild
    /// that replaced the element under it.</summary>
    float ScrollOffsetY { get; set; }

    /// <summary>Moves the view by a scroll delta — positive is further down and further
    /// right through the content. Returns true when something actually moved.</summary>
    bool ScrollBy(float dx, float dy);
}
