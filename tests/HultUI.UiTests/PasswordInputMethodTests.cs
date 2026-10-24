using System.Runtime.InteropServices;
using HultUI.Core.Platform;
using HultUI.Platform.SDL3;
using HultUI.WinForms.Services;
using SDL;
using Xunit;
using static SDL.SDL3;

namespace HultUI.UiTests;

/// <summary>
/// A password field keeps the input method out of the window on Windows, which SDL does not do
/// for it: its Windows backend reads no text-input type, and gave a password field the same IME
/// as any other — composing the password in the IME's own window and learning it. Only a real
/// window can show this: the input context is the window's, associated by SDL or by the
/// WinForms host.
/// </summary>
[Collection("ui")] // one Application per process, and SDL is process-global
public sealed unsafe class PasswordInputMethodTests
{
    [UiFact]
    public void APasswordField_LeavesTheWindowWithNoInputContext_EvenAfterRegainingFocus()
    {
        using var guard = ForegroundGuard.Begin();
        var platform = new SdlWindowingPlatform();
        using var app = new Application(platform, "ime", 320, 200);
        app.RunFrame(TimeSpan.Zero, forceRender: true);
        var hwnd = app.PrimaryWindow.NativeHandle;
        var host = app.PrimaryWindow.TextInputHost;

        // The control: an ordinary field gets SDL's input context. Without one the assertions
        // below that there is none would prove nothing.
        host.StartTextInput(TextInputKind.Text);
        var sdlContext = Context(hwnd);
        Assert.NotEqual(0, sdlContext);

        host.StartTextInput(TextInputKind.Password);
        Assert.Equal(0, Context(hwnd));

        // The next ordinary field has its input method back.
        host.StartTextInput(TextInputKind.Text);
        Assert.Equal(sdlContext, Context(hwnd));

        // Regaining keyboard focus, SDL starts text input over and associates its context
        // again; the focus-gained event that follows has to take it off once more.
        host.StartTextInput(TextInputKind.Password);
        ImmAssociateContext(hwnd, sdlContext);
        Assert.Equal(sdlContext, Context(hwnd));
        var focus = new SDL_Event
        {
            window = new SDL_WindowEvent { type = SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED, windowID = WindowId() },
        };
        Assert.True(SDL_PushEvent(&focus));
        platform.PumpEvents(null);
        Assert.Equal(0, Context(hwnd));

        host.StopTextInput();
    }

    /// <summary>
    /// The WinForms host drives IMM32 itself. A window starts out holding the thread's default
    /// input context, which disarming leaves alone when nothing was armed — so a password field
    /// focused first would have kept the default IME.
    /// </summary>
    [UiFact]
    public void AHostedControl_HasNoInputContextForAPassword_EvenOneNeverArmed()
    {
        using var guard = ForegroundGuard.Begin();
        var hwnd = CreateWindowExW(0, "STATIC", null, unchecked((int)0x80000000) /* WS_POPUP */,
            0, 0, 10, 10, 0, 0, 0, 0);
        Assert.NotEqual(0, hwnd);
        try
        {
            using var host = new WinFormsTextInputHost(() => hwnd, () => 1f);
            Assert.NotEqual(0, Context(hwnd)); // the control: the default context is there

            host.StartTextInput(TextInputKind.Password);
            Assert.Equal(0, Context(hwnd));

            host.StartTextInput(TextInputKind.Text);
            Assert.NotEqual(0, Context(hwnd));

            host.StartTextInput(TextInputKind.Password);
            Assert.Equal(0, Context(hwnd));
        }
        finally { DestroyWindow(hwnd); }
    }

    private static SDL_WindowID WindowId()
    {
        int count;
        var windows = SDL_GetWindows(&count);
        try
        {
            Assert.Equal(1, count);
            return SDL_GetWindowID(windows[0]);
        }
        finally { SDL_free(windows); }
    }

    private static nint Context(nint hwnd)
    {
        var context = ImmGetContext(hwnd);
        if (context != 0) ImmReleaseContext(hwnd, context);
        return context;
    }

    [DllImport("imm32.dll")] private static extern nint ImmGetContext(nint hwnd);
    [DllImport("imm32.dll")] private static extern bool ImmReleaseContext(nint hwnd, nint context);
    [DllImport("imm32.dll")] private static extern nint ImmAssociateContext(nint hwnd, nint context);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(int exStyle, string cls, string? name, int style,
        int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
}
