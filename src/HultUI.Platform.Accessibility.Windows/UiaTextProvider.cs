using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;

namespace HultUI.Platform.Accessibility.Windows;

/// <summary>
/// The text pattern (ITextProvider2) over a node's <see cref="AccessibleText"/>: seam A1 on
/// Windows. Every call reads the text as it is now, from the node's source, on UIA's thread.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class UiaTextProvider(WindowsAccessibilityBridge bridge, ElementId elementId) : INativeTextProvider, IElementBound
{
    public bool Gone => bridge.IsGone(elementId);

    public int GetSelection(out IntPtr pRetVal)
    {
        var text = UiaTextRange.TextOf(bridge, elementId);
        if (text.HasSelection) pRetVal = UiaTextRange.Array(bridge, elementId, text.SelectionStart, text.SelectionEnd);
        else if (text.Caret >= 0) pRetVal = UiaTextRange.Array(bridge, elementId, text.Caret, text.Caret);
        else pRetVal = UiaInterop.BuildProviderArray([]);
        return 0;
    }

    public int GetVisibleRanges(out IntPtr pRetVal)
    {
        var text = UiaTextRange.TextOf(bridge, elementId);
        pRetVal = UiaTextRange.Array(bridge, elementId, text.VisibleStart, text.VisibleEnd);
        return 0;
    }

    public int RangeFromChild(IntPtr childElement, out IntPtr pRetVal)
    {
        // The text has no child elements in it.
        pRetVal = IntPtr.Zero;
        return UiaInterop.E_INVALIDARG;
    }

    public int RangeFromPoint(UiaPoint point, out IntPtr pRetVal)
    {
        var text = UiaTextRange.TextOf(bridge, elementId);
        var local = bridge.FromScreen(new Point((float)point.X, (float)point.Y));
        int offset = text.OffsetAt(local);
        if (offset < 0 && bridge.TryGetBounds(elementId, out var box))
        {
            // Outside the text: the nearest character, from the point pulled into the element.
            var inside = new Point(Math.Clamp(local.X, box.X, box.X + Math.Max(0, box.Width - 1)),
                Math.Clamp(local.Y, box.Y, box.Y + Math.Max(0, box.Height - 1)));
            offset = text.OffsetAt(inside);
        }
        offset = Math.Max(0, offset);
        pRetVal = UiaTextRange.Create(bridge, elementId, offset, offset);
        return 0;
    }

    public int GetDocumentRange(out IntPtr pRetVal)
    {
        pRetVal = UiaTextRange.Create(bridge, elementId, 0, UiaTextRange.TextOf(bridge, elementId).Length);
        return 0;
    }

    public int GetSupportedTextSelection(out int pRetVal)
    {
        // A selection is reported (and the caret as an empty one); a reader cannot set one yet.
        pRetVal = UiaInterop.SupportedTextSelection_Single;
        return 0;
    }

    public int RangeFromAnnotation(IntPtr annotationElement, out IntPtr pRetVal)
    {
        pRetVal = IntPtr.Zero;
        return UiaInterop.E_INVALIDARG;
    }

    public int GetCaretRange(out int isActive, out IntPtr pRetVal)
    {
        var text = UiaTextRange.TextOf(bridge, elementId);
        isActive = bridge.FocusedNodeId == elementId ? 1 : 0;
        int caret = Math.Max(0, text.Caret);
        pRetVal = UiaTextRange.Create(bridge, elementId, caret, caret);
        return 0;
    }
}

