using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using Meridian.Core;
using Meridian.Theme;

namespace Meridian;

public partial class MeridianApp
{
    // ── Command palette (Ctrl+P) ─────────────────────────────────────────

    internal sealed record Command(string Title, string Group, string Hint, Action Run);

    private bool _paletteOpen;
    private string _paletteQuery = "";
    private int _paletteSel;
    private readonly Ref<TextInputElement> _paletteRef = new();
    private bool _pendingPaletteFocus;

    internal bool PaletteOpen => _paletteOpen;

    private void TogglePalette()
    {
        _paletteOpen = !_paletteOpen;
        _paletteQuery = "";
        _paletteSel = 0;
        _pendingPaletteFocus = _paletteOpen;
        MarkDirty();
    }

    /// <summary>
    /// Everything the palette can do right now — and only that. A command whose
    /// feature hasn't shipped is not listed: a palette full of dead entries is
    /// the same phantom-control problem as a dead button.
    /// </summary>
    internal List<Command> Commands()
    {
        var list = new List<Command>
        {
            new("Open a folder…", "Collection", "Ctrl O", OpenFolder),
            new("Go to Requests", "Go", "1", () => SetView(MeridianView.Requests)),
            new("Go to Runner", "Go", "2", () => SetView(MeridianView.Runner)),
            new("Go to Discovery", "Go", "3", () => SetView(MeridianView.Discovery)),
            new("Go to History", "Go", "4", () => SetView(MeridianView.History)),
            new("Go to Environments", "Go", "5", () => SetView(MeridianView.Environments)),
        };

        if (_workspace != null)
        {
            list.Add(new Command("Switch environment…", "Environment", "Ctrl E", OpenEnvSwitcher));
            list.Add(new Command("Run the collection", "Run", "Ctrl R", () =>
            {
                SetView(MeridianView.Runner);
                StartRun();
            }));
            list.Add(new Command("Import an OpenAPI spec…", "Discovery", "Ctrl I",
                () => OpenDialog("Import OpenAPI", "path to openapi.json", ImportSpecFile)));
            list.Add(new Command("Discover an API…", "Discovery", "",
                () => OpenDialog("Discover an API", "https://api.example.com", url =>
                {
                    if (url.Trim() is { Length: > 0 } trimmed) StartDiscovery(trimmed);
                })));
            list.Add(new Command("Save environments", "Environment", "Ctrl S", SaveEnvironments));

            // Every request in the collection is a command — the fastest way
            // to a known request in a large tree.
            void Walk(CollectionFolder folder)
            {
                foreach (var sub in folder.Folders) Walk(sub);
                foreach (var request in folder.Requests)
                {
                    var path = request.Path;
                    list.Add(new Command($"{request.Method ?? "?"}  {request.DisplayName}", "Request", "",
                        () => { OpenRequest(path); SetView(MeridianView.Requests); }));
                }
            }
            if (_tree is { } tree) Walk(tree.Root);
        }

        if (ActiveTab != null)
        {
            list.Add(new Command("Send", "Request", "Ctrl ↵", SendActive));
            list.Add(new Command("Save request", "Request", "Ctrl S", SaveActiveTab));
            list.Add(new Command("Close tab", "Request", "Ctrl W", CloseActiveTab));
            if (!_forceResponseBottom)
                list.Add(new Command("Flip the response pane", "Request", "Ctrl \\", FlipResponsePane));
        }

        list.Add(new Command("Options…", "App", "", () => { _optionsOpen = true; MarkDirty(); }));
        return list;
    }

    /// <summary>Subsequence match, the palette convention: "gtr" finds "Go to
    /// Runner". Ranked so earlier, tighter matches come first.</summary>
    internal static List<Command> Filter(IReadOnlyList<Command> commands, string query)
    {
        query = query.Trim();
        if (query.Length == 0) return commands.ToList();
        return commands
            .Select(c => (Command: c, Score: Score(c.Title + " " + c.Group, query)))
            .Where(x => x.Score >= 0)
            .OrderBy(x => x.Score)
            // Ties are common — "save" matches "Save" and "Save the whole
            // environment set" with the same span — so the shorter title wins:
            // fewer unmatched characters is the tighter match.
            .ThenBy(x => x.Command.Title.Length)
            .Select(x => x.Command)
            .ToList();
    }

