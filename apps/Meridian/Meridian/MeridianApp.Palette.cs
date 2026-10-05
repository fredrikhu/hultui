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

    private CommandPalette? _palette;

    /// <summary>The framework's palette, over <see cref="Commands"/>: the group in a column on
    /// the left, the key on the right, and Ctrl+P closing it again.</summary>
    private CommandPalette Palette => _palette ??= new CommandPalette(Commands)
    {
        ToggleKeys = Shortcuts.Map["palette"].Keys,
        GroupStyle = PaletteGroupStyle.Leading,
    };

    internal bool PaletteOpen => Palette.IsOpen;

    /// <summary>A row: title, group, the keys that do it without the palette ("" for none).</summary>
    private static PaletteCommand Command(string title, string group, string keys, Action run) =>
        new(title, run) { Group = group, Keys = keys.Length > 0 ? keys : null };

    private void TogglePalette() => Palette.Toggle(_window);

    /// <summary>
    /// Everything the palette can do right now — and only that. A command whose
    /// feature hasn't shipped is not listed: a palette full of dead entries is
    /// the same phantom-control problem as a dead button.
    /// </summary>
    internal List<PaletteCommand> Commands()
    {
        var list = new List<PaletteCommand>
        {
            new PaletteCommand("Open a folder…", OpenFolder) { Group = "Collection", Keys = Shortcuts.Keys("open") },
            new PaletteCommand("Go to Requests", () => SetView(MeridianView.Requests)) { Group = "Go", Keys = "1" },
            new PaletteCommand("Go to Runner", () => SetView(MeridianView.Runner)) { Group = "Go", Keys = "2" },
            new PaletteCommand("Go to Discovery", () => SetView(MeridianView.Discovery)) { Group = "Go", Keys = "3" },
            new PaletteCommand("Go to History", () => SetView(MeridianView.History)) { Group = "Go", Keys = "4" },
            new PaletteCommand("Go to Environments", () => SetView(MeridianView.Environments)) { Group = "Go", Keys = "5" },
        };

        if (_workspace != null)
        {
            list.Add(new PaletteCommand("Switch environment…", OpenEnvSwitcher) { Group = "Environment", Keys = Shortcuts.Keys("env") });
            list.Add(Command("Run the collection", "Run", Shortcuts.Keys("run"), () =>
            {
                SetView(MeridianView.Runner);
                StartRun();
            }));
            list.Add(new PaletteCommand("Import an OpenAPI spec…", OpenImportDialog) { Group = "Discovery", Keys = Shortcuts.Keys("import") });
            list.Add(Command("Discover an API…", "Discovery", "",
                () => OpenDialog("Discover an API", "https://api.example.com", url =>
                {
                    if (url.Trim() is { Length: > 0 } trimmed) StartDiscovery(trimmed);
                })));
            // Ctrl S saves whatever is on screen, so it is shown on the one row it does now.
            list.Add(Command("Save environments", "Environment",
                View == MeridianView.Environments ? Shortcuts.Keys("save") : "", SaveEnvironments));

            // Every request in the collection is a command — the fastest way
            // to a known request in a large tree.
            void Walk(CollectionFolder folder)
            {
                foreach (var sub in folder.Folders) Walk(sub);
                foreach (var request in folder.Requests)
                {
                    var path = request.Path;
                    list.Add(Command($"{request.Method ?? "?"}  {request.DisplayName}", "Request", "",
                        () => { OpenRequest(path); SetView(MeridianView.Requests); }));
                }
            }
            if (_tree is { } tree) Walk(tree.Root);
        }

        if (ActiveTab != null)
        {
            list.Add(Command("Send", "Request", View == MeridianView.Requests ? Shortcuts.Keys("send") : "", SendActive));
            list.Add(Command("Save request", "Request",
                View != MeridianView.Environments ? Shortcuts.Keys("save") : "", SaveActiveTab));
            list.Add(new PaletteCommand("Close tab", CloseActiveTab) { Group = "Request", Keys = Shortcuts.Keys("close") });
            if (!_forceResponseBottom)
                list.Add(Command("Flip the response pane", "Request",
                    View == MeridianView.Requests ? Shortcuts.Keys("flip") : "", FlipResponsePane));
        }

        list.Add(Command("Options…", "App", "", () => { _optionsOpen = true; MarkDirty(); }));
        return list;
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
            _forceResponseBottom ? "forced — the window is too narrow for the side pane" : $"{Shortcuts.Keys("flip")} also flips it",
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
