using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;

namespace Aperture;

// A shelf's page turned by a finger (plan 20, phase 3). A shelf pages with its two buttons rather
// than scrolling (CardRow says why); on a phone a sideways swipe on it did nothing, on the cards or
// the heading (a Galaxy S25). Now the row follows the finger, through its transform (a repaint, not
// a build), and let go past a quarter of its width, or flicked that way, it slides on and the shelf
// turns a page; short of that, or with no page that way, it springs back. OnPan is touch only: the
// mouse keeps the buttons, and an upright swipe still scrolls the page.
public sealed partial class ApertureApp
{
    /// <summary>How far, as a part of the row's width, a swipe turns the page.</summary>
    internal const float ShelfSwipeFraction = 0.25f;

    /// <summary>A flick this fast (layout units a second) the way it is pulled turns the page however short.</summary>
    internal const float ShelfFlingSpeed = 600f;

    /// <summary>How much of the finger's travel the row follows where there is no page to turn to.</summary>
    private const float ShelfEdgeResistance = 0.35f;

    private const float ShelfSlideSeconds = 0.18f;

    /// <summary>A shelf's swipe, by row id: where its row is drawn, and its slide. The live row is held
    /// only while a finger or a slide moves it, read from the shelf the pan is on: held for good (a ref
    /// in this map), it kept a page alive that a sign-out had let go (KeptPageTests).</summary>
    private sealed class ShelfSwipe
    {
        public Element? Row;
        public float X;
        public bool Dragging;
        public TweenHandle Slide;
    }

    private readonly Dictionary<string, ShelfSwipe> _shelfSwipes = new(StringComparer.Ordinal);

    /// <summary>Gives the shelf <paramref name="rowId"/>, on <paramref name="page"/> of
    /// <paramref name="pages"/>, its swipe: the pan on <paramref name="shelf"/> (heading and cards), the
    /// movement on <paramref name="row"/> (the cards).</summary>
    private void AddShelfSwipe(Element shelf, Element row, string rowId, int page, int pages)
    {
        if (!_shelfSwipes.TryGetValue(rowId, out var swipe)) _shelfSwipes[rowId] = swipe = new ShelfSwipe();
        // A build while the finger holds the row (a picture landed) keeps it where the finger has it.
        if (swipe.X != 0) row.Transform = Transform2D.Translate(swipe.X, 0);
        shelf.OnPan(PanAxis.Horizontal,
            e =>
            {
                swipe.Slide.Cancel();
                swipe.Dragging = true;
                // The live shelf the handler runs on; its cards are its last child.
                var children = e.CurrentTarget.Children;
                swipe.Row = children.Count > 0 ? children[children.Count - 1] : null;
            },
            e =>
            {
                if (!swipe.Dragging) return;
                float x = e.Total.X;
                // Pulled towards a page there is not: the row comes only part of the way.
                if ((x < 0 && page >= pages - 1) || (x > 0 && page <= 0)) x *= ShelfEdgeResistance;
                SetShelfX(swipe, x);
            },
            e =>
            {
                if (!swipe.Dragging) return;
                swipe.Dragging = false;
                float width = Math.Max(1f, swipe.Row?.Layout.AbsoluteBorderBox.Width ?? 0f);
                float x = swipe.X, vx = e.Velocity.X;
                int toward = x < 0 ? 1 : -1;   // pulled left shows the next page
                bool turns = !e.Cancelled && x != 0
                    && page + toward >= 0 && page + toward < pages
                    && (Math.Abs(x) >= width * ShelfSwipeFraction
                        || (Math.Abs(vx) >= ShelfFlingSpeed && Math.Sign(vx) == Math.Sign(x)));
                if (!turns)
                {
                    swipe.Slide = Animate.Value(x, 0f, v => SetShelfX(swipe, v))
                        .Duration(ShelfSlideSeconds).Ease(Easing.EaseOutQuad)
                        .OnComplete(() => swipe.Row = null)
                        .Start();
                    return;
                }
                swipe.Slide = Animate.Value(x, -toward * width, v => SetShelfX(swipe, v))
                    .Duration(ShelfSlideSeconds).Ease(Easing.EaseOutQuad)
                    .OnComplete(() =>
                    {
                        SetShelfX(swipe, 0f);
                        swipe.Row = null;
                        TurnRowTo(rowId, page + toward);
                    })
                    .Start();
            });
    }

    private static void SetShelfX(ShelfSwipe swipe, float x)
    {
        swipe.X = x;
        if (swipe.Row is { } row) row.Transform = x == 0 ? null : Transform2D.Translate(x, 0);
    }
}
