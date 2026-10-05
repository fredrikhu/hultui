using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Core.DevTools;

/// <summary>One labelled value in the inspector's property pane.</summary>
public readonly record struct PropertyRow(string Name, string Value);

/// <summary>A named block of <see cref="PropertyRow"/>s (Layout, Flex, Text, …).</summary>
public sealed record PropertyGroup(string Name, IReadOnlyList<PropertyRow> Rows);

/// <summary>
/// Turns a live <see cref="Element"/> into the human-readable labels and property
/// groups the inspector shows. Pure reads — describing an element never mutates it,
/// never allocates a style object on it (the <c>*Read</c> accessors fall back to the
/// shared defaults), and never marks it dirty. That matters: a debugger that
/// perturbs the tree it is describing is worse than no debugger.
/// </summary>
public static class ElementProperties
{
    /// <summary>
    /// Whether this build captures element origins (the "Created at" row). False in
    /// builds without <c>HultUIElementOrigin</c> — Release by default — so a panel
    /// can explain the row's absence instead of leaving it a mystery.
    /// </summary>
    public static bool OriginsAvailable =>
#if HULTUI_ELEMENT_ORIGIN
        true;
#else
        false;
#endif

    /// <summary>
    /// Short type label for a tree row: the <c>Element</c> suffix is dropped
    /// (<c>BoxElement</c> → <c>Box</c>) and generics are spelled out
    /// (<c>VirtualListElement&lt;FileRow&gt;</c> → <c>VirtualList&lt;FileRow&gt;</c>).
    /// A <see cref="ComponentHost"/> reports the component it hosts, which is the
    /// name the app author actually wrote.
    /// </summary>
    public static string TypeLabel(Element element)
    {
        if (element is ComponentHost host)
            return host.Component.GetType().Name;

        var type = element.GetType();
        var name = type.Name;

        if (type.IsGenericType)
        {
            int tick = name.IndexOf('`');
            if (tick >= 0) name = name[..tick];
            var args = string.Join(", ", type.GetGenericArguments().Select(a => a.Name));
            return $"{Strip(name)}<{args}>";
        }

        return Strip(name);

        static string Strip(string n) =>
            n.Length > 7 && n.EndsWith("Element", StringComparison.Ordinal) ? n[..^7] : n;
    }

    /// <summary>
    /// The identifying suffix for a tree row — a name, automation id, or the text
    /// content of a text-bearing element — or an empty string when there is nothing
    /// distinguishing to say. Long text is elided so one row stays one row.
    /// </summary>
    public static string Descriptor(Element element)
    {
        if (!string.IsNullOrEmpty(element.Name)) return "#" + element.Name;
        if (!string.IsNullOrEmpty(element.AutomationId)) return "#" + element.AutomationId;
        if (TextOf(element) is { Length: > 0 } text) return "\"" + Elide(text, 40) + "\"";
        if (!string.IsNullOrEmpty(element.AccessibleName)) return "\"" + Elide(element.AccessibleName, 40) + "\"";
        return "";
    }

    /// <summary>Type label plus descriptor, e.g. <c>Button "Save"</c>. Used for breadcrumbs.</summary>
    public static string Summarize(Element element)
    {
        var descriptor = Descriptor(element);
        return descriptor.Length == 0 ? TypeLabel(element) : $"{TypeLabel(element)} {descriptor}";
    }

    /// <summary>The user-visible text an element carries, or null if it carries none.</summary>
    public static string? TextOf(Element element) => element switch
    {
        TextElement t => t.Content,
        ButtonElement b => b.Label,
        TextInputElement i => i.Text,
        TextAreaElement a => a.Text,
        _ => null
    };

