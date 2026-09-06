using System.Text.Json;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Meridian.Core;
using Meridian.Theme;

namespace Meridian;

public partial class MeridianApp
{
    // ── Find in the response body (Ctrl+F) — the Junction find-bar idiom ─

    private sealed class FindBarState
    {
        public FindBarState(Action onChanged) => Search = new TextSearch(onChanged);

        public readonly TextSearch Search;
        public readonly Ref<TextInputElement> FieldRef = new();
        public bool Open;
        public bool PendingFocus;
        public bool PendingReveal;
        public object? BoundKey;
    }

    private readonly FindBarState _responseFind;
    private readonly Ref<TextAreaElement> _responseBodyRef = new();

    private bool HandleFindBarKey(KeyDownEvent e, FindBarState s)
    {
        bool ctrl = e.Modifiers.HasFlag(KeyModifiers.Ctrl) || e.Modifiers.HasFlag(KeyModifiers.Super);
        bool shift = e.Modifiers.HasFlag(KeyModifiers.Shift);
        bool alt = e.Modifiers.HasFlag(KeyModifiers.Alt);

        if (ctrl && e.Key == KeyCode.F) { s.Open = true; s.PendingFocus = true; MarkDirty(); return true; }
        if (!s.Open) return false;

        switch (e.Key)
        {
            case KeyCode.Escape:
                s.Open = false;
                s.Search.SetQuery("");
                MarkDirty();
                return true;
            case KeyCode.Enter: FindNav(s, shift ? -1 : 1); return true;
            case KeyCode.N when e.Target is not TextInputElement: FindNav(s, 1); return true;
            case KeyCode.P when e.Target is not TextInputElement: FindNav(s, -1); return true;
            default: return false;
        }
    }

    private void FindNav(FindBarState s, int dir)
    {
        s.Search.Move(dir);
        s.PendingReveal = true;
        MarkDirty();
    }

    private void ServiceFindBar(FindBarState s)
    {
        // Ctrl+F on a bar that is already open lands here too. The query is still in the
        // field, and the person pressed the key to search for something else, so it is
        // selected whole: the next keystroke replaces it rather than appending to it. On a
        // freshly opened bar the field is empty and the selection is nothing.
        if (s.PendingFocus && s.FieldRef.Current is { } field)
        {
            s.PendingFocus = false;
            field.Focus();
            field.SelectAll();
        }
        if (s.PendingReveal)
        {
            s.PendingReveal = false;
            _window.RequestPostFrameCallback(() => RevealResponseHit(s));
        }
    }

    /// <summary>
    /// Brings the current match on screen.
    ///
    /// <para>Handed to the body as an absolute offset rather than a line number. The body wraps
    /// at the pane width, and a JSON line is routinely longer than a 560dip pane: one logical
    /// line is then several drawn rows, a line index only names the row the line <i>starts</i>
    /// on, and a match near the end of such a line would be scrolled to just off the bottom.
    /// The offsets are the ones already computed for the tinting, so this costs nothing.</para>
    /// </summary>
    private void RevealResponseHit(FindBarState s)
    {
        if (_responseBodyRef.Current is not { } area) return;
        if (s.Search.Current is not { } hit) return;
        if (hit.Line >= 0 && hit.Line < _respLineStarts.Length)
            area.ScrollOffsetIntoView(_respLineStarts[hit.Line] + hit.Start);
        else
            area.ScrollLineIntoView(hit.Line);   // starts not built yet (nothing displayed)
    }

    // ── The response panel ───────────────────────────────────────────────

