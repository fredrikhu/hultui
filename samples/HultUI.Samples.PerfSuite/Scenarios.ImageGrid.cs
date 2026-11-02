using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Rendering.Skia;
using SkiaSharp;

namespace PerfSuite;

static partial class Scenarios
{
    /// <summary>A walk in an <see cref="ImageGridPane"/>: <paramref name="turn"/> presses of
    /// <paramref name="key"/>, as many of <paramref name="back"/>, and again. The walk starts in the
    /// grid's second column, so Right stays on one row of cards, and Down goes past the rows on
    /// screen. The row reports the rows of cards made a step; with <paramref name="keepCards"/> a
    /// row made again hands back its unchanged cards, and it reports the cards made.</summary>
    internal static Op ImageGridWalk(Ctx ctx, KeyCode key, KeyCode back, int turn, bool keepCards = false)
    {
        var pane = new ImageGridPane(ImageGridFilms(600), keepCards);
        var host = pane.Mount();
        host.On<KeyDownEvent>(e =>
        {
            pane.Press(e.Key);
            e.StopPropagation = true;
        });
        ctx.Mount(host);
        pane.Press(KeyCode.Right);
        ctx.Pump(2);
        int cursor = pane.Cursor;
        int step = key == KeyCode.Right ? 1 : ImageGridPane.PerRow;
        int madeBefore = 0;
        return new Op
        {
            Run = i =>
            {
                madeBefore = keepCards ? pane.CardsMade : pane.RowsMade;
                ctx.Window.PressKey(i / turn % 2 == 0 ? key : back);
            },
            Figure = () => (keepCards ? pane.CardsMade : pane.RowsMade) - madeBefore,
            FigureUnit = keepCards ? "cards made" : "rows made",
            Check = i =>
            {
                int steps = (i + 1) % (2 * turn), there = steps <= turn ? steps : 2 * turn - steps;
                return pane.Verify(cursor + there * step);
            },
        };
    }

    private static readonly string[] ImageGridGenres = ["Drama", "Comedy", "Thriller", "Science fiction", "Western"];

    private static List<ImageGridFilm> ImageGridFilms(int count)
    {
        var films = new List<ImageGridFilm>(count);
        for (int i = 0; i < count; i++)
            films.Add(new ImageGridFilm($"f{i:000}", $"Film number {i:000}",
                $"{1950 + i % 70} · {80 + i % 60} min · {ImageGridGenres[i % ImageGridGenres.Length]}",
                i % 7 == 2, i % 9 == 4 ? 35 : 0));
        return films;
    }

    /// <summary>Posters, each its own picture in its own colour, as a server's artwork comes back;
    /// made once for every run.</summary>
    internal static readonly Lazy<IImageSource[]> ImageGridPosters = new(() =>
    {
        var posters = new IImageSource[24];
        for (int i = 0; i < posters.Length; i++)
        {
            var bitmap = new SKBitmap(168, 252);
            bitmap.Erase(new SKColor((byte)(i * 10), (byte)(240 - i * 9), (byte)(i * 53 % 256)));
            posters[i] = new SkiaImageSource(bitmap);
        }
        return posters;
    });
}

/// <summary>One card of <see cref="ImageGridPane"/>: a title, a line under it, a watched tick and
/// a part-watched sliver on the picture.</summary>
sealed record ImageGridFilm(string Id, string Title, string Line, bool Watched, int Percent);

/// <summary>
/// The shape of Aperture's library: a bar over a <see cref="VirtualListElement{T}"/> whose item is
/// a row of cards, every card a focusable button of its own (<c>RowsAreComposite</c>), the cursor
/// a ring on a card. The owner's Build reads the cursor, and the list keeps a row while its item,
/// its place and the column the cursor is in there, is equal; a row a key changes is made again
/// with every card on it, each with its picture, its caption and its badges. With
/// <paramref name="keepCards"/> the row is keyed by its place and the cards by their films, and a
/// row made again hands back each card whose cursor did not change (REACTIVITY.md, "Hand back the
/// elements a key does not change"), as the app's grid does.
/// </summary>
sealed class ImageGridPane(List<ImageGridFilm> films, bool keepCards = false) : Component
{
    private const float CardWidth = 168f, CardHeight = 252f, Caption = 46f, Gap = 16f, Pad = 24f;
    private const float Stride = CardHeight + Caption + Gap;

