using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;

namespace PerfSuite;

static partial class Scenarios
{
    /// <summary>Wheel 3 notches through a <see cref="SectionPane"/>: rows come into view, and a
    /// section's header reaches the top every few steps.</summary>
    private static Op SectionWheel(Ctx ctx, bool held)
    {
        var pane = new SectionPane(ctx.Rng, held);
        ctx.Mount(pane.Mount());
        var list = pane.List!;
        var moved = ScrolledTo(() => list.ScrollOffsetY);
        return new Op
        {
            Run = step => ctx.Window.Wheel(500, 380, 0, WheelDirection(step) * 3),
            Check = step => moved(step) ?? pane.VerifyHeld(),
        };
    }

    /// <summary>A key walk down a <see cref="SectionPane"/>, as <see cref="SelectionWalk"/> walks
    /// its list: the selection a signal the pane's Build reads, a press rebuilding the pane and
    /// patching its list, every press scrolling a row, and a section's header met every few
    /// presses. The row reports the rows made per step.</summary>
    private static Op SectionWalk(Ctx ctx, bool held)
    {
        var pane = new SectionPane(ctx.Rng, held);
        var host = pane.Mount();
        host.On<KeyDownEvent>(e =>
        {
            if (e.Key == KeyCode.Down) { pane.Move(1); e.StopPropagation = true; }
        });
        ctx.Mount(host);
        pane.Select(pane.LastWholeRow());
        ctx.Session.PumpFrame();
        int first = pane.Selected;
        int madeBefore = 0;
        return new Op
        {
            Run = _ =>
            {
                madeBefore = pane.RowsMade;
                ctx.Window.PressKey(KeyCode.Down);
            },
            Figure = () => pane.RowsMade - madeBefore,
            FigureUnit = "rows made",
            Check = step => pane.Selected == first + step + 1 ? pane.VerifyHeld() : $"the selection is on row {pane.Selected}",
        };
    }

    /// <summary>One row of a <see cref="SectionPane"/>: a section's header, or a row of it.</summary>
    private readonly record struct SectionRow(int Index, bool Header, string Label);

    /// <summary>
    /// One <see cref="VirtualListElement{T}"/> of 1,000 tagged rows on item rows, every ninth the
    /// header of the section under it and a different height (<c>RowHeights</c>), made by one
    /// component whose Build a selection move reruns: the shape of Junction's Changes list.
    /// </summary>
    private sealed class SectionPane(Random rng, bool held) : Component
    {
        private const int Rows = 1_000;
        private const float RowHeight = 30f, HeaderHeight = 26f;

        private readonly SectionRow[] _rows = MakeRows(rng);
        private readonly Ref<VirtualListElement<SectionRow>> _list = new();
        private readonly Signal<int> _selected = new(-1);
        private int _built = -1;
        private bool _reveal;

        public VirtualListElement<SectionRow>? List => _list.Current;

        /// <summary>Rows the list's template has made.</summary>
        public int RowsMade { get; private set; }

        public int Selected => _selected.Value;

        public void Select(int index)
        {
            _reveal = true;
            _selected.Value = Math.Clamp(index, 0, Rows - 1);
        }

        public void Move(int delta) => Select(_selected.Value + delta);

        /// <summary>The last row the list shows whole, scrolled to the top.</summary>
        public int LastWholeRow()
        {
            float height = _list.Current?.Layout.AbsoluteContentBox.Height ?? 0;
            int index = 0;
            while (index + 1 < Rows && Top(index + 1) + HeightOf(index + 1) <= height) index++;
            return index;
        }

        private static bool IsHeader(int index) => index % 9 == 0;

        private static float HeightOf(int index) => IsHeader(index) ? HeaderHeight : RowHeight;

        /// <summary>Where row <paramref name="index"/> starts.</summary>
        private static float Top(int index)
        {
            int headers = (index + 8) / 9;
            return headers * HeaderHeight + (index - headers) * RowHeight;
        }

        public override Element Build()
        {
            int selected = _built = _selected.Value;
            var list = VirtualListElement<SectionRow>.New()
                .ItemHeight(RowHeight)
                .RowHeights(static (item, _) => HeightOf(item.Index))
                .Items(_rows)
                .RenderWith((item, isSelected) => Row(item, isSelected))
                .SelectedIndex(selected)
                .StickyRows(held ? HeaderRow : null);
            return list.FlexGrow(1).WithRef(_list);
        }

        private static readonly Func<SectionRow, int, bool> HeaderRow = static (item, _) => item.Header;

        /// <summary>Null when the header of the first row on screen is drawn at the top, or above it
        /// by as much as the next header has pushed it, on a list that holds its headers.</summary>
        public string? VerifyHeld()
        {
            if (!held) return null;
            var list = _list.Current!;
            float offset = list.ScrollOffsetY;
            int first = 0;
            while (first + 1 < Rows && Top(first + 1) <= offset) first++;
            int header = first / 9 * 9;
            var view = list.WindowContentBox;
            float top = view.Y + MathF.Min(0f, Top(header + 9) - offset - HeaderHeight);
            foreach (var child in list.Children)
                if (child is BoxElement { Children.Count: > 0 } row && row.Position.Type == PositionType.Sticky
                    && Math.Abs(row.WindowBorderBox.Y - top) < 0.5f)
                    return null;
            return $"no held row at {top:0.#} at offset {offset:0.#} (section of row {first})";
        }

        protected internal override void OnAfterBuild()
        {
            if (!_reveal) return;
            _reveal = false;
            _list.Current?.ScrollRangeIntoView(Top(_built), HeightOf(_built));
        }

        private Element Row(SectionRow item, bool selected)
        {
            RowsMade++;
            if (item.Header)
                return Flex.Row(gap: 8).AlignItems(AlignItems.Center).Padding(new Thickness(0, 16))
                    .Background(Color.FromHex("#E0E7FF"))
                    .Children(Text.New(item.Label).FontSize(12f).FontWeight(FontWeight.Bold).FlexGrow(1));
            return Flex.Row(gap: 8).AlignItems(AlignItems.Center).Padding(new Thickness(0, 16))
                .Background(selected ? Ui.Selected : Color.Transparent)
                .Children(
                    Text.New(item.Label).FontSize(13f).FlexGrow(1).FlexShrink(1).MaxLines(1)
                        .TextOverflow(TextOverflow.Ellipsis),
                    Text.New($"+{item.Index % 90}").FontSize(11f).TextColor(Ui.Muted));
        }

        private static SectionRow[] MakeRows(Random rng)
        {
            var rows = new SectionRow[Rows];
            for (int i = 0; i < rows.Length; i++)
                rows[i] = new SectionRow(i, IsHeader(i), IsHeader(i) ? $"SECTION {i / 9 + 1}" : Ui.Label(rng));
            return rows;
        }
    }
}
