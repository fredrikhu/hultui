using System.Globalization;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.Core.Builders;

public sealed partial class TableView<T>
{
    // The grip being dragged: which column, where the pointer was and how wide the column was
    // when it was grabbed. Held here rather than on the grip, which a rebuild may patch.
    private string? _dragColumn;
    private float _dragFrom;
    private float _dragWidth;

    // The live header row, for announcing the app's answer to a press from the pressed column's cell.
    private readonly Ref<Element> _headerRef = new();

    /// <summary>Whether Tab stops on the sortable headers. On by default — Enter or Space on one
    /// sorts it. Off for a list that owns Tab itself (a two-pane file manager switches panes).</summary>
    public bool HeadersTakeFocus { get; set; } = true;

    /// <summary>
    /// The header row: each visible column's name on the table's grid, the sorted one with its
    /// mark. A press on a sortable header sorts by it (again: the other way; in
    /// <see cref="TableSortMode.External"/> mode, asks the app: <see cref="SortRequested"/>); dragging the grip
    /// at a fixed column's end resizes it, and a double click on the grip puts its own width back;
    /// a right press lists the columns to show or hide.
    /// </summary>
    public Element Header()
    {
        var header = new HeaderElement { Columns = Tracks() };
        var visible = VisibleColumns;
        bool pastFill = false;
        for (int i = 0; i < visible.Count; i++)
        {
            var cell = HeaderCell(visible[i], pastFill);
            cell.Last = i == visible.Count - 1;
            header.AddChild(cell.GridColumn(i));
            pastFill |= visible[i].IsFill;
        }
        header.WithRef(_headerRef);
        return header;
    }

    /// <summary>Tells a reader the sort the app answered a press with, from the pressed column's
    /// header cell (the row, when that column is hidden now).</summary>
    private void AnnounceSort(string columnId)
    {
        if (_headerRef.Current is not { IsDetached: false } row) return;
        Element source = row;
        string automationId = "column-" + columnId;
        foreach (var child in row.Children)
            if (child.AutomationId == automationId) { source = child; break; }
        source.Announce(SortSummary());
    }

    /// <summary>The rows' height, for <see cref="Body"/>.</summary>
    public float RowHeight { get; set; } = 28f;

    /// <summary>
    /// The rows: a virtual list of <paramref name="items"/> in the table's sort — in
    /// <see cref="TableSortMode.External"/> mode, or with <paramref name="sort"/> false, as given —
    /// each a <see cref="Row"/> of <see cref="RowHeight"/>, named for assistive technology by
    /// <see cref="RowLabel"/>. Selection, activation and anything else a row does are the app's
    /// to add to the list it gets back; an app that sorts its own rows passes them sorted and
    /// leaves <see cref="SortColumn"/> to say how.
    /// </summary>
    public VirtualListElement<T> Body(IReadOnlyList<T> items, bool sort = true) =>
        VirtualListElement<T>.New()
            .Items(sort ? Sort(items) : items)
            .RenderWith(Row)
            .AccessibleItemLabel(RowLabel)
            .ItemHeight(RowHeight);

    /// <summary>
    /// One row's cells on the table's grid: each visible column's <see cref="TableColumn{T}.Cell"/>,
    /// or its <see cref="TableColumn{T}.Text"/> on one line. Use as a virtual list's template;
    /// the row's fill, selection and height stay with the list and the app.
    /// </summary>
    public Element Row(T item)
    {
        var row = new RowElement { Columns = Tracks() };
        var visible = VisibleColumns;
        for (int i = 0; i < visible.Count; i++)
        {
            var column = visible[i];
            var cell = new CellElement { Align = column.Align };
            if (column.Cell is { } build)
                cell.AddChild(build(item));
            else
                cell.AddChild(CellText(column.Text?.Invoke(item) ?? "", column.Align));
            row.AddChild(cell.GridColumn(i));
        }
        return row;
    }

