using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="KeptElements{TState}"/>: a build hands back the live element of each card it would
/// make from the same state, and the reconciler puts it back untouched. A card is made again when
/// its state moves, never handed back once a build has dropped it, and, with a place, only into a
/// parent keyed so. Each test fails with its rule taken out of the helper.
/// </summary>
public class KeptElementsTests
{
    private readonly record struct CardState(string Title, bool Cursor);

    /// <summary>Two shelves of cards, every card through the helper; <see cref="Shown"/> says which.</summary>
    private sealed class Shelves : Component
    {
        public readonly KeptElements<CardState> Cards = new();
        public int Cursor = -1;
        public string Suffix = "";
        public Func<int, bool> Shown = _ => true;
        public int Made;
        public bool Placed, KeyBySuffix;
        public int PerShelf = 4;
        /// <summary>Another view in the same shape, made without the helper: its cards, keyed as the
        /// shelves' are, are patched onto the shelves' live ones.</summary>
        public bool Other;
        private int _builds;

        public int Builds => _builds;

        public void Rebuild() => MarkDirty();

        public override Element Build()
        {
            _builds++;
            if (!Other) Cards.Begin(_builds);
            var page = Flex.Column(gap: 8);
            for (int s = 0; s < 2; s++)
            {
                var row = Flex.Row(gap: 4);
                row.Key = "shelf" + s;
                for (int k = 0; k < PerShelf; k++)
                {
                    int i = s * PerShelf + k;
                    if (!Shown(i)) continue;
                    if (Other)
                    {
                        var other = Box.New().Width(Dimension.Px(60)).Height(Dimension.Px(40)).AccessibleName($"other {i}");
                        other.Key = "c" + i;
                        row.AddChild(other);
                        continue;
                    }
                    var state = new CardState($"card {i}{Suffix}", i == Cursor);
                    string key = KeyBySuffix ? "c" + i + Suffix : "c" + i;
                    if (Cards.TryKeep(key, state, out var kept, Placed ? row.Key : null)) { row.AddChild(kept); continue; }
                    Made++;
                    var card = Box.New().Width(Dimension.Px(60)).Height(Dimension.Px(40)).AccessibleName(state.Title);
                    if (state.Cursor) card.Outline(2f, new Color(240, 170, 60), offset: 3f);
                    card.AddChild(TextB.New(state.Title).FontSize(11f));
                    row.AddChild(Cards.Made(key, state, card));
                }
                page.AddChild(row);
            }
            return page;
        }
    }

    private static (HeadlessSession Session, Shelves Shelves) Host()
    {
        var session = HeadlessSession.Create(600, 400);
        var shelves = new Shelves();
        session.Application.SetRoot(shelves.Mount());
        session.PumpFrames(2);
        return (session, shelves);
    }

    private static BoxElement Card(Shelves shelves, int i) =>
        (BoxElement)shelves.Mount().Children[0].Children.SelectMany(r => r.Children).Single(c => c.Key == "c" + i);

    private static void Step(HeadlessSession session, Shelves shelves, Action change)
    {
        change();
        shelves.Rebuild();
        session.PumpFrames(1);
    }

    /// <summary>A patch copies what the build gave an element, so a mark no build gave it stays only
    /// on the elements nothing patched.</summary>
    private static void Stamp(Shelves shelves)
    {
        for (int i = 0; i < 8; i++) Card(shelves, i).AccessibleDescription = "untouched";
    }

    [Fact]
    public void AnUnchangedCard_IsHandedBackAsItIs()
    {
        var (session, shelves) = Host();
        using var held = session;
        var before = Enumerable.Range(0, 8).Select(i => Card(shelves, i)).ToList();
        Stamp(shelves);
        int made = shelves.Made;

        Step(session, shelves, () => { });

        Assert.Equal(made, shelves.Made);
        Assert.Equal(before, Enumerable.Range(0, 8).Select(i => Card(shelves, i)));
        Assert.All(before, c => Assert.Equal("untouched", c.AccessibleDescription));
    }

    /// <summary>A cursor move makes the two cards it moved between, patched onto the live ones; the
    /// next build hands back the live card, not the one it was made as.</summary>
    [Fact]
    public void ACardWhoseStateMoved_IsMadeAgain_AndPatchedOntoTheLiveOne()
    {
        var (session, shelves) = Host();
        using var held = session;
        Step(session, shelves, () => shelves.Cursor = 1);
        var before = Enumerable.Range(0, 8).Select(i => Card(shelves, i)).ToList();
        Stamp(shelves);
        int made = shelves.Made;

        Step(session, shelves, () => shelves.Cursor = 5);

        Assert.Equal(made + 2, shelves.Made);
        Assert.Equal(before, Enumerable.Range(0, 8).Select(i => Card(shelves, i)));
        Assert.Equal([1, 5], Enumerable.Range(0, 8).Where(i => Card(shelves, i).AccessibleDescription is null));
        Assert.True(Card(shelves, 5).Outline.Width > 0);
        Assert.False(Card(shelves, 1).Outline.Width > 0);
        Assert.Same(Card(shelves, 5), shelves.Cards.Live("c5"));

        Stamp(shelves);
        Step(session, shelves, () => { });
        Assert.Equal(made + 2, shelves.Made);
        Assert.Equal("untouched", Card(shelves, 5).AccessibleDescription);
    }

