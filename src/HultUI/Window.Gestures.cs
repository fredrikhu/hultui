using HultUI.Core.Animation;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;

namespace HultUI;

// Touch gestures (docs/design/GESTURES.md). A touch on a phone is ambiguous when it lands: a tap,
// a scroll in either axis, a slider's drag, a long press. Every candidate on the press's path
// watches the primary finger, and the first whose condition is met claims it: movement past the
// touch slop claims for the innermost scroller (or OnPan element) that moves along the movement's
// axis and can still move that way; holding still for the long-press time claims for the long
// press; lifting first is a tap. The claim cancels the press (PointerCancel, so no click), and the
// finger's later moves are the claimant's. A press that captured the pointer opts out: the element
// owns the finger from the start.
public sealed partial class Window
{
    /// <summary>How long a finger is held still before it is a long press: Android's long-standing
    /// time (recent versions use 400 ms). A constant to tune on a device.</summary>
    internal const float LongPressSeconds = 0.5f;

    /// <summary>Slower than this at the lift, a pan does not fling (Android's minimum), in layout
    /// units a second.</summary>
    internal const float FlingMinVelocity = 50f;

    /// <summary>A fling starts no faster than this.</summary>
    internal const float FlingMaxVelocity = 8000f;

    /// <summary>A fling under this speed has stopped.</summary>
    internal const float FlingStopVelocity = 10f;

    /// <summary>The fling's velocity decays as e^(−t/τ) with this τ: a flick at 2,000 a second
    /// travels about 650, two-thirds of a phone screen.</summary>
    internal const double FlingTimeConstantMs = 325;

    private sealed class TouchGesture
    {
        public long Finger;
        public int PointerId;
        public Point DownAt;
        public Point LastAt;
        public KeyModifiers Modifiers;
        /// <summary>What the press landed on, and the overlay offset its events are measured by.</summary>
        public Element Target = null!;
        public Point Offset;
        /// <summary>The pan candidates on the path, innermost first.</summary>
        public readonly List<(Element Element, PanAxis Axis)> Pans = [];
        public TweenHandle? LongPressTimer;
        /// <summary>The finger landed on a fling and stopped it: its tap clicks nothing.</summary>
        public bool SwallowTap;
        public bool Decided;
        public Element? Claimant;
        public PanAxis Axis;
        public Point Total;
        public bool LongPressed;
        public bool TooltipShown;
        /// <summary>The text field the finger edits as it moves (Window.TouchText.cs): its caret, or
        /// the selection a long press made, a word at a time when <see cref="ExtendsWords"/>.</summary>
        public Element? TextField;
        public bool ExtendsWords;
        public readonly VelocityTracker Velocity = new();
    }

    private sealed class Fling
    {
        public IPanScrollable Scroller = null!;
        public Point Velocity;
        public double LastMs;
        /// <summary>Travel owed to a scroller a rebuild replaced, while its replacement cannot be
        /// found yet (built, not laid out), and how many frames it has been looked for.</summary>
        public Point Owed;
        public int Misses;
        /// <summary>Where the finger lifted: where a scroller a rebuild replaced is looked for.</summary>
        public Point At;
    }

    private TouchGesture? _gesture;
    private Fling? _fling;

    private double NowMs() => Application.Platform.TimeProvider.GetUtcNow().UtcTicks / (double)TimeSpan.TicksPerMillisecond;

    // ──── Press ────

    /// <summary>Before a primary finger's press: a fling under it stops, and the finger that stopped
    /// it is not a tap.</summary>
    private bool StopFlingUnder(Point at)
    {
        if (_fling is not { } fling || _root is null) return false;
        var (overlay, _) = HitTestOverlays(at);
        for (var e = overlay ?? HitTester.HitTest(_root, at); e is not null; e = e.Parent)
        {
            if (!ReferenceEquals(e, fling.Scroller)) continue;
            _fling = null;
            return true;
        }
        return false;
    }

