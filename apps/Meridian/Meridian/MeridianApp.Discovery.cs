using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Meridian.Core;
using Meridian.Theme;

namespace Meridian;

public partial class MeridianApp
{
    // ── Discovery state ──────────────────────────────────────────────────

    private ApiMap? _map;
    private string? _mapBaseUrl;
    private string? _specError;        // "openapi.json:212:7 — …"
    private string? _specErrorPath;
    private int _specErrorLine;
    private bool _probing;
    private Endpoint? _mapSelection;
    private string _mapFilter = "";
    private bool _promoteObserved;

    internal ApiMap? Map => _map;

    /// <summary>Every send folds into the map, so exploring by hand builds it —
    /// the mock's passive probing. Only when a base URL is being tracked.</summary>
    internal void RecordInMap(string method, string url, HttpResponseData response)
    {
        if (_map is not { } map) return;
        if (!url.StartsWith(map.BaseUrl, StringComparison.OrdinalIgnoreCase)) return;
        _map = map.Record(method, response.FinalUrl is { Length: > 0 } final ? final : url,
            response.StatusCode, response.Body.Kind == BodyKind.Text ? response.Body.Text : null);
        SaveMap();
    }

    private string MapCachePath(string baseUrl)
    {
        string safe = new(baseUrl.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        return Path.Combine(_workspace!.Root, ".meridian", "discovery", safe + ".map");
    }

    /// <summary>The map cache is derived state: a flat text file, one endpoint
    /// per line. Losing it costs nothing — the spec re-imports and traffic
    /// re-observes — so the format stays trivially readable rather than
    /// versioned.</summary>
    private void SaveMap()
    {
        if (_map is not { } map || _workspace == null) return;
        try
        {
            var path = MapCachePath(map.BaseUrl);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var lines = map.Endpoints.Select(e =>
                string.Join('\t', e.Method, e.Path, e.Confidence, e.Group, e.SeenCount,
                    string.Join(',', e.Codes.Select(c => $"{c.Key}={c.Value}")), e.Summary.Replace('\t', ' ')));
            File.WriteAllText(path, $"{map.BaseUrl}\t{map.SpecSource}\t{map.SpecVersion}\n" + string.Join('\n', lines));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ── Actions ──────────────────────────────────────────────────────────

    internal void StartDiscovery(string baseUrl)
    {
        _mapBaseUrl = baseUrl.TrimEnd('/');
        _map = ApiMap.Empty(_mapBaseUrl);
        _specError = null;
        _mapSelection = null;
        MarkDirty();
        ProbeForSpec(_mapBaseUrl);
    }

    /// <summary>Tries the well-known spec locations in order. Finding one is
    /// the delight moment: a quiet toast, and the map fills in behind it.</summary>
    private async void ProbeForSpec(string baseUrl)
    {
        _probing = true;
        MarkDirty();
        try
        {
            foreach (var candidate in OpenApi.WellKnownPaths)
            {
                var result = await Engine.SendAsync(new RequestSpec("GET", baseUrl + candidate, []));
                if (result.Response is not { StatusCode: >= 200 and < 300 } response) continue;
                if (response.Body.Kind != BodyKind.Text) continue;

                var imported = OpenApi.Parse(response.Body.Text, baseUrl, candidate, out var failure);
                if (imported == null)
                {
                    // A 200 that isn't a spec is not an error — keep probing.
                    if (failure != null && candidate == OpenApi.WellKnownPaths[^1])
                        SetSpecError(candidate, failure);
                    continue;
                }
                _map = Merge(imported, _map);
                SaveMap();
                Flash($"Found a spec at {candidate} — {imported.DeclaredCount} endpoints mapped.");
                return;
            }
        }
        finally
        {
            _probing = false;
            MarkDirty();
        }
    }

    /// <summary>Imports a spec file from disk (the front door's Ctrl+I).</summary>
    internal void ImportSpecFile(string path)
    {
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Flash($"Could not read {Path.GetFileName(path)}: {e.Message}");
            return;
        }

        var imported = OpenApi.Parse(text, _mapBaseUrl ?? "", Path.GetFileName(path), out var failure);
        if (imported == null)
        {
            SetSpecError(path, failure!);
            SetView(MeridianView.Discovery);
            MarkDirty();
            return;
        }
        _specError = null;
        _map = Merge(imported, _map);
        SaveMap();
        SetView(MeridianView.Discovery);
        Flash($"{imported.DeclaredCount} endpoints imported from {Path.GetFileName(path)}.");
    }

    private void SetSpecError(string path, OpenApi.ParseFailure failure)
    {
        _specErrorPath = path;
        _specErrorLine = failure.Line;
        _specError = $"{Path.GetFileName(path)}:{failure.Line}:{failure.Column} — {failure.Message}";
    }

    /// <summary>
    /// Spec import over an existing map: declared endpoints win on their own
    /// facts, but any traffic already seen is carried across. Evidence
    /// persists — an endpoint the spec later claims still shows what it really
    /// returned.
    /// </summary>
    internal static ApiMap Merge(ApiMap imported, ApiMap? existing)
    {
        if (existing == null) return imported;
        var merged = imported.Endpoints.Select(declared =>
        {
            var seen = existing.Endpoints.FirstOrDefault(o =>
                o.Method.Equals(declared.Method, StringComparison.OrdinalIgnoreCase) && o.Path == declared.Path);
            return seen == null ? declared : declared with
            {
                SeenCodes = seen.SeenCodes,
                SeenCount = seen.SeenCount,
                InferredShape = declared.ExampleResponse == null ? seen.InferredShape : null,
            };
        }).ToList();

        // Observed endpoints the spec doesn't mention stay in the map — that
        // gap is exactly what discovery is for.
        merged.AddRange(existing.Endpoints.Where(o =>
            o.Confidence == Confidence.Observed
            && !imported.Endpoints.Any(d => d.Method.Equals(o.Method, StringComparison.OrdinalIgnoreCase) && d.Path == o.Path)));

        return imported with { Endpoints = merged };
    }

    /// <summary>Writes the chosen endpoints as real <c>.req</c> files. The
    /// button says how many files — because that is what it does.</summary>
    internal int PromoteToCollection(IReadOnlyList<Endpoint> endpoints)
    {
        if (_workspace is not { } ws) return 0;
        int written = 0;
        foreach (var endpoint in endpoints)
        {
            try
            {
                var dir = Path.Combine(ws.Root, Sanitize(endpoint.Group));
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, OpenApi.FileNameFor(endpoint));
                for (int n = 2; File.Exists(file); n++)
                    file = Path.Combine(dir, OpenApi.FileNameFor(endpoint).Replace(".req", $"-{n}.req"));
                File.WriteAllText(file, OpenApi.ToRequestFile(endpoint));
                written++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Flash($"Could not write {OpenApi.FileNameFor(endpoint)}: {e.Message}");
            }
        }
        RescanWorkspace();
        MarkDirty();
        return written;
    }

    private static string Sanitize(string name) =>
        new(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());

    // ── The Discovery view (3) ───────────────────────────────────────────

    private Element BuildDiscoveryRealView()
    {
        if (_workspace == null)
            return EmptyView("Discovery", "nothing discovered yet",
                "Open a collection first — discovered endpoints are promoted into it.");

        var row = Flex.Row(gap: 0).FlexGrow(1).AlignItems(AlignItems.Stretch);
        var sources = BuildSourcesPanel(); sources.Key = "disc-sources";
        row.AddChild(sources);

        if (_specError is { } error)
        {
            var pane = BuildSpecErrorPane(error); pane.Key = "disc-error";
            row.AddChild(pane);
            return row;
        }

        var map = BuildEndpointMap(); map.Key = "disc-map";
        row.AddChild(map);
        if (_mapSelection is { } selected)
        {
            var detail = BuildEndpointDetail(selected); detail.Key = "disc-detail";
            row.AddChild(detail);
        }
        return row;
    }

    private Element BuildSourcesPanel()
    {
        var panel = Flex.Column(gap: Azimuth.S2).FlexShrink(0)
            .Width(Azimuth.TreeWidth)
            .Padding(new Thickness(Azimuth.S3))
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 0, 0, 1));

