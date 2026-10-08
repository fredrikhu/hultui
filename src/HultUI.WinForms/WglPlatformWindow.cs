using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Rendering.Skia;
using HultUI.WinForms.Input;
using HultUI.WinForms.Interop;
using HultUI.WinForms.Services;

namespace HultUI.WinForms;

/// <summary>
/// A HultUI platform window backed by a WinForms control's own window handle: an
/// OpenGL context created with WGL, the framework's ordinary Skia GL surface over it,
/// and input translated from the control's WinForms events.
///
/// <para>There is no SDL here and no child window of our own. The control's handle is
/// the render target, so WinForms keeps owning focus, tab order and the window
/// procedure — which is the right relationship for a control living inside someone
/// else's form.</para>
/// </summary>
internal sealed class WglPlatformWindow : IPlatformWindow, IWin32WindowHandle, INativeDragSource
{
    private readonly HultUIControl _control;
    private readonly SkiaGLRenderSurface _surface;
    private readonly WinFormsInputTranslator _input;
    private readonly IntPtr _context;
    private readonly IntPtr _dc;

    private float _userScale = 1f;
    private int _pixelWidth;
    private int _pixelHeight;
    private bool _disposed;

    // textServices: the platform's text services, which every control shares and the platform
    // disposes; asked for inside the surface's timing, since the first control's call builds them.
    internal WglPlatformWindow(HultUIControl control, string title, WindowStyle style,
        Func<SkiaTextServices> textServices)
    {
        _control = control;
        Title = title;
        Style = style;

        Hwnd = control.Handle;
        _pixelWidth = Math.Max(control.ClientSize.Width, 1);
        _pixelHeight = Math.Max(control.ClientSize.Height, 1);

        // Never load the driver on two threads at once: if a preload is still running,
        // wait for it rather than racing it into the loader lock.
        GraphicsPreload.Join();

        // Timed because these two are where a first control's start-up goes, and the
        // numbers are the only way to tell a working preload from a decorative one.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        (_context, _dc) = Wgl.CreateContext(Hwnd);
        Wgl.wglMakeCurrent(_dc, _context);
        VSyncDisabled = Wgl.DisableVSync();
        MsContext = clock.Elapsed.TotalMilliseconds;

        // The text pipeline every control shares: the preloaded one when there was a preload,
        // built here by the first control otherwise.
        clock.Restart();
        _surface = new SkiaGLRenderSurface(Wgl.GetProcAddress, textServices());
        MsSurface = clock.Elapsed.TotalMilliseconds;
        _surface.Resize(_pixelWidth, _pixelHeight, DisplayScale);

        _input = new WinFormsInputTranslator(control, () => DisplayScale, evt => InputReceived?.Invoke(evt));
        TextInputHost = new WinFormsTextInputHost(() => Hwnd, () => DisplayScale);
        IsOpen = true;
    }

    /// <summary>Milliseconds spent creating the GL context — the driver load, on the
    /// first control of a process that did not preload.</summary>
    internal double MsContext { get; }

    /// <summary>Milliseconds spent building the Skia surface, including the shared text
    /// pipeline when this is the first control and no preloaded one was available.</summary>
    internal double MsSurface { get; }

    /// <summary>False when the driver had no <c>WGL_EXT_swap_control</c>; every swap
    /// then blocks on the compositor. Surfaced so a host can say so rather than wonder
    /// why frames cost 8 ms.</summary>
    internal bool VSyncDisabled { get; }

    /// <inheritdoc/>
    public nint Hwnd { get; }

    /// <summary>The control's input translator, for the messages the control reads itself.</summary>
    internal WinFormsInputTranslator Input => _input;

    /// <summary>
    /// Matches the surface to the control's client size. Called from the control's
    /// resize and DPI-change handlers; the framework relayouts on the next frame
    /// through <see cref="ResizePending"/>, the same path a native resize takes.
    /// </summary>
    internal void SyncSize()
    {
        if (_disposed) return;

        int width = Math.Max(_control.ClientSize.Width, 1);
        int height = Math.Max(_control.ClientSize.Height, 1);
        if (width == _pixelWidth && height == _pixelHeight) return;

        _pixelWidth = width;
        _pixelHeight = height;
        MakeCurrent();
        _surface.Resize(width, height, DisplayScale);
        ResizePending = true;
    }

