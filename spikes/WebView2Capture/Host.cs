using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using HultUI;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Microsoft.Web.WebView2.Core;
using WinRT;
using Comp = Windows.UI.Composition;
using DPoint = System.Drawing.Point;
using DRect = System.Drawing.Rectangle;
using HRect = HultUI.Core.Primitives.Rect;
using HPoint = HultUI.Core.Primitives.Point;

namespace WebView2Capture;

/// <summary>An image of the page that reports where it landed on screen after each draw.</summary>
internal sealed class PageElement : ImageElement
{
    private HRect _last;
    public Action<HRect>? ScreenBoxChanged;

    public override void Render(IRenderContext ctx)
    {
        var box = ScreenContentBox;
        if (box != _last)
        {
            _last = box;
            var cb = ScreenBoxChanged;
            // Never call into WebView2 from inside a draw: defer to after the frame.
            if (cb is not null) Application.InvokeOnMainThread(() => cb(box));
        }
        base.Render(ctx);
    }
}

/// <summary>
/// One WebView2 composition controller parented to the HultUI window, its root visual
/// captured into the page element's sink, and HultUI pointer events forwarded to it.
/// </summary>
internal sealed class Host
{
    public required Window Window { get; init; }
    public required PageElement Page { get; init; }
    public required PixelFrameSink Sink { get; init; }

    public nint Hwnd;
    public float Scale;
    public CoreWebView2Environment Env = null!;
    public CoreWebView2CompositionController Cc = null!;
    public CoreWebView2 Web => Cc.CoreWebView2;
    public Comp.Compositor Compositor = null!;
    public Comp.ContainerVisual Root = null!;
    public VisualCapture Capture = null!;

    /// <summary>Where the page is, in the parent window's client pixels.</summary>
    public DRect PageRect;

    /// <summary>Whether WebView2 offsets its visual inside the root visual by Bounds.X/Y
    /// (decided by the placement probe). When it does, the capture is cropped.</summary>
    public bool BoundsOffsetsVisual = true;

    /// <summary>Whether SendMouseInput wants points relative to the parent window rather
    /// than to the WebView's bounds (decided by the popup probe).</summary>
    public bool MouseParentRelative;

    public string UserDataFolder { get; } = Path.Combine(Path.GetTempPath(), "hultui-webview2-spike");