    /// <summary>
    /// A row's name for assistive technology: each column's header and value, the way a reader
    /// speaks a table row ("Name a.txt, Size 100"). For a virtual list's
    /// <see cref="VirtualListElement{T}.AccessibleLabel"/>. Columns with only a custom cell are
    /// left out, having no text to give.
    /// </summary>
    public string RowLabel(T item)
    {
        var parts = new List<string>(VisibleColumns.Count);
        foreach (var column in VisibleColumns)
            if (column.Text?.Invoke(item) is { Length: > 0 } text)
                parts.Add(parts.Count == 0 ? text : $"{column.Header} {text}");
        return string.Join(", ", parts);
    }

    private static TextElement CellText(string text, ColumnAlign align)
    {
        var label = TextElement.New(text);
        label.MaxLines = 1;
        label.Overflow = TextOverflow.Ellipsis;
        label.Inherited.TextAlign = AlignOf(align);
        label.Flex.FlexGrow = 1;
        label.Flex.FlexShrink = 1;
        return label;
    }

    private static TextAlign AlignOf(ColumnAlign align) => align switch
    {
        ColumnAlign.Center => TextAlign.Center,
        ColumnAlign.End => TextAlign.End,
        _ => TextAlign.Start,
    };

    /// <param name="pastFill">Whether a fill column comes before this one. The fixed columns
    /// after a fill are pinned to the table's end, so their end edges cannot follow a drag: they
    /// resize from the start edge, the divider they share with the fill.</param>
    private HeaderCellElement HeaderCell(TableColumn<T> column, bool pastFill)
    {
        int rank = RankOf(column.Id);
        bool sortable = CanSortBy(column);
        var cell = new HeaderCellElement { Align = column.Align, Sortable = sortable };
        cell.Role = AccessibilityRole.ColumnHeader;
        cell.AccessibleName = rank < 0 ? column.Header
            : $"{column.Header}, sorted {(_sortKeys[rank].Direction == SortDirection.Ascending ? "ascending" : "descending")}"
              + (_sortKeys.Length > 1 ? $", sort key {rank + 1} of {_sortKeys.Length}" : "");
        cell.AutomationId = "column-" + column.Id;

        var label = CellText(column.Header, column.Align);
        // The name, not the mark, gives way when the column is narrow.
        label.Flex.FlexGrow = 0;
        // The key's place is drawn by the mark, not added as text: a reader would hear "Size 2".
        var mark = new SortMarkElement
        {
            Direction = rank >= 0 ? _sortKeys[rank].Direction : null,
            Priority = rank >= 0 && _sortKeys.Length > 1 ? rank + 1 : 0,
        };
        // The mark sits after the name — before it in a right-aligned column, so the names of
        // numeric columns stay flush with their numbers.
        if (column.Align == ColumnAlign.End) cell.ReplaceChildren(mark, label);
        else cell.ReplaceChildren(label, mark);

        if (sortable)
        {
            cell.IsFocusable = HeadersTakeFocus;
            cell.ShowsFocusVisual = HeadersTakeFocus;
            cell.On<ClickEvent>(e =>
            {
                if (e.Target is GripElement) return;
                Activate(column, e.Modifiers);
            });
            cell.On<KeyDownEvent>(e =>
            {
                if (!ReferenceEquals(e.Target, e.CurrentTarget)) return;
                if (e.Key is not (KeyCode.Enter or KeyCode.Space)) return;
                // No modifiers; Shift too where the app sorts, for its next key.
                var held = e.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Super | KeyModifiers.Shift);
                if (held != KeyModifiers.None && !(held == KeyModifiers.Shift && _sortMode == TableSortMode.External)) return;
                Activate(column, e.Modifiers);
                e.StopPropagation = true;
            });
        }
        cell.On<PointerDownEvent>(e =>
        {
            if (e.Button != PointerEvent.RightButton) return;
            if (cell.OverlayHost is not { } host || !_columns.Exists(c => c.CanHide)) return;
            // The menu goes where the press was, in the window's space.
            var offset = cell.WindowBorderBox.X - cell.ScreenBorderBox.X;
            var offsetY = cell.WindowBorderBox.Y - cell.ScreenBorderBox.Y;
            host.OpenMenu(ColumnMenu(), new Point(e.Position.X + offset, e.Position.Y + offsetY));
            e.StopPropagation = true;
        });

