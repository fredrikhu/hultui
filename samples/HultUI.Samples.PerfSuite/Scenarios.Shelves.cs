using HultUI.Core.Accessibility;
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
    /// <summary>A cursor walk over a <see cref="ShelfPane"/>: <paramref name="turn"/> presses of
    /// <paramref name="key"/>, as many of <paramref name="back"/>, and again. Right runs along a shelf
    /// and on into the next; Down goes to the card below, on the next shelf. The row reports the
    /// cards made a key.</summary>
    internal static Op ShelfWalk(Ctx ctx, KeyCode key, KeyCode back, int turn, bool keepCards = false, bool moveRing = false)
    {
        var pane = new ShelfPane(ImageGridFilms(ShelfPane.Shelves * ShelfPane.PerShelf), keepCards || moveRing) { MoveRing = moveRing };
        var host = pane.Mount();
        host.On<KeyDownEvent>(e =>
        {
            pane.Press(e.Key);
            e.StopPropagation = true;
        });
        ctx.Mount(host);
        pane.Press(KeyCode.Right);   // the first press lands on the first card
        ctx.Pump(2);
        int step = key == KeyCode.Right ? 1 : ShelfPane.PerShelf;
        int madeBefore = 0;
        return new Op
        {
            Run = i =>
            {
                madeBefore = pane.CardsMade;
                ctx.Window.PressKey(i / turn % 2 == 0 ? key : back);
            },
            Figure = () => pane.CardsMade - madeBefore,
            FigureUnit = "cards made",
            Check = i =>
            {
                int steps = (i + 1) % (2 * turn), there = steps <= turn ? steps : 2 * turn - steps;
                return pane.Verify(there * step, ctx.Window.Window.FocusedElement);
            },
        };
    }

    private static IEnumerable<Scenario> Shelves()
    {
        yield return new Scenario("component/shelf walk Right, 5 shelves of 7 cards, every card made",
            "A cursor walks along five shelves of seven poster cards in a scrolled page (Aperture's Home) and on from one shelf into the next; the page's Build makes every card of every shelf again on each move, as Aperture's Home did until plan 17's round 7",
            ctx => ShelfWalk(ctx, KeyCode.Right, KeyCode.Left, 2 * ShelfPane.PerShelf)) { Width = 1440, Height = 900 };

        yield return new Scenario("component/shelf walk Down, 5 shelves of 7 cards, every card made",
            "The same page, the cursor moving down a column from shelf to shelf, the page scrolling to follow the focus",
            ctx => ShelfWalk(ctx, KeyCode.Down, KeyCode.Up, ShelfPane.Shelves - 1)) { Width = 1440, Height = 900 };

        yield return new Scenario("component/shelf walk Right, 5 shelves of 7 cards, cards kept",
            "The same walk with the cards in a KeptElements: the page's Build hands back each card whose film and cursor are what it was made with, and makes the two the cursor moved between",
            ctx => ShelfWalk(ctx, KeyCode.Right, KeyCode.Left, 2 * ShelfPane.PerShelf, keepCards: true)) { Width = 1440, Height = 900 };

        yield return new Scenario("component/shelf walk Down, 5 shelves of 7 cards, cards kept",
            "The Down walk with the cards kept",
            ctx => ShelfWalk(ctx, KeyCode.Down, KeyCode.Up, ShelfPane.Shelves - 1, keepCards: true)) { Width = 1440, Height = 900 };

        yield return new Scenario("component/shelf walk Right, 5 shelves of 7 cards, ring moved without a build",
            "The same walk with no build: a move takes the ring off one live card and puts it on the next, which takes the keyboard (KeptElements.Live), as Aperture's Home does since plan 17's round 7",
            ctx => ShelfWalk(ctx, KeyCode.Right, KeyCode.Left, 2 * ShelfPane.PerShelf, moveRing: true)) { Width = 1440, Height = 900 };

        yield return new Scenario("component/shelf walk Down, 5 shelves of 7 cards, ring moved without a build",
            "The Down walk with no build, the page scrolling to follow the focus",
            ctx => ShelfWalk(ctx, KeyCode.Down, KeyCode.Up, ShelfPane.Shelves - 1, moveRing: true)) { Width = 1440, Height = 900 };
    }
}

/// <summary>
/// The shape of Aperture's Home: a scrolled page of shelves, each a heading and a row of poster cards,
/// every card a focusable button, the cursor a ring on one card and the keyboard on it. The cursor walks
/// one list made of every shelf in order; the page's Build reads it, and makes every shelf and card.
/// With <paramref name="keepCards"/> the cards go through a <see cref="KeptElements{TState}"/>, keyed
/// by film: a build hands back each card whose film and cursor did not change.
/// </summary>
sealed class ShelfPane(List<ImageGridFilm> films, bool keepCards = false) : Component
{
    private readonly record struct CardState(ImageGridFilm Film, int Index, bool Cursor);

    private readonly KeptElements<CardState> _kept = new();
    private int _builds;

    public const int Shelves = 5, PerShelf = 7;
    private const float CardWidth = 168f, CardHeight = 252f, Gap = 16f, Pad = 24f;

    private static readonly Color Ground = new(21, 21, 24), Raised = new(38, 38, 43), Text1 = new(232, 228, 220),
        Text2 = new(150, 146, 140), Accent = new(240, 170, 60);

