using System.Runtime.InteropServices;
using System.Text;

namespace WebView2Capture;

internal static partial class Native
{
    // ── Windows.UI.Composition needs a DispatcherQueue on the thread ──

    [StructLayout(LayoutKind.Sequential)]
    public struct DispatcherQueueOptions
    {
        public int dwSize;
        public int threadType;     // DQTYPE_THREAD_CURRENT = 2
        public int apartmentType;  // DQTAT_COM_STA = 2
    }

    [DllImport("CoreMessaging.dll")]
    public static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);

    // ── D3D11 device → WinRT IDirect3DDevice ──

    [DllImport("d3d11.dll")]
    public static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    // ── Window inspection for the popup and focus-window probes ──

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; public override string ToString() => $"({Left},{Top})-({Right},{Bottom}) {Right - Left}x{Bottom - Top}"; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, nint lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(nint parent, EnumWindowsProc proc, nint lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(nint hwnd, ref POINT point);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(nint hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] public static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll")] public static extern nint GetFocus();
    [DllImport("user32.dll")] public static extern nint SetFocus(nint hwnd);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();

    public static string ClassOf(nint hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}

// ── UI Automation, just enough to ask the page's provider where it thinks it lives ──

[StructLayout(LayoutKind.Sequential)]
internal struct UiaRect { public double Left, Top, Width, Height; public override string ToString() => $"({Left},{Top}) {Width}x{Height}"; }

[ComImport, Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRawElementProviderSimple
{
    int ProviderOptions { get; }
    [return: MarshalAs(UnmanagedType.IUnknown)] object? GetPatternProvider(int patternId);
    object? GetPropertyValue(int propertyId);
    IRawElementProviderSimple? HostRawElementProvider { get; }
}

[ComImport, Guid("620ce2a5-ab8f-40a9-86cb-de3c75599b58"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRawElementProviderFragmentRoot
{
    [return: MarshalAs(UnmanagedType.IUnknown)] object? ElementProviderFromPoint(double x, double y);
    [return: MarshalAs(UnmanagedType.IUnknown)] object? GetFocus();
}

[ComImport, Guid("f7063da8-8359-439c-9297-bbc5299a7d87"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRawElementProviderFragment
{
    [return: MarshalAs(UnmanagedType.IUnknown)] object? Navigate(int direction);
    nint GetRuntimeId();
    UiaRect BoundingRectangle { get; }
    nint GetEmbeddedFragmentRoots();
    void SetFocus();
    object? FragmentRoot { [return: MarshalAs(UnmanagedType.IUnknown)] get; }
}