        if (column.Resizable && !column.IsFill)
            cell.AddChild(Grip(column, atStart: pastFill));
        return cell;
    }

    /// <summary>A press on a sortable header: the table sorts by it, or, where the app sorts,
    /// asks it to.</summary>
    private void Activate(TableColumn<T> column, KeyModifiers modifiers)
    {
        if (_sortMode == TableSortMode.Internal) SortBy(column.Id);
        else Request(column, null, modifiers, fromHeader: true);
    }

    /// <summary>Every column that can be hidden, checked when shown.</summary>
    private List<ContextMenuItem> ColumnMenu()
    {
        var items = new List<ContextMenuItem>();
        foreach (var column in _columns)
        {
            if (!column.CanHide) continue;
            string id = column.Id;
            bool visible = IsVisible(column);
            items.Add(new ContextMenuItem
            {
                Label = column.Header,
                IsChecked = visible,
                Action = () => SetVisible(id, !visible),
            });
        }
        return items;
    }

    private GripElement Grip(TableColumn<T> column, bool atStart)
    {
        var grip = new GripElement { AtStart = atStart };
        string id = column.Id;
        // At the start edge, moving right makes the column narrower.
        float sign = atStart ? -1f : 1f;
        grip.On<PointerDownEvent>(e =>
        {
            // A second finger reaches the capture holder too (docs/design/POINTER-IDENTITY.md §2):
            // the edge follows the first.
            if (e.Button != PointerEvent.LeftButton || !e.IsPrimary) return;
            e.CapturePointer();
            e.StopPropagation = true;
            _dragColumn = id;
            _dragFrom = e.Position.X;
            _dragWidth = WidthOf(column);
        });
        grip.On<PointerMoveEvent>(e =>
        {
            if (_dragColumn != id || !e.IsPrimary) return;
            SetWidth(id, _dragWidth + sign * (e.Position.X - _dragFrom), settled: false);
        });
        // A cancel (the system took the press) settles the width it reached, as a release does.
        void Settle(PointerEvent e)
        {
            if (_dragColumn != id || !e.IsPrimary) return;
            _dragColumn = null;
            SetWidth(id, _dragWidth + sign * (e.Position.X - _dragFrom), settled: true);
        }
        grip.On<PointerUpEvent>(Settle);
        grip.On<PointerCancelEvent>(Settle);
        grip.On<DoubleClickEvent>(e =>
        {
            _widths.Remove(id);
            Changed(settled: true);
            e.StopPropagation = true;
        });
        return grip;
    }

    // ──── Elements ────

    /// <summary>A row of cells on the table's grid tracks.</summary>
    internal class RowElement : BoxElement
    {
        public GridTrack[] Columns
        {
            set
            {
                Grid.ColumnTemplate = value;
                Grid.RowTemplate = [GridTrack.Fr(1)];
            }
        }

        public RowElement()
        {
            Flex.FlexShrink = 0;
        }

        // Its tracks are grid styles, compared by the base; it copies nothing else.
        private protected override bool PatchMarksOwnLayout => GetType() == typeof(RowElement);
    }

    /// <summary>The header row: the grid, its rule underneath, its fill and its text style.</summary>
    internal sealed class HeaderElement : RowElement
    {
        // Its height and rule are the theme's, compared on a themed twin.
        private protected override bool PatchMarksOwnLayout => true;
        private protected override bool PatchThemesTwin => true;

        protected internal override void ApplyTheme(Theme theme)
        {
            var t = theme.Table;
            var box = BoxRead;
            var height = Dimension.Px(t.HeaderHeight);
            var rule = new Thickness(0, 0, 1, 0);
            if (box.Height != height || box.BorderWidth != rule)
            {
                Box.Height = height;
                Box.BorderWidth = rule;
                MarkLayoutDirty();
            }
            BackgroundColor = t.HeaderBackground ?? Color.Transparent;
            BorderColor = t.Divider ?? theme.Border;
            Inherited.TextColor = t.HeaderText ?? theme.Secondary;
            Inherited.FontSize = t.HeaderFontSize;
            Inherited.FontWeight = t.HeaderFontWeight;
            Inherited.LetterSpacing = t.HeaderLetterSpacing;
        }
    }

    /// <summary>A cell: the theme's padding, its content centred down the row and placed across
    /// it by the column's alignment.</summary>
    internal class CellElement : BoxElement
    {
        public ColumnAlign Align;

        public CellElement()
        {
            Flex.Direction = FlexDirection.Row;
            Flex.AlignItems = AlignItems.Center;
            Flex.FlexShrink = 1;
            Box.OverflowX = Overflow.Hidden;
        }

        // Its padding and placement are the theme's and its alignment's, compared on a themed twin
        // (which carries the incoming alignment).
        private protected override bool PatchMarksOwnLayout => GetType() == typeof(CellElement);
        private protected override bool PatchThemesTwin => GetType() == typeof(CellElement);

        protected internal override void ApplyTheme(Theme theme)
        {
            var t = theme.Table;
            if (t.CellFontSize is { } size && this is not HeaderCellElement) Inherited.FontSize = size;
            var justify = Align switch
            {
                ColumnAlign.Center => JustifyContent.Center,
                ColumnAlign.End => JustifyContent.FlexEnd,
                _ => JustifyContent.FlexStart,
            };
            if (BoxRead.Padding != t.CellPadding || FlexRead.JustifyContent != justify)
            {
                Box.Padding = t.CellPadding;
                Flex.JustifyContent = justify;
                MarkLayoutDirty();
            }
        }

        internal override void PatchFrom(Element source)
        {
            base.PatchFrom(source);
            if (source is CellElement cell) Align = cell.Align;
        }
    }

    /// <summary>A header cell: a cell that lights under the pointer when it sorts, with a mark
    /// between it and the next.</summary>
    internal sealed class HeaderCellElement : CellElement
    {
        public bool Sortable;
        /// <summary>The last column has no neighbour to be divided from.</summary>
        public bool Last;
        private Color _hover, _divider;
        private bool _dividers;

        public HeaderCellElement()
        {
            Flex.Gap = 4f;
            // No clip: the grip sits in the padding at the end, which a clip to the content box
            // cuts out of reach. The name ellipsizes on its own.
            Box.OverflowX = Overflow.Visible;
            AddHoverStyle(static e => ((HeaderCellElement)e).Lit());
        }

        private void Lit()
        {
            if (Sortable) BackgroundColor = _hover;
        }

        // A cell's geometry, compared on a themed twin; what it copies below is paint and cursor.
        private protected override bool PatchMarksOwnLayout => true;
        private protected override bool PatchThemesTwin => true;

        protected internal override void ApplyTheme(Theme theme)
        {
            base.ApplyTheme(theme);
            var t = theme.Table;
            _hover = t.HeaderHoverBackground ?? theme.SurfaceVariant;
            _divider = t.Divider ?? theme.Border;
            _dividers = t.ColumnDividers;
            Inherited.Cursor = Sortable ? CursorType.Pointer : CursorType.Default;
            BackgroundColor = Color.Transparent;
            RefreshInteractionAfterPatch();
        }

        internal override void PatchFrom(Element source)
        {
            base.PatchFrom(source);
            if (source is HeaderCellElement cell)
            {
                Sortable = cell.Sortable;
                Last = cell.Last;
            }
        }

        public override void Render(IRenderContext ctx)
        {
            base.Render(ctx);
            if (!_dividers || Last) return;
            // A short mark at the cell's end, a third of its height in from top and bottom.
            var box = Layout.AbsoluteBorderBox;
            float inset = box.Height / 3f;
            float x = MathF.Round(box.Right) - 0.5f;
            ctx.DrawLine(new Point(x, box.Y + inset), new Point(x, box.Bottom - inset), _divider, 1f);
        }
    }

    /// <summary>The ▲ or ▼ by the sorted column's name, and the key's place (1, 2, 3) after it
    /// when the rows are sorted by several; takes no room when the column is not sorted.</summary>
    internal sealed class SortMarkElement : Element
    {
        public SortDirection? Direction;
        /// <summary>The key's place, from 1, when there are several keys; 0 draws none.</summary>
        public int Priority;
        private string? _priorityText;
        private Color _color;

        public SortMarkElement() => Flex.FlexShrink = 0;

        // Its patch marks layout when the direction or the place, all it measures, changes.
        private protected override bool PatchMarksOwnLayout => true;

        protected internal override void ApplyTheme(Theme theme) =>
            _color = theme.Table.SortMark ?? theme.Table.HeaderText ?? theme.Secondary;

        internal override void PatchFrom(Element source)
        {
            base.PatchFrom(source);
            if (source is not SortMarkElement mark) return;
            if (mark.Direction != Direction)
            {
                Direction = mark.Direction;
                MarkLayoutDirty();
            }
            if (mark.Priority != Priority)
            {
                Priority = mark.Priority;
                _priorityText = null;
                MarkLayoutDirty();
            }
        }

        internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx)
        {
            if (Direction is null) return new Size(0, 0);
            if (Priority == 0) return new Size(8, 8);
            _priorityText ??= Priority.ToString(CultureInfo.InvariantCulture);
            var t = ctx.MeasureText(_priorityText, ResolvedInherited, float.PositiveInfinity, 1);
            return new Size(8 + 2 + t.Width, Math.Max(8, t.Height));
        }

        public override void Render(IRenderContext ctx)
        {
            if (Direction is not { } direction) return;
            var box = Layout.AbsoluteContentBox;
            // The chevron in the first 8 px: with no place drawn that is the whole box, as before.
            float cx = box.X + 4f, cy = box.Y + box.Height / 2f;
            float half = 3.5f, rise = direction == SortDirection.Ascending ? -1.75f : 1.75f;
            // A stroked chevron: up for ascending, down for descending.
            var left = new Point(cx - half, cy - rise);
            var tip = new Point(cx, cy + rise);
            var right = new Point(cx + half, cy - rise);
            ctx.DrawLine(left, tip, _color, 1.25f);
            ctx.DrawLine(tip, right, _color, 1.25f);
            if (Priority > 0)
            {
                _priorityText ??= Priority.ToString(CultureInfo.InvariantCulture);
                ctx.DrawText(_priorityText, new Rect(box.X + 10, box.Y, box.Width - 10, box.Height),
                    ResolvedInherited, TextOverflow.Clip, 1);
            }
        }
    }

    /// <summary>The grab area at a fixed column's end — or its start, past a fill column —
    /// inside the column: the neighbouring header cell would take a press on any part of it
    /// hanging over the edge.</summary>
    internal sealed class GripElement : BoxElement
    {
        public bool AtStart;

        public GripElement()
        {
            Position.Type = PositionType.Absolute;
            Position.Top = Dimension.Px(0);
            // Top and bottom together do not stretch an absolute box; the height does.
            Box.Height = Dimension.Percent(100);
            Inherited.Cursor = CursorType.ColResize;
        }

        // Its width and place are the theme's and its end's, compared on a themed twin (which
        // carries the incoming end).
        private protected override bool PatchMarksOwnLayout => true;
        private protected override bool PatchThemesTwin => true;

        internal override void PatchFrom(Element source)
        {
            base.PatchFrom(source);
            if (source is GripElement grip) AtStart = grip.AtStart;
        }

        protected internal override void ApplyTheme(Theme theme)
        {
            var t = theme.Table;
            var width = Dimension.Px(t.ResizeGrip);
            var pos = PositionRead;
            // Placed from the cell's content box; out through its padding to the real edge.
            var edge = Dimension.Px(AtStart ? -t.CellPadding.Left : -t.CellPadding.Right);
            if (BoxRead.Width == width && (AtStart ? pos.Left : pos.Right) == edge) return;
            Box.Width = width;
            if (AtStart) Position.Left = edge;
            else Position.Right = edge;
            MarkLayoutDirty();
        }
    }
}
