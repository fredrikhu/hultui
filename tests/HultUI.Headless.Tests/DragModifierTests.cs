using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Drag &amp; drop events carry the modifiers held at that instant. Shift-to-move is the
/// gesture every file manager offers, and it is decided <i>during</i> the drag — so the
/// modifiers have to be the live ones, not whatever was down when the press landed.
/// </summary>
public class DragModifierTests
{
    private sealed record Seen(string Phase, KeyModifiers Modifiers);

    /// <summary>A draggable source at the top and a drop target below it, both 100×40.</summary>
    private static (List<Seen> log, Element source, Element target) Build(HeadlessSession session)
    {
        var log = new List<Seen>();

        var source = Box.New().Width(100).Height(40).Draggable();
        source.OnDragStart(e => log.Add(new Seen("start", e.Modifiers)));
        source.OnDragEnd(e => log.Add(new Seen("end", e.Modifiers)));

        var target = Box.New().Width(100).Height(40);
        target.OnDragEnter(e => log.Add(new Seen("enter", e.Modifiers)));
        target.OnDragOver(e => { e.Accepted = true; log.Add(new Seen("over", e.Modifiers)); });
        target.OnDrop(e => log.Add(new Seen("drop", e.Modifiers)));

        session.Window.SetRoot(Flex.Column(gap: 0).Children(source, target));
        session.PumpFrame();
        return (log, source, target);
    }

    private static Point Mid(Element el) =>
        new(el.Layout.AbsoluteBorderBox.X + el.Layout.Width / 2,
            el.Layout.AbsoluteBorderBox.Y + el.Layout.Height / 2);

    [Fact]
    public void InternalDrag_CarriesModifiersOnEveryPhase()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var (log, source, target) = Build(session);

        session.Window.Drag(Mid(source), Mid(target), modifiers: KeyModifiers.Shift);
        session.PumpFrame();

        Assert.Contains(log, s => s.Phase == "start" && s.Modifiers == KeyModifiers.Shift);
        Assert.Contains(log, s => s.Phase == "over" && s.Modifiers == KeyModifiers.Shift);
        var drop = Assert.Single(log, s => s.Phase == "drop");
        Assert.Equal(KeyModifiers.Shift, drop.Modifiers);
        var end = Assert.Single(log, s => s.Phase == "end");
        Assert.Equal(KeyModifiers.Shift, end.Modifiers);
    }

    [Fact]
    public void InternalDrag_ModifierPressedMidDrag_ReachesTheDrop()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var (log, source, target) = Build(session);

        // Press and start moving with nothing held, then hold Ctrl for the rest — which is
        // exactly how the gesture is used: you commit to copy-vs-move after you have aimed.
        var from = Mid(source);
        var to = Mid(target);
        session.Window.MouseDown(from.X, from.Y);
        session.Window.MoveMouse(from.X, from.Y + 10f);            // crosses the 5px threshold
        session.Window.MoveMouse(to.X, to.Y, KeyModifiers.Ctrl);
        session.Window.MouseUp(to.X, to.Y, modifiers: KeyModifiers.Ctrl);
        session.PumpFrame();

        Assert.Contains(log, s => s.Phase == "start" && s.Modifiers == KeyModifiers.None);
        var drop = Assert.Single(log, s => s.Phase == "drop");
        Assert.Equal(KeyModifiers.Ctrl, drop.Modifiers);
    }

    [Fact]
    public void OsFileDrop_CarriesModifiers()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var (log, _, target) = Build(session);

        session.Window.DropFiles(Mid(target), [@"C:\tmp\a.txt"], KeyModifiers.Shift);
        session.PumpFrame();

        var drop = Assert.Single(log, s => s.Phase == "drop");
        Assert.Equal(KeyModifiers.Shift, drop.Modifiers);
    }

    [Fact]
    public void OsFileDrop_DeliversThePaths()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        List<string>? dropped = null;

        var target = Box.New().Width(100).Height(40);
        target.OnDragOver(e => e.Accepted = true);
        target.OnDrop(e => dropped = [.. e.DragData.Files]);
        session.Window.SetRoot(target);
        session.PumpFrame();

        session.Window.DropFiles(Mid(target), [@"C:\tmp\a.txt", @"C:\tmp\b.txt"]);
        session.PumpFrame();

        Assert.Equal([@"C:\tmp\a.txt", @"C:\tmp\b.txt"], dropped);
    }

    /// <summary>
    /// A DragEnter/DragOver handler that rebuilds — highlighting the folder under the cursor
    /// is exactly what a file manager does with one — used to lose the drop. MarkDirty
    /// reconciles synchronously, which detaches the element the hover recorded, and the drop
    /// was then dispatched at that orphan: it bubbled through a parentless chain and reached
    /// none of the live ancestors that had just accepted it.
    /// </summary>
    private sealed class HighlightOnHover : Core.Components.Component
    {
        private bool _hot;
        public int Drops;
        public KeyModifiers LastDropModifiers;

        public override Element Build()
        {
            var target = Box.New().Width(100).Height(40);
            if (_hot) target.Background(Core.Primitives.Color.FromHex("#336699"));
            target.OnDragEnter(_ =>
            {
                if (_hot) return;
                _hot = true;
                MarkDirty();          // synchronous reconcile — the hovered element is replaced
            });
            target.OnDragOver(e => e.Accepted = true);
            target.OnDrop(e => { Drops++; LastDropModifiers = e.Modifiers; });
            // The child the hit test actually lands on, keyed by the highlight state so the
            // rebuild swaps it for a fresh element rather than patching it in place — which
            // is what a real list does when a row re-realizes, and what detaches the node the
            // hover recorded. The drop then has to bubble from a node that no longer exists.
            var inner = Box.New().Width(100).Height(40);
            inner.Key = _hot ? "inner-hot" : "inner-cold";
            target.Children(inner);
            return Flex.Column(gap: 0).Children(target);
        }
    }

    [Fact]
    public void ADragHandlerThatRebuildsStillReceivesTheDrop()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        var component = new HighlightOnHover();
        session.Application.SetRoot(component.Mount());
        session.PumpFrame();

        session.Window.DropFiles(new Point(50, 20), [@"C:\tmp\a.txt"], KeyModifiers.Shift);
        session.PumpFrame();

        Assert.Equal(1, component.Drops);
        Assert.Equal(KeyModifiers.Shift, component.LastDropModifiers);
    }

    [Fact]
    public void UnacceptedDrag_NeverDrops()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200);
        bool dropped = false;

        var source = Box.New().Width(100).Height(40).Draggable();
        var target = Box.New().Width(100).Height(40);
        target.OnDrop(_ => dropped = true);    // no DragOver handler → never accepted
        session.Window.SetRoot(Flex.Column(gap: 0).Children(source, target));
        session.PumpFrame();

        session.Window.Drag(Mid(source), Mid(target), modifiers: KeyModifiers.Shift);
        session.PumpFrame();

        Assert.False(dropped);
    }
}