    /// <summary>After a primary finger's press was dispatched: collects the candidates on its path.</summary>
    private void BeginGesture(long finger, int pointerId, Point at, KeyModifiers modifiers, bool swallowTap)
    {
        _gesture = null;
        if (_root is null) return;
        // A press that captured the pointer owns the finger: no scroller can claim it.
        if (_pointerCaptureTarget is not null) return;
        var (overlay, offset) = HitTestOverlays(at);
        var target = overlay ?? HitTester.HitTest(_root, at);
        if (target is null) return;

        var g = new TouchGesture
        {
            Finger = finger, PointerId = pointerId, DownAt = at, LastAt = at, Modifiers = modifiers,
            Target = target, Offset = overlay is null ? default : offset, SwallowTap = swallowTap,
        };
        bool longPressable = false;
        for (var e = target; e is not null; e = e.Parent)
        {
            if (e is IPanScrollable) g.Pans.Add((e, PanAxis.Both));
            else if (e.HasEventHandler<PanUpdateEvent>() && PanAxisOf(e) is { } axis) g.Pans.Add((e, axis));
            longPressable |= e.HasEventHandler<LongPressEvent>() || e.TooltipText is not null;
        }
        longPressable |= FindContextMenuOwner(target).owner is not null;
        g.Velocity.Add(NowMs(), at);
        if (g.Pans.Count == 0 && !longPressable && !swallowTap) return;
        if (longPressable) g.LongPressTimer = Animate.After(LongPressSeconds, () => FireLongPress(g));
        _gesture = g;
    }

    private static PanAxis? PanAxisOf(Element element)
    {
        var query = new PanAxisQuery();
        EventDispatcher.DispatchDirect(query, element);
        return query.Axis;
    }

    // ──── Moves ────

    /// <summary>A primary finger's move. True when the gesture took it: claimed now, or already.</summary>
    private bool GestureMove(long finger, Point at, Point delta)
    {
        if (_gesture is not { } g || g.Finger != finger) return false;
        g.Velocity.Add(NowMs(), at);
        g.LastAt = at;
        if (g.TextField is not null)
        {
            MoveTouchText(g, at);
            return true;
        }
        if (g.LongPressed) return true;
        if (g.Claimant is not null)
        {
            Pan(g, delta);
            return true;
        }
        if (g.Decided || Distance(at, g.DownAt) <= PointerState.TouchSlop) return false;

        // Past the slop: no long press now, whatever claims.
        g.Decided = true;
        CancelLongPressTimer(g);
        return TryClaim(g, at);
    }

    private bool TryClaim(TouchGesture g, Point at)
    {
        float dx = at.X - g.DownAt.X, dy = at.Y - g.DownAt.Y;
        // Sideways on the field that has the focus: its caret follows the finger, as Android's
        // cursor drag from anywhere (from 11), before anything scrolls. It selected, as a mouse's
        // drag does (a Galaxy S25).
        if (Math.Abs(dx) >= Math.Abs(dy) && TouchTextOwnerOf(g.Target) is { } field
            && ReferenceEquals(_focusManager.FocusedElement, field) && IsShownInWindow(field))
        {
            CancelPressForGesture(g);
            g.Claimant = field;
            BeginTouchCaretDrag(g, field);
            MoveTouchText(g, at);
            return true;
        }
        var axis = Math.Abs(dx) > Math.Abs(dy) ? PanAxis.Horizontal : PanAxis.Vertical;
        // The content follows the finger, so it moves against it through the content.
        int direction = axis == PanAxis.Horizontal ? -Math.Sign(dx) : -Math.Sign(dy);
        foreach (var (element, panAxis) in g.Pans)
        {
            if (!IsShownInWindow(element)) continue;
            PanAxis claimed;
            if (element is IPanScrollable scroller)
            {
                if (!scroller.CanPan(axis, direction)) continue;
                // A surface that also scrolls across follows the finger both ways.
                var other = axis == PanAxis.Horizontal ? PanAxis.Vertical : PanAxis.Horizontal;
                claimed = scroller.CanPan(other, 1) || scroller.CanPan(other, -1) ? PanAxis.Both : axis;
            }
            else
            {
                if (panAxis != PanAxis.Both && panAxis != axis) continue;
                claimed = panAxis;
            }

            CancelPressForGesture(g);
            g.Claimant = element;
            g.Axis = claimed;
            var moved = OnAxis(new Point(dx, dy), claimed);
            if (element is IPanScrollable claimedScroller)
            {
                // The content takes up the movement past the slop, as Android's does: it neither
                // jumps by the slop nor lags the finger by the move that claimed it.
                float along = axis == PanAxis.Horizontal ? Math.Abs(dx) : Math.Abs(dy);
                float past = along <= PointerState.TouchSlop ? 0 : (along - PointerState.TouchSlop) / along;
                claimedScroller.PanBy(-moved.X * past, -moved.Y * past);
            }
            else
            {
                g.Total = moved;   // since the press, as PanUpdateEvent.Total counts
                DispatchPan(g, new PanStartEvent { Position = Local(g, at), Button = PointerEvent.LeftButton, Modifiers = g.Modifiers });
            }
            return true;
        }
        return false;
    }