    private static readonly Color Ground = new(21, 21, 24), Raised = new(38, 38, 43), Text1 = new(232, 228, 220),
        Text2 = new(150, 146, 140), Accent = new(240, 170, 60), Line = new(52, 52, 58);

    private readonly Signal<int> _cursor = new(0);
    private readonly Ref<VirtualListElement<CardRow>> _list = new();
    private readonly Ref<BoxElement> _cursorCard = new();

    /// <summary>A row as the list sees it: its place, and the column the cursor is in there.</summary>
    private readonly record struct CardRow(int Row, int CursorColumn);

    /// <summary>The rows as the list's items, each made when the list asks for it.</summary>
    private sealed class CardRows(int count, int perRow, int cursor) : IReadOnlyList<CardRow>
    {
        public int Count => count;

        public CardRow this[int index] => new(index, cursor / perRow == index ? cursor % perRow : -1);

        public IEnumerator<CardRow> GetEnumerator()
        {
            for (int i = 0; i < count; i++) yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>How many cards fit across at the scenario's window, 1440 wide.</summary>
    public const int PerRow = 7;

    /// <summary>The cards made so far by place, with the cursor on them, and the live element each is.</summary>
    private readonly Dictionary<int, (bool Cursor, Ref<BoxElement> Live)> _kept = [];

    /// <summary>Rows the template has made.</summary>
    public int RowsMade { get; private set; }

    /// <summary>Cards the template has made.</summary>
    public int CardsMade { get; private set; }

    public int Cursor => _cursor.Peek();

    public void Press(KeyCode key)
    {
        int move = key switch
        {
            KeyCode.Right => 1,
            KeyCode.Left => -1,
            KeyCode.Down => PerRow,
            KeyCode.Up => -PerRow,
            _ => 0,
        };
        if (move != 0) _cursor.Value = Math.Clamp(_cursor.Peek() + move, 0, films.Count - 1);
    }

    /// <summary>Why the grid does not show <paramref name="cursor"/> as the cursor, if it does not.</summary>
    public string? Verify(int cursor)
    {
        if (_cursor.Peek() != cursor) return $"the cursor is on {_cursor.Peek()}, not {cursor}";
        var id = "card-" + films[cursor].Id;
        var list = _list.Current;
        var ringed = list?.Children.SelectMany(r => r.Children).OfType<BoxElement>().Where(c => c.Outline.Width > 0).ToList();
        if (ringed is not { Count: 1 }) return $"{ringed?.Count ?? 0} cards ringed";
        return ringed[0].AutomationId == id ? null : $"{ringed[0].AutomationId} is ringed, not {id}";
    }

    public override Element Build()
    {
        int cursor = _cursor.Value;
        int rows = (films.Count + PerRow - 1) / PerRow;
        if (_list.Current is { } mounted) mounted.ScrollRangeIntoView(cursor / PerRow * Stride, Stride);

        var bar = Flex.Row(gap: 12).FlexShrink(0).Height(56).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Pad, 0, Pad))
            .BorderColor(Line).BorderWidth(new Thickness(0, 0, 1, 0))
            .Children(
                Text.New("Films").FontSize(18f).FontWeight(FontWeight.SemiBold).TextColor(Text1),
                Text.New($"{cursor + 1} of {films.Count}").FontSize(11.5f).TextColor(Text2));