    /// <summary>
    /// Every property group for <paramref name="element"/>. Rows whose value is the
    /// framework default are omitted, so the pane shows what this element actually
    /// sets rather than a wall of <c>Auto</c>. Layout is the exception: measured
    /// geometry is always shown, because "it laid out to zero" is the answer to most
    /// questions that bring someone to an inspector in the first place.
    /// </summary>
    public static IReadOnlyList<PropertyGroup> Describe(Element element)
    {
        var groups = new List<PropertyGroup>(8);

        Group(groups, "Element", rows =>
        {
            rows.Add(new("Type", element.GetType().Name));
            Add(rows, "Name", element.Name);
            Add(rows, "Key", VisibleKey(element.Key));
            Add(rows, "AutomationId", element.AutomationId);
            if (TextOf(element) is { } text) rows.Add(new("Text", "\"" + Elide(text, 120) + "\""));
            rows.Add(new("Children", element.Children.Count.ToString()));
            // Absent in builds with origins compiled out, and for elements built by
            // `new` rather than a factory. See Element.Origin.
            if (element.Origin is { } origin) rows.Add(new("Created at", origin.ToString()));
        });

        var layout = element.Layout;
        Group(groups, "Layout", rows =>
        {
            rows.Add(new("Position", $"{layout.AbsoluteX:0.#}, {layout.AbsoluteY:0.#}"));
            rows.Add(new("Size", $"{layout.Width:0.#} × {layout.Height:0.#}"));
            var content = layout.AbsoluteContentBox;
            rows.Add(new("Content box", $"{content.Width:0.#} × {content.Height:0.#}"));
            rows.Add(new("Margin", Format(layout.Margin)));
            rows.Add(new("Border", Format(layout.Border)));
            rows.Add(new("Padding", Format(layout.Padding)));
        });

        var box = element.BoxRead;
        Group(groups, "Box", rows =>
        {
            Add(rows, "Width", Format(box.Width), "auto");
            Add(rows, "Height", Format(box.Height), "auto");
            Add(rows, "Min width", Format(box.MinWidth), "none");
            Add(rows, "Min height", Format(box.MinHeight), "none");
            Add(rows, "Max width", Format(box.MaxWidth), "none");
            Add(rows, "Max height", Format(box.MaxHeight), "none");
            Add(rows, "Overflow X", box.OverflowX, Overflow.Visible);
            Add(rows, "Overflow Y", box.OverflowY, Overflow.Visible);
            if (element.Opacity != 1f) rows.Add(new("Opacity", element.Opacity.ToString("0.##")));
            if (element.Transform is { } t && !t.IsIdentity) rows.Add(new("Transform", Format(t)));
        });

        if (element is BoxElement visual)
        {
            Group(groups, "Paint", rows =>
            {
                Add(rows, "Background", Format(visual.BackgroundColor), "transparent");
                Add(rows, "Border color", Format(visual.BorderColor), "transparent");
                Add(rows, "Border width", Format(box.BorderWidth), "0");
                Add(rows, "Border style", visual.BorderStyle, BorderStyle.Solid);
                if (!visual.CornerRadius.IsZero) rows.Add(new("Corner radius", Format(visual.CornerRadius)));
                if (visual.BackgroundGradient is not null) rows.Add(new("Gradient", "yes"));
                if (visual.BoxShadows is { Count: > 0 } shadows) rows.Add(new("Shadows", shadows.Count.ToString()));
            });
        }

        var flex = element.FlexRead;
        Group(groups, "Flex", rows =>
        {
            // Direction is always shown: which axis a container lays out on is the
            // first thing to check when children stack the wrong way, and Column
            // being the default makes its absence ambiguous.
            if (element.Children.Count > 0) rows.Add(new("Direction", flex.Direction.ToString()));
            Add(rows, "Wrap", flex.Wrap, FlexWrap.NoWrap);
            Add(rows, "Justify content", flex.JustifyContent, JustifyContent.FlexStart);
            Add(rows, "Align items", flex.AlignItems, AlignItems.Stretch);
            Add(rows, "Align content", flex.AlignContent, AlignContent.FlexStart);
            Add(rows, "Align self", flex.AlignSelf, AlignSelf.Auto);
            Add(rows, "Grow", flex.FlexGrow, 0f);
            Add(rows, "Shrink", flex.FlexShrink, 1f);
            Add(rows, "Basis", Format(flex.FlexBasis), "auto");
            Add(rows, "Gap", flex.Gap, 0f);
            Add(rows, "Row gap", flex.RowGap, 0f);
            Add(rows, "Column gap", flex.ColumnGap, 0f);
        });

        var position = element.PositionRead;
        if (position.Type != PositionType.Relative || position.ZIndex != 0)
        {
            Group(groups, "Position", rows =>
            {
                rows.Add(new("Type", position.Type.ToString()));
                Add(rows, "Top", Format(position.Top), "none");
                Add(rows, "Right", Format(position.Right), "none");
                Add(rows, "Bottom", Format(position.Bottom), "none");
                Add(rows, "Left", Format(position.Left), "none");
                Add(rows, "Z-index", position.ZIndex, 0);
            });
        }

        var gridItem = element.GridItemRead;
        if (gridItem.Row != 0 || gridItem.Column != 0
            || gridItem.JustifySelf != JustifySelf.Auto || gridItem.AlignSelf != AlignSelf.Auto)
        {
            Group(groups, "Grid item", rows =>
            {
                // Grid coordinates are 0-based here, unlike CSS's 1-based lines;
                // label them so a reader doesn't silently convert.
                rows.Add(new("Row (0-based)", gridItem.Row.ToString()));
                rows.Add(new("Column (0-based)", gridItem.Column.ToString()));
                Add(rows, "Justify self", gridItem.JustifySelf, JustifySelf.Auto);
                Add(rows, "Align self", gridItem.AlignSelf, AlignSelf.Auto);
            });
        }

        var resolved = element.ResolvedInherited;
        Group(groups, "Text", rows =>
        {
            rows.Add(new("Font", $"{resolved.FontFamily} {resolved.FontSize:0.#} {resolved.FontWeight}"));
            rows.Add(new("Color", Format(resolved.TextColor)));
            Add(rows, "Slant", resolved.FontSlant, FontSlant.Normal);
            Add(rows, "Align", resolved.TextAlign, TextAlign.Start);
            Add(rows, "Decoration", resolved.Decoration, TextDecoration.None);
            Add(rows, "Direction", resolved.TextDirection, TextDirection.Ltr);
            Add(rows, "Line height", resolved.LineHeight, 0f);
            Add(rows, "Letter spacing", resolved.LetterSpacing, 0f);
            Add(rows, "Cursor", resolved.Cursor, CursorType.Default);
        });

        Group(groups, "State", rows =>
        {
            rows.Add(new("Hovered", YesNo(element.IsHovered)));
            rows.Add(new("Pressed", YesNo(element.IsPressed)));
            rows.Add(new("Focused", YesNo(element.IsFocused)));
            Add(rows, "Focusable", YesNo(element.IsFocusable), "no");
            Add(rows, "Tab index", element.TabIndex, 0);
            Add(rows, "Draggable", YesNo(element.Draggable), "no");
            // Which events are actually wired is the answer to "why doesn't my
            // click fire" — the handler list is the whole reason to look here.
            var handlers = element.HandlerEventNames.ToArray();
            if (handlers.Length > 0) rows.Add(new("Handlers", string.Join(", ", handlers)));
        });

        if (element.Role != Accessibility.AccessibilityRole.None
            || element.AccessibleName is not null || element.AccessibleDescription is not null
            || element.AccessibleExpanded is not null || element.AccessibleSelected is not null)
        {
            Group(groups, "Accessibility", rows =>
            {
                Add(rows, "Role", element.Role, Accessibility.AccessibilityRole.None);
                Add(rows, "Name", element.AccessibleName);
                Add(rows, "Description", element.AccessibleDescription);
                if (element.AccessibleExpanded is { } expanded) rows.Add(new("Expanded", YesNo(expanded)));
                if (element.AccessibleSelected is { } selected) rows.Add(new("Selected", YesNo(selected)));
            });
        }

        return groups;
    }