    public async Task InitAsync()
    {
        var platform = typeof(Window).GetProperty("PlatformWindow", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(Window);
        Hwnd = ((IWin32WindowHandle)platform!).Hwnd;
        Scale = Window.DisplayScale;

        var opts = new Native.DispatcherQueueOptions
        {
            dwSize = Marshal.SizeOf<Native.DispatcherQueueOptions>(),
            threadType = 2,      // DQTYPE_THREAD_CURRENT
            apartmentType = 2,   // DQTAT_COM_STA
        };
        Marshal.ThrowExceptionForHR(Native.CreateDispatcherQueueController(opts, out _));
        Compositor = new Comp.Compositor();
        Root = Compositor.CreateContainerVisual();

        Env = await CoreWebView2Environment.CreateAsync(null, UserDataFolder, null);
        Cc = await Env.CreateCoreWebView2CompositionControllerAsync(Hwnd);

        // The managed wrapper marshals RootVisualTarget as a classic IUnknown, which a
        // CsWinRT object is not; hand it an RCW over the native visual instead.
        Cc.RootVisualTarget = Marshal.GetObjectForIUnknown(((IWinRTObject)Root).NativeObject.ThisPtr);
        Cc.BoundsMode = CoreWebView2BoundsMode.UseRawPixels;
        Cc.ShouldDetectMonitorScaleChanges = false;
        Cc.RasterizationScale = Scale;

        Capture = new VisualCapture(Sink);
        Page.ScreenBoxChanged = OnScreenBoxChanged;
        // The element has usually been laid out already; later changes arrive by callback.
        OnScreenBoxChanged(Page.ScreenContentBox);
        WireInput();
        Cc.CursorChanged += (_, _) => ApplyCursor(Cc.SystemCursorId);
    }

    /// <summary>Places the WebView where the element is, and starts or resizes the capture.</summary>
    public void OnScreenBoxChanged(HRect box)
    {
        var r = new DRect((int)MathF.Round(box.X * Scale), (int)MathF.Round(box.Y * Scale),
            Math.Max(1, (int)MathF.Round(box.Width * Scale)), Math.Max(1, (int)MathF.Round(box.Height * Scale)));
        if (r == PageRect) return;
        PageRect = r;
        ApplyBounds();
    }

    public void ApplyBounds()
    {
        var r = PageRect;
        Cc.Bounds = r;
        int visW = BoundsOffsetsVisual ? r.Right : r.Width, visH = BoundsOffsetsVisual ? r.Bottom : r.Height;
        Root.Size = new Vector2(visW, visH);
        Capture.CropX = BoundsOffsetsVisual ? r.X : 0;
        Capture.CropY = BoundsOffsetsVisual ? r.Y : 0;
        Capture.CropW = r.Width;
        Capture.CropH = r.Height;
        if (Capture.ItemSize.Width == 0) Capture.Start(Root, visW, visH);
        else Capture.Resize(visW, visH);
    }

    // ── Input ──

    private CoreWebView2MouseEventVirtualKeys _held;

    public DPoint ToWebPoint(HPoint windowPos)
    {
        var box = Page.ScreenContentBox;
        int x = (int)MathF.Round((windowPos.X - box.X) * Scale), y = (int)MathF.Round((windowPos.Y - box.Y) * Scale);
        return MouseParentRelative ? new DPoint(x + PageRect.X, y + PageRect.Y) : new DPoint(x, y);
    }

    public DPoint WebPointFromPagePixels(int x, int y) =>
        MouseParentRelative ? new DPoint(x + PageRect.X, y + PageRect.Y) : new DPoint(x, y);

    private CoreWebView2MouseEventVirtualKeys Keys(KeyModifiers mods)
    {
        var k = _held;
        if (mods.HasFlag(KeyModifiers.Shift)) k |= CoreWebView2MouseEventVirtualKeys.Shift;
        if (mods.HasFlag(KeyModifiers.Ctrl)) k |= CoreWebView2MouseEventVirtualKeys.Control;
        return k;
    }

    private void WireInput()
    {
        Page.On<PointerMoveEvent>(e =>
            Cc.SendMouseInput(CoreWebView2MouseEventKind.Move, Keys(e.Modifiers), 0, ToWebPoint(e.Position)));

        Page.On<PointerDownEvent>(e =>
        {
            e.CapturePointer();
            Cc.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
            var (kind, dbl, flag) = e.Button switch
            {
                PointerEvent.RightButton => (CoreWebView2MouseEventKind.RightButtonDown, CoreWebView2MouseEventKind.RightButtonDoubleClick, CoreWebView2MouseEventVirtualKeys.RightButton),
                PointerEvent.MiddleButton => (CoreWebView2MouseEventKind.MiddleButtonDown, CoreWebView2MouseEventKind.MiddleButtonDoubleClick, CoreWebView2MouseEventVirtualKeys.MiddleButton),
                _ => (CoreWebView2MouseEventKind.LeftButtonDown, CoreWebView2MouseEventKind.LeftButtonDoubleClick, CoreWebView2MouseEventVirtualKeys.LeftButton),
            };
            _held |= flag;
            Cc.SendMouseInput(e.ClickCount == 2 ? dbl : kind, Keys(e.Modifiers), 0, ToWebPoint(e.Position));
            e.StopPropagation = true;
        });

        Page.On<PointerUpEvent>(e =>
        {
            var (kind, flag) = e.Button switch
            {
                PointerEvent.RightButton => (CoreWebView2MouseEventKind.RightButtonUp, CoreWebView2MouseEventVirtualKeys.RightButton),
                PointerEvent.MiddleButton => (CoreWebView2MouseEventKind.MiddleButtonUp, CoreWebView2MouseEventVirtualKeys.MiddleButton),
                _ => (CoreWebView2MouseEventKind.LeftButtonUp, CoreWebView2MouseEventVirtualKeys.LeftButton),
            };
            _held &= ~flag;
            Cc.SendMouseInput(kind, Keys(e.Modifiers), 0, ToWebPoint(e.Position));
        });

        Page.On<ScrollEvent>(e =>
        {
            if (e.DeltaY != 0)
                Cc.SendMouseInput(CoreWebView2MouseEventKind.Wheel, Keys(e.Modifiers), unchecked((uint)(int)(e.DeltaY * 120)), ToWebPoint(e.Position));
            if (e.DeltaX != 0)
                Cc.SendMouseInput(CoreWebView2MouseEventKind.HorizontalWheel, Keys(e.Modifiers), unchecked((uint)(int)(-e.DeltaX * 120)), ToWebPoint(e.Position));
            e.StopPropagation = true;
        });

        Page.On<PointerLeaveEvent>(_ =>
            Cc.SendMouseInput(CoreWebView2MouseEventKind.Leave, CoreWebView2MouseEventVirtualKeys.None, 0, default));
    }

    private void ApplyCursor(uint id)
    {
        // IDC_* resource ids.
        var cursor = id switch
        {
            32513 => CursorType.Text,
            32514 or 32650 => CursorType.Wait,
            32515 => CursorType.Crosshair,
            32649 => CursorType.Pointer,
            32644 => CursorType.ResizeEW,
            32645 => CursorType.ResizeNS,
            32642 => CursorType.ResizeNWSE,
            32643 => CursorType.ResizeNESW,
            32646 => CursorType.Move,
            32648 => CursorType.NotAllowed,
            _ => CursorType.Default,
        };
        if (Page.Inherited.Cursor == cursor) return;
        Page.Inherited.Cursor = cursor;
        Page.MarkStyleDirty();
    }
}
