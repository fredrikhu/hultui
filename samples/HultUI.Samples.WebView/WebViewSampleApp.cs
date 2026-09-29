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
    /// the WPE engine provides the same object.
    /// </summary>
    public const string HomePage = """
        <!doctype html>
        <html><head><title>Home</title><style>
          body { font: 16px system-ui, sans-serif; margin: 2rem; color: #1e293b; }
          button { font: inherit; padding: .4rem 1rem; }
          #from-app { margin-top: 1rem; color: #64748b; }
        </style></head><body>
          <h1>HultUI web view</h1>
          <p>Type an address above, or talk to the app:</p>
          <button id="ping">Send a message to the app</button>
          <p id="from-app">Nothing from the app yet.</p>
          <script>
            const bridge = window.chrome && window.chrome.webview;
            let clicks = 0;
            document.getElementById('ping').onclick = () =>
              bridge && bridge.postMessage({ clicked: ++clicks });
            bridge && bridge.addEventListener('message', e =>
              document.getElementById('from-app').textContent = 'The app says: ' + JSON.stringify(e.data));
          </script>
        </body></html>
        """;

    private readonly Color _muted = new(100, 116, 139);

    public WebViewSampleApp(IWebEngine engine)
    {
        Web = new WebViewController(engine);
        Web.MessageReceived += m => LastMessage.Value = m.Json;
        Web.NewWindowRequested += r => Web.Navigate(r.Uri);   // keep it in this window
        Web.NavigateToString(HomePage);
    }

    public WebViewController Web { get; }

    /// <summary>The last message the page posted, as JSON.</summary>
    public HultUI.Core.Reactivity.Signal<string?> LastMessage { get; } = new(null);

    public WebViewElement Page { get; private set; } = null!;

    public Element Mount()
    {
        Page = WebViewElement.New(Web).FlexGrow(1).FlexBasis(0);
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

            return Flex.Row(gap: 8).Padding(8f).AlignItems(AlignItems.Center).Children(
                back, forward, reload, address, send,
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
            string message = app.LastMessage.Value is { } json ? "Page said: " + json : "";
            return Flex.Row(gap: 16).Padding(new Thickness(8f, 4f)).Children(
                Text.New(state).FontSize(12f).TextColor(app._muted).FlexGrow(1),
                Text.New(message).FontSize(12f).TextColor(app._muted));
        }
    }
}
