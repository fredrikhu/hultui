using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
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

    /// <summary>Whether Tab stops on the sortable headers. On by default — Enter or Space on one
    /// sorts it. Off for a list that owns Tab itself (a two-pane file manager switches panes).</summary>
    public bool HeadersTakeFocus { get; set; } = true;

    /// <summary>
    /// The header row: each visible column's name on the table's grid, the sorted one with its
    /// mark. A press on a sortable header sorts by it (again: the other way); dragging the grip
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
        return header;
    }

    /// <summary>The rows' height, for <see cref="Body"/>.</summary>
    public float RowHeight { get; set; } = 28f;

    /// <summary>
    /// The rows: a virtual list of <paramref name="items"/> in the table's sort, each a
    /// <see cref="Row"/> of <see cref="RowHeight"/>, named for assistive technology by
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
        bool sorted = _sortColumn == column.Id;
        var cell = new HeaderCellElement { Align = column.Align, Sortable = column.IsSortable };
        cell.Role = AccessibilityRole.ColumnHeader;
        cell.AccessibleName = !sorted ? column.Header
            : $"{column.Header}, sorted {(_sortDirection == SortDirection.Ascending ? "ascending" : "descending")}";
        cell.AutomationId = "column-" + column.Id;

        var label = CellText(column.Header, column.Align);
        // The name, not the mark, gives way when the column is narrow.
        label.Flex.FlexGrow = 0;
        var mark = new SortMarkElement { Direction = sorted ? _sortDirection : null };
        // The mark sits after the name — before it in a right-aligned column, so the names of
        // numeric columns stay flush with their numbers.
        if (column.Align == ColumnAlign.End) cell.ReplaceChildren(mark, label);
        else cell.ReplaceChildren(label, mark);

        string id = column.Id;
        if (column.IsSortable)
        {
            cell.IsFocusable = HeadersTakeFocus;
            cell.ShowsFocusVisual = HeadersTakeFocus;
            cell.On<ClickEvent>(e =>
            {
                if (e.Target is GripElement) return;
                SortBy(id);
            });
            cell.On<KeyDownEvent>(e =>
            {
                if (!ReferenceEquals(e.Target, e.CurrentTarget)) return;
                if (e.Key is not (KeyCode.Enter or KeyCode.Space)) return;
                if ((e.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Super | KeyModifiers.Shift)) != 0) return;
                SortBy(id);
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
            if (e.Button != PointerEvent.LeftButton) return;
            e.CapturePointer();
            e.StopPropagation = true;
            _dragColumn = id;
            _dragFrom = e.Position.X;
            _dragWidth = WidthOf(column);
        });
        grip.On<PointerMoveEvent>(e =>
        {
            if (_dragColumn != id) return;
            SetWidth(id, _dragWidth + sign * (e.Position.X - _dragFrom), settled: false);
        });
        grip.On<PointerUpEvent>(e =>
        {
            if (_dragColumn != id) return;
            _dragColumn = null;
            SetWidth(id, _dragWidth + sign * (e.Position.X - _dragFrom), settled: true);
        });
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
    }

    /// <summary>The header row: the grid, its rule underneath, its fill and its text style.</summary>
    internal sealed class HeaderElement : RowElement
    {
        protected internal override void ApplyTheme(Theme theme)
        {
            var t = theme.Table;
            Box.Height = Dimension.Px(t.HeaderHeight);
            BackgroundColor = t.HeaderBackground ?? Color.Transparent;
            BorderColor = t.Divider ?? theme.Border;
            Box.BorderWidth = new Thickness(0, 0, 1, 0);
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

        protected internal override void ApplyTheme(Theme theme)
        {
            var t = theme.Table;
            Box.Padding = t.CellPadding;
            if (t.CellFontSize is { } size && this is not HeaderCellElement) Inherited.FontSize = size;
            Flex.JustifyContent = Align switch
            {
                ColumnAlign.Center => JustifyContent.Center,
                ColumnAlign.End => JustifyContent.FlexEnd,
                _ => JustifyContent.FlexStart,
            };
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

    /// <summary>The ▲ or ▼ by the sorted column's name; takes no room when the column is not sorted.</summary>
    internal sealed class SortMarkElement : Element
    {
        public SortDirection? Direction;
        private Color _color;

        public SortMarkElement() => Flex.FlexShrink = 0;

        protected internal override void ApplyTheme(Theme theme) =>
            _color = theme.Table.SortMark ?? theme.Table.HeaderText ?? theme.Secondary;

        internal override void PatchFrom(Element source)
        {
            base.PatchFrom(source);
            if (source is SortMarkElement mark && mark.Direction != Direction)
            {
                Direction = mark.Direction;
                MarkLayoutDirty();
            }
        }

        internal override Size MeasureContent(float availableWidth, float availableHeight, IMeasureContext ctx) =>
            Direction is null ? new Size(0, 0) : new Size(8, 8);

        public override void Render(IRenderContext ctx)
        {
            if (Direction is not { } direction) return;
            var box = Layout.AbsoluteContentBox;
            float cx = box.X + box.Width / 2f, cy = box.Y + box.Height / 2f;
            float half = 3.5f, rise = direction == SortDirection.Ascending ? -1.75f : 1.75f;
            // A stroked chevron: up for ascending, down for descending.
            var left = new Point(cx - half, cy - rise);
            var tip = new Point(cx, cy + rise);
            var right = new Point(cx + half, cy - rise);
            ctx.DrawLine(left, tip, _color, 1.25f);
            ctx.DrawLine(tip, right, _color, 1.25f);
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

        internal override void PatchFrom(Element source)
        {
            base.PatchFrom(source);
            if (source is GripElement grip) AtStart = grip.AtStart;
        }

        protected internal override void ApplyTheme(Theme theme)
        {
            var t = theme.Table;
            Box.Width = Dimension.Px(t.ResizeGrip);
            // Placed from the cell's content box; out through its padding to the real edge.
            if (AtStart) Position.Left = Dimension.Px(-t.CellPadding.Left);
            else Position.Right = Dimension.Px(-t.CellPadding.Right);
        }
    }
}
