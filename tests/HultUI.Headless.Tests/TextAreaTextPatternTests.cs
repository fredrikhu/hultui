// The AT-SPI bridge is Linux's, but with its signals recorded instead of sent it runs anywhere:
// nothing here reaches a bus.
#pragma warning disable CA1416
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.RichText;
using HultUI.Platform.Accessibility.Linux;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A text area offers its text to a reader as a text pattern (plan 17, round 4, the rule the user
/// approved in round 3): its rows as the lines, its caret and selection, where each character is.
/// A keystroke is told as a text change, never as the whole text in a value change: a 2,000-line
/// area raised a 121,744-character UIA ValueValue and an AT-SPI accessible-value of the same length
/// on every keystroke, and a caret move told a reader nothing. The value is still read on request.
/// </summary>
public sealed class TextAreaTextPatternTests : IDisposable
{
    // The area tells a reader its caret only while one listens, as the rich text editor does.
    public TextAreaTextPatternTests() => AccessibilityClients.Force = true;

    public void Dispose() => AccessibilityClients.Force = null;

    private sealed class Active(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) => inner.ApplyPatchedProperties(patch, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
    }

    private const string Long = "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen";

    /// <summary>Texts a reader moves through: wrapped lines, empty lines and a trailing break,
    /// surrogate pairs and joined emoji, Han, right to left.</summary>
    public static TheoryData<string> Corpus => new()
    {
        "plain",
        Long + "\nshort\n" + Long + " and more at its end",
        "a\n\nb\n",
        "emoji 😀 here 👍🏽 and 👨‍👩‍👧 done\nsecond line",
        string.Concat(Enumerable.Repeat("東京は日本の首都です。", 6)),
        "Hebrew שלום עולם mixed with English words that wrap onto another row",
        "",
    };

    private static (TextAreaElement Area, TextElement Label) Mount(HeadlessSession session, string text,
        float width = 180, float height = 160)
    {
        var area = TextArea.New(text).Width(width).Height(height);
        var label = TextB.New("status");
        session.Window.SetRoot(Flex.Column(gap: 8).Padding(8).Children(label, area));
        session.PumpFrames(2);
        session.Window.Click(area);
        session.PumpFrame();
        area.CursorPosition = 0;
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        session.PumpFrame(TimeSpan.FromMilliseconds(120));
        return (area, label);
    }

    private static AccessibilityNode NodeOf(AccessibilityNode tree, Element element) =>
        Find(tree, element.Id) ?? throw new InvalidOperationException("no node for the element");

    private static AccessibilityNode? Find(AccessibilityNode node, ElementId id)
    {
        if (node.ElementId == id) return node;
        foreach (var child in node.Children)
            if (Find(child, id) is { } found) return found;
        return null;
    }

    private static AccessibleText Read(Element area) =>
        Task.Run(() => area.AccessibleTextSource!.AccessibleText!).Result;

    private static List<int> Rows(TextAreaElement area) =>
        area.GetLineStarts(area.Layout.AbsoluteContentBox.Width, area.ResolvedInherited);

    [Fact]
    public void AKeystroke_IsATextChange_NotAValueChange_AndTheValueIsStillThere()
    {
        using var session = HeadlessSession.Create(600, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var (area, _) = Mount(session, "hello\nworld");
        bridge.ClearDiffs();

        session.Window.TypeText("x");
        session.PumpFrame();

        Assert.Equal([AccessibilityProperties.Text, AccessibilityProperties.TextSelection],
            bridge.Diffs.SelectMany(d => d.PropertyChanged).Where(c => c.Node.ElementId == area.Id).Select(c => c.PropertyName));
        var node = NodeOf(bridge.CurrentTree!, area);
        Assert.Equal("xhello\nworld", node.Value);
        Assert.Equal("xhello\nworld", node.Text!.Text);
        Assert.Equal(1, node.Text.Caret);
    }

    [Fact]
    public void ACaretMove_BuildsNoTree_AndIsTold()
    {
        using var session = HeadlessSession.Create(600, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var (area, _) = Mount(session, "hello\nworld");
        int trees = session.Window.AccessibilityTreesBuilt;
        bridge.ClearDiffs();

        session.Window.PressKey(KeyCode.Right);
        session.PumpFrame();
        session.Window.PressKey(KeyCode.Right, KeyModifiers.Shift);
        session.PumpFrame();

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.Equal(2, bridge.Diffs.Count);
        Assert.All(bridge.Diffs, d => Assert.Equal((area.Id, AccessibilityProperties.TextSelection),
            (Assert.Single(d.PropertyChanged).Node.ElementId, Assert.Single(d.PropertyChanged).PropertyName)));
        var text = NodeOf(bridge.CurrentTree!, area).Text!;
        Assert.Equal((2, 1, 2), (text.Caret, text.SelectionStart, text.SelectionEnd));
    }

    // ── UIA ──

    [SupportedOSPlatform("windows")]
    private sealed class UiaRig : IDisposable
    {
        private readonly TestHwnd _hwnd = TestHwnd.Create();
        public HeadlessSession Session { get; } = HeadlessSession.Create(600, 400);
        public WindowsAccessibilityBridge Bridge { get; }
        public readonly List<UiaRaised> Raised = [];

        public UiaRig()
        {
            var window = Session.Window.Window;
            var router = new AccessibilityActionRouter(window);
            var host = new UiaHostBridge(window, router);
            window.SetUiaHostBridge(host);
            window.SetActionRouter(router);
            Bridge = new WindowsAccessibilityBridge(_hwnd.Handle, host);
            Bridge.Raised = Raised.Add;
            Session.Window.InstallAccessibilityBridge(new Active(Bridge));
        }

        public UiaFragment Wrapper(Element element)
        {
            var native = Bridge.NativeFragment(element.Id);
            Assert.NotEqual(IntPtr.Zero, native);
            try { return Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native)); }
            finally { Marshal.Release(native); }
        }

        /// <summary>What a client reads as the element's value: the property, and the pattern.</summary>
        public (string? Property, string? Pattern) Value(Element element)
        {
            var wrapper = Wrapper(element);
            wrapper.GetPropertyValue(UiaInterop.UIA_ValueValuePropertyId, out var property);
            string? fromProperty = property.Vt == UiaVariant.VT_BSTR ? property.AsString() : null;
            property.Free();
            wrapper.GetPatternProvider(UiaInterop.UIA_ValuePatternId, out var pattern);
            Assert.NotEqual(IntPtr.Zero, pattern);
            try
            {
                var provider = Assert.IsType<UiaValueProvider>(NativeProvider.LogicOf(pattern));
                provider.GetValue(out var bstr);
                try { return (fromProperty, Marshal.PtrToStringBSTR(bstr)); }
                finally { Marshal.FreeBSTR(bstr); }
            }
            finally { Marshal.Release(pattern); }
        }

        public bool HasTextPattern(Element element)
        {
            Wrapper(element).GetPatternProvider(UiaInterop.UIA_TextPatternId, out var pattern);
            if (pattern == IntPtr.Zero) return false;
            Marshal.Release(pattern);
            return true;
        }

        public void Dispose()
        {
            Bridge.Dispose();
            Session.Dispose();
            _hwnd.Dispose();
        }
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void UIA_AKeystroke_RaisesTextChanged_NoValueValue_AndACaretMoveTextSelectionChanged()
    {
        using var rig = new UiaRig();
        var (area, _) = Mount(rig.Session, "hello\nworld");
        Assert.True(rig.HasTextPattern(area));
        rig.Raised.Clear();

        rig.Session.Window.TypeText("x");
        rig.Session.PumpFrame();
        Assert.Equal(
        [
            new UiaRaised(UiaRaisedKind.Event, UiaInterop.UIA_Text_TextChangedEventId, area.Id),
            new UiaRaised(UiaRaisedKind.Event, UiaInterop.UIA_Text_TextSelectionChangedEventId, area.Id),
        ], rig.Raised);
        Assert.Equal(("xhello\nworld", "xhello\nworld"), rig.Value(area));

        rig.Raised.Clear();
        rig.Session.Window.PressKey(KeyCode.Right);
        rig.Session.PumpFrame();
        Assert.Equal([new UiaRaised(UiaRaisedKind.Event, UiaInterop.UIA_Text_TextSelectionChangedEventId, area.Id)], rig.Raised);
        Assert.Equal(2, Task.Run(() => rig.Bridge.GetText(area.Id)!.Caret).Result);
    }

    // ── AT-SPI ──

    private sealed class Signals : IAtSpiSignals
    {
        public readonly List<string> Lines = [];
        private static string Short(string path) =>
            path.StartsWith(AtSpiConstants.AccessiblePathPrefix, StringComparison.Ordinal)
                ? path[AtSpiConstants.AccessiblePathPrefix.Length..] : "root";
        public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) => Lines.Add($"children-changed {Short(parentPath)}");
        public void EmitPropertyChange(string path, string atSpiProperty, string newValue) => Lines.Add($"property-change:{atSpiProperty} {Short(path)} {newValue.Length}");
        public void EmitValueChanged(string path, double value) => Lines.Add($"property-change:accessible-value {Short(path)}");
        public void EmitRoleChange(string path, uint role) => Lines.Add($"property-change:accessible-role {Short(path)}");
        public void EmitStateChanged(string path, string state, bool active) => Lines.Add($"state-changed:{state} {Short(path)} {(active ? 1 : 0)}");
        public void EmitTextChanged(string path, bool insert, int offset, string text) => Lines.Add($"text-changed:{(insert ? "insert" : "delete")} {Short(path)} {offset} {text}");
        public void EmitTextCaretMoved(string path, int offset) => Lines.Add($"text-caret-moved {Short(path)} {offset}");
        public void EmitTextSelectionChanged(string path) => Lines.Add($"text-selection-changed {Short(path)}");
        public void EmitAnnouncement(string path, string text, int politeness) => Lines.Add($"announcement {Short(path)}");
        public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) => Lines.Add($"cache-add {Short(node.Path)}");
        public void EmitCacheRemove(string path) => Lines.Add($"cache-remove {Short(path)}");
    }

    [Fact]
    public void ATSPI_AKeystroke_IsTextChanged_WithTheCharacter_ACaretMoveIsCaretMoved_NoAccessibleValue()
    {
        using var session = HeadlessSession.Create(600, 400);
        var signals = new Signals();
        using var bridge = new LinuxAccessibilityBridge(signals);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        var (area, _) = Mount(session, "hello\nworld");
        var obj = ((IAtSpiTree)bridge).Find(AtSpiConstants.AccessiblePathPrefix + area.Id.Value)!;
        Assert.Contains(AtSpiConstants.TextInterface, obj.Interfaces);
        Assert.Null(obj.Value);
        signals.Lines.Clear();

        session.Window.TypeText("ab");
        session.PumpFrame();
        session.Window.PressKey(KeyCode.Right);
        session.PumpFrame();
        session.Window.PressKey(KeyCode.Backspace);
        session.PumpFrame();

        ulong id = area.Id.Value;
        Assert.Equal(
        [
            $"text-changed:insert {id} 0 ab",
            $"text-caret-moved {id} 2",
            $"text-caret-moved {id} 3",
            $"text-changed:delete {id} 2 h",
            $"text-caret-moved {id} 2",
        ], signals.Lines);
    }

    [Fact]
    public void ATSPI_AKeystrokeInALongArea_DoesNotMakeItsText()
    {
        // AT-SPI describes a text change by what was removed and inserted: from the two strings
        // the versions hold, without making either text's tables of lines and paragraphs.
        var text = string.Join('\n', Enumerable.Range(0, 2_000).Select(i => $"line {i} with some words in it to make it long enough"));
        using var session = HeadlessSession.Create(600, 400);
        var signals = new Signals();
        using var bridge = new LinuxAccessibilityBridge(signals);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        var (area, _) = Mount(session, text, width: 500, height: 300);
        session.Window.TypeText("z");
        session.PumpFrame();

        Assert.Contains($"text-changed:insert {area.Id.Value} 0 z", signals.Lines);
        var version = typeof(TextAreaElement).GetField("_textVersion", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(area)!;
        Assert.Null(version.GetType().GetField("_text", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(version));
    }

    // ── The text ──

    [Theory]
    [MemberData(nameof(Corpus))]
    public void TheText_IsTheRowsTheAreaDraws_WithEachCharacterWhereItIsDrawn(string corpus)
    {
        using var session = HeadlessSession.Create(600, 400);
        session.Window.InstallAccessibilityBridge();
        var (area, _) = Mount(session, corpus);
        area.CursorPosition = corpus.Length / 2;
        session.PumpFrame();

        var text = area.AccessibleTextSource!.AccessibleText!;
        var rows = Rows(area);
        Assert.Equal(corpus, text.Text);
        Assert.Equal(area.CursorPosition, text.Caret);
        Assert.Equal(corpus.Count(c => c == '\n') + 1, text.ParagraphCount);
        Assert.Equal(rows.Count, text.LineCount);
        Assert.Equal(rows, Enumerable.Range(0, text.LineCount).Select(text.LineStart));
        Assert.NotNull(text.Geometry);

        // Each character's box is on its row, one row high, inside the area's width; a point inside
        // a character's box finds a character whose box holds it: the same one, but inside a joined
        // emoji, whose parts share their place, and where the direction changes, where a box runs
        // between two caret places, as the rich text editor's do.
        var content = area.Layout.AbsoluteContentBox;
        float lh = text.Geometry!.CharacterBounds(text, 0).Height;
        Assert.True(lh > 0);
        for (int at = text.VisibleStart; at < text.VisibleEnd; at = text.NextCharacter(at))
        {
            var box = text.Geometry.CharacterBounds(text, at);
            int row = text.LineAt(at);
            Assert.Equal(content.Y + row * lh - area.ScrollOffsetY, box.Y, 2);
            Assert.True(box.X >= content.X - 0.5f && box.X + box.Width <= content.X + content.Width + 0.5f, $"{at}: {box}");
            if (box.Width > 0 && text.Text[at] != '\n')
            {
                var point = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
                int found = text.OffsetAt(point);
                var hit = text.Geometry.CharacterBounds(text, found);
                Assert.True(found >= 0 && text.LineAt(found) == row && point.X >= hit.X && point.X <= hit.X + hit.Width,
                    $"{at} at {point} found {found} in {hit}");
                if (corpus.All(c => c < 0x0590)) Assert.Equal(at, found);   // left to right, no surrogates
            }
        }
    }

    /// <summary>
    /// Each character's box is where the render context puts the caret at its two boundaries, bit
    /// for bit: the text asks once a row (<c>IRenderContext.TextCaretXs</c>,
    /// each row's string cut once) what it asked once a character before (a substring and a layout
    /// lookup per character, O(rows × length²) a keystroke with a reader reading).
    /// </summary>
    [Theory]
    [MemberData(nameof(Corpus))]
    public void TheCharacterBoxes_AreWhereTheCaretGoesAtEachBoundary_BitForBit(string corpus)
    {
        using var session = HeadlessSession.Create(600, 400);
        session.Window.InstallAccessibilityBridge();
        var (area, _) = Mount(session, corpus);
        area.CursorPosition = corpus.Length / 2;
        session.PumpFrame();

        var text = area.AccessibleTextSource!.AccessibleText!;
        var ctx = (Core.Rendering.IRenderContext)typeof(TextAreaElement)
            .GetField("_renderCtx", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(area)!;
        var style = area.ResolvedInherited;
        var content = area.Layout.AbsoluteContentBox;
        float frameX = content.X, frameY = content.Y - area.ScrollOffsetY;
        float lh = text.Geometry!.CharacterBounds(text, 0).Height;
        int boxes = 0;
        for (int at = text.VisibleStart; at < text.VisibleEnd; at = text.NextCharacter(at))
        {
            int row = text.LineAt(at), start = text.LineStart(row), length = text.LineEnd(row) - start;
            string rowText = corpus.Substring(start, length);
            float X(int k) => k == 0 ? 0f : ctx.TextCaretX(rowText, style, k);
            int k = at - start;
            int next = k >= length ? k : text.NextCharacter(at) - start;
            float x0 = X(k), x1 = X(next);
            var expected = new Rect(frameX + Math.Min(x0, x1), frameY + row * lh, Math.Abs(x1 - x0), lh);
            Assert.Equal(expected, text.Geometry.CharacterBounds(text, at));
            boxes++;
        }
        Assert.Equal(corpus.Length == 0, boxes == 0);
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void TheText_AnswersAsTheRichTextEditorsDoes_ForTheSameLines(string corpus)
    {
        // Wide enough that neither wraps: the lines are the paragraphs in both.
        if (corpus.Length == 0) return;
        using var session = HeadlessSession.Create(1600, 400);
        session.Window.InstallAccessibilityBridge();
        var area = TextArea.New(corpus).Width(1500f).Height(200f);
        var editor = RichTextEditor.New(RichDocument.FromPlainText(corpus)).Width(1500f).Height(200f);
        session.Window.SetRoot(Flex.Column(gap: 8).Padding(8).Children(area, editor));
        session.PumpFrames(3);

        var a = area.AccessibleTextSource!.AccessibleText!;
        var e = editor.AccessibleTextSource!.AccessibleText!;
        Assert.Equal(e.Text, a.Text);
        Assert.Equal((e.LineCount, e.ParagraphCount), (a.LineCount, a.ParagraphCount));
        for (int line = 0; line < e.LineCount; line++)
            Assert.Equal((e.LineStart(line), e.LineEnd(line), e.LineRange(line)), (a.LineStart(line), a.LineEnd(line), a.LineRange(line)));
        for (int at = 0; at <= e.Length; at++)
        {
            Assert.Equal(e.WordRange(at), a.WordRange(at));
            Assert.Equal(e.ParagraphRange(at), a.ParagraphRange(at));
            Assert.Equal(e.NextCharacter(at), a.NextCharacter(at));
            for (uint g = 0; g <= 4; g++)
                Assert.Equal(AtSpiText.GetStringAtOffset(e, at, g), AtSpiText.GetStringAtOffset(a, at, g));
        }
    }

    [Fact]
    public void ACaretMove_WithAReaderReading_SharesTheTables_AndNotAfterAnEditOrAWidthChange()
    {
        using var session = HeadlessSession.Create(600, 400);
        session.Window.InstallAccessibilityBridge();
        var (area, _) = Mount(session, Long + "\nshort\n" + Long);

        AccessibleText MoveAndRead(Action move)
        {
            for (int i = 0; i < 2; i++)
            {
                Read(area);
                session.PumpFrame();
            }
            move();
            session.PumpFrame();
            return Read(area);
        }

        var first = MoveAndRead(() => session.Window.PressKey(KeyCode.Right));
        var second = MoveAndRead(() => session.Window.PressKey(KeyCode.Right, KeyModifiers.Shift));
        Assert.NotNull(first.Geometry);
        Assert.Same(first.Geometry, second.Geometry);
        Assert.Equal((2, 1, 2), (second.Caret, second.SelectionStart, second.SelectionEnd));
        var fresh = new AccessibleText(area.Text, 2, 1, 2, lineStarts: Rows(area));
        Assert.Equal(fresh.LineCount, second.LineCount);
        for (int at = 0; at <= fresh.Length; at++)
            Assert.Equal(fresh.LineAt(at), second.LineAt(at));

        var edited = MoveAndRead(() => session.Window.TypeText("x"));
        Assert.NotSame(second.Geometry, edited.Geometry);
        var moved = MoveAndRead(() => session.Window.PressKey(KeyCode.Left));
        Assert.Same(edited.Geometry, moved.Geometry);
        var resized = MoveAndRead(() => { area.Width(260); area.MarkLayoutDirty(); });
        Assert.NotSame(moved.Geometry, resized.Geometry);
        Assert.Equal(Rows(area).Count, resized.LineCount);
    }

    [Fact]
    public void TheFirstTree_HasTheRowsAsDrawn_TheFrameAfterTheFirst()
    {
        // The first tree is built before the area is first drawn, with no rows to read: its
        // version is replaced by one with the rows the first frame drew, at once, not when the
        // tree is next built for something else (the layout refresh, 100 ms on).
        using var session = HeadlessSession.Create(600, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var area = TextArea.New(Long + "\n" + Long).Width(180f).Height(160f);
        session.Window.SetRoot(Flex.Column().Padding(8).Children(area));
        session.PumpFrame();
        int trees = session.Window.AccessibilityTreesBuilt;
        session.PumpFrame();

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        var text = NodeOf(bridge.CurrentTree!, area).Text!;
        Assert.NotNull(text.Geometry);
        Assert.Equal(Rows(area).Count, text.LineCount);
    }

    [Fact]
    public void AWidthChange_ReachesTheRowsAReaderReads_WithinAFrame()
    {
        using var session = HeadlessSession.Create(600, 400);
        session.Window.InstallAccessibilityBridge();
        var (area, _) = Mount(session, Long + "\n" + Long);
        Read(area);
        session.PumpFrame();
        int narrow = Read(area).LineCount;

        area.Width(500);
        area.MarkLayoutDirty();
        session.PumpFrame();   // laid out and drawn at the new width
        Read(area);
        session.PumpFrame();   // the new rows' version on the applied tree, made for the reader
        var wide = Read(area);
        Assert.Equal(Rows(area).Count, wide.LineCount);
        Assert.True(wide.LineCount < narrow, $"{narrow} -> {wide.LineCount}");
    }

    [Fact]
    public void AReadOnlySwitch_ReachesTheTextAReaderReads_WithNoTreeBuilt()
    {
        using var session = HeadlessSession.Create(600, 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var (area, _) = Mount(session, "hello");
        int trees = session.Window.AccessibilityTreesBuilt;
        bridge.ClearDiffs();

        area.ReadOnly = true;
        session.PumpFrame();
        Assert.True(Read(area).IsReadOnly);
        area.ReadOnly = false;
        session.PumpFrame();
        Assert.False(Read(area).IsReadOnly);
        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.Empty(bridge.Diffs);
    }
}
