using System.Text;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Core.Layout;

/// <summary>
/// The layout-change audit (<c>HULTUI_LAYOUT_AUDIT</c>, a directory): after every outermost layout
/// pass the same tree is laid out again from nothing — every measurement and kept layout dropped,
/// by an engine of its own — and each box whose <see cref="LayoutResult"/> came out different from
/// the one the pass left is written down: its position, size, padding, border and margin exactly,
/// and its window position within 0.01 (a kept subtree is moved by a difference, OffsetDescendants,
/// which rounds). Then everything the pass left is put back, so the run carries on as it would
/// have. With nothing kept wrongly the files hold only the running totals.
///
/// <para>An engine change that keeps or shares more work proves itself by an empty audit across
/// every suite. To compare it with the code it replaces, the change takes its old path while
/// <see cref="InReference"/> is true (an edit made for the audit run only): every difference is then
/// either the change or something kept wrongly before it, which a run without that edit shows.</para>
///
/// <para>Each test process writes its own file, <c>layout-audit-&lt;process id&gt;.log</c>, with a
/// line per box that differs (at most a dozen a pass), where it was made (Debug builds), what it
/// sits in, and the totals every 200 passes. Off, it costs a static read a pass.</para>
/// </summary>
internal static class LayoutAudit
{
    private static readonly string? s_directory =
        Environment.GetEnvironmentVariable("HULTUI_LAYOUT_AUDIT") is { Length: > 0 } dir ? dir : null;

    /// <summary>Whether the audit is on.</summary>
    internal static bool Enabled => s_directory is not null;

    [ThreadStatic] private static bool t_inReference;

    /// <summary>True while the audit lays a tree out from nothing to compare with.</summary>
    internal static bool InReference => t_inReference;

    private static readonly object s_lock = new();
    private static string? s_file;
    private static bool s_exitHooked;
    private static long s_passes, s_boxes, s_differing, s_drifted;
    private const int LinesPerPass = 12;
    private const int TotalsEvery = 200;

    private struct Kept
    {
        public Element Element;
        public LayoutResult Layout;
        public MeasureCache Measure;
        public float ConstraintW, ConstraintH;
        public float? ForcedW, ForcedH;
        public Size Result;
        public DirtyFlags Dirty;
    }

    /// <summary>Lays <paramref name="root"/> out again from nothing, as <see cref="LayoutEngine.PerformLayout"/>
    /// was just asked to, and writes down every box that came out different.</summary>
    internal static void Check(IMeasureContext context, Element root, float availableWidth, float availableHeight,
        bool forceRootFill)
    {
        if (t_inReference) return;
        var kept = new List<Kept>();
        Collect(root, kept);
        foreach (var k in kept)
        {
            k.Element.DirtyFlags |= DirtyFlags.Layout;
            k.Element.MeasureCache.Invalidate();
        }

        t_inReference = true;
        try
        {
            new LayoutEngine(context).PerformLayout(root, availableWidth, availableHeight, forceRootFill);
        }
        finally
        {
            t_inReference = false;
        }

        StringBuilder? lines = null;
        int differing = 0, drifted = 0;
        foreach (var k in kept)
        {
            var e = k.Element;
            if (Difference(k.Layout, e.Layout, out bool driftOnly) is { } what)
            {
                if (driftOnly) drifted++;
                else if (differing++ < LinesPerPass)
                    (lines ??= new StringBuilder()).Append(Describe(e)).Append(": ").Append(what)
                        .Append(" | in ").Append(Ancestors(e)).AppendLine();
            }
            e.Layout = k.Layout;
            e.MeasureCache = k.Measure;
            e.CacheConstraintW = k.ConstraintW;
            e.CacheConstraintH = k.ConstraintH;
            e.CacheForcedW = k.ForcedW;
            e.CacheForcedH = k.ForcedH;
            e.CacheResultSize = k.Result;
            e.DirtyFlags = (e.DirtyFlags & ~DirtyFlags.Layout) | (k.Dirty & DirtyFlags.Layout);
        }
        if (differing > LinesPerPass)
            lines!.Append("  … and ").Append(differing - LinesPerPass).AppendLine(" more boxes in this pass");

        lock (s_lock)
        {
            if (!s_exitHooked)
            {
                // The last totals; a test host may be ended before this runs, hence the ones every
                // TotalsEvery passes too.
                s_exitHooked = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => { lock (s_lock) WriteTotals(); };
            }
            s_passes++;
            s_boxes += kept.Count;
            s_differing += differing;
            s_drifted += drifted;
            if (lines is not null) Write(lines.ToString());
            if (s_passes % TotalsEvery == 0) WriteTotals();
        }
    }

