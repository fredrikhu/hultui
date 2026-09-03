using System.ComponentModel;
using HultUI.Core.Elements;

namespace HultUI.WinForms;

/// <summary>
/// A WinForms control that renders a HultUI element tree.
///
/// <code>
/// var view = new HultUIControl { Dock = DockStyle.Fill, RootFactory = BuildUi };
/// tabPage.Controls.Add(view);
/// </code>
///
/// <para>It is an ordinary control: WinForms owns its handle, its place in the tab
/// order and its window procedure, and HultUI draws inside it with OpenGL. Several can
/// live in one form, sharing a single <see cref="HultUIApplicationHost.Application"/>
/// and the UI thread.</para>
///
/// <para><b>Not yet:</b> keyboard input, focus traversal into and out of the tree, and
/// IME. Pointer input works. See <c>docs/design/WINFORMS-INTEROP.md</c>.</para>
/// </summary>
[DesignerCategory("Code")]
public class HultUIControl : Control
{
    private const int WM_MOUSEHWHEEL = 0x020E;

    private WglPlatformWindow? _platformWindow;
    private Window? _window;
    private Func<Element>? _rootFactory;

    public HultUIControl()
    {
        // GL owns every pixel in this control: WinForms must not erase the background
        // or paint over it, or the surface flickers on every resize and repaint.
        SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
    }

    /// <summary>
    /// Builds the element tree to render. Required — the control has nothing to show
    /// without it.
    /// </summary>
    /// <remarks>
    /// A factory rather than an element, because WinForms recreates a control's handle
    /// on its own schedule (a docking change, a right-to-left switch) and a new handle
    /// means a new GL context, a new surface and a new framework window. The tree is
    /// rebuilt for the new window rather than moved to it, since a mounted element tree
    /// belongs to the window it was mounted in.
    /// </remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<Element>? RootFactory
    {
        get => _rootFactory;
        set
        {
            _rootFactory = value;
            if (value is not null && IsHandleCreated && _window is null) Mount();
        }
    }

    /// <summary>
    /// The framework window this control renders, or null before the handle exists.
    /// Valid until the handle is recreated, so read it inside <see cref="Mounted"/>
    /// rather than caching it.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Window? Window => _window;

    /// <summary>
    /// Raised after the tree is mounted and <see cref="Window"/> is live — the place to
    /// reach the window, and the place to reapply anything held across a handle
    /// recreation, because it fires again each time.
    /// </summary>
    public event EventHandler? Mounted;

    /// <summary>
    /// The user's own zoom on top of the control's DPI. 1.0 is "as WinForms says";
    /// larger draws the tree bigger and fits less in the control. Clamped to 0.5–3.
    /// </summary>
    [DefaultValue(1f)]
    public float UserScale
    {
        get => _platformWindow?.UserScale ?? 1f;
        set { if (_platformWindow is not null) _platformWindow.UserScale = value; }
    }

    // ──── handle lifetime: the GL context lives and dies with the HWND ────

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!DesignMode) Mount();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        Unmount();
        base.OnHandleDestroyed(e);
    }

    private void Mount()
    {
        if (_window is not null || _rootFactory is null || !IsHandleCreated) return;

        (_window, _platformWindow) = HultUIApplicationHost.Mount(this, _rootFactory());
        OnMounted(EventArgs.Empty);
    }

    private void Unmount()
    {
        if (_window is null) return;

        // Closing marks the window for the framework to reap next frame — but the GL
        // context is bound to a handle that is going away now, so the platform window
        // is disposed here rather than a frame later. Its Dispose is idempotent, so the
        // framework's own call during cleanup is a no-op.
        _window.Close();
        _platformWindow?.Dispose();
        _window = null;
        _platformWindow = null;
        HultUIApplicationHost.Unmount(this);
    }

    /// <summary>Raises <see cref="Mounted"/>.</summary>
    protected virtual void OnMounted(EventArgs e) => Mounted?.Invoke(this, e);

    // ──── sizing and scale ────

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _platformWindow?.SyncSize();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        // The pixel size may not have changed, but the layout viewport did: the same
        // path a monitor change takes on a stand-alone window.
        _platformWindow?.SyncScale();
    }

    // ──── painting ────

    protected override void OnPaint(PaintEventArgs e)
    {
        // WinForms is asking for pixels the GL back buffer may no longer hold — after
        // occlusion its contents are undefined — so this repaints regardless of whether
        // anything in the tree is dirty. Everything else renders from the host's pump.
        if (_window is not null) HultUIApplicationHost.RenderNow();
    }

    /// <summary>Suppressed: GL owns the pixels, and erasing first would flicker.</summary>
    protected override void OnPaintBackground(PaintEventArgs pevent) { }

    // ──── input WinForms does not surface as an event ────

    protected override void WndProc(ref Message m)
    {
        // Horizontal wheel: tilt wheels and precision touchpads send it, and WinForms
        // has no MouseHWheel event. HultUI scrolls sideways, so it is worth reading.
        if (m.Msg == WM_MOUSEHWHEEL && _platformWindow is not null)
        {
            int delta = (short)((long)m.WParam >> 16);
            var screen = new Point((short)(long)m.LParam, (short)((long)m.LParam >> 16));
            _platformWindow.EmitHorizontalWheel(delta, PointToClient(screen));
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Unmount();
        base.Dispose(disposing);
    }
}