    /// <summary>Renders <paramref name="element"/>'s properties as plain text (clipboard / logs).</summary>
    public static string ToText(Element element)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(Summarize(element)).Append('\n');
        foreach (var group in Describe(element))
        {
            sb.Append('\n').Append(group.Name).Append('\n');
            foreach (var row in group.Rows)
                sb.Append("  ").Append(row.Name).Append(": ").Append(row.Value).Append('\n');
        }
        return sb.ToString();
    }

    // ──── Formatting ────

    public static string Format(Dimension d) => d.Unit switch
    {
        DimensionUnit.Auto => "auto",
        DimensionUnit.None => "none",
        DimensionUnit.Percent => $"{d.Value:0.##}%",
        _ => $"{d.Value:0.##}px"
    };

    public static string Format(Thickness t) =>
        t.Top == t.Right && t.Right == t.Bottom && t.Bottom == t.Left
            ? $"{t.Top:0.##}"
            : $"{t.Top:0.##} {t.Right:0.##} {t.Bottom:0.##} {t.Left:0.##}";

    public static string Format(CornerRadius r) =>
        r.IsUniform ? $"{r.TopLeft:0.##}"
                    : $"{r.TopLeft:0.##} {r.TopRight:0.##} {r.BottomRight:0.##} {r.BottomLeft:0.##}";

    public static string Format(Color c) =>
        c.A == 0 ? "transparent"
        : c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
        : $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";

    public static string Format(Transform2D t)
    {
        var parts = new List<string>(3);
        if (t.RotationDegrees != 0) parts.Add($"rotate {t.RotationDegrees:0.##}°");
        if (t.ScaleX != 1 || t.ScaleY != 1) parts.Add($"scale {t.ScaleX:0.##}, {t.ScaleY:0.##}");
        if (t.TranslateX != 0 || t.TranslateY != 0) parts.Add($"translate {t.TranslateX:0.##}, {t.TranslateY:0.##}");
        return parts.Count == 0 ? "identity" : string.Join("  ", parts);
    }

    // ──── Helpers ────

    private static void Group(List<PropertyGroup> groups, string name, Action<List<PropertyRow>> fill)
    {
        var rows = new List<PropertyRow>();
        fill(rows);
        if (rows.Count > 0) groups.Add(new PropertyGroup(name, rows));
    }

    private static void Add(List<PropertyRow> rows, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value)) rows.Add(new PropertyRow(name, value));
    }

    private static void Add(List<PropertyRow> rows, string name, string value, string omitWhen)
    {
        if (!string.Equals(value, omitWhen, StringComparison.Ordinal)) rows.Add(new PropertyRow(name, value));
    }

    // Enums don't implement IEquatable<T>, so the comparison goes through the
    // default comparer rather than a constrained call.
    private static void Add<T>(List<PropertyRow> rows, string name, T value, T omitWhen) where T : struct
    {
        if (!EqualityComparer<T>.Default.Equals(value, omitWhen))
            rows.Add(new PropertyRow(name, value.ToString() ?? ""));
    }

    private static void Add(List<PropertyRow> rows, string name, float value, float omitWhen)
    {
        if (value != omitWhen) rows.Add(new PropertyRow(name, value.ToString("0.##")));
    }

    private static string YesNo(bool value) => value ? "yes" : "no";

    /// <summary>
    /// Component hosts key themselves with a private "\0CH:…" sentinel so the
    /// reconciler never patches across components. That is machinery, not authored
    /// state, so it is hidden rather than shown as a mysterious key.
    /// </summary>
    private static string? VisibleKey(string? key) =>
        key is not null && key.StartsWith('\0') ? null : key;

    private static string Elide(string text, int max)
    {
        var flat = text.ReplaceLineEndings("↵");
        return flat.Length <= max ? flat : flat[..max] + "…";
    }
}
