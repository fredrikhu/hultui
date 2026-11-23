using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Testing;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class TextElementSelectionTests
{
    private const float CharW = 7f;
    private readonly LayoutEngine _layout = new(new StubMeasureContext(CharW, 14f));

    // Builds a laid-out label and renders it once through a stub context, which
    // populates the cached line layout the pointer hit-testing relies on.
    private TextElement CreateLabel(string text, bool selectable = true, int width = 400, IReadOnlyList<TextLink>? links = null)
    {
        var label = TextElement.New(text);
        if (links is not null) label.Links = links;
        if (selectable) label.Selectable = true;

        var container = BoxElement.New();
        container.Box.Width = width;
        container.Box.Height = 200;
        container.AddChild(label);

        StyleResolver.Resolve(container, Theme.Light.ToResolvedStyle());
        _layout.PerformLayout(container, width, 200);
        label.Render(new StubTextRenderContext(CharW));
        return label;
    }

    // A point at the left edge of character `index` (exact multiple so the
    // binary-search hit test lands on that boundary).
    private static Point CharPoint(TextElement label, int index)
    {
        var cb = label.Layout.AbsoluteContentBox;
        return new Point(cb.X + index * CharW, cb.Y + 1);
    }

    private static void Drag(TextElement label, int from, int to)
    {
        EventDispatcher.DispatchDirect(new PointerDownEvent { Position = CharPoint(label, from), Button = 0 }, label);
        EventDispatcher.DispatchDirect(new PointerMoveEvent { Position = CharPoint(label, to) }, label);
        EventDispatcher.DispatchDirect(new PointerUpEvent { Position = CharPoint(label, to) }, label);
    }

    [Fact]
    public void Selectable_TogglesFocusableAndCursor()
    {
        var label = TextElement.New("Hi");
        Assert.False(label.IsFocusable);

        label.Selectable = true;
        Assert.True(label.IsFocusable);
        Assert.Equal(CursorType.Text, label.Inherited.Cursor);

        label.Selectable = false;
        Assert.False(label.IsFocusable);
        Assert.Equal(CursorType.Default, label.Inherited.Cursor);
    }

    [Fact]
    public void NonSelectable_IgnoresPointerSelection()
    {
        var label = CreateLabel("Hello World", selectable: false);
        Drag(label, 0, 5);
        Assert.False(label.HasSelection);
        Assert.Equal("", label.SelectedText);
    }

    [Fact]
    public void Drag_SelectsRange()
    {
        var label = CreateLabel("Hello World");
        Drag(label, 0, 5);
        Assert.True(label.HasSelection);
        Assert.Equal("Hello", label.SelectedText);
    }

    [Fact]
    public void DoubleClick_SelectsWord()
    {
        var label = CreateLabel("Hello World");
        EventDispatcher.DispatchDirect(new PointerDownEvent { Position = CharPoint(label, 8), ClickCount = 2 }, label);
        Assert.Equal("World", label.SelectedText);
    }

    [Fact]
    public void TripleClick_SelectsAll()
    {
        var label = CreateLabel("Hello World");
        EventDispatcher.DispatchDirect(new PointerDownEvent { Position = CharPoint(label, 3), ClickCount = 3 }, label);
        Assert.Equal("Hello World", label.SelectedText);
    }

    [Fact]
    public void DoubleClickDrag_KeepsSelectingWholeWords()
    {
        var label = CreateLabel("Hello brave new world");
        // Second press lands in "brave", then the pointer moves into "new" without releasing.
        EventDispatcher.DispatchDirect(new PointerDownEvent { Position = CharPoint(label, 8), ClickCount = 2 }, label);
        Assert.Equal("brave", label.SelectedText);
        EventDispatcher.DispatchDirect(new PointerMoveEvent { Position = CharPoint(label, 13) }, label);
        Assert.Equal("brave new", label.SelectedText);
    }

    [Fact]
    public void DoubleClickDragBackwards_KeepsTheAnchorWordWhole()
    {
        var label = CreateLabel("Hello brave new world");
        EventDispatcher.DispatchDirect(new PointerDownEvent { Position = CharPoint(label, 13), ClickCount = 2 }, label);
        Assert.Equal("new", label.SelectedText);
        EventDispatcher.DispatchDirect(new PointerMoveEvent { Position = CharPoint(label, 2) }, label);
        Assert.Equal("Hello brave new", label.SelectedText);
    }

    [Fact]
    public void ReleasingAfterADoubleClickDrag_KeepsTheDraggedRange()
    {
        var label = CreateLabel("Hello brave new world");
        EventDispatcher.DispatchDirect(new PointerDownEvent { Position = CharPoint(label, 8), ClickCount = 2 }, label);
        EventDispatcher.DispatchDirect(new PointerMoveEvent { Position = CharPoint(label, 13) }, label);
        EventDispatcher.DispatchDirect(new PointerUpEvent { Position = CharPoint(label, 13) }, label);
        // The click the release synthesizes must not snap the selection back to one word.
        EventDispatcher.DispatchDirect(new ClickEvent { Position = CharPoint(label, 13), ClickCount = 2 }, label);
        Assert.Equal("brave new", label.SelectedText);
    }

    [Fact]
    public void ShiftClick_ExtendsSelectionFromAnchor()
    {
        var label = CreateLabel("Hello World");
        EventDispatcher.DispatchDirect(new PointerDownEvent { Position = CharPoint(label, 0), Button = 0 }, label);
        EventDispatcher.DispatchDirect(new PointerUpEvent { Position = CharPoint(label, 0) }, label);
        EventDispatcher.DispatchDirect(
            new PointerDownEvent { Position = CharPoint(label, 5), Button = 0, Modifiers = KeyModifiers.Shift }, label);
        Assert.Equal("Hello", label.SelectedText);
    }

    [Fact]
    public void CtrlA_SelectsAll_AndCtrlC_Copies()
    {
        var label = CreateLabel("Hello World");
        var clipboard = new StubClipboard();
        label.Clipboard = clipboard;

        EventDispatcher.DispatchDirect(new KeyDownEvent { Key = KeyCode.A, Modifiers = KeyModifiers.Ctrl }, label);
        Assert.Equal("Hello World", label.SelectedText);

        EventDispatcher.DispatchDirect(new KeyDownEvent { Key = KeyCode.C, Modifiers = KeyModifiers.Ctrl }, label);
        Assert.Equal("Hello World", clipboard.Text);
    }

    [Fact]
    public void Blur_ClearsSelection()
    {
        var label = CreateLabel("Hello World");
        Drag(label, 0, 5);
        Assert.True(label.HasSelection);

        EventDispatcher.DispatchDirect(new BlurEvent(), label);
        Assert.False(label.HasSelection);
    }

    [Fact]
    public void ContextMenu_CopyAndSelectAll_Work()
    {
        var label = CreateLabel("Hello World");
        var clipboard = new StubClipboard();
        label.Clipboard = clipboard;

        var items = label.ContextMenuItems!();
        var copy = items[0];
        var selectAll = items[1];

        // Nothing selected yet: Copy is disabled, Select All enabled.
        Assert.True(copy.IsDisabled);
        Assert.False(selectAll.IsDisabled);

        selectAll.Action!();
        Assert.Equal("Hello World", label.SelectedText);

        // Re-query so Copy reflects the now-present selection.
        label.ContextMenuItems!()[0].Action!();
        Assert.Equal("Hello World", clipboard.Text);
    }

    [Fact]
    public void PatchFrom_KeepsLiveSelection_AndCarriesSelectable()
    {
        var live = CreateLabel("Hello World");
        Drag(live, 0, 5);
        Assert.Equal("Hello", live.SelectedText);

        var incoming = TextElement.New("Hello World");
        incoming.Selectable = true; // freshly built: no selection of its own
        live.PatchFrom(incoming);

        Assert.True(live.Selectable);
        Assert.Equal("Hello", live.SelectedText);
    }

    /// <summary>
    /// A selectable label's handlers are shared by every label and read the label they run on from
    /// the event (plan 17 round 18): wired to each label they were six delegates, a closure and an
    /// array grown 2 → 4 → 8 slots, 880 B a label, and every row a diff or a viewer makes carries one
    /// or two. What is left is the one array that holds the six exactly (24 B a slot).
    /// </summary>
    [Fact]
    public void MakingALabelSelectable_AllocatesOneArrayOfItsSixHandlers()
    {
        const int calls = 64;
        var labels = new TextElement[Allocations.MaxRounds * calls];
        for (int i = 0; i < labels.Length; i++)
        {
            labels[i] = TextElement.New("    var x = Something(i);");
            _ = labels[i].Inherited;   // the style the cursor goes in is not the wiring's
        }
        int next = 0;

        double perLabel = Allocations.PerCall(calls, () => labels[next++].Selectable = true, goal: 200);

        Assert.True(perLabel <= 200, $"making a label selectable allocated {perLabel:0} B");
        // A label that has links first wired its click handler then: the same six slots.
        var linked = TextElement.New("see the docs");
        linked.Links = [new TextLink(8, 4, "docs")];
        linked.Selectable = true;
        Assert.Equal(6, HandlerSlots(linked));
        Assert.Equal(6, HandlerSlots(labels[0]));
    }

    private static int HandlerSlots(Element element) =>
        ((Array)typeof(Element).GetField("_handlers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(element)!).Length;

    /// <summary>The shared handlers act on the label the event is on: after a patch, on the live label,
    /// not on the build's copy that was patched from.</summary>
    [Fact]
    public void AfterAPatch_TheSharedHandlers_ActOnTheLiveLabel()
    {
        var live = CreateLabel("Hello World");
        var other = CreateLabel("Other words");
        var incoming = TextElement.New("Hello World");
        incoming.Selectable = true;
        live.PatchFrom(incoming);

        Drag(live, 0, 5);
        Assert.Equal("Hello", live.SelectedText);
        Assert.False(incoming.HasSelection);
        Assert.False(other.HasSelection);

        EventDispatcher.DispatchDirect(new PointerDownEvent { Position = CharPoint(live, 8), ClickCount = 2 }, live);
        Assert.Equal("World", live.SelectedText);

        var clipboard = new StubClipboard();
        live.Clipboard = clipboard;
        EventDispatcher.DispatchDirect(new KeyDownEvent { Key = KeyCode.C, Modifiers = KeyModifiers.Ctrl }, live);
        Assert.Equal("World", clipboard.Text);

        EventDispatcher.DispatchDirect(new BlurEvent(), live);
        Assert.False(live.HasSelection);
    }

    /// <summary>A label with links made selectable keeps one click handler: a click follows the link once.</summary>
    [Fact]
    public void ALinkedLabelMadeSelectable_FollowsALinkOnce()
    {
        var label = CreateLabel("see the docs", links: [new TextLink(8, 4, "docs")]);
        var followed = new List<string>();
        label.LinkActivated = followed.Add;

        EventDispatcher.DispatchDirect(new ClickEvent { Position = CharPoint(label, 9), ClickCount = 1 }, label);

        Assert.Equal(["docs"], followed);
    }

    [Fact]
    public void PatchFrom_ContentChange_ClampsSelection()
    {
        var live = CreateLabel("Hello World");
        EventDispatcher.DispatchDirect(new KeyDownEvent { Key = KeyCode.A, Modifiers = KeyModifiers.Ctrl }, live);
        Assert.Equal("Hello World", live.SelectedText);

        var incoming = TextElement.New("Hi");
        incoming.Selectable = true;
        live.PatchFrom(incoming);

        // Offsets were clamped to the new, shorter content — no out-of-range slice.
        Assert.Equal("Hi", live.Content);
        var _ = live.SelectedText; // must not throw
    }

    private sealed class StubClipboard : IClipboard
    {
        public string? Text { get; private set; }
        public string? GetText() => Text;
        public void SetText(string text) => Text = text;

        // Selection only ever copies text; the data formats are here to satisfy the
        // interface, not because this test exercises them.
        public bool HasFormat(string mimeType) => false;
        public byte[]? GetData(string mimeType) => null;
        public void SetData(IReadOnlyDictionary<string, byte[]> data) => throw new NotSupportedException();
        public void Clear() => Text = null;
    }

    // Minimal render context: measures text at a fixed per-character width and
    // reports the whole string as a single line, matching StubMeasureContext so
    // layout and hit-testing agree.
    private sealed class StubTextRenderContext(float charWidth) : IRenderContext
    {
        public Size MeasureText(string text, ResolvedInheritedStyle style) => new(text.Length * charWidth, 14f);

        public IReadOnlyList<TextLineBox> LayoutTextLines(string text, ResolvedInheritedStyle style, float maxWidth, int? maxLines) =>
            string.IsNullOrEmpty(text) ? [] : [new TextLineBox(0, text.Length, 0, 14f, text.Length * charWidth)];

        public void DrawRect(Rect rect, Color fill) { }
        public void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill, Color stroke, Thickness strokeWidth) { }
        public void DrawText(string text, Rect bounds, ResolvedInheritedStyle style, TextOverflow overflow, int? maxLines) { }
        public void DrawImage(IImageSource source, Rect destRect, ImageFit fit) { }
        public void DrawLine(Point from, Point to, Color color, float strokeWidth) { }
        public void PushClip(Rect clipRect) { }
        public void PushRoundedClip(Rect clipRect, CornerRadius radius) { }
        public void PushTranslate(float x, float y) { }
        public void PushOpacity(float opacity) { }
        public void Pop() { }
        public ICanvasDrawingContext CreateCanvasContext(Rect bounds) => null!;
        public void BeginFrame(float width, float height, float scaleFactor, Color? clearColor = null) { }
        public void EndFrame() { }
    }
}
