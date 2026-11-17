using System.Runtime.CompilerServices;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;

namespace HultUI.Core.Layout;

/// <summary>
/// Which elements that listen for <see cref="LayoutChangedEvent"/> a layout pass wrote a box for,
/// and the box each was last told. Off until some element first listens, so a pass in an app that
/// never does pays one static read per box it writes.
/// </summary>
internal static class LayoutWatch
{
    /// <summary>Whether any element has listened for <see cref="LayoutChangedEvent"/>.</summary>
    internal static bool On;

    /// <summary>The engine laying out on this thread, which collects what its pass wrote. A box
    /// written outside a pass (an audit putting one back) is nobody's change.</summary>
    [ThreadStatic] internal static LayoutEngine? Pass;

    // Weak: an element that is dropped takes its entry with it.
    private static readonly ConditionalWeakTable<Element, StrongBox<Rect>> s_told = new();

    /// <summary>The element's box was written: noted for the pass's engine when it listens.</summary>
    internal static void Wrote(Element element)
    {
        if (Pass is { } engine && element.HasEventHandler<LayoutChangedEvent>())
            engine.NoteLayoutChange(element);
    }

    /// <summary>The event for <paramref name="element"/>'s box now, or null when it was told this
    /// box already. Records the box as told.</summary>
    internal static LayoutChangedEvent? Tell(Element element)
    {
        var box = element.Layout.AbsoluteBorderBox;
        if (s_told.TryGetValue(element, out var told))
        {
            if (told.Value == box) return null;
            var previous = told.Value;
            told.Value = box;
            return new LayoutChangedEvent { Box = box, Previous = previous };
        }
        s_told.AddOrUpdate(element, new StrongBox<Rect>(box));
        return new LayoutChangedEvent { Box = box };
    }

    /// <summary>Whether <paramref name="element"/>'s box now is one it was not told.</summary>
    internal static bool IsUntold(Element element)
        => !s_told.TryGetValue(element, out var told) || told.Value != element.Layout.AbsoluteBorderBox;

    /// <summary>The element starts listening, or left its tree: the next box it is laid out at is
    /// told as its first.</summary>
    internal static void Forget(Element element) => s_told.Remove(element);
}