    /// <summary>The press the gesture claimed ends as the system's cancel would: PointerCancel to the
    /// element that took it, no click, hover over. The finger stays the gesture's.</summary>
    private void CancelPressForGesture(TouchGesture g)
    {
        using var scope = PointerContext.Enter(g.PointerId, PointerType.Touch, primary: true, cancelling: true);
        HandleInputEvent(new InputEvent.MouseButtonInput(g.LastAt.X, g.LastAt.Y, PointerEvent.LeftButton, false, g.Modifiers));
        HandleInputEvent(new InputEvent.PointerLeftWindowInput());
    }

    private void Pan(TouchGesture g, Point delta)
    {
        var d = OnAxis(delta, g.Axis);
        if (d.X == 0 && d.Y == 0) return;
        g.Total = new Point(g.Total.X + d.X, g.Total.Y + d.Y);
        if (g.Claimant is IPanScrollable)
        {
            if (LiveScroller(g.Claimant, g.LastAt) is { } scroller)
            {
                g.Claimant = (Element)scroller;
                scroller.PanBy(-d.X, -d.Y);
            }
            return;
        }
        DispatchPan(g, new PanUpdateEvent
        {
            Position = Local(g, g.LastAt), Button = PointerEvent.LeftButton, Modifiers = g.Modifiers, Delta = d, Total = g.Total,
        });
    }

    private void DispatchPan(TouchGesture g, PointerEvent evt)
    {
        if (g.Claimant is not { } claimant) return;
        using var scope = PointerContext.Enter(g.PointerId, PointerType.Touch, primary: true);
        EventDispatcher.DispatchDirect(evt, claimant);
    }

    private static Point OnAxis(Point p, PanAxis axis) => axis switch
    {
        PanAxis.Horizontal => new Point(p.X, 0),
        PanAxis.Vertical => new Point(0, p.Y),
        _ => p,
    };

    private static Point Local(TouchGesture g, Point at) => new(at.X - g.Offset.X, at.Y - g.Offset.Y);

    // ──── Lift ────

    /// <summary>A primary finger lifted or was taken. True when the gesture had claimed it, so there
    /// is no press left to release; false leaves the release to the mouse's path, as a cancel when
    /// the finger stopped a fling (<paramref name="cancel"/> is set then).</summary>
    private bool GestureEnd(long finger, Point at, ref bool cancel)
    {
        if (_gesture is not { } g || g.Finger != finger) return false;
        _gesture = null;
        CancelLongPressTimer(g);
        if (g.TextField is not null)
        {
            EndTouchTextMove(g, cancel);
            return true;
        }
        if (g.LongPressed)
        {
            if (g.TooltipShown) HideTooltip();
            return true;
        }
        if (g.Claimant is null)
        {
            cancel |= g.SwallowTap;
            return false;
        }

        double now = NowMs();
        g.Velocity.Add(now, at);
        var velocity = cancel ? default : OnAxis(g.Velocity.Fit(now), g.Axis);
        if (g.Claimant is IPanScrollable)
        {
            if (!cancel && LiveScroller(g.Claimant, at) is { } scroller) StartFling(scroller, velocity, at, now);
            return true;
        }
        DispatchPan(g, new PanEndEvent
        {
            Position = Local(g, at), Button = PointerEvent.LeftButton, Modifiers = g.Modifiers, Velocity = velocity, Cancelled = cancel,
        });
        return true;
    }

