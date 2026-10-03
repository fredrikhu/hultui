using HultUI.WebView.Cef.Interop;

namespace HultUI.WebView.Cef;

/// <summary>
/// Developer tools on Linux (decided 2026-10-03): CEF's own DevTools, in a native top-level
/// window of their own (<c>show_dev_tools</c>), not drawn by HultUI. Opened by
/// <see cref="OpenDevTools"/>, by F12 or Ctrl+Shift+I in the page, and by the context menu's
/// "Inspect". Only for a page whose <see cref="WebViewOptions.DevTools"/> is on.
/// </summary>
internal sealed unsafe partial class CefView
{
    /// <summary>The DevTools browser's client: nothing to handle, but CEF wants one of its own.</summary>
    private cef_client_t* _inspectorClient;

    public void OpenDevTools() => OpenDevToolsAt(null);

    /// <summary>Opens the tools, inspecting the element at <paramref name="at"/> (view
    /// coordinates, CSS pixels) when given. A second call shows the window already open. Any thread.</summary>
    private void OpenDevToolsAt((int X, int Y)? at)
    {
        if (!_options.DevTools) return;
        OnUi(() =>
        {
            if (_browserHost == null) return;
            var host = _browserHost;
            if (_inspectorClient == null) _inspectorClient = CefObject.New<cef_client_t>(this);
            var info = new cef_window_info_t { size = (ulong)sizeof(cef_window_info_t) };
            // A top-level window of its own: no parent, not windowless. CEF sizes it.
            info.bounds = new cef_rect_t { x = 0, y = 0, width = 1000, height = 700 };
            var settings = new cef_browser_settings_t { size = (ulong)sizeof(cef_browser_settings_t) };
            if (at is { } point)
            {
                var p = new cef_point_t { x = point.X, y = point.Y };
                host->show_dev_tools(host, &info, CefObject.Give(_inspectorClient), &settings, &p);
            }
            else host->show_dev_tools(host, &info, CefObject.Give(_inspectorClient), &settings, null);
        });
    }

    /// <summary>CEF's UI thread: whether this page's tools are open.</summary>
    internal bool HasDevToolsWindow => _browserHost != null && _browserHost->has_dev_tools(_browserHost) != 0;

    /// <summary>CEF's UI thread, as the page closes: the tools go with it.</summary>
    private void CloseDevToolsWindow()
    {
        if (_browserHost != null && _browserHost->has_dev_tools(_browserHost) != 0)
            _browserHost->close_dev_tools(_browserHost);
    }

    /// <summary>CEF's UI thread, when the page is gone.</summary>
    private void ReleaseDevToolsWindow()
    {
        CefObject.Release(_inspectorClient);
        _inspectorClient = null;
    }
}