        var list = VirtualListElement<CardRow>.New();
        list.RowsAreComposite = true;
        return Flex.Column(gap: 0).FlexGrow(1).Background(Ground).Children(bar,
            list.ItemHeight((int)Stride)
                .Items(new CardRows(rows, PerRow, cursor))
                .RenderWith((r, _) => MakeRow(r))
                .WithRef(_list)
                .Padding(new Thickness(0, Pad, Pad, Pad))
                .AutomationId("grid")
                .FlexGrow(1));
    }

    private Element MakeRow(CardRow r)
    {
        RowsMade++;
        var row = Flex.Row(gap: Gap).AlignItems(AlignItems.FlexStart).Height(CardHeight + Caption)
            .Padding(new Thickness(0, 0, Gap, 0));
        if (keepCards) row.Key = "row" + r.Row;
        for (int k = 0; k < PerRow; k++)
        {
            int i = r.Row * PerRow + k;
            if (i >= films.Count) break;
            bool cursor = k == r.CursorColumn;
            if (keepCards && _kept.TryGetValue(i, out var kept) && kept.Cursor == cursor
                && kept.Live.Current is { Parent: { } parent } live && parent.Key == row.Key
                && ReferenceEquals(parent.Parent, _list.Current))
            {
                row.AddChild(live);
                continue;
            }
            CardsMade++;
            var card = Card(films[i], i, cursor);
            if (keepCards)
            {
                var made = new Ref<BoxElement>();
                card.Key = films[i].Id;
                card.WithRef(made);
                _kept[i] = (cursor, made);
            }
            row.AddChild(card);
        }
        return row;
    }

    private BoxElement Card(ImageGridFilm film, int index, bool cursor)
    {
        var card = Flex.Column(gap: 8).Width(CardWidth).FlexShrink(0).CornerRadius(8)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName($"{film.Title}, {film.Line}")
            .AutomationId("card-" + film.Id)
            .Tooltip(film.Title);
        card.IsFocusable = true;
        card.ContextMenu(() => [new ContextMenuItem { Label = "Open" }]);
        if (cursor)
        {
            card.WithRef(_cursorCard);
            card.Outline(2f, Accent, offset: 3f);
        }
        card.AddChild(Artwork(film, index));
        var caption = Flex.Column(gap: 1).Width(CardWidth);
        caption.AddChild(Text.New(film.Title).FontSize(12.5f).TextColor(Text1).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        caption.AddChild(Text.New(film.Line).FontSize(11.5f).TextColor(Text2).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        card.AddChild(caption);
        card.OnClick(_ => _cursor.Value = index);
        return card;
    }

    private static BoxElement Artwork(ImageGridFilm film, int index)
    {
        var box = Box.New().Width(CardWidth).Height(CardHeight).FlexShrink(0).CornerRadius(8).Background(Raised)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        box.Box.OverflowX = Overflow.Hidden;
        box.Box.OverflowY = Overflow.Hidden;

        var posters = Scenarios.ImageGridPosters.Value;
        var picture = Image.New(posters[index % posters.Length]);
        picture.Fit = ImageFit.Cover;
        box.AddChild(picture.Size(Dimension.Percent(100), Dimension.Percent(100)));

        if (film.Watched)
        {
            var tick = Box.New().Width(22).Height(22).CornerRadius(11f).Background(Accent);
            tick.Position.Type = PositionType.Absolute;
            tick.Position.Right = Dimension.Px(8);
            tick.Position.Top = Dimension.Px(8);
            box.AddChild(tick);
        }
        if (film.Percent > 0)
        {
            var track = Box.New().Width(CardWidth).Height(3f).Background(Line);
            track.AddChild(Box.New().Width(CardWidth * film.Percent / 100f).Height(3f).Background(Accent));
            track.Position.Type = PositionType.Absolute;
            track.Position.Left = Dimension.Px(0);
            track.Position.Bottom = Dimension.Px(0);
            box.AddChild(track);
        }
        return box;
    }
}