    private static void CancelLongPressTimer(TouchGesture g)
    {
        g.LongPressTimer?.Cancel();
        g.LongPressTimer = null;
    }

    // ──── Long press ────

    /// <summary>
    /// A reader's long press (TalkBack's double tap held; the router's <c>LongPress</c>) on the element
    /// <paramref name="id"/> names, or on a list's item: what a finger's long press does there, but
    /// the parts for the eyes. A long-press handler on the way up runs; else the context menu opens
    /// from the keyboard's place, its first item highlighted, which the reader's focus goes to. A
    /// finger's long press would select a word of text, or show a tooltip: neither is for a reader.
    /// An item is found where its row is drawn: a reader's focus is on an item on screen.
    /// </summary>
    internal void LongPressFromAssistiveTool(ElementId id)
    {
        if (_root is null) return;
        Element? target;
        Point at;
        if (VirtualItemId.TryDecode(id, out var listId, out int index))
        {
            if (FindElement(listId) is not ScrollViewElement list || list is not Core.Accessibility.IAccessibleItemSource source
                || index < 0 || index >= source.ItemCount)
                return;
            var content = list.WindowContentBox;
            var cell = source.ItemRect(index) is { } r ? r
                : source.ItemBounds(index) is { } rows ? new Rect(0, rows.Top, content.Width, rows.Height)
                : new Rect(0, index * source.ItemHeight, content.Width, source.ItemHeight);
            at = new Point(content.X + cell.X - list.ScrollOffsetX + cell.Width / 2,
                content.Y + cell.Y - list.ScrollOffsetY + cell.Height / 2);
            var (overlay, _) = HitTestOverlays(at);
            target = overlay ?? HitTester.HitTest(_root, at);
        }
        else
        {
            target = FindElement(id);
            if (target is null) return;
            var box = target.WindowBorderBox;
            at = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);
        }
        if (target is not { IsEnabledForAccessibility: true }) return;