    /// <summary>A card a build dropped left the window: when it comes back it is made, though its
    /// state is what it was made with.</summary>
    [Fact]
    public void ACardABuildDropped_IsNotHandedBack()
    {
        var (session, shelves) = Host();
        using var held = session;
        var dropped = Card(shelves, 2);

        Step(session, shelves, () => shelves.Shown = i => i != 2);
        Assert.Null(shelves.Cards.Live("c2"));
        int made = shelves.Made;
        Step(session, shelves, () => shelves.Shown = _ => true);

        Assert.Equal(made + 1, shelves.Made);
        Assert.NotSame(dropped, Card(shelves, 2));
        Assert.Equal("card 2", Card(shelves, 2).AccessibleName);
    }

    /// <summary>A card taken out of the window between builds (as a virtual list lets a row go in a
    /// frame) is made again, though the build before made it.</summary>
    [Fact]
    public void ACardRemovedBetweenBuilds_IsNotHandedBack()
    {
        var (session, shelves) = Host();
        using var held = session;
        var removed = Card(shelves, 6);
        removed.Parent!.RemoveChild(removed);
        session.PumpFrames(1);
        Assert.Null(shelves.Cards.Live("c6"));
        int made = shelves.Made;

        Step(session, shelves, () => { });

        Assert.Equal(made + 1, shelves.Made);
        Assert.NotSame(removed, Card(shelves, 6));
    }

    /// <summary>A build of the owner that made none of the cards (another view, whose elements were
    /// patched onto them): the next build makes every card, though each is still in the window.</summary>
    [Fact]
    public void OnlyTheBuildBefore_HandsBack()
    {
        var (session, shelves) = Host();
        using var held = session;
        var live = Card(shelves, 3);

        Step(session, shelves, () => shelves.Other = true);
        Assert.Same(live, Card(shelves, 3));
        Assert.Equal("other 3", live.AccessibleName);
        int made = shelves.Made;
        Step(session, shelves, () => shelves.Other = false);

        Assert.Equal(made + 8, shelves.Made);
        Assert.Equal("card 3", Card(shelves, 3).AccessibleName);
    }

    /// <summary>With a place, a card is handed back only into a parent keyed so: the same card asked
    /// for under another shelf is made there.</summary>
    [Fact]
    public void WithAPlace_ACardIsHandedBackOnlyUnderAParentKeyedSo()
    {
        var (session, shelves) = Host();
        using var held = session;
        shelves.Placed = true;
        Step(session, shelves, () => { });
        var card = Card(shelves, 1);
        var state = new CardState("card 1", false);
        shelves.Cards.Begin(shelves.Builds + 1);   // as the next build would

        Assert.False(shelves.Cards.TryKeep("c1", state, out _, place: "shelf1"));
        Assert.False(shelves.Cards.TryKeep("c1", state with { Cursor = true }, out _, place: "shelf0"));
        Assert.True(shelves.Cards.TryKeep("c1", state, out var here, place: "shelf0"));
        Assert.Same(card, here);
    }

    /// <summary>A change every card shows and no state holds: <see cref="KeptElements{TState}.Clear"/>
    /// has every card made again.</summary>
    [Fact]
    public void Clear_MakesEveryCardAgain()
    {
        var (session, shelves) = Host();
        using var held = session;
        int made = shelves.Made;

        Step(session, shelves, shelves.Cards.Clear);

        Assert.Equal(made + 8, shelves.Made);
    }

    /// <summary>A build that makes more cards than the helper lets go at keeps every one. The cards a
    /// build makes are not in the window until it is reconciled, and those let go then had no live
    /// element after it, and were made again by the next build, which had changed nothing.</summary>
    [Fact]
    public void ABuildThatMakesManyCards_KeepsEveryOne()
    {
        var session = HeadlessSession.Create(600, 400);
        using var held = session;
        var shelves = new Shelves { PerShelf = 50 };
        session.Application.SetRoot(shelves.Mount());
        session.PumpFrames(2);
        Assert.Equal(100, shelves.Made);
        Assert.All(Enumerable.Range(0, 100), i => Assert.Same(Card(shelves, i), shelves.Cards.Live("c" + i)));

        Step(session, shelves, () => { });

        Assert.Equal(100, shelves.Made);
    }

    /// <summary>Cards that left the window are let go as more are made: a walk through many keeps
    /// about what is on screen.</summary>
    [Fact]
    public void CardsThatLeftTheWindow_AreLetGo()
    {
        var (session, shelves) = Host();
        using var held = session;
        shelves.KeyBySuffix = true;
        for (int round = 0; round < 60; round++)
        {
            int r = round;
            Step(session, shelves, () => shelves.Suffix = " " + r);
        }

        Assert.InRange(shelves.Cards.Count, 8, 64);
    }
}
