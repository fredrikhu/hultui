using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace VesperCommander;

/// <summary>
/// The Windows shell context menu — the one Explorer and Total Commander show, with every
/// registered extension in it (7-Zip, git clients, "Send to", "Open with").
/// </summary>
/// <remarks>
/// <para>Reached through raw COM vtables rather than <c>[ComImport]</c> interfaces, because
/// built-in COM interop is off under NativeAOT and Vesper publishes AOT. Only four methods
/// are needed, so hand-written vtable calls cost less than source-generated interfaces and
/// keep the reference counting explicit.</para>
/// <para>Everything here runs on the UI thread. <c>TrackPopupMenuEx</c> must be called on the
/// thread that owns the window, and it runs its own modal message loop — so HultUI stops
/// rendering while the menu is up, exactly as it would for any native menu.</para>
/// </remarks>
internal static unsafe class ShellContextMenu
{
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>Pop the context menu for <paramref name="paths"/> at a screen point and run
    /// whatever the user picks. Returns false when nothing could be shown.</summary>
    public static bool Show(IReadOnlyList<string> paths, int screenX, int screenY)
    {
        if (!OperatingSystem.IsWindows() || paths.Count == 0) return false;
        IntPtr owner = GetActiveWindow();

        return WithContextMenu(paths, owner, pcm =>
        {
            IntPtr menu = CreatePopupMenu();
            if (menu == IntPtr.Zero) return false;
            try
            {
                if (QueryContextMenu(pcm, menu, 0, IdFirst, IdLast, CMF_NORMAL | CMF_EXPLORE) < 0)
                    return false;

                // Owner-drawn items and lazily-filled submenus ("Send to", "New") are built by
                // the extension in response to messages sent to the *owner window*, so the menu
                // is empty in places unless those are routed back to it. Installed only for the
                // life of the modal loop below, on top of whatever HultUI's custom frame has.
                using (var hook = MenuHook.Install(owner, pcm))
                {
                    // Without this the menu doesn't dismiss on an outside click: it stays up
                    // until it regains activation, because it never had it.
                    SetForegroundWindow(owner);
                    int cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_LEFTALIGN | TPM_RIGHTBUTTON,
                        screenX, screenY, owner, IntPtr.Zero);
                    if (cmd <= 0) return true;   // dismissed — nothing picked, but nothing wrong

                    // Verbs are identified by their offset from the first id we handed out.
                    return InvokeVerbPointer(pcm, owner, (IntPtr)(cmd - IdFirst));
                }
            }
            finally { DestroyMenu(menu); }
        });
    }

    /// <summary>Run a named verb over <paramref name="paths"/> without showing anything —
    /// how a combined Properties sheet for a multi-selection is raised, since
    /// <c>ShellExecuteEx</c> has no multi-item form.</summary>
    public static bool InvokeVerb(IReadOnlyList<string> paths, string verb)
    {
        if (!OperatingSystem.IsWindows() || paths.Count == 0) return false;
        IntPtr owner = GetActiveWindow();
        IntPtr ansi = Marshal.StringToHGlobalAnsi(verb);
        try { return WithContextMenu(paths, owner, pcm => InvokeVerbPointer(pcm, owner, ansi)); }
        finally { Marshal.FreeHGlobal(ansi); }
    }

    /// <summary>Bind <paramref name="paths"/> to one <c>IContextMenu</c> and hand it to
    /// <paramref name="use"/>, then release everything in reverse order.</summary>
    /// <remarks>All the paths must share a parent folder — <c>GetUIObjectOf</c> takes child
    /// ids relative to one <c>IShellFolder</c>. A pane's selection always does.</remarks>
    private static bool WithContextMenu(IReadOnlyList<string> paths, IntPtr owner, Func<IntPtr, bool> use)
    {
        int n = paths.Count;
        var absolute = new IntPtr[n];       // full ids — ours to free
        var children = new IntPtr[n];       // tails pointing into `absolute` — not ours to free
        IntPtr folder = IntPtr.Zero, pcm = IntPtr.Zero;
        try
        {
            for (int i = 0; i < n; i++)
            {
                if (SHParseDisplayName(paths[i], IntPtr.Zero, out absolute[i], 0, out _) < 0)
                    return false;
                // Every item resolves its own parent; only the first one's is kept, since the
                // caller guarantees they share it.
                if (SHBindToParent(absolute[i], IID_IShellFolder, out IntPtr parent, out children[i]) < 0)
                    return false;
                if (i == 0) folder = parent; else Release(parent);
            }

            Guid wanted = IID_IContextMenu;
            fixed (IntPtr* apidl = children)
            {
                if (GetUIObjectOf(folder, owner, (uint)n, apidl, &wanted, null, out pcm) < 0) return false;
            }
            return use(pcm);
        }
        finally
        {
            if (pcm != IntPtr.Zero) Release(pcm);
            if (folder != IntPtr.Zero) Release(folder);
            foreach (var p in absolute) if (p != IntPtr.Zero) CoTaskMemFree(p);
        }
    }

    /// <summary><c>lpVerb</c> is either a string pointer or a command offset stuffed into the
    /// pointer — the shell tells them apart by the high word being zero.</summary>
    private static bool InvokeVerbPointer(IntPtr pcm, IntPtr owner, IntPtr verb)
    {
        var ici = new CMINVOKECOMMANDINFO
        {
            cbSize = sizeof(CMINVOKECOMMANDINFO),
            hwnd = owner,
            lpVerb = verb,
            nShow = SW_SHOWNORMAL,
        };
        return InvokeCommand(pcm, &ici) >= 0;
    }

    // ── Window procedure for the life of the menu ────────────────────────
    /// <summary>Routes the menu messages an <c>IContextMenu2</c>/<c>3</c> extension needs back
    /// into it, and everything else to the window procedure that was there before. Static
    /// state is safe: the menu is modal, so only one can be up at a time.</summary>
    private sealed class MenuHook : IDisposable
    {
        private static IntPtr _prev, _cm2, _cm3, _hwnd;

        public static MenuHook Install(IntPtr hwnd, IntPtr pcm)
        {
            _cm2 = QueryInterfaceOrZero(pcm, IID_IContextMenu2);
            _cm3 = QueryInterfaceOrZero(pcm, IID_IContextMenu3);
            _hwnd = hwnd;
            _prev = _cm2 == IntPtr.Zero && _cm3 == IntPtr.Zero
                ? IntPtr.Zero                                        // nothing to route
                : SetWindowLongPtrW(hwnd, GWLP_WNDPROC, (IntPtr)(delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr>)&Proc);
            return new MenuHook();
        }

        public void Dispose()
        {
            if (_prev != IntPtr.Zero) SetWindowLongPtrW(_hwnd, GWLP_WNDPROC, _prev);
            if (_cm3 != IntPtr.Zero) Release(_cm3);
            if (_cm2 != IntPtr.Zero) Release(_cm2);
            _prev = _cm2 = _cm3 = _hwnd = IntPtr.Zero;
        }

        [UnmanagedCallersOnly]
        private static IntPtr Proc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg is WM_INITMENUPOPUP or WM_DRAWITEM or WM_MEASUREITEM or WM_MENUCHAR)
            {
                // WM_INITMENUPOPUP is a notification — the extension fills the submenu as a
                // side effect and the window procedure still owes a zero. The other three ask
                // a question, and the extension's answer is the return value.
                if (_cm3 != IntPtr.Zero)
                {
                    IntPtr answer = IntPtr.Zero;
                    if (HandleMenuMsg2(_cm3, msg, wParam, lParam, &answer) >= 0)
                        return msg == WM_INITMENUPOPUP ? IntPtr.Zero : answer;
                }
                else if (_cm2 != IntPtr.Zero && HandleMenuMsg(_cm2, msg, wParam, lParam) >= 0)
                {
                    return msg == WM_INITMENUPOPUP ? IntPtr.Zero : 1;
                }
            }
            return CallWindowProcW(_prev, hwnd, msg, wParam, lParam);
        }
    }

    // ── COM vtable calls ─────────────────────────────────────────────────
    // Slot numbers are the interface's own methods offset by IUnknown's three. Getting one
    // wrong calls a different method with the wrong arguments, so they are spelled out.
    private const int SlotRelease = 2;
    private const int SlotGetUIObjectOf = 10;      // IShellFolder, 8th of its own
    private const int SlotQueryContextMenu = 3;    // IContextMenu, 1st of its own
    private const int SlotInvokeCommand = 4;
    private const int SlotHandleMenuMsg = 6;       // IContextMenu2, 1st past IContextMenu
    private const int SlotHandleMenuMsg2 = 7;      // IContextMenu3, 1st past IContextMenu2

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void* Slot(IntPtr obj, int index) => (*(void***)obj)[index];

    private static uint Release(IntPtr obj) =>
        ((delegate* unmanaged<IntPtr, uint>)Slot(obj, SlotRelease))(obj);

    private static IntPtr QueryInterfaceOrZero(IntPtr obj, Guid iid)
    {
        var fn = (delegate* unmanaged<IntPtr, Guid*, IntPtr*, int>)Slot(obj, 0);
        IntPtr result = IntPtr.Zero;
        return fn(obj, &iid, &result) >= 0 ? result : IntPtr.Zero;
    }

    private static int GetUIObjectOf(IntPtr folder, IntPtr owner, uint count, IntPtr* apidl,
        Guid* riid, uint* reserved, out IntPtr ppv)
    {
        var fn = (delegate* unmanaged<IntPtr, IntPtr, uint, IntPtr*, Guid*, uint*, IntPtr*, int>)
            Slot(folder, SlotGetUIObjectOf);
        IntPtr result;
        int hr = fn(folder, owner, count, apidl, riid, reserved, &result);
        ppv = result;
        return hr;
    }

    private static int QueryContextMenu(IntPtr pcm, IntPtr menu, uint index, uint idFirst, uint idLast, uint flags) =>
        ((delegate* unmanaged<IntPtr, IntPtr, uint, uint, uint, uint, int>)Slot(pcm, SlotQueryContextMenu))
            (pcm, menu, index, idFirst, idLast, flags);

    private static int InvokeCommand(IntPtr pcm, CMINVOKECOMMANDINFO* ici) =>
        ((delegate* unmanaged<IntPtr, CMINVOKECOMMANDINFO*, int>)Slot(pcm, SlotInvokeCommand))(pcm, ici);

    private static int HandleMenuMsg(IntPtr cm2, uint msg, IntPtr wParam, IntPtr lParam) =>
        ((delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, int>)Slot(cm2, SlotHandleMenuMsg))
            (cm2, msg, wParam, lParam);

    private static int HandleMenuMsg2(IntPtr cm3, uint msg, IntPtr wParam, IntPtr lParam, IntPtr* result) =>
        ((delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr*, int>)Slot(cm3, SlotHandleMenuMsg2))
            (cm3, msg, wParam, lParam, result);

    // ── Interop ──────────────────────────────────────────────────────────
    private static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu2 = new("000214F4-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu3 = new("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719");

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;         // LPCSTR, or a command offset in the low word
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
    }

    private const uint IdFirst = 1;
    private const uint IdLast = 0x7FFF;
    private const uint CMF_NORMAL = 0x0000;
    private const uint CMF_EXPLORE = 0x0004;      // "this is a folder view" — gives Explorer's set
    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const int SW_SHOWNORMAL = 1;
    private const int GWLP_WNDPROC = -4;
    private const uint WM_INITMENUPOPUP = 0x0117;
    private const uint WM_DRAWITEM = 0x002B;
    private const uint WM_MEASUREITEM = 0x002C;
    private const uint WM_MENUCHAR = 0x0120;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl,
        uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, in Guid riid, out IntPtr ppv, out IntPtr pidlLast);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr p);

    /// <summary>Map a point in the active window's client area (physical pixels) to the screen
    /// coordinates <c>TrackPopupMenuEx</c> wants. Falls through unchanged off Windows.</summary>
    public static (int X, int Y) ClientToScreen(int clientX, int clientY)
    {
        if (!OperatingSystem.IsWindows()) return (clientX, clientY);
        var pt = new POINT { X = clientX, Y = clientY };
        return ClientToScreen(GetActiveWindow(), ref pt) ? (pt.X, pt.Y) : (clientX, clientY);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT pt);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hWnd, IntPtr tpm);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProcW(IntPtr prev, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
