using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.WebView;

/// <summary>
/// Shows a <see cref="WebViewController"/>'s page: draws its frames, tells the engine
/// where the page is, and forwards pointer, focus and — for engines that take them — keys
/// and text. It has no size of its own; give it one (<c>FlexGrow</c>, <c>Width</c>/<c>Height</c>).
/// </summary>
/// <remarks>
/// <para><b>Placement.</b> Each draw works out where the page is in its window's device
/// pixels — scroll and overlay position included — and reports a change after the frame,
/// never from inside it. The frame is drawn one device pixel per pixel from the top-left
/// and clipped to the element: during a resize the page's previous frame shows, uncropped
/// or short, rather than stretched.</para>
/// <para><b>Lifetime.</b> Listening starts at the first draw while hosted and stops on
/// unmount, or when a frame finds the element no longer hosted — the same rules as
/// <c>VideoElement</c>, for the same reasons (rebuild twins mount without unmounting;
/// <c>SetRoot</c> unmounts nothing). Unmounting hides the page; it keeps its state.</para>
/// <para><b>Focus.</b> Focusable, and focused by a press on it. Tab into it lands on the
/// page's first stop, Shift+Tab on its last; tabbing out of the page moves focus on to
/// the window's next element.</para>
/// </remarks>
public class WebViewElement : Element
{
    private WebViewController? _controller;
    private bool _listening;
    private WebViewPlacement? _placement;
    private bool _flushQueued;
    private WebPointerButtons _held;
    private string? _titleName;

    public WebViewElement()
    {
        IsFocusable = true;
        Role = AccessibilityRole.Group;
        OnOwn<PointerMoveEvent>(OnPointerMove);
        OnOwn<PointerDownEvent>(OnPointerDown);
        OnOwn<PointerUpEvent>(OnPointerUp);
        OnOwn<PointerLeaveEvent>(OnPointerLeave);
        OnOwn<ScrollEvent>(OnScroll);
        OnOwn<FocusEvent>(e => _controller?.Focus(e.Reason));
        OnOwn<BlurEvent>(_ => _controller?.Blur());
        OnOwn<KeyDownEvent>(OnKeyDown);
        OnOwn<KeyUpEvent>(OnKeyUp);
        OnOwn<TextInputEvent>(OnTextInput);
        OnOwn<TextEditingEvent>(OnTextEditing);
    }

    public WebViewController? Controller
    {
        get => _controller;
        set
        {
            if (ReferenceEquals(_controller, value)) return;
            StopListening();
            _controller = value;
            _placement = null;
            if (value is not null)
            {
                ApplyTitle(value.Title);
                ApplyCursor(value.Cursor);
            }
            StartListening();
        }
    }

