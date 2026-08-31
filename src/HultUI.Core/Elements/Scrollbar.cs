using HultUI.Core.Animation;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;

namespace HultUI.Core.Elements;

/// <summary>Where a press on the bar landed.</summary>
internal enum ScrollbarHit
{
    None,
    Thumb,
    Track,
}

/// <summary>
/// One scrollbar: its colours, its widths, its hover state, and the arithmetic that turns a
/// scroll offset into a thumb and back.
///
/// <para>Extracted because there were two copies of it. <see cref="ScrollViewElement"/> scrolls
/// child elements and <see cref="TextAreaElement"/> scrolls its own wrapped rows, so they share
/// no base class below <c>BoxElement</c> — and each had grown its own version of the same
/// thumb-length formula, the same track-click centring, and the same drag maths. They stayed in
/// step through the rounded-corner change by luck and a grep. Everything here takes the numbers
/// as arguments (viewport, content length, offset) precisely so it does not care where they
/// came from.</para>
/// </summary>
internal sealed class Scrollbar
{
    /// <summary>Shortest a thumb may get, so a very long document still leaves something to
    /// grab.</summary>
    private const float MinThumbLength = 20f;

    // ──── Appearance ────

    public Color TrackColor = new(128, 128, 128, 100);
    public Color ThumbColor = new(128, 128, 128, 180);

    /// <summary>Track colour while expanded. Null keeps <see cref="TrackColor"/>.</summary>
    public Color? TrackHoverColor;

    public float Width = 6f;

    /// <summary>Negative means "half the drawn width" — a pill. See
    /// <see cref="ScrollViewElement.ScrollbarRadius"/>.</summary>
    public float Radius = -1f;

    // ──── Expand on hover ────

    public bool ExpandOnHover = true;
    public float ExpandedWidth = 12f;
    public float ExpandDuration = 0.12f;

    private float _t;                // 0 at rest, 1 fully expanded
    private TweenHandle _tween;
    private bool _hovered;

    /// <summary>What the pointer has to be within for this bar to expand, and for a press to
    /// count as a press on it. The expanded width even at rest — otherwise a hairline bar is a
    /// hairline target and nothing would ever expand it.</summary>
    public float HitWidth => ExpandOnHover ? Math.Max(Width, ExpandedWidth) : Width;

    public float TrackWidthNow => ExpandOnHover ? Width + (ExpandedWidth - Width) * _t : Width;

    /// <summary>Half the expanded width, the proportion Windows uses (a 12px bar around a 6px
    /// thumb), and never narrower than at rest.</summary>
    public float ThumbWidthNow =>
        ExpandOnHover ? Width + (Math.Max(Width, ExpandedWidth / 2f) - Width) * _t : Width;

    public Color TrackColorNow =>
        TrackHoverColor is { } hover ? Lerp.Color(TrackColor, hover, _t) : TrackColor;

    /// <summary>Starts or reverses the expansion. <paramref name="owner"/> is marked dirty on
    /// every animation step — the bar is painted by its owner, so nothing else would.</summary>
    public void SetHover(bool hovered, Element owner)
    {
        if (hovered == _hovered) return;
        _hovered = hovered;
        if (!ExpandOnHover) return;

        float target = hovered ? 1f : 0f;
        _tween.Cancel();

        if (ExpandDuration <= 0f)
        {
            _t = target;
            owner.MarkPaintDirty();
            return;
        }

        // Scaled by the distance left to travel, so reversing part-way through does not take a
        // full duration to cover the little that remains.
        _tween = Animate.Value(_t, target, v => { _t = v; owner.MarkPaintDirty(); })
            .Duration(ExpandDuration * MathF.Abs(target - _t))
            .Ease(Easing.EaseOutCubic)
            .Start();
    }

    public void CopyFrom(Scrollbar other)
    {
        TrackColor = other.TrackColor;
        ThumbColor = other.ThumbColor;
        TrackHoverColor = other.TrackHoverColor;
        Width = other.Width;
        Radius = other.Radius;
        ExpandOnHover = other.ExpandOnHover;
        ExpandedWidth = other.ExpandedWidth;
        ExpandDuration = other.ExpandDuration;
        // Not _t / _hovered: those belong to the live element and the pointer that is on it,
        // not to the freshly built one being patched in.
    }

    // ──── Geometry ────
    //
    // One axis at a time. `vertical` picks which of the content box's dimensions is the track
    // and which is the bar's thickness; everything else is the same arithmetic either way.

    private static float Viewport(Rect contentBox, bool vertical) =>
        vertical ? contentBox.Height : contentBox.Width;

    private static float TrackStart(Rect contentBox, bool vertical) =>
        vertical ? contentBox.Y : contentBox.X;

    private static float ThumbLength(float viewport, float contentLength) =>
        Math.Max(MinThumbLength, viewport * (viewport / contentLength));