    /// <summary>The response region: status line, sub-tabs, and the active
    /// tab's content. Sits right (560dip) or bottom (380dip) of the editor —
    /// the caller decides which, this builds the same panel either way.</summary>
    private Element BuildResponsePanel(RequestTab tab, bool bottom)
    {
        var col = Flex.Column(gap: 0).FlexShrink(0);
        if (bottom)
        {
            col.Height(Azimuth.ResponseBottom)
               .BorderColor(Azimuth.BorderStrong).BorderWidth(new Thickness(0, 2, 0, 0));
        }
        else
        {
            col.Width(Azimuth.ResponseRight)
               .BorderColor(Azimuth.BorderStrong).BorderWidth(new Thickness(2, 0, 0, 0));
        }
        // A role, so the panel itself lands in the accessibility tree (and is
        // findable by automation id — containers without one are invisible there).
        col.AccessibleRole(AccessibilityRole.TabPanel).AccessibleName("Response");
        col.AutomationId(bottom ? "response-bottom" : "response-right");

        col.AddChild(BuildResponseStatusLine(tab));

        if (tab.Sending != null && tab.Result == null)
        {
            col.AddChild(CenterNote("The response lands here."));
            return col;
        }

        if (tab.Result?.Failure is { } failure)
        {
            col.AddChild(BuildFailureBody(failure));
            return col;
        }

        if (tab.Result?.Response is not { } response)
        {
            col.AddChild(CenterNote("Send with Ctrl ↵ to see the response here"));
            return col;
        }

        col.AddChild(BuildResponseTabs(tab, response));

        // A response shown while the next send is in flight: greyed underneath,
        // per the mock — it is the previous answer, not this one's.
        var content = BuildResponseContent(tab, response);
        if (tab.Sending != null) content.Opacity(0.45f);
        var contentBox = Box.New().FlexGrow(1).FlexBasis(Dimension.Px(0));
        contentBox.AddChild(content);
        col.AddChild(contentBox);
        return col;
    }

    private static Element CenterNote(string text) =>
        Flex.Column(gap: 0).FlexGrow(1)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(Text.New(text).FontSize(12f).TextColor(Azimuth.TextFaint));

