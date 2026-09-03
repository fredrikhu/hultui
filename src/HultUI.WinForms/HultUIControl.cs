using System.ComponentModel;
using HultUI.Core.Elements;
using HultUI.WinForms.Interop;

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
/// <para>Keyboard behaves the way a WinForms control's does: Tab moves into the tree,
/// through it, and back out into the form's own tab order; arrows, Enter and typing
/// reach the focused element; and an East Asian IME composes inside it with its
/// candidate window at the caret. See <c>docs/design/WINFORMS-INTEROP.md</c>.</para>
/// </summary>
[DesignerCategory("Code")]
public class HultUIControl : Control
{
    private const int WM_MOUSEHWHEEL = 0x020E;

    private WglPlatformWindow? _platformWindow;
    private Window? _window;
    private Func<Element>? _rootFactory;

    /// <summary>Which end of the tree focus should land on when Tab brings it in.</summary>
    private bool _enteringBackwards;

    public HultUIControl()
    {
        // GL owns every pixel in this control: WinForms must not erase the background
        // or paint over it, or the surface flickers on every resize and repaint.
        SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, false);

        // Without Selectable the control can never take keyboard focus, and every key
        // question below is moot.
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
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

    /// <summary>
    /// Whether Escape is delivered to the tree instead of to the form. Default true,
    /// which is what a HultUI dropdown, context menu or dialog needs to be dismissable
    /// from the keyboard.
    /// </summary>
    /// <remarks>
    /// The cost is that a form's <c>CancelButton</c> does not fire while focus is inside
    /// this control. There is no way to have both: the framework does not report whether
    /// anything consumed a key, so forwarding Escape and also letting WinForms proceed
    /// would close a dropdown <em>and</em> cancel the dialog behind it. Set false where
    /// the form's Cancel semantics matter more than in-tree dismissal.
    /// </remarks>
    [DefaultValue(true)]
    public bool HandlesEscape { get; set; } = true;

    // ──── keyboard ────

    /// <summary>
    /// Claims the keys WinForms would otherwise treat as navigation and never deliver.
    /// </summary>
    /// <remarks>
    /// Tab is deliberately absent: it goes to <see cref="ProcessDialogKey"/> instead, so
    /// the decision about moving within the tree versus leaving it lives in one place.
    /// </remarks>
    protected override bool IsInputKey(Keys keyData)
    {
        if (_window is null) return base.IsInputKey(keyData);

        return (keyData & Keys.KeyCode) switch
        {
            Keys.Up or Keys.Down or Keys.Left or Keys.Right => true,
            Keys.Enter => true,
            Keys.Escape => HandlesEscape,
            _ => base.IsInputKey(keyData),
        };
    }

    /// <summary>
    /// Tab moves through the tree, and out of it once the tree is exhausted.
    /// </summary>
    /// <remarks>
    /// A hosted tree is one stop in the host's tab order, not a trap.
    /// <c>MoveFocus(wrap: false)</c> returning false means there is nothing further this
    /// way, so the key goes back to WinForms and the next control in the form takes over.
    /// </remarks>
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (_window is null || (keyData & Keys.KeyCode) != Keys.Tab || !Focused)
            return base.ProcessDialogKey(keyData);

        bool forward = (keyData & Keys.Shift) == 0;
        if (_window.MoveFocus(forward, wrap: false))
            return true;

        // Leaving: drop focus so no ring is left behind, then let WinForms move on.
        _window.ClearFocus();
        _enteringBackwards = false;
        return base.ProcessDialogKey(keyData);
    }

    protected override void OnPreviewKeyDown(PreviewKeyDownEventArgs e)
    {
        // Read before focus arrives, so OnGotFocus knows which end to land on.
        if ((e.KeyData & Keys.KeyCode) == Keys.Tab) _enteringBackwards = (e.KeyData & Keys.Shift) != 0;
        base.OnPreviewKeyDown(e);
    }

    /// <summary>
    /// Takes keyboard focus when clicked, the way every other control in the form does.
    /// </summary>
    /// <remarks>
    /// Not free. WinForms gives a control focus on mouse-down by letting
    /// <c>DefWindowProc</c> do it, and <c>DefWindowProc</c> does not focus a custom-painted
    /// child window — so without this the control renders and handles pointer input while
    /// never holding Win32 focus. The visible symptom is subtle and specific: clicking
    /// into the tree works, typing works (a posted <c>WM_CHAR</c> reaches the window
    /// procedure regardless), and then Tab does nothing, because the form's dialog-key
    /// routing starts at the focused control and never reaches
    /// <see cref="ProcessDialogKey"/>.
    /// </remarks>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (CanFocus && !Focused) Focus();
        base.OnMouseDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        _platformWindow?.Input.EmitKey(e.KeyData, isDown: true, isRepeat: false);
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _platformWindow?.Input.EmitKey(e.KeyData, isDown: false, isRepeat: false);
        base.OnKeyUp(e);
    }

    // ──── focus ────

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        // Arriving from the form's tab order with nothing focused inside: land on the
        // near end, so Shift+Tab reaches the last element rather than the first.
        if (_window is { FocusedElement: null }) _window.FocusEdge(first: !_enteringBackwards);
        _enteringBackwards = false;
    }

    protected override void OnLostFocus(EventArgs e)
    {
        // Otherwise a focus ring and a blinking caret stay behind in a control the user
        // has left, which no other control in the form would do.
        _window?.ClearFocus();
        base.OnLostFocus(e);
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
        if (_platformWindow is { } platform)
        {
            switch (m.Msg)
            {
                // Horizontal wheel: tilt wheels and precision touchpads send it, and
                // WinForms surfaces no event for it. HultUI scrolls sideways, so it is
                // worth reading.
                case WM_MOUSEHWHEEL:
                {
                    int delta = (short)((long)m.WParam >> 16);
                    var screen = new Point((short)(long)m.LParam, (short)((long)m.LParam >> 16));
                    platform.Input.EmitHorizontalWheel(delta, PointToClient(screen));
                    m.Result = IntPtr.Zero;
                    return;
                }

                // Typed text. Keys the framework treats as keys — Backspace, Enter,
                // Escape, Tab — also arrive here as control characters, and the
                // translator drops those rather than inserting them.
                case Imm.WM_CHAR:
                    platform.Input.EmitChar((char)(long)m.WParam);
                    m.Result = IntPtr.Zero;
                    return;

                // The control draws its own text, so it takes the composition over and
                // renders it as the framework's preedit. The message is deliberately not
                // passed on: letting DefWindowProc see GCS_COMPSTR would have Windows
                // draw its own composition string on top of ours.
                case Imm.WM_IME_COMPOSITION:
                {
                    long flags = (long)m.LParam;

                    if ((flags & Imm.GCS_RESULTSTR) != 0
                        && Imm.GetCompositionString(Handle, Imm.GCS_RESULTSTR) is { Length: > 0 } committed)
                    {
                        platform.Input.EmitComposition("", 0);
                        platform.Input.EmitText(committed);
                    }

                    if ((flags & Imm.GCS_COMPSTR) != 0
                        && Imm.GetCompositionString(Handle, Imm.GCS_COMPSTR) is { } composing)
                    {
                        platform.Input.EmitComposition(composing, Imm.GetCursorPosition(Handle));
                    }

                    m.Result = IntPtr.Zero;
                    return;
                }

                case Imm.WM_IME_ENDCOMPOSITION:
                    // Clears whatever preedit is on screen; anything committed arrived
                    // with the composition message just before this one.
                    platform.Input.EmitComposition("", 0);
                    break;
            }
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Unmount();
        base.Dispose(disposing);
    }
}