        panel.AddChild(Text.New("Sources").FontSize(15f).FontWeight(FontWeight.SemiBold).TextColor(Azimuth.TextDim));

        if (_map is { } map)
        {
            if (map.SpecSource is { } source)
            {
                var spec = Flex.Column(gap: 2).FlexShrink(0)
                    .Padding(new Thickness(Azimuth.S2))
                    .CornerRadius(Azimuth.Radius1)
                    .Background(Azimuth.Tint(Azimuth.StatusPass));
                spec.AddChild(Flex.Row(gap: Azimuth.S2).AlignItems(AlignItems.Center).Children(
                    Ui.Mono("✓", 11f, Azimuth.StatusPass),
                    Ui.Mono(source, 11f, Azimuth.TextDim),
                    map.SpecVersion is { } version ? Ui.Mono("v" + version, 10f, Azimuth.TextFaint) : Box.New()));
                spec.AddChild(Ui.Mono($"{map.DeclaredCount} endpoints declared", 10f, Azimuth.TextMuted));
                panel.AddChild(spec);
            }

            var traffic = Flex.Column(gap: 2).FlexShrink(0)
                .Padding(new Thickness(Azimuth.S2))
                .CornerRadius(Azimuth.Radius1)
                .BorderColor(Azimuth.Border).BorderWidth(1f);
            traffic.AddChild(Flex.Row(gap: Azimuth.S2).AlignItems(AlignItems.Center).Children(
                Ui.Mono("◎", 11f, Azimuth.Accent300),
                Text.New("live traffic").FontSize(11.5f).TextColor(Azimuth.TextDim)));
            traffic.AddChild(Ui.Mono($"{map.ObservedCount} observed", 10f, Azimuth.TextMuted));
            panel.AddChild(traffic);

            panel.AddChild(Ui.Mono($"base: {map.BaseUrl}", 10f, Azimuth.TextFaint).AutomationId("disc-base"));
            if (_probing)
                panel.AddChild(Ui.Mono("probing for a spec…", 10f, Azimuth.Accent300).AutomationId("disc-probing"));

            panel.AddChild(Box.New().Height(Azimuth.S2));
            int promoteCount = PromotionSet().Count;
            panel.AddChild(BarAction($"Promote {promoteCount} to collection…", "disc-promote", () =>
            {
                int written = PromoteToCollection(PromotionSet());
                Flash($"Wrote {written} file{(written == 1 ? "" : "s")}.");
            }));
            var toggle = Flex.Row(gap: Azimuth.S2).AlignItems(AlignItems.Center)
                .Cursor(CursorType.Pointer)
                .AccessibleRole(AccessibilityRole.Checkbox)
                .AccessibleName($"include observed endpoints{(_promoteObserved ? ", on" : ", off")}")
                .AutomationId("disc-include-observed");
            toggle.AddChild(Ui.Mono(_promoteObserved ? "☑" : "☐", 12f, _promoteObserved ? Azimuth.Accent300 : Azimuth.TextMuted));
            toggle.AddChild(Text.New($"{map.ObservedCount} observed (marked inferred)").FontSize(11f).TextColor(Azimuth.TextMuted));
            toggle.OnClick(_ => { _promoteObserved = !_promoteObserved; MarkDirty(); });
            panel.AddChild(toggle);

            panel.AddChild(Box.New().FlexGrow(1));
            panel.AddChild(Ui.Mono($"map cached at .meridian/discovery/", 10f, Azimuth.TextFaint));
        }
        else
        {
            panel.AddChild(Text.New("No base URL yet.").FontSize(12f).TextColor(Azimuth.TextMuted));
            panel.AddChild(BarAction("Discover an API…", "disc-start", () =>
                OpenDialog("Discover an API", "https://api.example.com", url =>
                {
                    if (url.Trim() is { Length: > 0 } trimmed) StartDiscovery(trimmed);
                })));
            panel.AddChild(BarAction("Import a spec file…", "disc-import", () =>
                OpenDialog("Import OpenAPI", "path to openapi.json", ImportSpecFile)));
        }
        return panel;
    }

    /// <summary>What "Promote" would write: declared endpoints always, observed
    /// ones only when opted in (and they keep their inferred marking).</summary>
    private List<Endpoint> PromotionSet() =>
        _map is not { } map ? []
        : map.Endpoints.Where(e => e.Confidence == Confidence.Declared || _promoteObserved).ToList();

    private Element BuildEndpointMap()
    {
        var col = Flex.Column(gap: 0).FlexGrow(1);
        var map = _map;

        var header = Flex.Row(gap: Azimuth.S3).Height(36).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 0, 1, 0));
        header.AddChild(Text.New("Endpoint map").FontSize(13f).FontWeight(FontWeight.Medium).TextColor(Azimuth.TextDim));
        if (map != null)
            header.AddChild(Ui.Mono($"{map.DeclaredCount} declared · {map.ObservedCount} observed", 10.5f, Azimuth.TextMuted));
        col.AddChild(header);

        if (map == null || map.Endpoints.Count == 0)
        {
            col.AddChild(Flex.Column(gap: Azimuth.S2).FlexGrow(1)
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
                .Children(
                    Text.New("nothing discovered yet").FontSize(12f).TextColor(Azimuth.TextMuted).AutomationId("disc-empty"),
                    Text.New(map == null
                        ? "Give a base URL, or import a spec file."
                        : $"No spec at the usual paths. Send any request against {map.BaseUrl} and it appears here.")
                        .FontSize(11.5f).TextColor(Azimuth.TextFaint)));
            return col;
        }

        var scroll = ScrollView.New();
        var list = Flex.Column(gap: 1).Padding(new Thickness(Azimuth.S1, Azimuth.S3, Azimuth.S3, Azimuth.S3));

        foreach (var group in map.Endpoints
            .Where(e => e.Confidence == Confidence.Declared)
            .GroupBy(e => e.Group).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            list.AddChild(Ui.Mono(group.Key.ToUpperInvariant(), 10f, Azimuth.TextMuted).FlexShrink(0));
            foreach (var endpoint in group) list.AddChild(EndpointRow(endpoint));
        }

        var observed = map.Endpoints.Where(e => e.Confidence == Confidence.Observed).ToList();
        if (observed.Count > 0)
        {
            list.AddChild(Box.New().Height(Azimuth.S2));
            list.AddChild(Ui.Mono("OBSERVED — NOT IN SPEC", 10f, Azimuth.StatusWarn).FlexShrink(0)
                .AutomationId("disc-observed-header"));
            foreach (var endpoint in observed) list.AddChild(EndpointRow(endpoint));
        }

        scroll.AddChild(list);
        var box = Box.New().FlexGrow(1).FlexBasis(Dimension.Px(0));
        box.AddChild(scroll);
        col.AddChild(box);
        return col;
    }

    /// <summary>A map row. Confidence is a chip treatment, not just a section:
    /// declared endpoints get the solid method tint, observed ones a hollow
    /// dashed-reading chip and an italic-equivalent muted path plus their
    /// evidence ("seen 4× · 200").</summary>
    private Element EndpointRow(Endpoint endpoint)
    {
        bool selected = _mapSelection?.Key == endpoint.Key;
        bool observed = endpoint.Confidence == Confidence.Observed;

        var row = Flex.Row(gap: Azimuth.S2).Height(Azimuth.RowList).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S2, 0, Azimuth.S2))
            .CornerRadius(Azimuth.Radius1)
            .BorderColor(selected ? Azimuth.Accent : Color.Transparent)
            .BorderWidth(new Thickness(2, 0, 0, 0))
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName($"{endpoint.Method} {endpoint.Path}, {(observed ? "observed" : "spec-declared")}{(endpoint.Deprecated ? ", deprecated" : "")}")
            .AutomationId("ep-" + endpoint.Method + endpoint.Path.Replace('/', '-').Replace("{", "").Replace("}", ""));
        if (selected) row.Background(Azimuth.Raised);

        var color = Azimuth.MethodColor(endpoint.Method);
        var chip = Box.New()
            .Padding(new Thickness(2, 5, 2, 5))
            .CornerRadius(Azimuth.Radius1)
            .BorderColor(observed ? color : Color.Transparent).BorderWidth(1f)
            .FlexShrink(0)
            .Children(Ui.ChipLabel(endpoint.Method, 9f, color).FontWeight(FontWeight.SemiBold));
        if (!observed) chip.Background(Azimuth.Tint(color));
        row.AddChild(chip);

        row.AddChild(Ui.Mono(endpoint.Path, 11.5f, endpoint.Deprecated ? Azimuth.TextFaint : Azimuth.TextDim)
            .FlexGrow(1).TextOverflow(TextOverflow.Ellipsis).MaxLines(1));

        if (endpoint.Deprecated)
            row.AddChild(Ui.Mono("deprecated", 9f, Azimuth.StatusWarn).FlexShrink(0));
        if (observed && endpoint.SeenCount > 0)
            row.AddChild(Ui.Mono($"seen {endpoint.SeenCount}× · {string.Join(", ", endpoint.Codes.Keys.Order())}",
                9.5f, Azimuth.TextFaint).FlexShrink(0));

        var captured = endpoint;
        row.OnClick(_ => { _mapSelection = captured; MarkDirty(); });
        if (!selected) row.HoverBg(Azimuth.Raised, Color.Transparent);
        return row;
    }

    private Element BuildEndpointDetail(Endpoint endpoint)
    {
        var panel = Flex.Column(gap: Azimuth.S2).FlexShrink(0)
            .Width(Azimuth.ResponseRight)
            .Padding(new Thickness(Azimuth.S3))
            .BorderColor(Azimuth.BorderStrong).BorderWidth(new Thickness(2, 0, 0, 0));
        panel.AccessibleRole(AccessibilityRole.TabPanel).AccessibleName("Endpoint detail");
        panel.AutomationId("disc-detail");

        panel.AddChild(Flex.Row(gap: Azimuth.S2).AlignItems(AlignItems.Center).Children(
            MethodChip(endpoint.Method),
            Ui.Mono(endpoint.Path, 12f, Azimuth.Text).FlexShrink(1),
            Ui.Mono(endpoint.Confidence == Confidence.Declared ? "spec-declared" : "observed",
                9.5f, endpoint.Confidence == Confidence.Declared ? Azimuth.Accent300 : Azimuth.StatusWarn)));

        if (endpoint.Summary.Length > 0)
            panel.AddChild(Text.New(endpoint.Summary).FontSize(11.5f).TextColor(Azimuth.TextMuted));

        if (endpoint.Params.Count > 0)
        {
            panel.AddChild(Ui.Mono("PARAMETERS", 10f, Azimuth.TextMuted));
            foreach (var p in endpoint.Params)
                panel.AddChild(Flex.Row(gap: Azimuth.S2).AlignItems(AlignItems.Center).Children(
                    Ui.Mono(p.Name, 11f, Azimuth.TextDim).Width(140).FlexShrink(0),
                    Ui.Mono($"{p.In} · {p.Type}", 10.5f, Azimuth.TextMuted),
                    Ui.Mono(p.Required ? "required" : "optional", 10f,
                        p.Required ? Azimuth.StatusWarn : Azimuth.TextFaint)));
        }

        if (endpoint.ExampleResponse is { } example)
        {
            panel.AddChild(Ui.Mono("RESPONSE — from the spec", 10f, Azimuth.TextMuted));
            var area = TextArea.New(example);
            area.FontFamily(Fonts.Mono).FontSize(11f).TextColor(Azimuth.TextDim).Height(Dimension.Px(120));
            area.ReadOnly = true;
            area.AutomationId("disc-example");
            panel.AddChild(area);
        }
        else if (endpoint.InferredShape is { } shape)
        {
            // Inference is labelled as inference — never dressed up as truth.
            panel.AddChild(Ui.Mono($"No example schema. Shape inferred from {Math.Max(endpoint.SeenCount, 1)} response(s):",
                10f, Azimuth.StatusWarn).AutomationId("disc-inferred"));
            panel.AddChild(Ui.Mono(shape, 11f, Azimuth.TextMuted));
        }

        if (endpoint.Codes.Count > 0)
        {
            panel.AddChild(Ui.Mono("SEEN IN TRAFFIC", 10f, Azimuth.TextMuted));
            panel.AddChild(Flex.Row(gap: Azimuth.S2).Children(
                endpoint.Codes.OrderBy(c => c.Key)
                    .Select(c => (Element)Ui.Mono($"{c.Key} ×{c.Value}", 10.5f,
                        c.Key < 400 ? Azimuth.StatusPass : Azimuth.StatusWarn)).ToArray()));
        }

        panel.AddChild(Box.New().FlexGrow(1));
        var captured = endpoint;
        panel.AddChild(BarAction($"Generate request → {Sanitize(endpoint.Group)}/", "disc-generate", () =>
        {
            int written = PromoteToCollection([captured]);
            if (written > 0) Flash($"Wrote {OpenApi.FileNameFor(captured)}");
        }));
        return panel;
    }

    /// <summary>The parse-error state: the file, the position, the parser's own
    /// words, and a jump action — never a bare "invalid JSON".</summary>
    private Element BuildSpecErrorPane(string error)
    {
        var col = Flex.Column(gap: Azimuth.S2).FlexGrow(1)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Padding(new Thickness(Azimuth.S4));
        col.AddChild(Text.New("✗ spec parse failed").FontSize(13f).FontWeight(FontWeight.Medium)
            .TextColor(Azimuth.StatusFail));
        col.AddChild(Ui.Mono(error, 11.5f, Azimuth.TextDim).AutomationId("disc-parse-error"));
        col.AddChild(Flex.Row(gap: Azimuth.S2).Children(
            BarAction($"open at line {_specErrorLine}", "disc-open-at-line",
                () => { if (_specErrorPath is { } p) OpenWithShell(p); }),
            BarAction("retry", "disc-retry",
                () => { if (_specErrorPath is { } p) ImportSpecFile(p); })));
        return col;
    }
}
