using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A custom-drawn document offers its text to a screen reader through
/// <see cref="CanvasElement.TextSource"/>, by composition: a canvas with the Document role reads as
/// the terminal does, its text, caret and selection, and tells a change when the app says so or
/// when a rebuild hands it another source.
/// </summary>
public class CanvasTextSourceTests
{
    /// <summary>The app's snapshot of what it shows: replaced whole, read on a bridge's thread.</summary>
    private sealed class Visible : IAccessibleTextSource
    {
        public volatile AccessibleText? Current;
        public AccessibleText? AccessibleText => Current;
    }

    private sealed class Viewer : Component
    {
        public IAccessibleTextSource Source = new Visible();
        public CanvasElement? Drawn;

        public void Rebuild() => MarkDirty();

        public override Element Build()
        {
            var canvas = HultUI.Core.Builders.Canvas.New(_ => { }).Width(300f).Height(100f)
                .AccessibleRole(AccessibilityRole.Document).AccessibleName("viewer");
            canvas.TextSource = Source;
            Drawn = canvas;
            return Box.New().Padding(10f).AlignItems(AlignItems.FlexStart).Children(canvas);
        }
    }

    private static (HeadlessSession Session, HeadlessAccessibilityBridge Bridge, Viewer Viewer, ComponentHost Host) Mount(Visible source)
    {
        var session = HeadlessSession.Create(400, 200);
        var bridge = session.Window.InstallAccessibilityBridge();
        var viewer = new Viewer { Source = source };
        var host = viewer.Mount();
        session.Window.SetRoot(host);
        session.PumpFrames(2);
        return (session, bridge, viewer, host);
    }

    private static CanvasElement CanvasOf(ComponentHost host) => (CanvasElement)host.Children[0].Children[0];

    private static AccessibilityNode NodeOf(HeadlessAccessibilityBridge bridge, Element element) =>
        Find(bridge.CurrentTree!, element.Id) ?? throw new InvalidOperationException("no node");

    private static AccessibilityNode? Find(AccessibilityNode node, ElementId id)
    {
        if (node.ElementId == id) return node;
        foreach (var child in node.Children)
            if (Find(child, id) is { } found) return found;
        return null;
    }

    private static string[] Told(HeadlessAccessibilityBridge bridge, Element element) =>
        [.. bridge.Diffs.SelectMany(d => d.PropertyChanged).Where(c => c.Node.ElementId == element.Id).Select(c => c.PropertyName)];

    [Fact]
    public void ADocumentCanvas_ExposesTheSourcesText_CaretAndSelection()
    {
        var source = new Visible { Current = new AccessibleText("line one\nline two", caret: 5, selectionStart: 5, selectionEnd: 8, isReadOnly: true) };
        var (session, bridge, _, host) = Mount(source);
        using var _ = session;

        var node = NodeOf(bridge, CanvasOf(host));

        Assert.Equal(AccessibilityRole.Document, node.Role);
        Assert.Equal("line one\nline two", node.CurrentText!.Text);
        Assert.Equal(5, node.CurrentText.Caret);
        Assert.Equal((5, 8), (node.CurrentText.SelectionStart, node.CurrentText.SelectionEnd));
    }

    [Fact]
    public void NotifyTextSourceChanged_TellsATextChange_AndACaretMove()
    {
        var source = new Visible { Current = new AccessibleText("line one\nline two", caret: 0) };
        var (session, bridge, _, host) = Mount(source);
        using var _ = session;
        var canvas = CanvasOf(host);

        bridge.ClearDiffs();
        source.Current = new AccessibleText("line two\nline three", caret: 0);
        canvas.NotifyTextSourceChanged();
        session.PumpFrame();
        Assert.Contains(AccessibilityProperties.Text, Told(bridge, canvas));
        Assert.Equal("line two\nline three", NodeOf(bridge, canvas).Text!.Text);

        bridge.ClearDiffs();
        source.Current = new AccessibleText("line two\nline three", caret: 6);
        canvas.NotifyTextSourceChanged();
        session.PumpFrame();
        Assert.Equal([AccessibilityProperties.TextSelection], Told(bridge, canvas));
    }

    [Fact]
    public void ARebuildWithANewSource_TellsItWithoutACall()
    {
        var (session, bridge, viewer, host) = Mount(new Visible { Current = new AccessibleText("before") });
        using var _ = session;
        var canvas = CanvasOf(host);

        bridge.ClearDiffs();
        viewer.Source = new Visible { Current = new AccessibleText("after") };
        viewer.Rebuild();
        session.PumpFrames(2);

        Assert.Same(canvas, CanvasOf(host));                 // the live canvas was patched, not replaced
        Assert.Contains(AccessibilityProperties.Text, Told(bridge, canvas));
        Assert.Equal("after", NodeOf(bridge, canvas).Text!.Text);
    }
}
