using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Windows;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Seam A1 on Windows: the text pattern a node with text offers, tested at the provider
/// interfaces UI Automation calls: ranges, units, endpoints, the caret, the boxes. What a real
/// client makes of it was checked from outside with Windows' own UI Automation client.
/// </summary>
[SupportedOSPlatform("windows")]
public class UiaTextPatternTests
{
    private const int Character = 0, Word = 2, Line = 3, Paragraph = 4, Document = 6;
    private const int Start = 0, End = 1;

    /// <summary>A grid: every character 10 wide, every line 20 tall, from (10, 30).</summary>
    private sealed class Grid : AccessibleTextGeometry
    {
        public override Rect CharacterBounds(AccessibleText text, int offset)
        {
            int line = text.LineAt(offset);
            return new Rect(10 + (offset - text.LineStart(line)) * 10, 30 + line * 20, offset >= text.LineEnd(line) ? 0 : 10, 20);
        }

        public override int OffsetAt(AccessibleText text, Point point)
        {
            int line = (int)((point.Y - 30) / 20);
            if (point.Y < 30 || line >= text.LineCount) return -1;
            return Math.Clamp(text.LineStart(line) + (int)((point.X - 10) / 10), text.LineStart(line), text.LineEnd(line));
        }
    }

    private sealed class Rig : IDisposable
    {
        private readonly TestHwnd _window = TestHwnd.Create();
        private readonly HandleWindowHost _host;

        public WindowsAccessibilityBridge Bridge { get; }
        public AccessibilityNode Doc { get; }

        public Rig(AccessibleText text)
        {
            _host = HandleWindowHost.Create(_window.Handle);
            Bridge = Assert.IsType<WindowsAccessibilityBridge>(AccessibilityBridgeFactory.Create(_host.Application.PrimaryWindow));
            var app = new AccessibilityNode(ElementId.New(), AccessibilityRole.Application, null, null, new Rect(0, 0, 400, 300));
            Doc = new AccessibilityNode(ElementId.New(), AccessibilityRole.Document, "Terminal", null, new Rect(10, 30, 300, 200)) { Text = text };
            app.AddChild(Doc);
            Bridge.ApplyChanges(AccessibilityTreeDiffer.Diff(null, app), app);
        }

