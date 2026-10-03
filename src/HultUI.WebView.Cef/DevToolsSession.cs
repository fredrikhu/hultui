using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using HultUI.WebView.Cef.Interop;

namespace HultUI.WebView.Cef;

/// <summary>
/// One browser's DevTools protocol session, inside the browser process: how script runs and
/// messages travel, with no code of ours in the renderer (spike L0, question 6; decided
/// 2026-10-02). Commands are raw JSON through <c>send_dev_tools_message</c>; replies and
/// events come back through a <c>cef_dev_tools_message_observer_t</c>, parsed here rather
/// than by CEF, so the messages of child sessions (cross-origin iframes) would parse too.
/// </summary>
/// <remarks>
/// Everything runs on CEF's UI thread: <see cref="Send"/> posts there, because only there does
/// <c>send_dev_tools_message</c> say whether it took the message, and replies and events
/// arrive there.
/// </remarks>
internal sealed unsafe class DevToolsSession : IDisposable
{
    private readonly cef_browser_host_t* _host;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private cef_registration_t* _registration;
    private int _nextId;
    private bool _disposed;

    /// <summary>A protocol event (no id): the method and its parameters. CEF's UI thread.</summary>
    public event Action<string, JsonElement>? EventReceived;

    /// <param name="host">The browser's host, which this session keeps a reference to.</param>
    public DevToolsSession(cef_browser_host_t* host)
    {
        _host = CefObject.AddRef(host);
        var observer = CefObject.New<cef_dev_tools_message_observer_t>(this);
        observer->on_dev_tools_message = &OnMessage;
        _registration = host->add_dev_tools_message_observer(host, observer);   // ours passes over
    }

    /// <summary>
    /// Sends a command; its <c>result</c>, or a fault with the protocol's error. Any thread.
    /// </summary>
    public Task<JsonElement> Send(string method, Action<Utf8JsonWriter>? writeParams = null)
    {
        var done = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        int id = Interlocked.Increment(ref _nextId);
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteNumber("id", id);
            w.WriteString("method", method);
            w.WriteStartObject("params");
            writeParams?.Invoke(w);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        var message = buffer.WrittenSpan.ToArray();
        _pending[id] = done;
        bool posted = CefRuntime.Post(() =>
        {
            if (_disposed) { Fail(id, "The page is closed."); return; }
            int ok;
            fixed (byte* p = message) ok = _host->send_dev_tools_message(_host, p, (ulong)message.Length);
            if (ok == 0) Fail(id, $"CEF did not take the DevTools command {method}.");
        });
        if (!posted) Fail(id, "CEF is not running.");
        return done.Task;
    }

    private void Fail(int id, string why)
    {
        if (_pending.TryRemove(id, out var done)) done.TrySetException(new InvalidOperationException(why));
    }

    private void Receive(ReadOnlySpan<byte> json)
    {
        using var doc = JsonDocument.Parse(json.ToArray());
        var root = doc.RootElement;
        if (root.TryGetProperty("sessionId", out _)) return;   // a child session's; none are made yet
        if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var id))
        {
            if (!_pending.TryRemove(id, out var done)) return;
            if (root.TryGetProperty("error", out var error))
                done.TrySetException(new InvalidOperationException(
                    error.TryGetProperty("message", out var m) ? m.GetString() : error.GetRawText()));
            else
                done.TrySetResult(root.TryGetProperty("result", out var result) ? result.Clone() : default);
        }
        else if (root.TryGetProperty("method", out var method))
        {
            var parameters = root.TryGetProperty("params", out var p) ? p.Clone() : default;
            EventReceived?.Invoke(method.GetString() ?? "", parameters);
        }
    }

    [UnmanagedCallersOnly]
    private static int OnMessage(cef_dev_tools_message_observer_t* self, cef_browser_t* browser, void* message, ulong size)
    {
        try { CefObject.Owner<DevToolsSession>(self)?.Receive(new ReadOnlySpan<byte>(message, (int)size)); }
        catch (Exception ex) { CefLog.Error("DevTools message", ex); }
        finally { CefObject.Release(browser); }
        return 1;   // parsed here; CEF need not parse it again
    }

    /// <summary>CEF's UI thread: stop observing, fail what is still waiting.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_registration != null) { CefObject.Release(_registration); _registration = null; }
        foreach (var id in _pending.Keys) Fail(id, "The page is closed.");
        CefObject.Release(_host);
    }
}
