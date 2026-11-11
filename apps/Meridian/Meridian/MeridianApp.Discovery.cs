using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
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
    // ── Discovery state ──────────────────────────────────────────────────

    private ApiMap? _map;
    private string? _mapBaseUrl;
    private string? _specError;        // "openapi.json:212:7 — …"
    private string? _specErrorPath;
    private int _specErrorLine;
    private bool _probing;
    private Endpoint? _mapSelection;
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
    /// <remarks>Written off the UI thread, the latest map of each file at a time: every send into a
    /// tracked base wrote the whole file on the UI thread, 1,106 KB of strings a send at 2,000
    /// endpoints. A map is never changed, so the writer takes the one the send made, and sends that
    /// land while it writes are written once, as the last of them. <see cref="Dispose"/> waits for
    /// it.</remarks>
    private void SaveMap()
    {
        if (_map is not { } map || _workspace == null) return;
        string path = MapCachePath(map.BaseUrl);
        lock (_mapSaveLock)
        {
            _mapsToSave[path] = map;
            _mapSaver ??= Task.Run(WriteMaps);
        }
    }

    private readonly object _mapSaveLock = new();
    private readonly Dictionary<string, ApiMap> _mapsToSave = new(StringComparer.Ordinal);
    private Task? _mapSaver;

    private void WriteMaps()
    {
        var batch = new List<KeyValuePair<string, ApiMap>>();
        while (true)
        {
            lock (_mapSaveLock)
            {
                if (_mapsToSave.Count == 0)
                {
                    _mapSaver = null;
                    return;
                }
                batch.AddRange(_mapsToSave);
                _mapsToSave.Clear();
            }
            foreach (var (path, map) in batch)
            {
                // Derived state: a write that fails loses nothing the next send or import does not
                // write again, and must not stop the writer.
                try { WriteMap(map, path); }
                catch (Exception e) when (e is not OutOfMemoryException) { }
            }
            batch.Clear();
        }
    }

    /// <summary>Waits for the cache's writer, so a map saved just before is on disk (for Dispose, and
    /// for tests).</summary>
    internal void FlushMapCache()
    {
        Task? saver;
        lock (_mapSaveLock) saver = _mapSaver;
        saver?.Wait(TimeSpan.FromSeconds(10));
    }

    /// <summary>The cache file's text, written as it is made: the first line the base, the spec and
    /// its version, then a line an endpoint, tab-separated, no newline at the end.</summary>
    internal static void WriteMap(ApiMap map, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var writer = new StreamWriter(path, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(map.BaseUrl);
            writer.Write('\t');
            writer.Write(map.SpecSource);
            writer.Write('\t');
            writer.Write(map.SpecVersion);
            writer.Write('\n');
            for (int i = 0; i < map.Endpoints.Count; i++)
            {
                var e = map.Endpoints[i];
                if (i > 0) writer.Write('\n');
                writer.Write(e.Method);
                writer.Write('\t');
                writer.Write(e.Path);
                writer.Write('\t');
                writer.Write(e.Confidence == Confidence.Declared ? nameof(Confidence.Declared) : nameof(Confidence.Observed));
                writer.Write('\t');
                writer.Write(e.Group);
                writer.Write('\t');
                writer.Write(e.SeenCount);
                writer.Write('\t');
                if (e.SeenCodes is { } codes)
                {
                    bool first = true;
                    foreach (var (code, count) in codes)
                    {
                        if (!first) writer.Write(',');
                        first = false;
                        writer.Write(code);
                        writer.Write('=');
                        writer.Write(count);
                    }
                }
                writer.Write('\t');
                writer.Write(e.Summary.Replace('\t', ' '));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    // ── Actions ──────────────────────────────────────────────────────────

    internal void StartDiscovery(string pastedUrl)
    {
        var (baseUrl, specUrls) = OpenApi.ProbeCandidates(pastedUrl);
        _mapBaseUrl = baseUrl;
        _map = ApiMap.Empty(_mapBaseUrl);
        _specError = null;
        _mapSelection = null;
        MarkDirty();
        ProbeForSpec(baseUrl, specUrls);
    }

    /// <summary>Tries the pasted URL, then the well-known spec locations, in
    /// order. Finding one is the delight moment: a quiet toast, and the map
    /// fills in behind it.</summary>
    private async void ProbeForSpec(string baseUrl, IReadOnlyList<string> specUrls)
    {
        _probing = true;
        MarkDirty();
        try
        {
            foreach (var url in specUrls)
            {
                var result = await Engine.SendAsync(new RequestSpec("GET", url, []));
                if (result.Response is not { StatusCode: >= 200 and < 300 } response) continue;
                if (response.Body.Kind != BodyKind.Text) continue;

                // The path under the base, as the toast and the map's source say it.
                var candidate = url.Length > baseUrl.Length && url.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase)
                    ? url[baseUrl.Length..] : url;
                // Parsed from its UTF-8 bytes: as text it cost a string and a UTF-8 copy left in the pool.
                var imported = OpenApi.Parse(OpenApi.FullUtf8(response.Body), baseUrl, candidate, out var failure);
                if (imported == null)
                {
                    // A 200 that isn't a spec is not an error — keep probing.
                    if (failure != null && url == specUrls[^1])
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
        // The file's bytes, parsed where they are (OpenApi.ReadUtf8): read as text, a spec of 2,000
        // operations left 8 MB in the shared pool besides the string.
        ReadOnlyMemory<byte> utf8;
        try { utf8 = OpenApi.ReadUtf8(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Flash($"Could not read {Path.GetFileName(path)}: {e.Message}");
            return;
        }

        var imported = OpenApi.Parse(utf8, _mapBaseUrl ?? "", Path.GetFileName(path), out var failure);
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
    /// button says how many files — because that is what it does. Builds
    /// nothing: the caller's toast saying what was written rebuilds the window,
    /// the tree with it (a rebuild here as well built it twice a click).</summary>
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
        // The detail is a part of its own (see PlaceMapDetail), keyed by its host.
        if (_mapSelection is { } selected)
            row.AddChild(PlaceMapDetail(selected).FlexShrink(0));
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
            // The counts as the map's lines have them: worked out once a map, not on every build.
            var lines = LinesOf(map);
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
                spec.AddChild(Ui.Mono($"{lines.Declared} endpoints declared", 10f, Azimuth.TextMuted));
                panel.AddChild(spec);
            }

            var traffic = Flex.Column(gap: 2).FlexShrink(0)
                .Padding(new Thickness(Azimuth.S2))
                .CornerRadius(Azimuth.Radius1)
                .BorderColor(Azimuth.Border).BorderWidth(1f);
            traffic.AddChild(Flex.Row(gap: Azimuth.S2).AlignItems(AlignItems.Center).Children(
                Ui.Mono("◎", 11f, Azimuth.Accent300),
                Text.New("live traffic").FontSize(11.5f).TextColor(Azimuth.TextDim)));
            traffic.AddChild(Ui.Mono($"{lines.Observed} observed", 10f, Azimuth.TextMuted));
            panel.AddChild(traffic);

            panel.AddChild(Ui.Mono($"base: {map.BaseUrl}", 10f, Azimuth.TextFaint).AutomationId("disc-base"));
            if (_probing)
                panel.AddChild(Ui.Mono("probing for a spec…", 10f, Azimuth.Accent300).AutomationId("disc-probing"));

            panel.AddChild(Box.New().Height(Azimuth.S2));
            int promoteCount = lines.Declared + (_promoteObserved ? lines.Observed : 0);   // PromotionSet().Count
            panel.AddChild(BarAction($"Promote {promoteCount} to collection…", "disc-promote", () =>
            {
                int written = PromoteToCollection(PromotionSet());
                Flash($"Wrote {written} file{(written == 1 ? "" : "s")}.");   // builds the window, the tree too
            }));
            var toggle = Flex.Row(gap: Azimuth.S2).AlignItems(AlignItems.Center)
                .Cursor(CursorType.Pointer)
                .AccessibleRole(AccessibilityRole.Checkbox)
                .AccessibleName($"include observed endpoints{(_promoteObserved ? ", on" : ", off")}")
                .AutomationId("disc-include-observed");
            toggle.AddChild(Ui.Mono(_promoteObserved ? "☑" : "☐", 12f, _promoteObserved ? Azimuth.Accent300 : Azimuth.TextMuted));
            toggle.AddChild(Text.New($"{lines.Observed} observed (marked inferred)").FontSize(11f).TextColor(Azimuth.TextMuted));
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

    // ── The map's rows ───────────────────────────────────────────────────
    //
    // The map is a virtual list of its lines: only the rows on screen are elements. As a scroll view
    // over a column of every row and heading, a hover move hit-tested every row (618 elements at 600
    // endpoints, 2,046 at 2,000), every drawn frame culled them, a click's build patched them, and the
    // map's elements took 2.5 MB of the heap at 600 endpoints. The rows show their line and nothing
    // else (RenderWith((line, _) => …)), so the list keeps a realized row while the line at its place
    // is equal: a line holds its endpoint, a record that is equal when every fact the row shows and
    // the detail a click selects is. The list draws the selection (the Raised fill and the 2 px top
    // accent the selected row drew, MapSelection) and the hover tint under the other rows, so the
    // rows are made unselected and never need to know. A click moves the selection, which only the
    // list and the detail beside it show, so it tells the list through its ref and the detail builds
    // alone (SelectEndpoint, PlaceMapDetail); the list makes no row, it draws the highlight
    // elsewhere. It made the two rows the selection moved between. The click captures the
    // row's endpoint, so a kept row selects what a row made now would. The lines (the order, the
    // headings, the keys, the counts) are worked out once a map: a map is never changed, a send or
    // an import makes a new one. The rows are as tall as they were in the column, and the column's
    // padding and gaps are the lines' margins (MapLineMargin), so the map looks and scrolls as it
    // did. A reader is told every line, named as its row is, and selects or invokes one off screen
    // as a click would (OnActivateItem).
    private MapLines? _mapLines;
    private readonly Ref<VirtualListElement<MapLine>> _mapListRef = new();
    private Func<MapLine, int, float>? _mapLineHeight;
    private Func<MapLine, bool, Element>? _mapRow;
    private Func<MapLine, string?>? _mapLineLabel;
    private Action<MapLine>? _mapActivate;
    private int _mapListBuild = -1;

    /// <summary>The selected row's accent: 2 px on the top edge. Before MapSelection, which reads it
    /// (static fields are set in the order they are written).</summary>
    private static readonly Thickness MapAccent = new(2, 0, 0, 0);

    /// <summary>The selected row as the list draws it: what EndpointRow drew on a selected row, the
    /// Raised fill at its corners and a 2 px accent on its top edge (the row keeps a transparent
    /// border of that width, so its contents stay where they were).</summary>
    private static readonly RowHighlight MapSelection =
        new(Azimuth.Raised, new CornerRadius(Azimuth.Radius1), Azimuth.Accent) { BorderThickness = MapAccent };

    /// <summary>The rows the list tints under the pointer: an endpoint's, as each tinted itself; a
    /// heading took no tint.</summary>
    private static readonly Func<MapLine, int, bool> MapRowTakesHover = static (line, _) => line.Endpoint is not null;

    /// <summary>A map's lines in the order the map shows them: each declared group's heading and
    /// rows, then the observed endpoints under their own heading, each with the key its row has.</summary>
    private sealed record MapLines(ApiMap Map, MapLine[] Lines, int Declared, int Observed);

    /// <summary>A group heading (<paramref name="Heading"/>), the observed endpoints' heading
    /// (<paramref name="Observed"/>), or an endpoint's row. <paramref name="First"/> and
    /// <paramref name="Last"/> say the line takes the list's top or bottom padding (MapLineMargin).</summary>
    private readonly record struct MapLine(string Key, Endpoint? Endpoint, string? Heading,
        bool Observed = false, bool First = false, bool Last = false);

    private const string ObservedHeading = "OBSERVED — NOT IN SPEC";

    /// <summary>Endpoint rows the map has made so far, for tests: a click makes none, however long
    /// the map, and a list makes the rows on screen.</summary>
    internal int EndpointRowsMade { get; private set; }

    /// <summary>Has the list on screen make every row again, so the next frame makes them all: for
    /// tests, whose oracle is a build that keeps nothing.</summary>
    internal void ForgetMapRows() => _mapListRef.Current?.InvalidateRows();

    /// <summary>At the end of every build of the window: a build that shows no map lets go of the
    /// list's ref, which through the list's parents would hold the whole view (REACTIVITY.md, "A ref
    /// into a view keeps the whole view").</summary>
    private void EndMapBuild()
    {
        if (_mapListBuild == Builds) return;
        _mapListRef.Current = null;
        _mapDetailRef.Current = null;
    }

    private MapLines LinesOf(ApiMap map)
    {
        if (_mapLines is { } known && ReferenceEquals(known.Map, map)) return known;
        var lines = new List<MapLine>(map.Endpoints.Count + 16);
        // A spec can name one method twice in a path ("get" and "GET"); keys stay apart.
        var keys = new HashSet<string>(StringComparer.Ordinal);
        string KeyOf(Endpoint endpoint)
        {
            string key = "ep " + endpoint.Key;
            for (int n = 2; !keys.Add(key); n++) key = $"ep {endpoint.Key} #{n}";
            return key;
        }

        int declared = 0;
        foreach (var group in map.Endpoints
            .Where(e => e.Confidence == Confidence.Declared)
            .GroupBy(e => e.Group).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add(new MapLine("group " + group.Key, null, group.Key.ToUpperInvariant()));
            foreach (var endpoint in group)
            {
                lines.Add(new MapLine(KeyOf(endpoint), endpoint, null));
                declared++;
            }
        }
        int observed = 0;
        foreach (var endpoint in map.Endpoints)
        {
            if (endpoint.Confidence != Confidence.Observed) continue;
            if (observed == 0) lines.Add(new MapLine("observed-heading", null, ObservedHeading, Observed: true));
            lines.Add(new MapLine(KeyOf(endpoint), endpoint, null));
            observed++;
        }
        if (lines.Count > 0)
        {
            lines[0] = lines[0] with { First = true };
            lines[^1] = lines[^1] with { Last = true };
        }
        return _mapLines = new MapLines(map, lines.ToArray(), declared, observed);
    }

    /// <summary>A line's margins: the column's padding (the list's top on the first line, its
    /// bottom on the last, its sides on every line) and its gaps (1 under each line, and the room
    /// above the observed heading), laid out by the list inside the line's place.</summary>
    private static Thickness MapLineMargin(MapLine line) => new(
        (line.First ? Azimuth.S1 : 0f) + (line.Observed ? Azimuth.S2 + 1f : 0f),
        Azimuth.S3,
        line.Last ? Azimuth.S3 : 1f,
        Azimuth.S3);

    /// <summary>A line's place in the list: its row's height and its margins.</summary>
    private float MapLineHeight(MapLine line) =>
        (line.Endpoint is not null ? Azimuth.RowList : HeadingHeight(line.Heading!)) + MapLineMargin(line).Vertical;

    // A heading is a line of mono text at 10 px, as tall as layout measures it: worked out once a
    // text and display scale, since the list asks for every line's height when it is patched.
    private readonly Dictionary<string, float> _headingHeights = new(StringComparer.Ordinal);
    private float _headingScale;

    private float HeadingHeight(string text)
    {
        float scale = Ui.DisplayScale;
        if (scale != _headingScale)
        {
            _headingHeights.Clear();
            _headingScale = scale;
        }
        if (_headingHeights.TryGetValue(text, out float height)) return height;
        height = TextMeasure.Measure(text, TextMeasure.StyleFor(10f, Fonts.Mono)).Height;
        // No measure context yet (no window): a line's height in the default face, until a build measures.
        if (height <= 0) return 13f;
        _headingHeights[text] = height;
        return height;
    }

    /// <summary>The line of the endpoint the detail shows: the same record, or else the first with
    /// its method and path (the selection is the endpoint as it was when it was clicked, and a send
    /// makes a new map), or -1.</summary>
    private static int SelectedLine(MapLines lines, Endpoint? selection)
    {
        if (selection is null) return -1;
        var all = lines.Lines;
        int byName = -1;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].Endpoint is not { } endpoint) continue;
            if (ReferenceEquals(endpoint, selection)) return i;
            if (byName < 0 && endpoint.Method == selection.Method && endpoint.Path == selection.Path) byName = i;
        }
        return byName;
    }

    private Element BuildEndpointMap()
    {
        var col = Flex.Column(gap: 0).FlexGrow(1);
        var map = _map;
        var lines = map is null ? null : LinesOf(map);

        var header = Flex.Row(gap: Azimuth.S3).Height(36).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 0, 1, 0));
        header.AddChild(Text.New("Endpoint map").FontSize(13f).FontWeight(FontWeight.Medium).TextColor(Azimuth.TextDim));
        if (lines != null)
            header.AddChild(Ui.Mono($"{lines.Declared} declared · {lines.Observed} observed", 10.5f, Azimuth.TextMuted));
        col.AddChild(header);

        if (map == null || lines == null || map.Endpoints.Count == 0)
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

        // Every row is keyed by its line, as the column's children were.
        _mapListBuild = Builds;
        var list = VirtualListElement<MapLine>.New()
            .Items(lines.Lines)
            .RowHeights(_mapLineHeight ??= (line, _) => MapLineHeight(line))
            .RenderWith(_mapRow ??= (line, _) => MapRow(line))
            // One delegate from build to build, reading only its line, so the list keeps each label.
            .AccessibleItemLabel(_mapLineLabel ??= static line => line.Endpoint is { } e ? EndpointName(e) : line.Heading)
            .OnActivateItem(_mapActivate ??= line => { if (line.Endpoint is { } e) SelectEndpoint(e); })
            // The list draws the selection and the hover tint, so a click makes no row. Its items are
            // what a reader reads (the rows are not composite), and they say which one is selected.
            .SelectionHighlight(MapSelection)
            .HoverHighlight(Azimuth.Raised, Azimuth.Radius1, rows: MapRowTakesHover)
            .SelectedIndex(SelectedLine(lines, _mapSelection));
        // The box's height, as the scroll view took it by shrinking to it: a list holds no
        // children to measure until its rows are realized.
        list.FlexGrow(1);
        list.WithRef(_mapListRef);
        var box = Box.New().FlexGrow(1).FlexBasis(Dimension.Px(0));
        box.AddChild(list);
        col.AddChild(box);
        return col;
    }

    /// <summary>A line of the map as the list makes it: a group heading, the observed heading, or an
    /// endpoint's row, with the line's margins. The list draws the selection (MapSelection).</summary>
    private Element MapRow(MapLine line)
    {
        Element row;
        if (line.Endpoint is { } endpoint)
        {
            EndpointRowsMade++;
            row = EndpointRow(endpoint);
        }
        else if (line.Observed)
            row = Ui.Mono(line.Heading!, 10f, Azimuth.StatusWarn).FlexShrink(0).AutomationId("disc-observed-header");
        else
            row = Ui.Mono(line.Heading!, 10f, Azimuth.TextMuted).FlexShrink(0);
        row.Margin(MapLineMargin(line));
        row.Key = line.Key;
        return row;
    }

    /// <summary>What a reader hears for an endpoint's row: what the row shows. The row's labels were
    /// its button's children, which a reader could walk to; an item has none, so an observed row's
    /// evidence is in its name.</summary>
    private static string EndpointName(Endpoint endpoint) =>
        $"{endpoint.Method} {endpoint.Path}, {(endpoint.Confidence == Confidence.Observed ? "observed" : "spec-declared")}{(endpoint.Deprecated ? ", deprecated" : "")}"
        + (Evidence(endpoint) is { } evidence ? ", " + evidence : "");

    /// <summary>An observed endpoint's evidence, as its row shows it ("seen 4× · 200"), or null.</summary>
    private static string? Evidence(Endpoint endpoint) =>
        endpoint.Confidence == Confidence.Observed && endpoint.SeenCount > 0
            ? $"seen {endpoint.SeenCount}× · {string.Join(", ", endpoint.Codes.Keys.Order())}"
            : null;

    /// <summary>A map row. Confidence is a chip treatment, not just a section:
    /// declared endpoints get the solid method tint, observed ones a hollow
    /// dashed-reading chip and an italic-equivalent muted path plus their
    /// evidence ("seen 4× · 200"). The selected look (the Raised fill, a 2 px accent on the top edge)
    /// and the hover tint are the list's to draw (MapSelection, MapRowTakesHover), under the row.</summary>
    private Element EndpointRow(Endpoint endpoint)
    {
        bool observed = endpoint.Confidence == Confidence.Observed;

        var row = Flex.Row(gap: Azimuth.S2).Height(Azimuth.RowList).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S2, 0, Azimuth.S2))
            .CornerRadius(Azimuth.Radius1)
            // Transparent: the room for the selection's accent, which the list draws.
            .BorderColor(Color.Transparent)
            .BorderWidth(MapAccent)
            .Cursor(CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName(EndpointName(endpoint))
            .AutomationId("ep-" + endpoint.Method + endpoint.Path.Replace('/', '-').Replace("{", "").Replace("}", ""));

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
        if (Evidence(endpoint) is { } evidence)
            row.AddChild(Ui.Mono(evidence, 9.5f, Azimuth.TextFaint).FlexShrink(0));

        row.OnClick(_ => SelectEndpoint(endpoint));
        return row;
    }

    /// <summary>A click on a map row, or a reader's select or invoke of its item. Only the map's list
    /// and the detail beside it show the selection, so with both on screen the list is told through
    /// its ref and the detail builds alone: the window does not build. Otherwise — no detail yet (the
    /// first selection), or the list on screen made from another map than this one (a send folded
    /// into the map before the build it asks for) — the view builds.</summary>
    private void SelectEndpoint(Endpoint endpoint)
    {
        _mapSelection = endpoint;
        if (_mapListRef.Current is not { IsHosted: true } list || _mapDetailRef.Current is not { IsHosted: true }
            || _mapLines is not { } lines || !ReferenceEquals(lines.Map, _map) || !ReferenceEquals(list.Items, lines.Lines))
        {
            MarkViewDirty();
            return;
        }
        // What BuildEndpointMap hands the list: the line SelectedLine finds.
        list.SelectedIndex = SelectedLine(lines, endpoint);
        if (MapDetailStale(endpoint)) _mapDetailRevision.Value++;
    }

    // ── The endpoint's detail: a part of its own ─────────────────────────
    //
    // The detail beside the map shows the selected endpoint, and nothing else in the view shows the
    // selection but the map's list. A click built the view for it: the sources panel, the map's
    // header and a patch of the list, which works every line's height out again. The detail is now a
    // part (see Part) with a revision of its own: a click tells the list through its ref and writes
    // the revision, so the detail builds alone (SelectEndpoint), as History's archived send does. It
    // builds with the view when the view places it showing another endpoint than the selected one,
    // and after a full rebuild (_partRevision, safe by default); a view-only build that leaves the
    // selection where it was (the observed toggle) leaves it as it is.
    private readonly PartGroup _mapDetailParts = new();
    private readonly Signal<int> _mapDetailRevision = new(0);
    private Part? _mapDetailPart;
    private readonly Ref<BoxElement> _mapDetailRef = new();
    /// <summary>What the detail was last built from: the endpoint and the full-rebuild revision.</summary>
    private Endpoint? _mapDetailShown;
    private int _mapDetailRevisionShown = -1;

    /// <summary>Builds of the endpoint's detail so far, for tests.</summary>
    internal int MapDetailBuilds => _mapDetailParts.Builds;

    /// <summary>Whether the detail on screen is not what a build of it for <paramref name="selected"/>
    /// makes now.</summary>
    private bool MapDetailStale(Endpoint selected) =>
        !ReferenceEquals(_mapDetailShown, selected) || _mapDetailRevisionShown != _partRevision.Peek();

    /// <summary>The detail's host, for the view's build to place beside the map: built again first
    /// when it is stale (<see cref="MapDetailStale"/>). The host takes the detail's place in the row:
    /// it never shrinks, and the detail fills its height as it filled the row's.</summary>
    private ComponentHost PlaceMapDetail(Endpoint selected)
    {
        var part = _mapDetailPart ??= new(_mapDetailParts, () =>
        {
            // Placed only beside a selected endpoint, and its own build runs after a click selected
            // one; nothing takes the selection away but a new base, which builds the view.
            if (_mapSelection is not { } endpoint) return Box.New();
            _mapDetailShown = endpoint;
            _mapDetailRevisionShown = _partRevision.Peek();
            var detail = BuildEndpointDetail(endpoint);
            detail.Key = "disc-detail";
            detail.FlexGrow(1);
            detail.WithRef(_mapDetailRef);
            return detail;
        }, own: _mapDetailRevision);
        part.Mount();   // the first build, before asking what it was built from
        return part.Place(stale: MapDetailStale(selected));
    }

    private BoxElement BuildEndpointDetail(Endpoint endpoint)
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
            if (written > 0) Flash($"Wrote {OpenApi.FileNameFor(captured)}");   // builds the window, the tree too
            else MarkDirty();
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