        public UiaFragment Fragment()
        {
            var native = Bridge.NativeFragment(Doc.ElementId);
            try { return Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native)); }
            finally { Marshal.Release(native); }
        }

        public UiaTextProvider Provider()
        {
            Assert.Equal(0, Fragment().GetPatternProvider(UiaInterop.UIA_TextPattern2Id, out var native));
            Assert.NotEqual(0, native);
            try { return Assert.IsType<UiaTextProvider>(NativeProvider.LogicOf(native)); }
            finally { Marshal.Release(native); }
        }

        public void Dispose()
        {
            Bridge.Dispose();
            _host.Dispose();
            _window.Dispose();
        }
    }

    /// <summary>Takes a native range pointer's logic, keeping the pointer alive for the test.</summary>
    private static UiaTextRange Range(IntPtr native)
    {
        Assert.NotEqual(0, native);
        return Assert.IsType<UiaTextRange>(NativeProvider.LogicOf(native));
    }

    private static string TextOf(UiaTextRange range, int max = -1)
    {
        Assert.Equal(0, range.GetText(max, out var bstr));
        try { return Marshal.PtrToStringBSTR(bstr); }
        finally { Marshal.FreeBSTR(bstr); }
    }

    private static readonly AccessibleText Screen = new("$ ls -la\nfile.txt\n$ ", caret: 20, geometry: new Grid());

    [WindowsFact]
    public void ANodeWithText_OffersTheTextPattern_AndOneWithout_DoesNot()
    {
        using var rig = new Rig(Screen);
        Assert.Equal(0, rig.Fragment().GetPatternProvider(UiaInterop.UIA_TextPatternId, out var text));
        Assert.NotEqual(0, text);
        // The native object answers for ITextProvider and ITextProvider2 both.
        Assert.Equal(0, Marshal.QueryInterface(text, new Guid("0dc5e6ed-3e16-4bf1-8f9a-a979878bc195"), out var text2));
        Marshal.Release(text2);
        Marshal.Release(text);

        var node = new AccessibilityNode(ElementId.New(), AccessibilityRole.Button, "OK", null, new Rect(0, 0, 10, 10));
        var fragment = new UiaFragment(rig.Bridge, node);
        Assert.Equal(0, fragment.GetPatternProvider(UiaInterop.UIA_TextPatternId, out var none));
        Assert.Equal(0, none);
    }

    [WindowsFact]
    public void TheDocument_IsTheText_AndGetTextStopsAtTheLimit()
    {
        using var rig = new Rig(Screen);
        Assert.Equal(0, rig.Provider().GetDocumentRange(out var native));
        try
        {
            var doc = Range(native);
            Assert.Equal(Screen.Text, TextOf(doc));
            Assert.Equal("$ ls", TextOf(doc, 4));
        }
        finally { Marshal.Release(native); }
    }

    [WindowsFact]
    public void InTextThatWraps_ALineIsWhatIsDrawn_AndAParagraphWhatANewlineEnds()
    {
        using var rig = new Rig(new AccessibleText("one two\nthree", lineStarts: [0, 4], geometry: new Grid()));
        Assert.Equal(0, rig.Provider().GetDocumentRange(out var native));
        try
        {
            var r = Range(native);
            Assert.Equal(0, r.ExpandToEnclosingUnit(Line));
            Assert.Equal("one ", TextOf(r));
            Assert.Equal(0, r.Move(Line, 1, out int moved));
            Assert.Equal((1, "two\n"), (moved, TextOf(r)));
            Assert.Equal(0, r.ExpandToEnclosingUnit(Paragraph));
            Assert.Equal("one two\n", TextOf(r));
            Assert.Equal(0, r.Move(Paragraph, 1, out moved));
            Assert.Equal((1, "three"), (moved, TextOf(r)));
        }
        finally { Marshal.Release(native); }
    }

    [WindowsFact]
    public void ARange_MovesByCharacterWordAndLine()
    {
        using var rig = new Rig(Screen);
        Assert.Equal(0, rig.Provider().GetDocumentRange(out var native));
        try
        {
            var r = Range(native);
            Assert.Equal(0, r.ExpandToEnclosingUnit(Line));
            Assert.Equal("$ ls -la\n", TextOf(r));

            Assert.Equal(0, r.Move(Line, 1, out int moved));
            Assert.Equal((1, "file.txt\n"), (moved, TextOf(r)));
            // Paragraph is a line here; page and anything larger is the document.
            Assert.Equal(0, r.Move(Paragraph, -1, out moved));
            Assert.Equal((1, "$ ls -la\n"), (moved, TextOf(r)));

            Assert.Equal(0, r.ExpandToEnclosingUnit(Word));
            Assert.Equal("$ ", TextOf(r));
            Assert.Equal(0, r.Move(Word, 2, out moved));
            Assert.Equal((2, "-"), (moved, TextOf(r)));

            Assert.Equal(0, r.ExpandToEnclosingUnit(Character));
            Assert.Equal(0, r.Move(Character, 1, out moved));
            Assert.Equal((1, "l"), (moved, TextOf(r)));

            // A range of text does not move onto the empty end: the last line is the last stop.
            Assert.Equal(0, r.ExpandToEnclosingUnit(Line));
            Assert.Equal(0, r.Move(Line, 5, out moved));
            Assert.Equal((2, "$ "), (moved, TextOf(r)));
        }
        finally { Marshal.Release(native); }
    }

    [WindowsFact]
    public void Endpoints_MoveAndCompare()
    {
        using var rig = new Rig(Screen);
        var provider = rig.Provider();
        Assert.Equal(0, provider.GetDocumentRange(out var a));
        Assert.Equal(0, provider.GetDocumentRange(out var b));
        try
        {
            var ra = Range(a);
            Assert.Equal(0, ra.MoveEndpointByUnit(End, Line, -2, out int moved));
            Assert.Equal((2, "$ ls -la\n"), (moved, TextOf(ra)));
            Assert.Equal(0, ra.CompareEndpoints(End, b, End, out int order));
            Assert.True(order < 0);
            Assert.Equal(0, ra.Compare(b, out int same));
            Assert.Equal(0, same);

            Assert.Equal(0, ra.MoveEndpointByRange(End, b, End));
            Assert.Equal(0, ra.Compare(b, out same));
            Assert.Equal(1, same);

            // An endpoint moved past the other takes it along.
            Assert.Equal(0, ra.MoveEndpointByUnit(Start, Document, 1, out _));
            Assert.Equal("", TextOf(ra));
        }
        finally { Marshal.Release(a); Marshal.Release(b); }
    }

    [WindowsFact]
    public void TheCaret_IsTheSelection_WhenNothingIsSelected()
    {
        using var rig = new Rig(Screen);
        var provider = rig.Provider();
        Assert.Equal(0, provider.GetCaretRange(out int active, out var caret));
        try
        {
            Assert.Equal(0, active); // not focused
            var r = Range(caret);
            Assert.Equal((20, 20), (r.Start, r.End));
        }
        finally { Marshal.Release(caret); }

        Assert.Equal(0, provider.GetSelection(out var array));
        Assert.Equal(1, SafeArrayLength(array));
        SafeArrayDestroy(array);
        Assert.Equal(0, provider.GetSupportedTextSelection(out int supported));
        Assert.Equal(UiaInterop.SupportedTextSelection_Single, supported);
    }

    [WindowsFact]
    public void Boxes_AreALinePerLine_InScreenPixels_AndAPointFindsACharacter()
    {
        using var rig = new Rig(Screen);
        Assert.Equal(0, rig.Provider().GetDocumentRange(out var native));
        try
        {
            var r = Range(native);
            Assert.Equal(0, r.MoveEndpointByUnit(Start, Character, 5, out _)); // "-la\nfile.txt\n$ "
            Assert.Equal(0, r.GetBoundingRectangles(out var array));
            var values = Doubles(array);
            Assert.Equal(12, values.Length); // three lines
            var first = rig.Bridge.ToScreen(new Rect(60, 30, 30, 20));
            Assert.Equal(new double[] { first.X, first.Y, first.Width, first.Height }, values[..4]);
        }
        finally { Marshal.Release(native); }

        var point = rig.Bridge.ToScreen(new Rect(10 + 3.5f * 10, 30 + 1.5f * 20, 0, 0));
        Assert.Equal(0, rig.Provider().RangeFromPoint(new UiaPoint { X = point.X, Y = point.Y }, out var at));
        try { Assert.Equal((12, 12), (Range(at).Start, Range(at).End)); } // "e" of file.txt
        finally { Marshal.Release(at); }
    }

    [WindowsFact]
    public void FindText_FindsWithinTheRange()
    {
        using var rig = new Rig(Screen);
        Assert.Equal(0, rig.Provider().GetDocumentRange(out var native));
        var needle = Marshal.StringToBSTR("$");
        try
        {
            var r = Range(native);
            Assert.Equal(0, r.FindText(needle, backward: 1, ignoreCase: 0, out var found));
            Assert.Equal((18, 19), (Range(found).Start, Range(found).End));
            Marshal.Release(found);
        }
        finally { Marshal.FreeBSTR(needle); Marshal.Release(native); }
    }

    [WindowsFact]
    public void TheVtable_ReachesTheRange()
    {
        // GetText through the native object's own vtable (slot 3 + 9), as UI Automation calls it.
        using var rig = new Rig(Screen);
        Assert.Equal(0, rig.Provider().GetDocumentRange(out var native));
        try
        {
            var slot = Marshal.ReadIntPtr(Marshal.ReadIntPtr(native), (3 + 9) * IntPtr.Size);
            var getText = Marshal.GetDelegateForFunctionPointer<GetTextFn>(slot);
            Assert.Equal(0, getText(native, 4, out var bstr));
            Assert.Equal("$ ls", Marshal.PtrToStringBSTR(bstr));
            Marshal.FreeBSTR(bstr);
        }
        finally { Marshal.Release(native); }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetTextFn(IntPtr self, int maxLength, out IntPtr text);

    [WindowsFact]
    public void Selecting_IsRefused_ReadingIsTheOffer()
    {
        using var rig = new Rig(Screen);
        Assert.Equal(0, rig.Provider().GetDocumentRange(out var native));
        try { Assert.Equal(UiaInterop.UIA_E_INVALIDOPERATION, Range(native).Select()); }
        finally { Marshal.Release(native); }
    }

    // ──── SAFEARRAY reading ────

    [DllImport("oleaut32.dll")] private static extern int SafeArrayGetUBound(IntPtr psa, uint dim, out int bound);
    [DllImport("oleaut32.dll")] private static extern int SafeArrayAccessData(IntPtr psa, out IntPtr data);
    [DllImport("oleaut32.dll")] private static extern int SafeArrayUnaccessData(IntPtr psa);
    [DllImport("oleaut32.dll")] private static extern int SafeArrayDestroy(IntPtr psa);

    private static int SafeArrayLength(IntPtr psa)
    {
        Assert.Equal(0, SafeArrayGetUBound(psa, 1, out int upper));
        return upper + 1;
    }

    private static double[] Doubles(IntPtr psa)
    {
        try
        {
            var values = new double[SafeArrayLength(psa)];
            Assert.Equal(0, SafeArrayAccessData(psa, out var data));
            Marshal.Copy(data, values, 0, values.Length);
            SafeArrayUnaccessData(psa);
            return values;
        }
        finally { SafeArrayDestroy(psa); }
    }
}
