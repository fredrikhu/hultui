using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.WebView;

namespace HultUI.Samples.WebView;

// Inside a HultUI.* namespace bare `Text` binds to the HultUI.Text namespace.
using Text = HultUI.Core.Builders.Text;

/// <summary>
/// The browser window: a toolbar (back, forward, reload or stop, the address, a dropdown
/// that opens over the page), the page, and a status line with the last message the page
/// sent. Built as a class with <see cref="Mount"/> so tests can host it on the fake engine.
/// </summary>
public sealed class WebViewSampleApp : IDisposable
{
    /// <summary>
    /// The start page. Its button posts a message to the app, and it shows what the app
    /// posts back — the two directions of the bridge. <c>chrome.webview</c> is WebView2's;
    /// the CEF engine provides the same object.
    /// </summary>
    public const string HomePage = """
        <!doctype html>
        <html><head><title>Home</title><style>
          body { font: 16px system-ui, sans-serif; margin: 2rem; color: #1e293b; }
          button { font: inherit; padding: .4rem 1rem; }
          #from-app { margin-top: 1rem; color: #64748b; }
          #drop { margin-top: 1.5rem; padding: 1rem; border: 2px dashed #94a3b8; border-radius: 8px; color: #64748b; }
          #drop.over { border-color: #2563eb; color: #2563eb; }
          #order li { cursor: grab; padding: .2rem 0; }
        </style></head><body>
          <h1>HultUI web view</h1>
          <p>Type an address above, or talk to the app:</p>
          <button id="ping">Send a message to the app</button>
          <p id="from-app">Nothing from the app yet.</p>
          <div id="drop">Drop files here, from outside the app.</div>
          <p>Drag to reorder, inside the page:</p>
          <ol id="order"><li draggable="true">First</li><li draggable="true">Second</li><li draggable="true">Third</li></ol>
          <script>
            const bridge = window.chrome && window.chrome.webview;
            let clicks = 0;
            document.getElementById('ping').onclick = () =>
              bridge && bridge.postMessage({ clicked: ++clicks });
            bridge && bridge.addEventListener('message', e =>
              document.getElementById('from-app').textContent = 'The app says: ' + JSON.stringify(e.data));

            // Files from outside: the drop zone lists what it was given.
            const drop = document.getElementById('drop');
            drop.ondragover = e => { e.preventDefault(); drop.classList.add('over'); };
            drop.ondragleave = () => drop.classList.remove('over');
            drop.ondrop = e => {
              e.preventDefault();
              drop.classList.remove('over');
              drop.textContent = 'Dropped: ' + ([...e.dataTransfer.files].map(f => f.name).join(', ') || e.dataTransfer.getData('text'));
            };

            // A drag that starts in the page: HTML5 drag and drop, as a sortable list does it.
            let dragged = null;
            for (const li of document.querySelectorAll('#order li')) {
              li.ondragstart = e => { dragged = li; e.dataTransfer.setData('text/plain', li.textContent); };
              li.ondragover = e => e.preventDefault();
              li.ondrop = e => { e.preventDefault(); if (dragged && dragged !== li) li.before(dragged); };
            }
          </script>
        </body></html>
        """;

    private readonly Color _muted = new(100, 116, 139);

    /// <param name="devTools">Allows the page's developer tools: F12 in the page, and a
    /// toolbar button that opens them from code (<c>--devtools</c>).</param>
    public WebViewSampleApp(IWebEngine engine, bool devTools = false)
    {
        Web = new WebViewController(engine, new WebViewOptions { DevTools = devTools });
        Web.MessageReceived += m => LastMessage.Value = m.Json;
        Web.NewWindowRequested += r => Web.Navigate(r.Uri);   // keep it in this window
        Web.DownloadRequested += OnDownload;
        Web.NavigateToString(HomePage);
    }

    /// <summary>What the last download is doing, for the status line.</summary>
    public HultUI.Core.Reactivity.Signal<string?> DownloadStatus { get; } = new(null);

    /// <summary>
    /// A download is cancelled unless the app says where it goes. This one asks: a save dialog,
    /// then progress in the status line. The answer comes after the dialog, so the request is
    /// deferred, and the dialog's continuation comes back to the UI thread to give it.
    /// </summary>
    private void OnDownload(WebDownloadRequest request)
    {
        if (Page?.OverlayHost is not Window window) return;
        request.Defer();
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        window.FileDialogs.SaveFileAsync(request.SuggestedFileName, downloads).ContinueWith(chosen =>
            Application.InvokeOnMainThread(() =>
            {
                if (chosen.Result is not { } path) { request.Cancel(); return; }
                request.SaveTo(path).Changed += d => DownloadStatus.Value = d.State switch
                {
                    WebDownloadState.InProgress => d.TotalBytes is > 0 and var total
                        ? $"Downloading {Path.GetFileName(d.Path)}: {100 * d.BytesReceived / total} %"
                        : $"Downloading {Path.GetFileName(d.Path)}: {d.BytesReceived / 1024} KB",
                    WebDownloadState.Completed => $"Saved {d.Path}",
                    WebDownloadState.Failed => $"The download of {Path.GetFileName(d.Path)} failed",
                    _ => null,
                };
            }));
    }

