using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.WebView;
using HultUI.WebView.Cef;
using HultUI.WebView.WebView2;
using Meridian.Core;
using Meridian.Theme;

namespace Meridian;

public partial class MeridianApp
{
    // ── The body's web preview: an HTML response, rendered ───────────────
    //
    // An API's HTML is untrusted, so the page is shown as a string document under a policy that
    // lets nothing run and nothing load: no scripts, no requests (images, styles, fonts, frames,
    // fetch), only the page's own inline styles and data: images. A link, a form or a refresh
    // that would leave the document is refused. Pretty and raw stay the way to read the bytes.

    /// <summary>The policy the preview's document carries ahead of its own markup. A page's own
    /// policy can only narrow it: every policy a document has must allow a load.</summary>
    internal const string PreviewPolicy =
        "default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src data:";

    private IWebEngine? _webEngine;
    private readonly bool _ownsWebEngine;
    private bool _webEngineTried;
    private WebViewController? _preview;
    private HttpResponseData? _previewShown;

    /// <summary>The preview's page, once one was made: for tests.</summary>
    internal WebViewController? PreviewWeb => _preview;

    // ── The page off screen ──────────────────────────────────────────────
    //
    // The page stays while the user is on pretty, raw, another response or another view, so a
    // toggle back to web is instant. Off screen its GPU memory goes at once (the web view gives it
    // back), but the browser's processes stay: six of them, about 300 MB. After a minute off screen
    // the page goes too, and the next toggle starts the browser again (plan 17 round 9: the first
    // frame 0.4–0.7 s after the document goes in). One timer, armed when the page leaves the screen:
    // an idle window wakes once for it, at the minute, and not at all while the page is shown.

    /// <summary>How long the preview's page is kept off screen.</summary>
    internal static readonly TimeSpan PreviewKeptHidden = TimeSpan.FromSeconds(60);

    /// <summary>The clock the minute is timed on: the application's unless a test sets one it moves
    /// by hand.</summary>
    internal TimeProvider? PreviewClock { get; set; }

    private TimeProvider Clock => PreviewClock ?? _app.Time;

    // The page's elements a build made, kept while on screen: the one in the window and the newest.
    // On screen is IsHosted, not mounted: a rebuild's twin is mounted too, and never unmounted.
    private readonly List<PreviewPage> _previewPages = [];
    private long _previewHiddenAt;       // Clock's timestamp when the page last left the screen
    private ITimer? _previewLetGo;

    private bool PreviewOnScreen(PreviewPage? besides = null)
    {
        foreach (var page in _previewPages)
            if (page != besides && page.IsHosted) return true;
        return false;
    }

    /// <summary>The preview's page in the tree: it says when it leaves.</summary>
    private sealed class PreviewPage(MeridianApp app) : WebViewElement
    {
        protected override void OnUnmounted()
        {
            base.OnUnmounted();
            app.PreviewUnmounted(this);
        }
    }

    private void PreviewUnmounted(PreviewPage page)
    {
        if (_preview is null || PreviewOnScreen(besides: page)) return;
        _previewHiddenAt = Clock.GetTimestamp();
        // On the UI thread, where the page belongs: the timer fires on the pool.
        _previewLetGo ??= Clock.CreateTimer(static state =>
            HultUI.Application.InvokeOnMainThread(((MeridianApp)state!).LetPreviewGo),
            this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _previewLetGo.Change(PreviewKeptHidden, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The minute ran out. Shown again since, the page is kept (its next leaving starts
    /// the minute again); hidden again since, the minute is waited out from then.</summary>
    private void LetPreviewGo()
    {
        if (_preview is null || PreviewOnScreen()) return;
        var left = PreviewKeptHidden - Clock.GetElapsedTime(_previewHiddenAt);
        if (left > TimeSpan.Zero)
        {
            _previewLetGo?.Change(left, Timeout.InfiniteTimeSpan);
            return;
        }
        _preview.Dispose();
        _preview = null;
        _previewShown = null;
        _previewPages.Clear();
    }

    private static bool IsHtml(HttpResponseData response) =>
        response.Body.Kind == BodyKind.Text
        && response.ContentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The engine, made the first time a preview is asked for: it owns a browser process.
    /// WebView2 on Windows, CEF on Linux; none on macOS yet.</summary>
    private IWebEngine? PreviewEngine()
    {
        if (_webEngine != null || _webEngineTried) return _webEngine;
        _webEngineTried = true;
        _webEngine = OperatingSystem.IsWindows() ? new WebView2Engine()
            : OperatingSystem.IsLinux() ? new CefEngine()
            : null;
        return _webEngine;
    }

    /// <summary>
    /// The HTML with <see cref="PreviewPolicy"/> as a <c>&lt;meta&gt;</c> before any of its own
    /// markup, so it holds before the first script could run. After a doctype rather than ahead
    /// of it: a document whose doctype isn't first renders in quirks mode.
    /// </summary>
    internal static string PreviewDocument(string html)
    {
        string meta = $"<meta http-equiv=\"Content-Security-Policy\" content=\"{PreviewPolicy}\">";
        int start = 0;
        while (start < html.Length && (char.IsWhiteSpace(html[start]) || html[start] == '﻿')) start++;
        if (string.Compare(html, start, "<!doctype", 0, 9, StringComparison.OrdinalIgnoreCase) == 0
            && html.IndexOf('>', start) is var end and >= 0)
            return html[..(end + 1)] + meta + html[(end + 1)..];
        return meta + html;
    }

    private WebViewController NewPreview(IWebEngine engine)
    {
        var web = new WebViewController(engine, new WebViewOptions { MessageOrigins = [] });
        // The string document loads (about: or data:, by engine); nothing else does.
        web.NavigationStarting += request =>
        {
            if (request.Uri.Scheme is not ("about" or "data")) request.Cancel();
        };
        return web;
    }

    private BoxElement BuildWebPreview(HttpResponseData response)
    {
        var col = Flex.Column(gap: 0).FlexGrow(1);
        var engine = PreviewEngine();
        if (engine is not { Availability.IsAvailable: true })
        {
            string reason = engine?.Availability.Reason ?? "there is no web engine for this platform yet";
            col.JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Padding(new Thickness(Azimuth.S4))
                .Children(
                    Text.New("No web preview on this machine").FontSize(12f).TextColor(Azimuth.TextDim),
                    Text.New(reason).FontSize(11f).TextColor(Azimuth.TextFaint).AutomationId("web-unavailable"));
            return col;
        }

        _preview ??= NewPreview(engine);
        if (!ReferenceEquals(_previewShown, response))
        {
            _previewShown = response;
            _preview.NavigateToString(PreviewDocument(response.Body.Text));
        }
        col.AddChild(Flex.Row(gap: 0).Height(22).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .Children(Text.New("rendered without scripts or network — links don't open")
                .FontSize(10.5f).TextColor(Azimuth.TextFaint)));
        var view = new PreviewPage(this) { Controller = _preview }.FlexGrow(1);
        view.AutomationId("body-web-view");
        _previewPages.RemoveAll(static p => !p.IsHosted);
        _previewPages.Add(view);
        col.AddChild(view);
        return col;
    }

    private void DisposePreview()
    {
        _previewLetGo?.Dispose();
        _previewLetGo = null;
        _preview?.Dispose();
        _preview = null;
        if (_ownsWebEngine) _webEngine?.Dispose();
    }
}