    private static void Collect(Element element, List<Kept> into)
    {
        into.Add(new Kept
        {
            Element = element,
            Layout = element.Layout,
            Measure = element.MeasureCache,
            ConstraintW = element.CacheConstraintW,
            ConstraintH = element.CacheConstraintH,
            ForcedW = element.CacheForcedW,
            ForcedH = element.CacheForcedH,
            Result = element.CacheResultSize,
            Dirty = element.DirtyFlags,
        });
        var children = element.Children;
        for (int i = 0; i < children.Count; i++) Collect(children[i], into);
    }

    /// <summary>What differs between the box the pass left and the one laid out from nothing, or
    /// null; <paramref name="driftOnly"/> when only the window position does, by 0.01 or less.</summary>
    private static string? Difference(LayoutResult kept, LayoutResult fresh, out bool driftOnly)
    {
        driftOnly = false;
        var sb = new StringBuilder();
        Field(sb, "x", kept.X, fresh.X);
        Field(sb, "y", kept.Y, fresh.Y);
        Field(sb, "w", kept.Width, fresh.Width);
        Field(sb, "h", kept.Height, fresh.Height);
        if (kept.Padding != fresh.Padding) sb.Append($" padding {kept.Padding}->{fresh.Padding}");
        if (kept.Border != fresh.Border) sb.Append($" border {kept.Border}->{fresh.Border}");
        if (kept.Margin != fresh.Margin) sb.Append($" margin {kept.Margin}->{fresh.Margin}");
        bool exact = sb.Length == 0;
        Field(sb, "absX", kept.AbsoluteX, fresh.AbsoluteX);
        Field(sb, "absY", kept.AbsoluteY, fresh.AbsoluteY);
        if (sb.Length == 0) return null;
        driftOnly = exact && float.Abs(kept.AbsoluteX - fresh.AbsoluteX) <= 0.01f
            && float.Abs(kept.AbsoluteY - fresh.AbsoluteY) <= 0.01f;
        return sb.ToString(1, sb.Length - 1);
    }

    private static void Field(StringBuilder sb, string name, float kept, float fresh)
    {
        if (!kept.Equals(fresh)) sb.Append($" {name} {kept:R}->{fresh:R}");
    }

    private static string Describe(Element e)
    {
        var type = e.GetType();
        string name = type.IsGenericType ? type.Name[..type.Name.IndexOf('`')] : type.Name;
        var sb = new StringBuilder(name);
        if (e.Parent is { } parent && parent.Children.Count > 0 && parent.Children[0] == e
            && parent.GetType().Name.StartsWith("VirtualListElement", StringComparison.Ordinal))
            sb.Append("[spacer]");
        if ((e.Key ?? e.Name) is { Length: > 0 } id) sb.Append('#').Append(id);
        if (e.Origin is { } origin) sb.Append('@').Append(Path.GetFileName(origin.File)).Append(':').Append(origin.Line);
        return sb.ToString();
    }

    private static string Ancestors(Element e)
    {
        var sb = new StringBuilder();
        int n = 0;
        for (var p = e.Parent; p is not null && n < 5; p = p.Parent, n++)
        {
            if (n > 0) sb.Append(" < ");
            sb.Append(Describe(p));
            var flex = p.FlexRead;
            sb.Append(p.GridRead.HasTemplate ? " grid" : $" {flex.Direction}");
            if (p.BoxRead.OverflowY == Overflow.Scroll) sb.Append(" scrollY");
            if (p.BoxRead.OverflowX == Overflow.Scroll) sb.Append(" scrollX");
        }
        return n == 0 ? "(root)" : sb.ToString();
    }

    private static void Write(string text)
    {
        s_file ??= Path.Combine(s_directory!, $"layout-audit-{Environment.ProcessId}.log");
        Directory.CreateDirectory(s_directory!);
        File.AppendAllText(s_file, text);
    }

    private static void WriteTotals() =>
        Write($"totals: {s_passes} passes, {s_boxes} boxes, {s_differing} differing, {s_drifted} drifted within 0.01{Environment.NewLine}");
}