    public WebViewController Web { get; }

    /// <summary>The last message the page posted, as JSON.</summary>
    public HultUI.Core.Reactivity.Signal<string?> LastMessage { get; } = new(null);

    public WebViewElement Page { get; private set; } = null!;

    public Element Mount()
    {
        Page = WebViewElement.New(Web).FlexGrow(1).FlexBasis(0);
        Page.AutomationId = "page"; // how a UI Automation client finds the page's place in the tree
        return Flex.Column().Children(
            new Toolbar(this).Mount().FlexShrink(0),
            Page,
            new StatusLine(this).Mount().FlexShrink(0));
    }

    /// <summary>Goes to what was typed: an absolute address as is, anything else over https.</summary>
    public void Go(string typed)
    {
        typed = typed.Trim();
        if (typed.Length == 0) return;
        Web.Navigate(AddressOf(typed));
    }

    public static Uri AddressOf(string typed) =>
        Uri.TryCreate(typed, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "file" or "about"
            ? uri
            : new Uri("https://" + typed);

    public void Dispose() => Web.Dispose();

    private sealed class Toolbar(WebViewSampleApp app) : Component
    {
        public override Element Build()
        {
            var web = app.Web;
            var address = TextInput.New(web.Source is { Scheme: not "about" } s ? s.ToString() : "").FlexGrow(1);
            address.AccessibleName = "Address";
            // CurrentTarget, not `address`: after a rebuild the live element is the first
            // build's, and this closure's is a discarded twin with stale text.
            address.OnKeyDown(e =>
            {
                if (e.Key == KeyCode.Enter && e.CurrentTarget is TextInputElement live) app.Go(live.Text);
            });

            var back = Button.New("◀", ButtonVariant.Secondary).OnClick(_ => web.GoBack());
            back.AccessibleName = "Back";
            back.IsEnabled = web.CanGoBack;
            var forward = Button.New("▶", ButtonVariant.Secondary).OnClick(_ => web.GoForward());
            forward.AccessibleName = "Forward";
            forward.IsEnabled = web.CanGoForward;
            var reload = web.IsLoading
                ? Button.New("✕", ButtonVariant.Secondary).OnClick(_ => web.Stop())
                : Button.New("↻", ButtonVariant.Secondary).OnClick(_ => web.Reload());
            reload.AccessibleName = web.IsLoading ? "Stop" : "Reload";
            var send = Button.New("Send to page", ButtonVariant.Secondary)
                .OnClick(_ => web.PostMessage($"{{\"time\":\"{DateTime.Now:HH:mm:ss}\"}}"));
            // Only where it would work: the options allow it and the engine can.
            Element devTools = web.Options.DevTools && (web.Capabilities & WebEngineCapabilities.DevTools) != 0
                ? Button.New("DevTools", ButtonVariant.Secondary).OnClick(_ => web.OpenDevTools())
                : Box.New();

            return Flex.Row(gap: 8).Padding(8f).AlignItems(AlignItems.Center).Children(
                back, forward, reload, address, send, devTools,
                // Opens over the page: the reason the page is drawn rather than a child window.
                Dropdown.New(["Menus open over the page", "…like this one", "…and this"], selectedIndex: 0));
        }
    }

    private sealed class StatusLine(WebViewSampleApp app) : Component
    {
        public override Element Build()
        {
            var web = app.Web;
            string state = web.State switch
            {
                WebViewState.Initializing => "Starting the engine…",
                WebViewState.Failed => "The engine failed: " + web.Error,
                WebViewState.Crashed => "The page crashed — reload to recover",
                _ => web.IsLoading ? "Loading…" : web.Title,
            };
            string message = app.DownloadStatus.Value
                ?? (app.LastMessage.Value is { } json ? "Page said: " + json : "");
            return Flex.Row(gap: 16).Padding(new Thickness(8f, 4f)).Children(
                Text.New(state).FontSize(12f).TextColor(app._muted).FlexGrow(1),
                Text.New(message).FontSize(12f).TextColor(app._muted));
        }
    }
}