    /// <summary>Re-sizes the surface after a DPI change without the pixel size having moved.</summary>
    internal void SyncScale()
    {
        if (_disposed) return;
        MakeCurrent();
        _surface.Resize(_pixelWidth, _pixelHeight, DisplayScale);
        ResizePending = true;
    }

    // ──── IPlatformWindow ────

    public IRenderSurface Surface => _surface;

    public float LayoutWidth => _pixelWidth / DisplayScale;
    public float LayoutHeight => _pixelHeight / DisplayScale;

    /// <summary>
    /// The control's DPI, times the user's zoom. It comes from the control rather than
    /// the monitor on purpose: WinForms already decided what this control's scale is,
    /// and disagreeing with it would put HultUI's content at a different size from the
    /// controls beside it. A host that is not PerMonitorV2 reports 96 here and the
    /// surface renders at 1× on a scaled display — that is WinForms' answer, not a bug
    /// to work around.
    /// </summary>
    public float DisplayScale => _control.DeviceDpi / 96f * _userScale;
    public float RefreshRate => 0f;   // WinForms owns the loop; nothing here paces frames

    /// <inheritdoc/>
    public float UserScale
    {
        get => _userScale;
        set
        {
            float clamped = Math.Clamp(value, 0.5f, 3f);
            if (Math.Abs(clamped - _userScale) < 0.0001f) return;
            _userScale = clamped;
            SyncScale();
        }
    }

    public WindowStyle Style { get; }
    public bool IsOpen { get; private set; }

    /// <summary>Always false: a control is not a window the user can maximise.</summary>
    public bool IsMaximized => false;

    /// <summary>Kept for the framework's benefit; nothing shows it, because a control
    /// has no caption, task-bar entry or switcher presence of its own.</summary>
    public string Title { get; set; }

    public IClipboard Clipboard { get; } = new WinFormsClipboard();
    public IFileDialogs FileDialogs { get; } = new WinFormsFileDialogs();
    public ITextInputHost TextInputHost { get; }

    public event Action<InputEvent>? InputReceived;

    public bool ResizePending { get; set; }
    public bool ActivatedPending { get; set; }
    public long LastResizeRenderTick { get; set; }

    /// <summary>Never invoked: WinForms resizes a control without a modal loop, so
    /// there is no pump the framework has to be driven from.</summary>
    public Action? LiveResizeFrame { get; set; }

    public void MakeCurrent() => Wgl.wglMakeCurrent(_dc, _context);

    /// <summary>No-op: WinForms decides when the control is visible.</summary>
    public void EnsureShown() { }

    public void SwapBuffers() => Wgl.SwapBuffers(_dc);

    public void Close() => IsOpen = false;

    // Window-management verbs belong to the form, not to a control inside it. They are
    // no-ops rather than throws so framework code that offers them (custom chrome, an
    // app's own minimise button) degrades quietly when it is hosted rather than crashing.
    public void Minimize() { }
    public void Maximize() { }
    public void Restore() { }

    /// <summary>Brings the hosting form forward and focuses the control — the nearest
    /// honest meaning of "raise" for something that is not a window.</summary>
    public void Raise()
    {
        _control.FindForm()?.Activate();
        _control.Focus();
    }

    /// <summary>No-op: a control's minimum size is the layout's business, not ours.</summary>
    public void SetMinimumSize(int layoutWidth, int layoutHeight) { }

    /// <summary>No-op: the icon belongs to the form.</summary>
    public void SetIcon(int width, int height, ReadOnlySpan<byte> rgbaPixels) { }

    public void SetCursor(CursorType cursor) => _control.Cursor = CursorMap.ToWinForms(cursor);

    // ──── outbound drag (INativeDragSource) ────

