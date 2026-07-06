using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Platform.Accessibility.Windows;
using SDL;
using static SDL.SDL3;

namespace HultUI;

/// <summary>
/// Hooks the Windows UIA bridge into our SDL-based event loop.
///
/// UIA clients call provider methods from worker threads, which means we must
/// either snapshot everything (we do, for reads) or marshal writes onto the
/// UI thread. This adapter uses a <see cref="ConcurrentQueue{T}"/> drained at
/// the start of each frame, so element mutations happen on the same thread as
/// the render loop.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class Sdl3UiaHostBridge : IUiaHostBridge, IDisposable
{
    private readonly Window _window;
    private readonly ConcurrentQueue<Action> _pendingActions = new();
    private UiaMessageHandler? _handler;
    private GCHandle _gcHandle;
    private bool _hookInstalled;

    public Sdl3UiaHostBridge(Window window)
    {
        _window = window;
    }

    /// <summary>Called by <see cref="Window"/> once per frame to run queued actions on the UI thread.</summary>
    public void DrainPendingActions()
    {
        while (_pendingActions.TryDequeue(out var action))
        {
            try { action(); }
            catch { /* never let AT clients crash the UI loop */ }
        }
    }

    public unsafe void InstallMessageHook(UiaMessageHandler handler)
    {
        _handler = handler;
        _gcHandle = GCHandle.Alloc(this);
        SDL_SetWindowsMessageHook(&MessageHookCallback, GCHandle.ToIntPtr(_gcHandle));
        _hookInstalled = true;
    }

    public unsafe void UninstallMessageHook()
    {
        if (!_hookInstalled) return;
        SDL_SetWindowsMessageHook(null, IntPtr.Zero);
        if (_gcHandle.IsAllocated) _gcHandle.Free();
        _hookInstalled = false;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static unsafe SDLBool MessageHookCallback(IntPtr userdata, MSG* msg)
    {
        // This runs for every Win32 message. An exception escaping an
        // [UnmanagedCallersOnly] method tears down the runtime, and a stale
        // GCHandle during teardown would throw — guard both. We always return
        // true ("continue dispatching") so UIA's WM_GETOBJECT still reaches the
        // normal window proc regardless of what the handler did.
        try
        {
            if (userdata == IntPtr.Zero) return (SDLBool)true;
            var handle = GCHandle.FromIntPtr(userdata);
            if (!handle.IsAllocated || handle.Target is not Sdl3UiaHostBridge self)
                return (SDLBool)true;
            if (self._handler is null || msg is null)
                return (SDLBool)true;

            self._handler(msg->hwnd, msg->message, (IntPtr)msg->wParam, msg->lParam);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[HultUI] UIA message hook failed: {ex}");
        }
        return (SDLBool)true;
    }

    // ──── Actions — enqueue for UI-thread drain

    public void InvokeElement(ElementId id) =>
        _pendingActions.Enqueue(() => EventDispatcher.DispatchDirect(new ClickEvent { Position = default }, GetElementOrNull(id) ?? Throw()));

    public void ToggleElement(ElementId id) => _pendingActions.Enqueue(() =>
    {
        if (GetElementOrNull(id) is CheckboxElement cb)
        {
            cb.IsChecked = !cb.IsChecked;
            cb.OnChanged?.Invoke(cb.IsChecked);
        }
    });

    public void SelectElement(ElementId id) => _pendingActions.Enqueue(() =>
    {
        if (GetElementOrNull(id) is RadioButtonElement rb)
        {
            if (!rb.IsChecked)
            {
                rb.IsChecked = true;
                rb.OnChanged?.Invoke(true);
            }
        }
    });

    public void SetValue(ElementId id, string value) => _pendingActions.Enqueue(() =>
    {
        switch (GetElementOrNull(id))
        {
            case TextInputElement input: input.Text = value; input.TextChanged?.Invoke(value); break;
            case TextAreaElement area: area.Text = value; area.TextChanged?.Invoke(value); break;
        }
    });

    public void RequestFocus(ElementId id) => _pendingActions.Enqueue(() =>
    {
        GetElementOrNull(id)?.Focus();
    });

    // ──── Reads — safe to run on the calling thread since we only touch
    //      simple CLR properties that don't mutate collections.

    public string? GetValue(ElementId id) => GetElementOrNull(id) switch
    {
        TextInputElement input => input.Text,
        TextAreaElement area => area.Text,
        _ => null,
    };

    public bool? GetToggleState(ElementId id) => GetElementOrNull(id) switch
    {
        CheckboxElement cb => cb.IsChecked,
        RadioButtonElement rb => rb.IsChecked,
        _ => null,
    };

    private Element? GetElementOrNull(ElementId id) => _window.FindElement(id);

    private static Element Throw() => throw new InvalidOperationException("Element id no longer in tree");

    public void Dispose()
    {
        UninstallMessageHook();
        _pendingActions.Clear();
    }
}