    private Element BuildResponseStatusLine(RequestTab tab)
    {
        var status = Flex.Row(gap: Azimuth.S3).Height(32).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 0, 1, 0));
        status.AutomationId("response-status");

        if (tab.Sending != null)
        {
            status.AddChild(Text.New("sending…").FontSize(12.5f).TextColor(Azimuth.TextMuted));
            status.AddChild(Ui.Mono($"{SendElapsed(tab).TotalSeconds:0.0} s", 11f, Azimuth.TextMuted));
        }
        else if (tab.Result?.Response is { } response)
        {
            var (glyph, color) = StatusFace(response.StatusCode);
            status.AddChild(Text.New($"{glyph} {response.StatusCode} {response.ReasonPhrase}".TrimEnd())
                .FontSize(12.5f).FontWeight(FontWeight.Medium).TextColor(color));
            status.AddChild(Ui.Mono($"{response.Timings.Total.TotalMilliseconds:0} ms", 11f, Azimuth.TextMuted));
            status.AddChild(Ui.Mono(SizeLabel(response.Body.TotalBytes), 11f, Azimuth.TextMuted));
            if (response.Body.Kind == BodyKind.Binary && response.ContentType is { } ct)
                status.AddChild(Ui.Mono(ct, 11f, Azimuth.TextMuted));
        }
        else if (tab.Result?.Failure is { } failure)
        {
            status.AddChild(Text.New($"! {failure.Headline}").FontSize(12.5f).FontWeight(FontWeight.Medium)
                .TextColor(Azimuth.StatusWarn));
        }
        else
        {
            status.AddChild(Text.New("never sent").FontSize(12f).TextColor(Azimuth.TextFaint));
        }

        status.AddChild(Box.New().FlexGrow(1));
        if (!_forceResponseBottom)
        {
            var flip = Flex.Row(gap: 4).AlignItems(AlignItems.Center)
                .Padding(new Thickness(3, 6, 3, 6))
                .CornerRadius(Azimuth.Radius1)
                .Cursor(CursorType.Pointer)
                .AccessibleRole(AccessibilityRole.Button)
                .AccessibleName(ResponseAtBottom ? "Move response pane right" : "Move response pane to bottom")
                .AutomationId("flip-pane");
            flip.AddChild(Ui.Kbd("Ctrl \\"));
            flip.AddChild(Text.New("flip pane").FontSize(11f).TextColor(Azimuth.TextMuted));
            flip.OnClick(_ => FlipResponsePane());
            flip.HoverBg(Azimuth.Raised, Color.Transparent);
            status.AddChild(flip);
        }
        return status;
    }

    // ── Infra failure states (mock 02's four, with their actions) ────────

    private Element BuildFailureBody(InfraFailure failure)
    {
        var col = Flex.Column(gap: Azimuth.S2).FlexGrow(1)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Padding(new Thickness(Azimuth.S4));

        col.AddChild(Ui.Mono(failure.Detail, 12f, Azimuth.TextDim));

        switch (failure.Kind)
        {
            case InfraKind.DnsFailure:
                col.AddChild(Text.New($"Check the URL{(_envName is { } env ? $", or the variables in env: {env}" : "")}")
                    .FontSize(11.5f).TextColor(Azimuth.TextFaint));
                break;
            case InfraKind.ConnectionRefused:
                col.AddChild(Text.New("Nothing is listening. Is the server up?")
                    .FontSize(11.5f).TextColor(Azimuth.TextFaint));
                break;
            case InfraKind.Timeout:
                col.AddChild(BarAction("retry", "retry-send", SendActive));
                break;
            case InfraKind.TlsFailure when failure.CertThumbprint is { } thumbprint:
                col.AddChild(BarAction("trust for this workspace", "trust-workspace",
                    () => TrustAndResend(thumbprint)));
                break;
        }
        return col;
    }

    // ── Sub-tabs + content ───────────────────────────────────────────────

    private Element BuildResponseTabs(RequestTab tab, HttpResponseData response)
    {
        var bar = Flex.Row(gap: Azimuth.S1).Height(30).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 0, 1, 0));

        int cookieCount = CookiesFor(response).Count;
        bar.AddChild(RespTabButton(tab, ResponseTab.Body, "Body"));
        bar.AddChild(RespTabButton(tab, ResponseTab.Headers, $"Headers {CountLabel(response.Headers.Count)}"));
        bar.AddChild(RespTabButton(tab, ResponseTab.Cookies, $"Cookies {CountLabel(cookieCount)}"));
        bar.AddChild(RespTabButton(tab, ResponseTab.Timeline, "Timeline"));
        bar.AddChild(Box.New().FlexGrow(1));

        if (tab.RespTab == ResponseTab.Body && response.Body.Kind == BodyKind.Text)
        {
            bar.AddChild(PrettyRawToggle(tab, raw: false));
            bar.AddChild(PrettyRawToggle(tab, raw: true));
            bar.AddChild(Box.New().Width(Azimuth.S2));
            bar.AddChild(Ui.Kbd("Ctrl F"));
        }
        return bar;
    }

    private Element RespTabButton(RequestTab tab, ResponseTab which, string label)
    {
        bool active = tab.RespTab == which;
        var b = Box.New()
            .Padding(new Thickness(4, Azimuth.S3, 4, Azimuth.S3))
            .CornerRadius(Azimuth.Radius1)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button).AccessibleName(label.TrimEnd())
            .AutomationId("resptab-" + which.ToString().ToLowerInvariant())
            .Children(Text.New(label.TrimEnd()).FontSize(11.5f)
                .FontWeight(active ? FontWeight.Medium : FontWeight.Regular)
                .TextColor(active ? Azimuth.Text : Azimuth.TextMuted));
        if (active) b.Background(Azimuth.Raised);
        b.OnClick(_ => { tab.RespTab = which; MarkDirty(); });
        if (!active) b.HoverBg(Azimuth.Raised, Color.Transparent);
        return b;
    }

    private Element PrettyRawToggle(RequestTab tab, bool raw)
    {
        bool active = tab.RespRaw == raw;
        var label = raw ? "raw" : "pretty";
        var b = Box.New()
            .Padding(new Thickness(3, Azimuth.S2, 3, Azimuth.S2))
            .CornerRadius(Azimuth.Radius1)
            .BorderColor(active ? Azimuth.Accent700 : Azimuth.Border).BorderWidth(1f)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button).AccessibleName(label)
            .AutomationId("body-" + label)
            .Children(Ui.Mono(label, 10f, active ? Azimuth.Accent300 : Azimuth.TextMuted));
        if (active) b.Background(Azimuth.Tint(Azimuth.Accent));
        b.OnClick(_ => { tab.RespRaw = raw; MarkDirty(); });
        return b;
    }

    private BoxElement BuildResponseContent(RequestTab tab, HttpResponseData response) => tab.RespTab switch
    {
        ResponseTab.Headers => BuildHeadersTable(response),
        ResponseTab.Cookies => BuildCookiesTable(response),
        ResponseTab.Timeline => BuildTimeline(response),
        _ => BuildBodyTab(tab, response),
    };

    // ── Body ─────────────────────────────────────────────────────────────

    private BoxElement BuildBodyTab(RequestTab tab, HttpResponseData response)
    {
        var col = Flex.Column(gap: 0).FlexGrow(1);

        if (_responseFind.Open)
            col.AddChild(BuildFindBar(_responseFind));

        if (response.Body.Truncated)
        {
            var banner = Flex.Row(gap: Azimuth.S3).Height(30).FlexShrink(0)
                .AlignItems(AlignItems.Center)
                .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
                .Background(Azimuth.Alpha(Azimuth.StatusWarn, 0.10));
            banner.AddChild(Text.New($"! showing first {SizeLabel(response.Body.Kind == BodyKind.Text ? response.Body.Text.Length : response.Body.Preview.Length)} of {SizeLabel(response.Body.TotalBytes)}")
                .FontSize(11.5f).TextColor(Azimuth.StatusWarn).AutomationId("truncated-banner"));
            banner.AddChild(Box.New().FlexGrow(1));
            if (response.Body.SpillPath is { } spill)
                banner.AddChild(BarAction("open full", "open-full", () => OpenWithShell(spill)));
            banner.AddChild(BarAction("save to file", "save-body", () => SaveBody(response)));
            col.AddChild(banner);
        }

        switch (response.Body.Kind)
        {
            case BodyKind.None:
                col.AddChild(CenterNote("empty body"));
                break;

            case BodyKind.Binary:
            {
                var box = Flex.Column(gap: Azimuth.S2).FlexGrow(1)
                    .Padding(new Thickness(Azimuth.S2, Azimuth.S3, Azimuth.S2, Azimuth.S3));
                var hex = TextArea.New(HexStub(response.Body.Preview));
                hex.FontFamily(Fonts.Mono).FontSize(11.5f).TextColor(Azimuth.TextDim).FlexGrow(1);
                hex.ReadOnly = true;
                hex.AutomationId("response-hex");
                box.AddChild(hex);
                if (!response.Body.Truncated)
                {
                    var actions = Flex.Row(gap: Azimuth.S2).FlexShrink(0);
                    actions.AddChild(BarAction("save to file", "save-body", () => SaveBody(response)));
                    box.AddChild(actions);
                }
                col.AddChild(box);
                break;
            }

            default:
            {
                var (text, spans) = ResponseBodyTextAndSpans(tab, response);
                var area = TextArea.New(text);
                area.FontFamily(Fonts.Mono).FontSize(12f).TextColor(Azimuth.TextDim).FlexGrow(1);
                area.ReadOnly = true;
                area.Spans = spans;
                area.AutomationId("response-body");
                area.WithRef(_responseBodyRef);
                var box = Flex.Column(gap: 0).FlexGrow(1)
                    .Padding(new Thickness(Azimuth.S2, Azimuth.S3, Azimuth.S2, Azimuth.S3));
                box.AddChild(area);
                col.AddChild(box);
                break;
            }
        }
        return col;
    }

    // The displayed body text + merged spans, memoized per (result, raw-mode):
    // pretty-printing and re-tokenizing on every repaint would drag the pane.
    private object? _respBodyMemoKey;
    private string _respBodyText = "";
    private string[] _respBodyLines = [];
    /// <summary>Character offset where each displayed line begins. Both the search tinting and
    /// the scroll-to-match speak absolute offsets into the flattened body, so this is computed
    /// once with the text rather than rebuilt on every repaint.</summary>
    private int[] _respLineStarts = [];
    private IReadOnlyList<TextSpan>? _respSyntaxSpans;

    private (string Text, IReadOnlyList<TextSpan>? Spans) ResponseBodyTextAndSpans(RequestTab tab, HttpResponseData response)
    {
        var key = (response, tab.RespRaw);
        if (!Equals(_respBodyMemoKey, key))
        {
            _respBodyMemoKey = key;
            string text = tab.RespRaw ? response.Body.Text : Prettify(response);
            text = text.ReplaceLineEndings("\n");
            _respBodyText = text;
            _respBodyLines = text.Split('\n');
            _respLineStarts = LineStarts(_respBodyLines);
            _respSyntaxSpans = SyntaxSpans(text, response.ContentType);

            // (Re)bind the search to the displayed text.
            _responseFind.BoundKey = key;
            var lines = _respBodyLines;
            _responseFind.Search.Bind(lines.Length, lanes: 1,
                (line, _) => line >= 0 && line < lines.Length ? lines[line] : null);
        }
        return (_respBodyText, DecorateResponseBody(_respLineStarts, _respSyntaxSpans));
    }

    private static int[] LineStarts(string[] lines)
    {
        var starts = new int[lines.Length];
        int offset = 0;
        for (int i = 0; i < lines.Length; i++) { starts[i] = offset; offset += lines[i].Length + 1; }
        return starts;
    }

    /// <summary>Pretty mode: JSON re-indented via a real parse; anything that
    /// isn't valid JSON (or isn't JSON at all) shows as received.</summary>
    internal static string Prettify(HttpResponseData response)
    {
        if (response.Body.Truncated) return response.Body.Text;   // a cut JSON never parses
        var ct = response.ContentType;
        bool json = ct != null && (ct.Contains("/json", StringComparison.OrdinalIgnoreCase)
            || ct.Contains("+json", StringComparison.OrdinalIgnoreCase));
        if (!json) return response.Body.Text;
        try
        {
            // Utf8JsonWriter, not JsonSerializer: no reflection, so Native AOT
            // has nothing to trim away.
            using var doc = JsonDocument.Parse(response.Body.Text);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
                doc.WriteTo(writer);
            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }
        catch (JsonException)
        {
            return response.Body.Text;
        }
    }

    private static IReadOnlyList<TextSpan>? SyntaxSpans(string text, string? contentType)
    {
        string? ext = contentType switch
        {
            null => null,
            var ct when ct.Contains("json", StringComparison.OrdinalIgnoreCase) => ".json",
            var ct when ct.Contains("xml", StringComparison.OrdinalIgnoreCase) => ".xml",
            var ct when ct.Contains("html", StringComparison.OrdinalIgnoreCase) => ".html",
            _ => null,
        };
        if (ext == null) return null;

        IReadOnlyList<TextSpan>[]? perLine = null;
        try { perLine = HultUI.Syntax.SyntaxHighlighter.TokenizeLines("response" + ext, text); }
        catch { /* highlight is best-effort */ }
        if (perLine == null) return null;

        // Flatten per-line spans to absolute offsets into the joined text.
        var lines = text.Split('\n');
        var flat = new List<TextSpan>();
        int offset = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (i < perLine.Length && perLine[i] is { } lineSpans)
                foreach (var s in lineSpans)
                    flat.Add(new TextSpan(offset + s.Start, s.Length, s.Color, s.Background));
            offset += lines[i].Length + 1;
        }
        return flat;
    }

    /// <summary>Search tints merged into the syntax spans, both in absolute
    /// offsets — and sorted, because TextArea slices the list per visible line
    /// and stops at the first span past the line end (the lesson Junction's
    /// Files view learned the hard way).</summary>
    private IReadOnlyList<TextSpan>? DecorateResponseBody(int[] starts, IReadOnlyList<TextSpan>? syntax)
    {
        var search = _responseFind.Search;
        if (!search.HasHits) return syntax;

        var current = search.Current;
        var merged = syntax is null ? new List<TextSpan>() : new List<TextSpan>(syntax);
        foreach (var hit in search.Hits)
        {
            if (hit.Length <= 0 || hit.Line >= starts.Length) continue;
            bool isCurrent = current is { } c && c.Line == hit.Line && c.Start == hit.Start;
            merged.Add(new TextSpan(starts[hit.Line] + hit.Start, hit.Length, default,
                isCurrent ? Azimuth.Alpha(Azimuth.Accent, 0.55) : Azimuth.Alpha(Azimuth.Accent700, 0.55)));
        }
        merged.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return merged;
    }

    private Element BuildFindBar(FindBarState s)
    {
        var search = s.Search;
        var row = Flex.Row(gap: Azimuth.S2).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(4, Azimuth.S3, 4, Azimuth.S3))
            .Background(Azimuth.Surface)
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 0, 1, 0));

        var inputBox = Flex.Row(gap: 0).Height(26).Width(220).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S2, 0, Azimuth.S2))
            .Background(Azimuth.Bg)
            .BorderColor(Azimuth.Border).BorderWidth(1f)
            .CornerRadius(Azimuth.Radius1);
        var input = TextInput.New(search.Query).Height(Dimension.Px(22)).FlexGrow(1);
        input.PlaceholderText("find in response…");
        input.OnTextChanged(v => { search.SetQuery(v); s.PendingReveal = true; });
        input.FontFamily(Fonts.Mono).FontSize(11.5f).TextColor(Azimuth.Text);
        input.WithRef(s.FieldRef);
        input.AutomationId("find-input");
        row.AddChild(inputBox);
        inputBox.AddChild(input);

        string status = search.Query.Length == 0 ? ""
            : search.Error is { } why ? why
            : !search.HasHits ? "no matches"
            : $"{search.Index + 1} of {search.Hits.Count}";
        row.AddChild(Ui.Mono(status, 10.5f, search.Query.Length > 0 && !search.HasHits
            ? Azimuth.StatusWarn : Azimuth.TextMuted).AutomationId("find-status"));

        row.AddChild(Box.New().FlexGrow(1));
        row.AddChild(BarAction("‹", "find-prev", () => FindNav(s, -1)));
        row.AddChild(BarAction("›", "find-next", () => FindNav(s, 1)));
        return row;
    }

    // ── Headers / Cookies / Timeline ─────────────────────────────────────

    private static BoxElement BuildHeadersTable(HttpResponseData response)
    {
        var scroll = ScrollView.New();
        var col = Flex.Column(gap: 2).Padding(new Thickness(Azimuth.S2, Azimuth.S3, Azimuth.S3, Azimuth.S3));
        foreach (var (name, value) in response.Headers)
        {
            col.AddChild(Flex.Row(gap: Azimuth.S3).AlignItems(AlignItems.FlexStart).FlexShrink(0)
                .Children(
                    Ui.Mono(name, 11.5f, Azimuth.TextMuted).Width(180).FlexShrink(0),
                    Ui.Mono(value, 11.5f, Azimuth.TextDim).FlexShrink(1)));
        }
        scroll.AddChild(col);
        var box = Box.New().FlexGrow(1);
        box.AddChild(scroll);
        return box;
    }

    private List<System.Net.Cookie> CookiesFor(HttpResponseData response)
    {
        try
        {
            var uri = new Uri(response.FinalUrl);
            return Engine.Cookies.GetCookies(uri).Cast<System.Net.Cookie>().ToList();
        }
        catch (UriFormatException)
        {
            return [];
        }
    }

    private BoxElement BuildCookiesTable(HttpResponseData response)
    {
        var cookies = CookiesFor(response);
        if (cookies.Count == 0)
            return Flex.Column(gap: 0).FlexGrow(1)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(Text.New("no cookies for this host").FontSize(12f).TextColor(Azimuth.TextFaint));

        var col = Flex.Column(gap: 2).Padding(new Thickness(Azimuth.S2, Azimuth.S3, Azimuth.S3, Azimuth.S3));
        col.AddChild(Text.New("session only — the jar never serializes to the collection")
            .FontSize(10.5f).TextColor(Azimuth.TextFaint).FlexShrink(0));
        foreach (var cookie in cookies)
        {
            var flags = string.Join(" · ", new[]
            {
                cookie.HttpOnly ? "httponly" : null,
                cookie.Secure ? "secure" : null,
                cookie.Expires == default ? "session" : cookie.Expires.ToString("u"),
            }.Where(f => f != null));
            col.AddChild(Flex.Row(gap: Azimuth.S3).AlignItems(AlignItems.Center).FlexShrink(0)
                .Children(
                    Ui.Mono(cookie.Name, 11.5f, Azimuth.TextDim).Width(140).FlexShrink(0),
                    Ui.Mono(cookie.Value, 11.5f, Azimuth.TextMuted).FlexShrink(1),
                    Ui.Mono(flags, 10f, Azimuth.TextFaint).FlexShrink(0)));
        }
        var box = Box.New().FlexGrow(1);
        box.AddChild(col);
        return box;
    }

    /// <summary>The mini-waterfall: phase bars on a shared time axis. Phase
    /// colours borrow the method register's hues at full value — the two never
    /// appear together, so there is no collision (the sheet's note).</summary>
    private static BoxElement BuildTimeline(HttpResponseData response)
    {
        var t = response.Timings;
        var phases = new (string Label, TimeSpan Duration, Color Color)[]
        {
            ("DNS", t.Dns, Azimuth.MethodGet),
            ("TCP", t.Connect, Azimuth.MethodPost),
            ("TLS", t.Tls, Azimuth.MethodPut),
            ("TTFB", t.TimeToFirstByte - t.Dns - t.Connect - t.Tls, Azimuth.MethodPatch),
            ("Download", t.Download, Azimuth.MethodDelete),
        };
        double total = Math.Max(t.Total.TotalMilliseconds, 1);

        var col = Flex.Column(gap: Azimuth.S2).FlexGrow(1)
            .Padding(new Thickness(Azimuth.S3, Azimuth.S4, Azimuth.S3, Azimuth.S4));
        double startPct = 0;
        foreach (var (label, duration, color) in phases)
        {
            double ms = Math.Max(duration.TotalMilliseconds, 0);
            double widthPct = Math.Clamp(ms / total * 100, 0, 100);
            var row = Flex.Row(gap: Azimuth.S3).Height(20).FlexShrink(0).AlignItems(AlignItems.Center);
            row.AddChild(Ui.Mono(label, 10.5f, Azimuth.TextMuted).Width(64).FlexShrink(0));

            var track = Flex.Row(gap: 0).FlexGrow(1).Height(8).AlignItems(AlignItems.Stretch);
            if (startPct > 0) track.AddChild(Box.New().Width(Dimension.Percent((float)startPct)));
            var bar = Box.New().Width(Dimension.Percent((float)Math.Max(widthPct, 0.5))).CornerRadius(2f);
            bar.Background(color);
            track.AddChild(bar);
            row.AddChild(track);

            row.AddChild(Ui.Mono($"{ms:0} ms", 10.5f, Azimuth.TextDim).Width(64).FlexShrink(0));
            col.AddChild(row);
            startPct += widthPct;
        }

        col.AddChild(Box.New().Height(Azimuth.S2));
        col.AddChild(Ui.Mono(
            $"total {t.Total.TotalMilliseconds:0} ms · reused connection: {(t.ConnectionReused ? "yes" : "no")} · {t.HttpVersion}{(t.TlsProtocol is { } tls ? $" · {tls}" : "")}",
            10.5f, Azimuth.TextMuted).AutomationId("timeline-summary"));
        return col;
    }

    // ── Actions ──────────────────────────────────────────────────────────

    private static string HexStub(byte[] bytes)
    {
        var sb = new System.Text.StringBuilder();
        int rows = Math.Min(bytes.Length, 512) / 8 + (Math.Min(bytes.Length, 512) % 8 > 0 ? 1 : 0);
        for (int row = 0; row < rows; row++)
        {
            int start = row * 8;
            sb.Append($"{start:x8}  ");
            for (int i = 0; i < 8; i++)
                sb.Append(start + i < bytes.Length ? $"{bytes[start + i]:x2} " : "   ");
            sb.Append(' ');
            for (int i = 0; i < 8 && start + i < bytes.Length; i++)
            {
                byte b = bytes[start + i];
                sb.Append(b is >= 32 and < 127 ? (char)b : '.');
            }
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    private async void SaveBody(HttpResponseData response)
    {
        string suggested = "response" + (response.Body.Kind == BodyKind.Binary ? ".bin" : ".txt");
        var dest = await _fileDialogs.SaveFileAsync(suggested, _workspace?.Root);
        if (dest == null) return;
        try
        {
            if (response.Body.SpillPath is { } spill && File.Exists(spill))
                File.Copy(spill, dest, overwrite: true);
            else if (response.Body.Kind == BodyKind.Binary)
                await File.WriteAllBytesAsync(dest, response.Body.Preview);
            else
                await File.WriteAllTextAsync(dest, response.Body.Text);
            Flash($"Saved {Path.GetFileName(dest)}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Flash($"Could not save: {e.Message}");
        }
    }

    private void OpenWithShell(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Flash($"Could not open: {e.Message}");
        }
    }
}