/// <summary>
/// One range of a node's text (ITextRangeProvider), its endpoints offsets into
/// <see cref="AccessibleText.Text"/>. The text may change under a range; every call clamps the
/// endpoints into the text as it is now.
/// </summary>
/// <remarks>
/// Units: a character steps over a surrogate pair whole; a word is a run with the blanks after
/// it (<see cref="AccessibleText.WordRange"/>); a line is a line with its line break. Units the
/// text has no notion of take the next larger one, as UIA asks: format is word, paragraph is
/// line, page is the document.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class UiaTextRange : INativeTextRange, IElementBound
{
    private static readonly AccessibleText Empty = new("");

    private readonly WindowsAccessibilityBridge _bridge;
    private readonly ElementId _elementId;
    private int _start, _end;

    private UiaTextRange(WindowsAccessibilityBridge bridge, ElementId elementId, int start, int end)
    {
        _bridge = bridge;
        _elementId = elementId;
        _start = start;
        _end = end;
    }

    internal int Start => _start;
    internal int End => _end;
    internal ElementId ElementId => _elementId;
    public bool Gone => _bridge.IsGone(_elementId);

    internal static AccessibleText TextOf(WindowsAccessibilityBridge bridge, ElementId id) => bridge.GetText(id) ?? Empty;

    /// <summary>A new range as a native ITextRangeProvider* carrying one reference.</summary>
    internal static IntPtr Create(WindowsAccessibilityBridge bridge, ElementId id, int start, int end)
        => NativeProvider.Create(new UiaTextRange(bridge, id, Math.Min(start, end), Math.Max(start, end)));

    /// <summary>A SAFEARRAY holding one new range.</summary>
    internal static IntPtr Array(WindowsAccessibilityBridge bridge, ElementId id, int start, int end)
        => UiaInterop.BuildProviderArray([Create(bridge, id, start, end)]);

    private AccessibleText Current(out int start, out int end)
    {
        var text = TextOf(_bridge, _elementId);
        _start = start = Math.Clamp(_start, 0, text.Length);
        _end = end = Math.Clamp(_end, start, text.Length);
        return text;
    }

    private UiaTextRange? Other(IntPtr range) =>
        NativeProvider.LogicOf(range) is UiaTextRange other && other._elementId == _elementId ? other : null;

    public int Clone(out IntPtr pRetVal)
    {
        pRetVal = Create(_bridge, _elementId, _start, _end);
        return 0;
    }

    public int Compare(IntPtr range, out int pRetVal)
    {
        pRetVal = Other(range) is { } o && o._start == _start && o._end == _end ? 1 : 0;
        return 0;
    }

    public int CompareEndpoints(int endpoint, IntPtr targetRange, int targetEndpoint, out int pRetVal)
    {
        pRetVal = 0;
        if (Other(targetRange) is not { } o) return UiaInterop.E_INVALIDARG;
        int mine = endpoint == UiaInterop.TextPatternRangeEndpoint_Start ? _start : _end;
        int theirs = targetEndpoint == UiaInterop.TextPatternRangeEndpoint_Start ? o._start : o._end;
        pRetVal = Math.Sign(mine - theirs);
        return 0;
    }

    public int ExpandToEnclosingUnit(int unit)
    {
        var text = Current(out int start, out _);
        (_start, _end) = Enclosing(text, Normalize(unit), start);
        return 0;
    }

    public int FindAttribute(int attributeId, UiaVariant value, int backward, out IntPtr pRetVal)
    {
        // The text carries no attributes worth finding a run of.
        pRetVal = IntPtr.Zero;
        return 0;
    }

    public int FindText(IntPtr text, int backward, int ignoreCase, out IntPtr pRetVal)
    {
        pRetVal = IntPtr.Zero;
        string? needle = Marshal.PtrToStringBSTR(text);
        if (string.IsNullOrEmpty(needle)) return UiaInterop.E_INVALIDARG;
        var current = Current(out int start, out int end);
        var span = current.Text.AsSpan(start, end - start);
        var comparison = ignoreCase != 0 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int at = backward != 0 ? span.LastIndexOf(needle, comparison) : span.IndexOf(needle, comparison);
        if (at >= 0) pRetVal = Create(_bridge, _elementId, start + at, start + at + needle.Length);
        return 0;
    }

    public int GetAttributeValue(int attributeId, out UiaVariant pRetVal)
    {
        if (attributeId == UiaInterop.UIA_IsReadOnlyAttributeId)
        {
            pRetVal = UiaVariant.FromBool(TextOf(_bridge, _elementId).IsReadOnly);
            return 0;
        }
        // Any other attribute: UIA's reserved "not supported" object, a reference handed over.
        UiaInterop.UiaGetReservedNotSupportedValue(out var notSupported);
        if (notSupported != IntPtr.Zero) Marshal.AddRef(notSupported);
        pRetVal = UiaVariant.FromUnknown(notSupported);
        return 0;
    }

    public int GetBoundingRectangles(out IntPtr pRetVal)
    {
        var text = Current(out int start, out int end);
        var boxes = text.RangeBounds(start, end);
        var values = new double[boxes.Count * 4];
        for (int i = 0; i < boxes.Count; i++)
        {
            var screen = _bridge.ToScreen(boxes[i]);
            values[i * 4] = screen.X;
            values[i * 4 + 1] = screen.Y;
            values[i * 4 + 2] = screen.Width;
            values[i * 4 + 3] = screen.Height;
        }
        pRetVal = UiaInterop.BuildDoubleArray(values);
        return 0;
    }

    public int GetEnclosingElement(out IntPtr pRetVal)
    {
        pRetVal = _bridge.NativeFragmentSimple(_elementId);
        return 0;
    }

    public int GetText(int maxLength, out IntPtr pRetVal)
    {
        var text = Current(out int start, out int end);
        int length = end - start;
        if (maxLength >= 0 && length > maxLength) length = maxLength;
        pRetVal = Marshal.StringToBSTR(text.Text.Substring(start, length));
        return 0;
    }

    public int Move(int unit, int count, out int pRetVal)
    {
        pRetVal = 0;
        if (count == 0) return 0;
        var text = Current(out int start, out int end);
        unit = Normalize(unit);
        bool degenerate = start == end;
        int p = start, moved = 0;
        for (int i = 0; i < Math.Abs(count); i++)
        {
            int q = count > 0 ? Next(text, unit, p) : Previous(text, unit, p);
            // A range of text never moves onto the empty end of the document: there is no unit
            // there to read.
            if (q == p || (count > 0 && !degenerate && q >= text.Length && unit != UiaInterop.TextUnit_Document)) break;
            p = q;
            moved++;
        }
        _start = p;
        _end = degenerate ? p : Math.Max(p, Next(text, unit, p));
        pRetVal = moved;
        return 0;
    }

    public int MoveEndpointByUnit(int endpoint, int unit, int count, out int pRetVal)
    {
        var text = Current(out int start, out int end);
        unit = Normalize(unit);
        int p = endpoint == UiaInterop.TextPatternRangeEndpoint_Start ? start : end, moved = 0;
        for (int i = 0; i < Math.Abs(count); i++)
        {
            int q = count > 0 ? Next(text, unit, p) : Previous(text, unit, p);
            if (q == p) break;
            p = q;
            moved++;
        }
        SetEndpoint(endpoint, p);
        pRetVal = moved;
        return 0;
    }

    public int MoveEndpointByRange(int endpoint, IntPtr targetRange, int targetEndpoint)
    {
        if (Other(targetRange) is not { } o) return UiaInterop.E_INVALIDARG;
        SetEndpoint(endpoint, targetEndpoint == UiaInterop.TextPatternRangeEndpoint_Start ? o._start : o._end);
        return 0;
    }

    /// <summary>Moves one endpoint; one moved past the other takes the other with it.</summary>
    private void SetEndpoint(int endpoint, int value)
    {
        if (endpoint == UiaInterop.TextPatternRangeEndpoint_Start)
        {
            _start = value;
            if (_end < value) _end = value;
        }
        else
        {
            _end = value;
            if (_start > value) _start = value;
        }
    }

    // Readers read the text; selecting it from a reader is not offered yet.
    public int Select() => UiaInterop.UIA_E_INVALIDOPERATION;
    public int AddToSelection() => UiaInterop.UIA_E_INVALIDOPERATION;
    public int RemoveFromSelection() => UiaInterop.UIA_E_INVALIDOPERATION;

    public int ScrollIntoView(int alignToTop) => 0;

    public int GetChildren(out IntPtr pRetVal)
    {
        pRetVal = UiaInterop.BuildProviderArray([]);
        return 0;
    }

    // ──── Units ────

    /// <summary>A unit the text has no notion of becomes the next larger one.</summary>
    private static int Normalize(int unit) => unit switch
    {
        UiaInterop.TextUnit_Format => UiaInterop.TextUnit_Word,
        UiaInterop.TextUnit_Page => UiaInterop.TextUnit_Document,
        < UiaInterop.TextUnit_Character or > UiaInterop.TextUnit_Document => UiaInterop.TextUnit_Document,
        _ => unit,
    };

    internal static int Next(AccessibleText text, int unit, int p) => unit switch
    {
        UiaInterop.TextUnit_Character => text.NextCharacter(p),
        UiaInterop.TextUnit_Word => text.NextWordStart(p),
        UiaInterop.TextUnit_Line => text.LineRange(text.LineAt(p)).End,
        UiaInterop.TextUnit_Paragraph => text.ParagraphRange(p).End,
        _ => text.Length,
    };

    internal static int Previous(AccessibleText text, int unit, int p)
    {
        switch (unit)
        {
            case UiaInterop.TextUnit_Character: return text.PreviousCharacter(p);
            case UiaInterop.TextUnit_Word: return text.PreviousWordStart(p);
            case UiaInterop.TextUnit_Line:
                int line = text.LineAt(p);
                return p == text.LineStart(line) && line > 0 ? text.LineStart(line - 1) : text.LineStart(line);
            case UiaInterop.TextUnit_Paragraph:
                int start = text.ParagraphRange(p).Start;
                return p == start && start > 0 ? text.ParagraphRange(start - 1).Start : start;
            default: return 0;
        }
    }

    internal static (int Start, int End) Enclosing(AccessibleText text, int unit, int p) => unit switch
    {
        UiaInterop.TextUnit_Character => (p, text.NextCharacter(p)),
        UiaInterop.TextUnit_Word when p >= text.Length => (text.PreviousWordStart(p), text.Length),
        UiaInterop.TextUnit_Word => text.WordRange(p),
        UiaInterop.TextUnit_Line => text.LineRange(text.LineAt(p)),
        UiaInterop.TextUnit_Paragraph => text.ParagraphRange(p),
        _ => (0, text.Length),
    };
}