    private static float ThumbStart(Rect contentBox, float contentLength, float offset, bool vertical)
    {
        float viewport = Viewport(contentBox, vertical);
        float thumb = ThumbLength(viewport, contentLength);
        float ratio = Math.Clamp(offset / Math.Max(1, contentLength - viewport), 0, 1);
        return TrackStart(contentBox, vertical) + ratio * (viewport - thumb);
    }

    /// <summary>Whether this bar is drawn at all: a view with nothing to scroll has none.</summary>
    public static bool IsVisible(Rect contentBox, float contentLength, bool vertical) =>
        contentLength > Viewport(contentBox, vertical);

    /// <summary>Whether <paramref name="p"/> is within the bar's reach.</summary>
    public bool Contains(Rect contentBox, float contentLength, Point p, bool vertical)
    {
        if (!IsVisible(contentBox, contentLength, vertical)) return false;

        return vertical
            ? p.X >= contentBox.X + contentBox.Width - HitWidth && p.X <= contentBox.X + contentBox.Width
              && p.Y >= contentBox.Y && p.Y <= contentBox.Y + contentBox.Height
            : p.Y >= contentBox.Y + contentBox.Height - HitWidth && p.Y <= contentBox.Y + contentBox.Height
              && p.X >= contentBox.X && p.X <= contentBox.X + contentBox.Width;
    }

    /// <summary>
    /// Where a press landed. <paramref name="grabOffset"/> is how far into the thumb it was (for
    /// a drag), and <paramref name="trackOffset"/> is the scroll offset a track click jumps to —
    /// the one that centres the thumb under the pointer.
    /// </summary>
    public ScrollbarHit HitTest(
        Rect contentBox, float contentLength, float offset, Point p, bool vertical,
        out float grabOffset, out float trackOffset)
    {
        grabOffset = 0f;
        trackOffset = 0f;
        if (!Contains(contentBox, contentLength, p, vertical)) return ScrollbarHit.None;

        float viewport = Viewport(contentBox, vertical);
        float thumb = ThumbLength(viewport, contentLength);
        float thumbStart = ThumbStart(contentBox, contentLength, offset, vertical);
        float along = vertical ? p.Y : p.X;

        if (along >= thumbStart && along <= thumbStart + thumb)
        {
            grabOffset = along - thumbStart;
            return ScrollbarHit.Thumb;
        }

        float ratio = Math.Clamp(
            (along - TrackStart(contentBox, vertical) - thumb / 2f) / (viewport - thumb), 0, 1);
        trackOffset = ratio * (contentLength - viewport);
        return ScrollbarHit.Track;
    }

    /// <summary>The scroll offset a drag has moved the thumb to.</summary>
    public float OffsetFromDrag(
        Rect contentBox, float contentLength, Point p, float grabOffset, bool vertical)
    {
        float viewport = Viewport(contentBox, vertical);
        float thumb = ThumbLength(viewport, contentLength);
        float thumbStart = (vertical ? p.Y : p.X) - grabOffset;
        float ratio = Math.Clamp(
            (thumbStart - TrackStart(contentBox, vertical)) / (viewport - thumb), 0, 1);
        return ratio * (contentLength - viewport);
    }

    // ──── Paint ────

    public void Paint(IRenderContext ctx, Rect contentBox, float contentLength, float offset, bool vertical)
    {
        if (!IsVisible(contentBox, contentLength, vertical)) return;

        float viewport = Viewport(contentBox, vertical);
        float barW = TrackWidthNow;
        float thumbW = ThumbWidthNow;
        float thumbLen = ThumbLength(viewport, contentLength);
        float thumbPos = ThumbStart(contentBox, contentLength, offset, vertical);

        // Off the drawn width, not the resting one: a radius fixed to the resting width leaves
        // the expanding thumb with blunt ends halfway through the animation.
        var trackRadius = new CornerRadius(Radius < 0 ? barW / 2f : Radius);
        var thumbRadius = new CornerRadius(Radius < 0 ? thumbW / 2f : Radius);

        // The thumb is centred in the track, so it grows outward from the line it rests on.
        float inset = (barW - thumbW) / 2f;

        if (vertical)
        {
            float x = contentBox.X + contentBox.Width - barW;
            ctx.DrawRoundedRect(new Rect(x, contentBox.Y, barW, viewport),
                trackRadius, TrackColorNow, Color.Transparent, default);
            ctx.DrawRoundedRect(new Rect(x + inset, thumbPos, thumbW, thumbLen),
                thumbRadius, ThumbColor, Color.Transparent, default);
        }
        else
        {
            float y = contentBox.Y + contentBox.Height - barW;
            ctx.DrawRoundedRect(new Rect(contentBox.X, y, viewport, barW),
                trackRadius, TrackColorNow, Color.Transparent, default);
            ctx.DrawRoundedRect(new Rect(thumbPos, y + inset, thumbLen, thumbW),
                thumbRadius, ThumbColor, Color.Transparent, default);
        }
    }
}
