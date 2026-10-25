using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static HultUI.Platform.SDL3.VulkanSwapchainChoice;

namespace HultUI.Platform.SDL3;

/// <summary>
/// Which kind of Win32 modal size/move loop a window is in, followed from the messages its
/// procedure sees. Kept apart from the window procedure so it can be tested without a window.
///
/// <para>A drag runs inside <c>DefWindowProc</c>'s handling of the <c>WM_SYSCOMMAND</c> that starts
/// it — <c>SC_SIZE</c> for an edge or a corner, <c>SC_MOVE</c> for the title bar — and the loop is
/// framed by <c>WM_ENTERSIZEMOVE</c> and <c>WM_EXITSIZEMOVE</c>, sent from inside that call. So the
/// command is noted on the way in, read when the loop starts, and forgotten on the way out.</para>
/// </summary>
internal sealed class SizeMoveTracker
{
    private const uint ScSize = 0xF000;
    private const uint ScMove = 0xF010;

    /// <summary>What the <c>WM_SYSCOMMAND</c> being handled asked for, if it starts a loop.</summary>
    private SizeMoveLoop _commanded;

    /// <summary>The loop running now.</summary>
    public SizeMoveLoop Loop { get; private set; }

    /// <summary>A <c>WM_SYSCOMMAND</c> is about to be handled. Returns what to give back to
    /// <see cref="SysCommandHandled"/> once it has been.</summary>
    /// <param name="wParam">The command. Its low four bits are the system's own (the hit-test code
    /// a mouse drag adds), so they are masked off.</param>
    public SizeMoveLoop SysCommand(nuint wParam)
    {
        var before = _commanded;
        _commanded = ((uint)wParam & 0xFFF0) switch
        {
            ScMove => SizeMoveLoop.Moving,
            ScSize => SizeMoveLoop.Sizing,
            _ => SizeMoveLoop.None,
        };
        return before;
    }

    /// <summary>The <c>WM_SYSCOMMAND</c> has been handled, loop and all.</summary>
    public void SysCommandHandled(SizeMoveLoop before) => _commanded = before;

    /// <summary><c>WM_ENTERSIZEMOVE</c>: the loop starts. One that no <c>SC_MOVE</c> started counts
    /// as sizing.</summary>
    public SizeMoveLoop Entered() =>
        Loop = _commanded == SizeMoveLoop.Moving ? SizeMoveLoop.Moving : SizeMoveLoop.Sizing;

    /// <summary><c>WM_EXITSIZEMOVE</c>: the loop is over.</summary>
    public void Exited() => Loop = SizeMoveLoop.None;
}

/// <summary>
/// Tells a window when Win32's modal size/move loop starts and ends — the settled-size signal the
/// Vulkan swapchain over-allocates by (<see cref="SdlVulkanHost.SizeMoveStarted"/>). SDL handles
/// <c>WM_ENTERSIZEMOVE</c> for its live-resize timer and reports nothing of it.
///
/// <para>A comctl32 subclass, which stacks with the window's other hooks (the custom frame, the
/// accessibility bridge, a web view's) and unhooks in any order. Installed and removed on the
/// window's own thread.</para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe class WindowsSizeMoveWatch
{
    private const uint WM_SYSCOMMAND = 0x0112;
    private const uint WM_ENTERSIZEMOVE = 0x0231;
    private const uint WM_EXITSIZEMOVE = 0x0232;
    private const uint WM_NCDESTROY = 0x0082;
    private const nuint SubclassId = 0x48554C53; // "HULS"

    /// <summary>
    /// The message a GPU source's request for a frame is posted as while the window is in the loop
    /// (<see cref="Core.Rendering.LiveFrameRequests"/>): the loop dispatches it, and the window draws.
    /// Registered, so no other code's private message can be taken for it.
    /// </summary>
    public static readonly uint LiveFrameMessage = RegisterWindowMessageW("HultUI.LiveFrame") is var m and not 0 ? m : 0x8000 + 0x3A1;

    private sealed class Watch(nint hwnd, Action<SizeMoveLoop> entered, Action exited, Action? liveFrame) : IDisposable
    {
        public readonly SizeMoveTracker Tracker = new();
        public readonly Action<SizeMoveLoop> Entered = entered;
        public readonly Action Exited = exited;
        public readonly Action? LiveFrame = liveFrame;
        private GCHandle _self;

        public bool Hook()
        {
            _self = GCHandle.Alloc(this);
            if (SetWindowSubclass(hwnd, &Proc, SubclassId, (nuint)GCHandle.ToIntPtr(_self))) return true;
            _self.Free();
            return false;
        }

        /// <summary>Unhooks, once; from the window's thread (its disposal, or WM_NCDESTROY).</summary>
        public void Dispose()
        {
            if (!_self.IsAllocated) return;
            RemoveWindowSubclass(hwnd, &Proc, SubclassId);
            _self.Free();
        }
    }

    /// <summary>Calls <paramref name="entered"/> when a loop starts and <paramref name="exited"/>
    /// when it ends, after SDL has seen each, and <paramref name="liveFrame"/> for each
    /// <see cref="LiveFrameMessage"/>. Null when the window could not be subclassed.</summary>
    public static IDisposable? Install(nint hwnd, Action<SizeMoveLoop> entered, Action exited, Action? liveFrame = null)
    {
        if (hwnd == 0) return null;
        var watch = new Watch(hwnd, entered, exited, liveFrame);
        return watch.Hook() ? watch : null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint Proc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == LiveFrameMessage)
        {
            // Ours alone: nothing below needs to see it.
            try { (GCHandle.FromIntPtr((nint)data).Target as Watch)?.LiveFrame?.Invoke(); }
            catch (Exception ex) { Console.Error.WriteLine($"[HultUI] live frame failed: {ex}"); }
            return 0;
        }
        if (message is not (WM_SYSCOMMAND or WM_ENTERSIZEMOVE or WM_EXITSIZEMOVE or WM_NCDESTROY)
            || GCHandle.FromIntPtr((nint)data).Target is not Watch watch)
            return DefSubclassProc(hwnd, message, wParam, lParam);

        // The loop runs inside WM_SYSCOMMAND's handling, so its command is known when it starts.
        var before = SizeMoveLoop.None;
        if (message == WM_SYSCOMMAND) before = watch.Tracker.SysCommand((nuint)wParam);
        // SDL sees every message first, and once: at the end of a loop it stops its live-resize
        // timer, and the swapchain rebuilt after it is drawn at once.
        nint result = DefSubclassProc(hwnd, message, wParam, lParam);
        try
        {
            switch (message)
            {
                case WM_SYSCOMMAND:
                    watch.Tracker.SysCommandHandled(before);
                    break;
                case WM_ENTERSIZEMOVE:
                    watch.Entered(watch.Tracker.Entered());
                    break;
                case WM_EXITSIZEMOVE:
                    watch.Tracker.Exited();
                    watch.Exited();
                    break;
                case WM_NCDESTROY:
                    watch.Dispose();
                    break;
            }
        }
        catch (Exception ex)
        {
            // Nothing may escape into the window procedure.
            Console.Error.WriteLine($"[HultUI] WindowsSizeMoveWatch failed: {ex}");
        }
        return result;
    }

    [DllImport("comctl32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint hwnd,
        delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id, nuint data);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint hwnd,
        delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    /// <summary>Posts <see cref="LiveFrameMessage"/>: returns at once, from any thread.</summary>
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);
}