    public static WebViewElement New(WebViewController? controller = null, [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        new WebViewElement { Controller = controller }.WithOrigin(file, line);

    internal bool IsListening => _listening;

    /// <summary>Engines that take forwarded keys need the window's text input (and IME) while focused.</summary>
    internal override bool WantsTextInput => ForwardsKeys;

    private bool ForwardsKeys =>
        _controller is { } c && (c.Capabilities & WebEngineCapabilities.NativeKeyboard) == 0;

    // ──── Lifetime ────

    protected override void OnMounted()
    {
        base.OnMounted();
        StartListening();
    }

    protected override void OnUnmounted()
    {
        base.OnUnmounted();
        StopListening();
    }

    private void StartListening()
    {
        if (_listening || _controller is null || !IsHosted) return;
        _controller.FrameArrived += OnFrameArrived;
        _controller.CursorChanged += ApplyCursor;
        _controller.FocusLeaving += OnFocusLeaving;
        _controller.KeyFromPage += OnKeyFromPage;
        _controller.TitleChanged += ApplyTitle;
        _listening = true;
    }

    private void StopListening()
    {
        if (!_listening) return;
        var c = _controller!;
        c.FrameArrived -= OnFrameArrived;
        c.CursorChanged -= ApplyCursor;
        c.FocusLeaving -= OnFocusLeaving;
        c.KeyFromPage -= OnKeyFromPage;
        c.TitleChanged -= ApplyTitle;
        _listening = false;
        _placement = null;
        c.SetVisible(false);
    }

    private void OnFrameArrived()
    {
        // Left behind without an unmount: the root swapped by SetRoot.
        if (!IsHosted) { StopListening(); return; }
        MarkPaintDirty();
    }

    // ──── Drawing and placement ────

    public override void Render(IRenderContext ctx)
    {
        StartListening();
        if (_controller is null || OverlayHost is not Window window) return;
        UpdatePlacement(window);

        // Drawn even before the first frame, as a video is: that first visit is what tells a
        // GPU frame sink which kind of surface it is on, and so how the engine should deliver.
        var frames = _controller.Frames;
        var box = Layout.AbsoluteContentBox;
        var size = frames.IntrinsicSize;
        float scale = window.DisplayScale;
        var rect = size.Width > 0 && size.Height > 0
            ? new Rect(box.X, box.Y, size.Width / scale, size.Height / scale)
            : box;
        ctx.PushClip(box);
        ctx.DrawImage(frames, rect, ImageFit.Fill);
        ctx.Pop();
    }

    private void UpdatePlacement(Window window)
    {
        float scale = window.DisplayScale;
        var box = window.ContentBoxInWindow(this);
        // Edges snap and the size follows from them, as the renderer snaps fills: two
        // elements that touch in layout units touch in pixels too.
        int x = (int)MathF.Round(box.X * scale), y = (int)MathF.Round(box.Y * scale);
        int right = (int)MathF.Round((box.X + box.Width) * scale), bottom = (int)MathF.Round((box.Y + box.Height) * scale);
        var placement = new WebViewPlacement(window.NativeHandle, new PixelRect(x, y, right - x, bottom - y), scale);
        if (_placement == placement) return;
        _placement = placement;
        if (_flushQueued) return;
        // Never call into an engine from inside a draw: WebView2 answers a new size with
        // a synchronous round trip to its browser process. After the frame, then.
        _flushQueued = true;
        Application.InvokeOnMainThread(FlushPlacement);
    }

    private void FlushPlacement()
    {
        _flushQueued = false;
        if (!_listening || _placement is not { } placement || _controller is null) return;
        _controller.SetPlacement(placement);
        _controller.SetVisible(true);
    }

    // ──── Pointer ────

    private float Scale => OverlayHost is Window w ? w.DisplayScale : 1f;

    private WebPointerInput At(PointerEvent e, WebPointerAction action) => At(e.Position, action, e.Modifiers);

    private WebPointerInput At(Point position, WebPointerAction action, KeyModifiers modifiers)
    {
        // Pointer positions are in the same space as ScreenContentBox — window-relative,
        // or overlay-relative inside an overlay — so no overlay offset here.
        var box = ScreenContentBox;
        float scale = Scale;
        return new WebPointerInput(action,
            (int)MathF.Floor((position.X - box.X) * scale), (int)MathF.Floor((position.Y - box.Y) * scale),
            Held: _held, Modifiers: modifiers);
    }

    private static WebPointerButtons ButtonOf(int button) => button switch
    {
        PointerEvent.LeftButton => WebPointerButtons.Left,
        PointerEvent.MiddleButton => WebPointerButtons.Middle,
        PointerEvent.RightButton => WebPointerButtons.Right,
        PointerEvent.BackButton => WebPointerButtons.Back,
        PointerEvent.ForwardButton => WebPointerButtons.Forward,
        _ => WebPointerButtons.None,
    };

    private void OnPointerMove(PointerMoveEvent e) => _controller?.SendPointer(At(e, WebPointerAction.Move));

    private void OnPointerDown(PointerDownEvent e)
    {
        if (_controller is null) return;
        // Held from press to release, so a drag-select that leaves the element keeps going.
        e.CapturePointer();
        var button = ButtonOf(e.Button);
        _held |= button;
        _controller.SendPointer(At(e, WebPointerAction.Down) with { Button = button, ClickCount = Math.Max(1, e.ClickCount) });
        e.StopPropagation = true;
    }

    private void OnPointerUp(PointerUpEvent e)
    {
        var button = ButtonOf(e.Button);
        // A release whose press the page never saw is not the page's: the press opened or
        // closed a menu over it, and the menu is gone by the time the button comes up.
        if (_controller is null || (_held & button) == 0) return;
        _held &= ~button;
        _controller.SendPointer(At(e, WebPointerAction.Up) with { Button = button, ClickCount = 1 });
        e.StopPropagation = true;
    }

    private void OnPointerLeave(PointerLeaveEvent e) => _controller?.SendPointer(At(e, WebPointerAction.Leave));

    private void OnScroll(ScrollEvent e)
    {
        if (_controller is null) return;
        // The page scrolls itself; an enclosing scroll view must not scroll as well.
        _controller.SendPointer(At(e.Position, WebPointerAction.Wheel, e.Modifiers) with { WheelX = e.DeltaX, WheelY = e.DeltaY });
        e.StopPropagation = true;
    }

    // ──── Focus and keys ────

    private void OnFocusLeaving(bool forward)
    {
        if (!IsFocused || OverlayHost is not Window window) return;
        // Tab past the page's last stop: on to the window's next element. When there is
        // nowhere else to go, wrap inside the page instead of leaving focus nowhere.
        if (!window.MoveFocus(forward) || IsFocused)
            _controller?.Focus(forward ? FocusReason.Next : FocusReason.Previous);
    }

    /// <summary>
    /// A shortcut pressed inside a page that holds the keyboard itself. It never passed
    /// through the window, so it is dispatched here, from this element, and bubbles through
    /// the app's handlers as a key pressed on the element would. A handler that stops it keeps
    /// it from the page.
    /// </summary>
    private void OnKeyFromPage(WebKeyRequest request)
    {
        if (!IsHosted) return;
        ElementEvent e = request.IsDown
            ? new KeyDownEvent { Key = request.Key, Modifiers = request.Modifiers, IsRepeat = request.IsRepeat }
            : new KeyUpEvent { Key = request.Key, Modifiers = request.Modifiers };
        EventDispatcher.Dispatch(e, this);
        if (e.StopPropagation || e.Handled) request.Handled = true;
    }

    private void OnKeyDown(KeyDownEvent e)
    {
        if (!ForwardsKeys) return;
        _controller!.SendKey(new WebKeyInput(true, e.Key, e.Modifiers, e.IsRepeat));
        // Tab included: the page has a tab order of its own and says when it is done.
        e.StopPropagation = true;
    }

    private void OnKeyUp(KeyUpEvent e)
    {
        if (!ForwardsKeys) return;
        _controller!.SendKey(new WebKeyInput(false, e.Key, e.Modifiers));
        e.StopPropagation = true;
    }

    private void OnTextInput(TextInputEvent e)
    {
        if (!ForwardsKeys) return;
        _controller!.SendText(new WebTextInput(e.Text));
        e.StopPropagation = true;
    }

    private void OnTextEditing(TextEditingEvent e)
    {
        if (!ForwardsKeys) return;
        _controller!.SendText(new WebTextInput(e.Text, IsComposition: true, e.SegmentStart, e.SegmentLength));
        e.StopPropagation = true;
    }

    // ──── Title and cursor ────

    private void ApplyTitle(string title)
    {
        // The page's title names it for a screen reader, unless the app named it.
        if (AccessibleName is not null && AccessibleName != _titleName) return;
        var name = string.IsNullOrEmpty(title) ? null : title;
        AccessibleName = name;
        _titleName = name;
    }

    private void ApplyCursor(CursorType cursor)
    {
        if (Inherited.Cursor == cursor) return;
        Inherited.Cursor = cursor;
        MarkStyleDirty();
    }

    internal override void PatchFrom(Element source)
    {
        base.PatchFrom(source);
        if (source is not WebViewElement web) return;
        // The copy took the twin's name and cursor; the page's own win where they are the page's.
        _titleName = web._titleName;
        Controller = web.Controller;
        if (_controller is not null)
        {
            ApplyTitle(_controller.Title);
            ApplyCursor(_controller.Cursor);
        }
    }
}