        for (var e = target; e is not null; e = e.Parent)
        {
            if (!e.HasEventHandler<LongPressEvent>()) continue;
            EventDispatcher.Dispatch(new LongPressEvent { Position = at, Button = PointerEvent.LeftButton }, target);
            _root.MarkPaintDirty();
            return;
        }
        var (owner, itemsProvider) = FindContextMenuOwner(target);
        if (owner is null || itemsProvider is null) return;
        var items = itemsProvider();
        if (items.Count == 0) return;
        ShowContextMenu(items, default, onClosed: null, anchor: target.ContextMenuAnchor);
    }

    /// <summary>
    /// A reader's dismiss (Android's dismiss action; the router's <c>Dismiss</c>) of a dialog or a menu:
    /// an open context menu closes; anything else hears Escape where the keyboard is, as Back's second
    /// step has it, which closes a dialog, a popover, a dropdown's list.
    /// </summary>
    internal void DismissFromAssistiveTool(ElementId id)
    {
        if (_contextMenuElement is { } menu && (menu.Id == id || FindInTree(menu, id) is not null))
        {
            CloseContextMenu(MenuCloseReason.Dismissed);
            return;
        }
        if (FindElement(id) is null) return;
        var target = KeyTarget();
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Escape, TargetTakesText = target is { TakesTypedText: true } }, target);
    }

    private void FireLongPress(TouchGesture g)
    {
        g.LongPressTimer = null;
        if (!ReferenceEquals(_gesture, g) || g.Decided || _root is null) return;
        g.Decided = true;
        g.LongPressed = true;

        // The element held may have been rebuilt away meanwhile: what is under the finger now.
        var target = g.Target;
        if (!IsShownInWindow(target))
        {
            var (overlay, offset) = HitTestOverlays(g.LastAt);
            if ((overlay ?? HitTester.HitTest(_root, g.LastAt)) is not { } now) return;
            target = now;
            g.Offset = overlay is null ? default : offset;
        }
        CancelPressForGesture(g);

        // 1. A handler on the path is the long press's meaning.
        for (var e = target; e is not null; e = e.Parent)
        {
            if (!e.HasEventHandler<LongPressEvent>()) continue;
            HideTouchText();
            using var scope = PointerContext.Enter(g.PointerId, PointerType.Touch, primary: true);
            EventDispatcher.Dispatch(new LongPressEvent { Position = Local(g, g.LastAt), Button = PointerEvent.LeftButton, Modifiers = g.Modifiers }, target);
            _root.MarkPaintDirty();   // the input safety net's rule: a handler ran
            return;
        }
        // Text: the word, its handles and the edit bar, in place of the field's context menu
        // (docs/design/SOFT-KEYBOARD.md §4). Anything else puts away the handles shown.
        if (SelectWordByTouch(target, g.LastAt))
        {
            // The finger, still down, goes on: a word's selection grows from it a word at a time;
            // past the text, the caret follows it.
            if (_touchText is { } field && TouchTextOf(field) is { } edited)
            {
                if (edited.HasTouchSelection)
                {
                    g.TextField = field;
                    g.ExtendsWords = true;
                }
                else BeginTouchCaretDrag(g, field);
            }
            return;
        }
        // 2. The context menu, as a right click opens it: every menu made for the mouse works by touch.
        if (FindContextMenuOwner(target).owner is not null)
        {
            // With a reader on, its focus goes into the menu (ShowContextMenu puts an item current):
            // TalkBack's double tap held arrives here, as a press Android passes on at TalkBack's
            // focus, and the menu, an overlay at the tree's end, was swiped to only past the rest of
            // the page.
            TryShowContextMenu(target, g.LastAt);
            return;
        }
        // 3. The tooltip, while the finger stays down: how Android names an icon button.
        if (FindTooltipOwner(target) is ({ } owner, { } text))
        {
            HideTooltip();
            _tooltipOwner = owner;
            ShowTooltipOverlay(owner, text);
            g.TooltipShown = true;
        }
    }

    // ──── Fling ────

    private void StartFling(IPanScrollable scroller, Point velocity, Point at, double now)
    {
        float speed = MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);
        if (speed < FlingMinVelocity) return;
        if (speed > FlingMaxVelocity)
            velocity = new Point(velocity.X * FlingMaxVelocity / speed, velocity.Y * FlingMaxVelocity / speed);
        _fling = new Fling { Scroller = scroller, Velocity = velocity, LastMs = now, At = at };
        _root?.MarkPaintDirty();
    }

    /// <summary>Once a frame, before style and layout, beside the autoscroll: the throw moves on by
    /// the time since the last frame, so a dropped frame does not shorten it, and slows as
    /// e^(−t/τ). It stops at the content's edge, under <see cref="FlingStopVelocity"/>, on a touch
    /// that lands on it, or on a wheel.</summary>
    private void TickFling()
    {
        if (_fling is not { } fling) return;
        double now = NowMs();
        double dt = now - fling.LastMs;
        if (dt <= 0) return;
        fling.LastMs = now;

        double decay = Math.Exp(-dt / FlingTimeConstantMs);
        double reach = FlingTimeConstantMs / 1000 * (1 - decay);   // seconds of travel at the old speed
        var travel = new Point((float)(fling.Velocity.X * reach), (float)(fling.Velocity.Y * reach));
        fling.Velocity = new Point((float)(fling.Velocity.X * decay), (float)(fling.Velocity.Y * decay));

        var live = LiveScroller((Element)fling.Scroller, fling.At);
        if (live is null)
        {
            // A replacement built this frame is not laid out yet, so nothing can be hit there:
            // the travel is owed to it, as the autoscroll waits for its replacement.
            fling.Owed = new Point(fling.Owed.X + travel.X, fling.Owed.Y + travel.Y);
            if (++fling.Misses > 3) _fling = null;
            return;
        }
        if (!ReferenceEquals(live, fling.Scroller))
        {
            // Replaced by a rebuild: the new one holds the offset carried over and the travel
            // owed to it, for its layout to apply.
            var from = fling.Scroller.PanOffset;
            live.PanOffset = new Point(from.X - fling.Owed.X - travel.X, from.Y - fling.Owed.Y - travel.Y);
            fling.Scroller = live;
            fling.Owed = default;
            fling.Misses = 0;
        }
        else
        {
            var moved = live.PanBy(-travel.X, -travel.Y);
            // An axis that has reached its edge stops there; no bounce.
            float vx = Math.Abs(moved.X) < Math.Abs(travel.X) * 0.5f ? 0 : fling.Velocity.X;
            float vy = Math.Abs(moved.Y) < Math.Abs(travel.Y) * 0.5f ? 0 : fling.Velocity.Y;
            fling.Velocity = new Point(vx, vy);
        }
        if (MathF.Sqrt(fling.Velocity.X * fling.Velocity.X + fling.Velocity.Y * fling.Velocity.Y) < FlingStopVelocity)
            _fling = null;
    }

    /// <summary>The scroller a gesture holds, or the one a rebuild put in its place: the nearest
    /// surface of the same kind under <paramref name="at"/>. Null when there is none.</summary>
    private IPanScrollable? LiveScroller(Element held, Point at)
    {
        if (!held.IsDetached && IsShownInWindow(held)) return (IPanScrollable)held;
        if (_root is null) return null;
        var (overlay, _) = HitTestOverlays(at);
        for (var e = overlay ?? HitTester.HitTest(_root, at); e is not null; e = e.Parent)
            if (e is IPanScrollable scroller && e.GetType() == held.GetType())
                return scroller;
        return null;
    }

    /// <summary>
    /// The finger's velocity at the lift: a least-squares line through its positions over the
    /// last 100 ms, as Android's VelocityTracker fits, steadier than the last step. A finger that
    /// stopped for 40 ms before it lifted has no velocity.
    /// </summary>
    private sealed class VelocityTracker
    {
        private const double WindowMs = 100;
        private const double StoppedMs = 40;
        private readonly List<(double Ms, Point At)> _samples = [];

        public void Add(double ms, Point at)
        {
            _samples.Add((ms, at));
            int keep = 0;
            while (keep < _samples.Count && ms - _samples[keep].Ms > WindowMs) keep++;
            if (keep > 0) _samples.RemoveRange(0, keep);
        }

        /// <summary>Layout units a second, at <paramref name="now"/>.</summary>
        public Point Fit(double now)
        {
            if (_samples.Count < 2) return default;
            // Stopped: the finger reached where it lifted too long ago.
            var last = _samples[^1];
            int arrived = _samples.Count - 1;
            while (arrived > 0 && _samples[arrived - 1].At == last.At) arrived--;
            if (now - _samples[arrived].Ms > StoppedMs) return default;

            double mt = 0, mx = 0, my = 0;
            foreach (var (ms, at) in _samples) { mt += ms; mx += at.X; my += at.Y; }
            int n = _samples.Count;
            mt /= n; mx /= n; my /= n;
            double stt = 0, stx = 0, sty = 0;
            foreach (var (ms, at) in _samples)
            {
                double t = ms - mt;
                stt += t * t;
                stx += t * (at.X - mx);
                sty += t * (at.Y - my);
            }
            if (stt <= 0) return default;
            return new Point((float)(stx / stt * 1000), (float)(sty / stt * 1000));
        }
    }
}