    /// <summary>
    /// True when this control can start a drag other applications will see.
    /// </summary>
    /// <remarks>
    /// An outbound drag is an OLE operation, and OLE will not initialise on an MTA
    /// thread. WinForms requires STA for its own reasons, so this is all but always
    /// true — but reporting the capability honestly lets an app hide a drag affordance
    /// rather than offer one that quietly does nothing.
    /// </remarks>
    public bool CanDragFiles =>
        !_disposed
        && _control.IsHandleCreated
        && Thread.CurrentThread.GetApartmentState() == ApartmentState.STA;

    /// <summary>
    /// Offers files to the rest of the desktop, blocking until the drop resolves.
    /// </summary>
    /// <remarks>
    /// <c>DoDragDrop</c> runs a nested modal loop that owns the mouse until the button
    /// comes up, so no frame renders while it is running — the framework documents this
    /// and calls it from a DragStart handler, while the button is still down.
    /// </remarks>
    public DragEffect DragFiles(IReadOnlyList<string> paths, DragEffect allowed)
    {
        if (!CanDragFiles || paths.Count == 0) return DragEffect.None;

        // The drag begins from a DragStart handler, which the framework raises while
        // handling a mouse move — and the input translator took mouse capture on the
        // press so that a drag started inside the control keeps receiving moves outside
        // it. OLE's drag loop takes capture of its own, and will not start while someone
        // else holds it, so the capture is handed over here.
        _control.Capture = false;

        var payload = new DataObject();
        payload.SetData(DataFormats.FileDrop, true, paths.ToArray());
        var result = _control.DoDragDrop(payload, DragDropInterop.ToWinForms(allowed));
        return DragDropInterop.ToFramework(result);
    }

    public string? DescribeGraphics()
        => $"wgl hwnd=0x{Hwnd:X} {_pixelWidth}x{_pixelHeight}px scale={DisplayScale:0.##} " +
           $"swapInterval={(VSyncDisabled ? 0 : 1)} ctx={MsContext:0.0}ms surface={MsSurface:0.0}ms " +
           $"preload={(GraphicsPreload.Succeeded ? $"{GraphicsPreload.ElapsedMilliseconds:0}ms" : "off")}";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsOpen = false;

        _input.Detach();
        (TextInputHost as IDisposable)?.Dispose();

        // The surface owns GPU resources, so it has to go while its context is current.
        MakeCurrent();
        _surface.Dispose();

        Wgl.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        Wgl.wglDeleteContext(_context);
        Wgl.ReleaseDC(Hwnd, _dc);
    }
}

/// <summary>The framework's cursor vocabulary, in WinForms terms.</summary>
internal static class CursorMap
{
    internal static Cursor ToWinForms(CursorType cursor) => cursor switch
    {
        CursorType.Pointer => Cursors.Hand,
        CursorType.Text => Cursors.IBeam,
        CursorType.Crosshair => Cursors.Cross,
        CursorType.Move => Cursors.SizeAll,
        CursorType.ResizeNS => Cursors.SizeNS,
        CursorType.ResizeEW => Cursors.SizeWE,
        CursorType.ResizeNESW => Cursors.SizeNESW,
        CursorType.ResizeNWSE => Cursors.SizeNWSE,
        CursorType.NotAllowed => Cursors.No,
        CursorType.Wait => Cursors.WaitCursor,
        CursorType.Progress => Cursors.AppStarting,
        // Splitter cursors: WinForms has no dedicated column/row resize, and the plain
        // resize arrows are what every splitter in a WinForms app already uses.
        CursorType.ColResize => Cursors.VSplit,
        CursorType.RowResize => Cursors.HSplit,
        CursorType.None => Hidden,
        _ => Cursors.Default,
    };

    private static Cursor? _hidden;

    /// <summary>
    /// WinForms has no "no cursor" value and <c>Cursor.Hide()</c> is process-wide and
    /// reference-counted, so hiding one control's cursor means a fully transparent one.
    /// Built once and kept, because a <see cref="Cursor"/> owns an OS handle.
    /// </summary>
    private static Cursor Hidden
    {
        get
        {
            if (_hidden is not null) return _hidden;
            using var blank = new Bitmap(1, 1);   // ARGB zero: transparent
            return _hidden = new Cursor(blank.GetHicon());
        }
    }
}
