using System.Runtime.InteropServices;
using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Platform.Accessibility.Windows;
using HultUI.Platform.SDL3;
using HultUI.Rendering.Skia;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Accessibility used to be the privilege of one backend: the factory type-tested
/// <c>SDL3Window</c>, so a HultUI surface hosted on anyone else's window — a WinForms
/// or WPF control — silently got the Null bridge and a screen reader saw nothing
/// there. It now keys on <see cref="IWin32WindowHandle"/> instead.
///
/// <para>These tests pin that selection down from both sides: a non-SDL window with a
/// real handle must get the live bridge, and a window with no handle must still get
/// the Null one. The end-to-end proof that a real UI Automation client can then read
/// the tree lives in <c>HultUI.UiTests</c>.</para>
/// </summary>
public class AccessibilityBridgeSelectionTests
{
    [WindowsFact]
    public void AWindowWithAHandle_GetsTheLiveBridge_EvenThoughItIsNotAnSdlWindow()
    {
        using var hwnd = TestHwnd.Create();
        using var host = HandleWindowHost.Create(hwnd.Handle);

        var bridge = AccessibilityBridgeFactory.Create(host.Application.PrimaryWindow);

        Assert.IsType<WindowsAccessibilityBridge>(bridge);
        (bridge as IDisposable)?.Dispose();
    }

    [WindowsFact]
    public void AWindowWhoseHandleIsZero_StaysOnTheNullBridge()
    {
        // A control whose handle has not been created yet, or has been destroyed.
        using var host = HandleWindowHost.Create(IntPtr.Zero);

        var bridge = AccessibilityBridgeFactory.Create(host.Application.PrimaryWindow);

        Assert.Same(NullAccessibilityBridge.Instance, bridge);
    }

    [Fact]
    public void AHeadlessWindow_StaysOnTheNullBridge()
    {
        // Regression guard: display-free sessions must not acquire a platform bridge,
        // on any OS. On Linux this also keeps CI from probing an AT-SPI bus it has not got.
        using var session = HeadlessSession.Create();

        var bridge = AccessibilityBridgeFactory.Create(session.Window.Window);

        Assert.Same(NullAccessibilityBridge.Instance, bridge);
    }

    [Fact]
    public void TheSdlBackendStillExposesItsHandle()
    {
        // The SDL path reaches the same bridge through the same interface now. Losing
        // this implementation would take accessibility away from every normal window,
        // and nothing else would fail to compile.
        Assert.True(typeof(SDL3Window).IsAssignableTo(typeof(IWin32WindowHandle)));
    }
}

/// <summary>Runs only on Windows; the UIA bridge does not exist elsewhere.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows-only: UI Automation bridge.";
    }
}

/// <summary>A real, hidden Win32 window — the bridge subclasses its window procedure.</summary>
internal sealed class TestHwnd : IDisposable
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(int exStyle, string cls, string? name, int style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);

    public IntPtr Handle { get; private set; }

    private TestHwnd(IntPtr handle) => Handle = handle;

    public static TestHwnd Create()
    {
        // "STATIC" is a pre-registered system class, so no class registration and no
        // window procedure of our own to keep alive.
        var h = CreateWindowExW(0, "STATIC", null, unchecked((int)0x80000000) /* WS_POPUP */,
            0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, h);
        return new TestHwnd(h);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) { DestroyWindow(Handle); Handle = IntPtr.Zero; }
    }
}

/// <summary>
/// An application whose platform window is not the SDL one but does carry an HWND —
/// the shape of a host embedding HultUI in a control it owns. Raster-backed, because
/// what is under test is bridge selection, not drawing.
/// </summary>
internal sealed class HandleWindowHost : IDisposable
{
    public Application Application { get; }

    private HandleWindowHost(Application application) => Application = application;

    public static HandleWindowHost Create(IntPtr hwnd)
        => new(new Application(new HandlePlatform(hwnd), "handle-backed", 200, 100));

    public void Dispose() => Application.Dispose();
}

internal sealed class HandlePlatform(IntPtr hwnd) : IWindowingPlatform
{
    public TimeProvider TimeProvider => TimeProvider.System;

    public IPlatformWindow CreateWindow(string title, int width, int height, WindowStyle style)
        => new HandlePlatformWindow(hwnd, title, width, height, style);

    public bool PumpEvents(Action<IPlatformWindow>? onWindowClose) => true;
    public void WaitForEvents(int timeoutMs) { }
    public void Wake() { }
    public void Dispose() { }
}

internal sealed class HandlePlatformWindow : IPlatformWindow, IWin32WindowHandle
{
    private readonly SkiaRasterRenderSurface _surface = new();

    public HandlePlatformWindow(IntPtr hwnd, string title, int width, int height, WindowStyle style)
    {
        Hwnd = hwnd;
        Title = title;
        Style = style;
        LayoutWidth = width;
        LayoutHeight = height;
        _surface.Resize(width, height, 1f);
        IsOpen = true;
    }

    public nint Hwnd { get; }

    public IRenderSurface Surface => _surface;
    public float LayoutWidth { get; }
    public float LayoutHeight { get; }
    public float DisplayScale => 1f;
    public float RefreshRate => 0f;
    public float UserScale { get; set; } = 1f;
    public WindowStyle Style { get; }
    public bool IsOpen { get; private set; }
    public bool IsMaximized => false;
    public string Title { get; set; }

    public IClipboard Clipboard { get; } = new NoClipboard();
    public IFileDialogs FileDialogs { get; } = new NoFileDialogs();
    public ITextInputHost TextInputHost { get; } = new NoTextInput();

    public event Action<InputEvent>? InputReceived;

    public bool ResizePending { get; set; }
    public bool ActivatedPending { get; set; }
    public long LastResizeRenderTick { get; set; }
    public Action? LiveResizeFrame { get; set; }

    public void MakeCurrent() { }
    public void EnsureShown() { }
    public void SwapBuffers() { }
    public void Close() => IsOpen = false;
    public void Minimize() { }
    public void Maximize() { }
    public void Restore() { }
    public void Raise() { }
    public void SetMinimumSize(int layoutWidth, int layoutHeight) { }
    public void SetIcon(int width, int height, ReadOnlySpan<byte> rgbaPixels) { }
    public void SetCursor(CursorType cursor) { }

    public void Dispose()
    {
        IsOpen = false;
        _surface.Dispose();
        _ = InputReceived;   // the event is part of the contract; nothing raises it here
    }
}

internal sealed class NoClipboard : IClipboard
{
    public string? GetText() => null;
    public void SetText(string text) { }
    public bool HasFormat(string mimeType) => false;
    public byte[]? GetData(string mimeType) => null;
    public void SetData(IReadOnlyDictionary<string, byte[]> data) { }
    public void Clear() { }
}

internal sealed class NoFileDialogs : IFileDialogs
{
    public bool IsAvailable => false;
    public Task<string?> OpenFolderAsync(string? initialDirectory = null, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
    public Task<string?> OpenFileAsync(string? initialDirectory = null, IReadOnlyList<FileDialogFilter>? filters = null,
        CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    public Task<IReadOnlyList<string>> OpenFilesAsync(string? initialDirectory = null,
        IReadOnlyList<FileDialogFilter>? filters = null, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<string?> SaveFileAsync(string? suggestedName = null, string? initialDirectory = null,
        IReadOnlyList<FileDialogFilter>? filters = null, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);
}

internal sealed class NoTextInput : ITextInputHost
{
    public void StartTextInput() { }
    public void StopTextInput() { }
    public void SetInputArea(Rect caretRect, int cursorOffset) { }
    public void ClearComposition() { }
}