    private static readonly Outline Ring = new(2f, Accent, 3f);

    /// <summary>Where the cursor is; a write of <see cref="_revision"/> builds the page.</summary>
    private int _at = -1;
    private readonly Signal<int> _revision = new(0);
    private readonly Ref<BoxElement> _cursorCard = new();

    /// <summary>Cards the page has made.</summary>
    public int CardsMade { get; private set; }

    /// <summary>Whether a move takes the ring from one live card to the other with no build, as
    /// Aperture's Home does (the cards kept, so the page knows them).</summary>
    public bool MoveRing { get; init; }

    public void Press(KeyCode key)
    {
        int move = key switch
        {
            KeyCode.Right => 1,
            KeyCode.Left => -1,
            KeyCode.Down => PerShelf,
            KeyCode.Up => -PerShelf,
            _ => 0,
        };
        if (move == 0) return;
        int was = _at;
        _at = was < 0 ? 0 : Math.Clamp(was + move, 0, films.Count - 1);
        if (MoveRing && _at != was && _kept.Live(films[_at].Id) is BoxElement reached)
        {
            if (was >= 0 && _kept.Live(films[was].Id) is BoxElement left) left.Outline = default;
            reached.Outline = Ring;
            _cursorCard.Current = reached;
            reached.Focus();
            return;
        }
        _revision.Value++;
    }

    /// <summary>Why the page does not show <paramref name="cursor"/> as the cursor, with the keyboard
    /// on its card, if it does not.</summary>
    public string? Verify(int cursor, Element? focused)
    {
        if (_at != cursor) return $"the cursor is on {_at}, not {cursor}";
        var id = "card-" + films[cursor].Id;
        var ringed = new List<BoxElement>();
        void Walk(Element e)
        {
            if (e is BoxElement b && b.Outline.Width > 0) ringed.Add(b);
            foreach (var c in e.Children) Walk(c);
        }
        Walk(Mount());
        if (ringed.Count != 1) return $"{ringed.Count} cards ringed";
        if (ringed[0].AutomationId != id) return $"{ringed[0].AutomationId} is ringed, not {id}";
        return ReferenceEquals(focused, ringed[0]) ? null : $"the keyboard is on {focused?.AutomationId ?? "nothing"}, not {id}";
    }

    public override Element Build()
    {
        _ = _revision.Value;
        int cursor = _at;
        _kept.Begin(++_builds);
        var page = Flex.Column(gap: 32f).Padding(new Thickness(Pad));
        for (int s = 0; s < Shelves; s++)
        {
            var shelf = Flex.Column(gap: 12f).FlexShrink(0).AutomationId("shelf-" + s);
            shelf.AddChild(Flex.Row(gap: 8f).AlignItems(AlignItems.Center).Children(
                Text.New($"Latest in shelf {s + 1}").FontSize(15f).FontWeight(FontWeight.SemiBold).TextColor(Text1),
                Box.New().FlexGrow(1),
                Text.New($"{PerShelf} films").FontSize(11f).TextColor(Text2)));
            var row = Flex.Row(gap: Gap).AlignItems(AlignItems.FlexStart);
            for (int k = 0; k < PerShelf; k++)
            {
                int i = s * PerShelf + k;
                if (!keepCards)
                {
                    row.AddChild(Card(films[i], i, i == cursor));
                    continue;
                }
                var state = new CardState(films[i], i, i == cursor);
                if (_kept.TryKeep(films[i].Id, state, out var kept)) row.AddChild(kept);
                else row.AddChild(_kept.Made(films[i].Id, state, Card(films[i], i, i == cursor)));
            }
            shelf.AddChild(row);
            page.AddChild(shelf);
        }
        return ScrollView.Vertical().FlexGrow(1).Background(Ground).Children(page);
    }

    protected internal override void OnAfterBuild() => _cursorCard.Current?.Focus();

    private BoxElement Card(ImageGridFilm film, int index, bool cursor)
    {
        CardsMade++;
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
            card.Outline = Ring;
        }
        var art = Box.New().Width(CardWidth).Height(CardHeight).FlexShrink(0).CornerRadius(8).Background(Raised)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center);
        art.Box.OverflowX = Overflow.Hidden;
        art.Box.OverflowY = Overflow.Hidden;
        var posters = Scenarios.ImageGridPosters.Value;
        var picture = Image.New(posters[index % posters.Length]);
        picture.Fit = ImageFit.Cover;
        art.AddChild(picture.Size(Dimension.Percent(100), Dimension.Percent(100)));
        if (film.Watched)
        {
            var tick = Box.New().Width(22).Height(22).CornerRadius(11f).Background(Accent);
            tick.Position.Type = PositionType.Absolute;
            tick.Position.Right = Dimension.Px(8);
            tick.Position.Top = Dimension.Px(8);
            art.AddChild(tick);
        }
        card.AddChild(art);
        var caption = Flex.Column(gap: 1).Width(CardWidth);
        caption.AddChild(Text.New(film.Title).FontSize(12.5f).TextColor(Text1).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        caption.AddChild(Text.New(film.Line).FontSize(11.5f).TextColor(Text2).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
        card.AddChild(caption);
        card.OnClick(_ => { _at = index; _revision.Value++; });
        return card;
    }
}