    /// <summary>Lower is better; -1 means no match. The score is the span the
    /// match needed, so contiguous prefixes beat scattered letters.</summary>
    internal static int Score(string haystack, string needle)
    {
        int first = -1, i = 0;
        foreach (char c in needle)
        {
            if (c == ' ') continue;
            int found = haystack.AsSpan(i).IndexOf(c.ToString(), StringComparison.OrdinalIgnoreCase);
            if (found < 0) return -1;
            found += i;
            if (first < 0) first = found;
            i = found + 1;
        }
        return first < 0 ? -1 : (i - first) * 100 + first;
    }

    private void RunPaletteSelection()
    {
        var matches = Filter(Commands(), _paletteQuery);
        if (_paletteSel < 0 || _paletteSel >= matches.Count) return;
        var command = matches[_paletteSel];
        _paletteOpen = false;
        command.Run();
        MarkDirty();
    }

    /// <summary>The palette owns the keyboard while it is up.</summary>
    private bool HandlePaletteKey(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case KeyCode.Escape: TogglePalette(); return true;
            case KeyCode.Enter: RunPaletteSelection(); return true;
            case KeyCode.Down:
                _paletteSel = Math.Min(_paletteSel + 1, Math.Max(Filter(Commands(), _paletteQuery).Count - 1, 0));
                MarkDirty();
                return true;
            case KeyCode.Up:
                _paletteSel = Math.Max(_paletteSel - 1, 0);
                MarkDirty();
                return true;
            default:
                return false;
        }
    }

    private Element BuildPalette()
    {
        var matches = Filter(Commands(), _paletteQuery);
        if (_paletteSel >= matches.Count) _paletteSel = Math.Max(matches.Count - 1, 0);

        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.FlexStart)
            .ZIndex(65);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.Background(Azimuth.Alpha(Azimuth.Bg, 0.62));
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; TogglePalette(); });

        var card = Flex.Column(gap: 0)
            .Width(560).MaxHeight(420)
            .Background(Azimuth.Surface)
            .BorderColor(Azimuth.BorderStrong).BorderWidth(1f)
            .CornerRadius(Azimuth.Radius2);
        card.Position.Type = PositionType.Absolute;
        card.Position.Top = Dimension.Px(90);
        card.On<ClickEvent>(e => e.StopPropagation = true);
        card.AccessibleRole(AccessibilityRole.Dialog).AccessibleName("Command palette");
        card.AutomationId("palette");

        var field = Flex.Row(gap: Azimuth.S2).Height(38).FlexShrink(0).AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
            .BorderColor(Azimuth.Border).BorderWidth(new Thickness(0, 0, 1, 0));
        var input = TextInput.New(_paletteQuery).Height(Dimension.Px(30)).FlexGrow(1);
        input.PlaceholderText("Search or run a command…");
        input.OnTextChanged(v => { _paletteQuery = v; _paletteSel = 0; MarkDirty(); });
        input.FontSize(13f).TextColor(Azimuth.Text).WithRef(_paletteRef);
        input.AutomationId("palette-input");
        field.AddChild(input);
        field.AddChild(Ui.Kbd("Ctrl P"));
        card.AddChild(field);

        var scroll = ScrollView.New();
        var list = Flex.Column(gap: 1).Padding(new Thickness(Azimuth.S1));
        if (matches.Count == 0)
            list.AddChild(Box.New().Padding(new Thickness(Azimuth.S3))
                .Children(Text.New($"no command matches \"{_paletteQuery}\"")
                    .FontSize(12f).TextColor(Azimuth.TextMuted).AutomationId("palette-no-match")));

        for (int i = 0; i < matches.Count && i < 60; i++)
        {
            var command = matches[i];
            bool selected = i == _paletteSel;
            int index = i;
            var row = Flex.Row(gap: Azimuth.S2).Height(Azimuth.RowRun).FlexShrink(0)
                .AlignItems(AlignItems.Center)
                .Padding(new Thickness(0, Azimuth.S3, 0, Azimuth.S3))
                .CornerRadius(Azimuth.Radius1)
                .Cursor(CursorType.Pointer)
                .AccessibleRole(AccessibilityRole.Button)
                .AccessibleName($"{command.Title} ({command.Group})")
                .AutomationId($"palette-row-{i}");
            if (selected) row.Background(Azimuth.Raised);
            row.AddChild(Ui.Mono(command.Group, 9.5f, Azimuth.TextFaint).Width(84).FlexShrink(0));
            row.AddChild(Text.New(command.Title).FontSize(12.5f)
                .TextColor(selected ? Azimuth.Text : Azimuth.TextDim)
                .TextOverflow(TextOverflow.Ellipsis).MaxLines(1).FlexGrow(1));
            if (command.Hint.Length > 0) row.AddChild(Ui.Kbd(command.Hint));
            row.OnClick(_ => { _paletteSel = index; RunPaletteSelection(); });
            if (!selected) row.HoverBg(Azimuth.Raised, Color.Transparent);
            list.AddChild(row);
        }
        scroll.AddChild(list);
        var box = Box.New().FlexGrow(1);
        box.AddChild(scroll);
        card.AddChild(box);

        backdrop.AddChild(card);
        return backdrop;
    }

    // ── Options ──────────────────────────────────────────────────────────

    private bool _optionsOpen;

    internal bool OptionsOpen => _optionsOpen;

    /// <summary>
    /// Only the settings that exist. Everything here changes behaviour the
    /// moment it is toggled — there is no "apply", and nothing is listed that
    /// the app does not honour.
    /// </summary>
    private Element BuildOptions()
    {
        var backdrop = Box.New()
            .Width(Dimension.Percent(100)).Height(Dimension.Percent(100))
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .ZIndex(66);
        backdrop.Position.Type = PositionType.Absolute;
        backdrop.Position.Top = Dimension.Px(0);
        backdrop.Position.Left = Dimension.Px(0);
        backdrop.Background(Azimuth.Alpha(Azimuth.Bg, 0.62));
        backdrop.On<ClickEvent>(e => { e.StopPropagation = true; _optionsOpen = false; MarkDirty(); });

        var card = Flex.Column(gap: Azimuth.S3)
            .Width(520).Padding(new Thickness(Azimuth.S4))
            .Background(Azimuth.Surface)
            .BorderColor(Azimuth.BorderStrong).BorderWidth(1f)
            .CornerRadius(Azimuth.Radius2);
        card.On<ClickEvent>(e => e.StopPropagation = true);
        card.AccessibleRole(AccessibilityRole.Dialog).AccessibleName("Options");
        card.AutomationId("options");

        card.AddChild(Text.New("Options").FontSize(14f).FontWeight(FontWeight.Medium).TextColor(Azimuth.Text));

        card.AddChild(OptionRow("Response pane at the bottom",
            _forceResponseBottom ? "forced — the window is too narrow for the side pane" : "Ctrl \\ also flips it",
            ResponseAtBottom, _forceResponseBottom, "option-response-bottom", () => FlipResponsePane()));

        card.AddChild(OptionRow("Stop the runner on the first failure",
            "a red assertion ends the run; unreachable hosts never do",
            _runBail, false, "option-bail", () => { _runBail = !_runBail; MarkDirty(); }));

        card.AddChild(OptionRow("Promote observed endpoints too",
            "discovery writes traffic-only endpoints, marked as inferred",
            _promoteObserved, false, "option-promote-observed",
            () => { _promoteObserved = !_promoteObserved; MarkDirty(); }));

        card.AddChild(Box.New().Height(Azimuth.S2));
        card.AddChild(Ui.Mono(_workspace is { } ws ? ws.Root : "no collection open", 10.5f, Azimuth.TextFaint));
        card.AddChild(Ui.Mono($"history {HistoryLog.Cap:N0} entries · response viewer cap 1 MB", 10.5f, Azimuth.TextFaint));

        var buttons = Flex.Row(gap: Azimuth.S2).JustifyContent(JustifyContent.FlexEnd);
        buttons.AddChild(BarAction("close", "options-close", () => { _optionsOpen = false; MarkDirty(); }));
        card.AddChild(buttons);

        backdrop.AddChild(card);
        return backdrop;
    }

    private Element OptionRow(string title, string detail, bool on, bool locked, string automationId, Action toggle)
    {
        var row = Flex.Row(gap: Azimuth.S3).AlignItems(AlignItems.Center)
            .Cursor(locked ? CursorType.Default : CursorType.Pointer)
            .AccessibleRole(AccessibilityRole.Checkbox)
            .AccessibleName($"{title}{(on ? ", on" : ", off")}{(locked ? ", locked" : "")}")
            .AutomationId(automationId);
        row.AddChild(Ui.Mono(on ? "☑" : "☐", 13f, locked ? Azimuth.TextFaint : on ? Azimuth.Accent300 : Azimuth.TextMuted)
            .Width(20).FlexShrink(0));
        row.AddChild(Flex.Column(gap: 1).FlexGrow(1).Children(
            Text.New(title).FontSize(12.5f).TextColor(Azimuth.TextDim),
            Text.New(detail).FontSize(10.5f).TextColor(Azimuth.TextFaint)));
        if (locked) row.Opacity(0.55f);
        else row.OnClick(_ => toggle());
        return row;
    }
}
