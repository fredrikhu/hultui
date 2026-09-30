using System.Numerics;
using System.Runtime.InteropServices;

namespace HultUI.WebView.WebView2.Interop;

/// <summary>Flat entry points: WebView2Loader, WinRT activation, D3D11, and a little user32.</summary>
internal static unsafe partial class Native
{
    private const string Loader = "WebView2Loader";

    static Native()
    {
        NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, Resolve);
    }

    private static nint Resolve(string name, System.Reflection.Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Loader) return 0;
        return TryLoadLoader(out var handle, out _) ? handle : 0;
    }

    /// <summary>
    /// Finds WebView2Loader.dll: <c>HULTUI_WEBVIEW2_LOADER</c> (a file or a folder), beside
    /// the application, where a framework-dependent build puts the package's copy
    /// (<c>runtimes/&lt;rid&gt;/native</c>), then the host's own probing. Never throws.
    /// </summary>
    internal static bool TryLoadLoader(out nint handle, out string? error)
    {
        error = null;
        var candidates = new List<string>();
        var hint = Environment.GetEnvironmentVariable("HULTUI_WEBVIEW2_LOADER");
        if (!string.IsNullOrEmpty(hint))
            candidates.Add(File.Exists(hint) ? hint : Path.Combine(hint, "WebView2Loader.dll"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "WebView2Loader.dll"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", "WebView2Loader.dll"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "runtimes", "win-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), "native", "WebView2Loader.dll"));

        foreach (var file in candidates)
        {
            if (!File.Exists(file)) continue;
            try
            {
                handle = NativeLibrary.Load(file);
                return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
            {
                error ??= $"{file}: {ex.Message}";
            }
        }
        if (NativeLibrary.TryLoad("WebView2Loader.dll", typeof(Native).Assembly, null, out handle))
        {
            error = null;
            return true;
        }
        error ??= "WebView2Loader.dll was not found beside the application.";
        return false;
    }

    // ──── WebView2Loader ────

    [LibraryImport(Loader)]
    public static partial int GetAvailableCoreWebView2BrowserVersionString(char* browserExecutableFolder, char** versionInfo);

    [LibraryImport(Loader)]
    public static partial int CreateCoreWebView2EnvironmentWithOptions(char* browserExecutableFolder, char* userDataFolder, nint options, nint handler);

    // ──── WinRT ────

    [LibraryImport("combase.dll")]
    public static partial int WindowsCreateString(char* sourceString, uint length, nint* hstring);

    [LibraryImport("combase.dll")]
    public static partial int WindowsDeleteString(nint hstring);

    [LibraryImport("combase.dll")]
    public static partial int RoActivateInstance(nint activatableClassId, nint* instance);

    [LibraryImport("combase.dll")]
    public static partial int RoGetActivationFactory(nint activatableClassId, Guid* iid, nint* factory);

    [StructLayout(LayoutKind.Sequential)]
    public struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;       // DQTYPE_THREAD_CURRENT = 2
        public int ApartmentType;    // DQTAT_COM_STA = 2
    }

    [LibraryImport("CoreMessaging.dll")]
    public static partial int CreateDispatcherQueueController(DispatcherQueueOptions options, nint* controller);

    // ──── D3D11 ────

    public const int D3D_DRIVER_TYPE_HARDWARE = 1;
    public const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    public const uint D3D11_SDK_VERSION = 7;
    public const int DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    public const int D3D11_USAGE_STAGING = 3;
    public const uint D3D11_CPU_ACCESS_READ = 0x20000;
    public const int D3D11_MAP_READ = 1;

    [LibraryImport("d3d11.dll")]
    public static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags,
        int* featureLevels, uint featureLevelCount, uint sdkVersion, nint* device, int* featureLevel, nint* immediateContext);

    [LibraryImport("d3d11.dll")]
    public static partial int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, nint* graphicsDevice);

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_TEXTURE2D_DESC
    {
        public uint Width, Height, MipLevels, ArraySize;
        public int Format;
        public uint SampleCount, SampleQuality;
        public int Usage;
        public uint BindFlags, CPUAccessFlags, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_MAPPED_SUBRESOURCE
    {
        public nint Data;
        public uint RowPitch, DepthPitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_BOX
    {
        public uint Left, Top, Front, Right, Bottom, Back;
    }

    // ──── Shared value types ────

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    /// <summary>Windows.Graphics.SizeInt32.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SizeInt32
    {
        public int Width, Height;
    }

    /// <summary>Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized.</summary>
    public const int DirectXPixelFormat_B8G8R8A8UIntNormalized = 87;

    // ──── user32 ────

    [LibraryImport("user32.dll")]
    public static partial nint GetFocus();

    [LibraryImport("user32.dll")]
    public static partial short GetKeyState(int virtualKey);

    [LibraryImport("user32.dll")]
    public static partial int EnumChildWindows(nint parent, delegate* unmanaged[Stdcall]<nint, nint, int> proc, nint state);

    [LibraryImport("user32.dll")]
    public static partial int GetClassNameW(nint hwnd, char* name, int max);

    [LibraryImport("user32.dll")]
    public static partial int EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> proc, nint state);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, uint* process);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hwnd, out RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(nint hwnd, ref POINT point);

    // ──── comctl32: window subclassing ────

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS
    {
        public nint Hwnd, InsertAfter;
        public int X, Y, Cx, Cy;
        public uint Flags;
    }

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowSubclass(nint hwnd, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id, nuint data);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RemoveWindowSubclass(nint hwnd, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id);

    [LibraryImport("comctl32.dll")]
    public static partial nint DefSubclassProc(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial nint SetFocus(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsChild(nint parent, nint hwnd);

    public static Vector2 Size(int width, int height) => new(width, height);
}
