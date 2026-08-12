using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Meridian.Theme;

namespace Meridian;

public partial class MeridianApp
{
    // ── Requests (view 1): front door, or the collection shell ───────────

    private Element BuildRequestsView() =>
        _workspace == null ? BuildFrontDoor() : BuildCollectionShell();

    /// <summary>
    /// First run, per mock 02 frame 3: the wordmark with flanking rules fading
    /// outward, one sentence, four entry cards. The Discover card carries the
    /// accent border — the differentiating pillar gets the emphasis. Cards whose
    /// feature hasn't landed use the sheet's disabled treatment (45% opacity),
    /// never a click that silently does nothing.
    /// </summary>
    private Element BuildFrontDoor()
    {
        var door = Flex.Column(gap: 0).FlexGrow(1)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center);

        // Wordmark row: 1px rules fading FROM the wordmark outward, accent at the
        // near end — the meridian line.
        var mark = Flex.Row(gap: Azimuth.S4).AlignItems(AlignItems.Center);
        mark.AddChild(Rule(fadeLeft: true));
        mark.AddChild(BuildWordmarkLarge());
        mark.AddChild(Rule(fadeLeft: false));
        door.AddChild(mark);

        door.AddChild(Box.New().Height(Azimuth.S3));
        door.AddChild(Text.New("Collections are folders of plain text files. Point me at one, or start from an API.")
            .FontSize(13f).TextColor(Azimuth.TextDim));
        door.AddChild(Box.New().Height(Azimuth.S5 + Azimuth.S2));

        // 2×2 card grid.
        var grid = Flex.Column(gap: Azimuth.S3).AlignItems(AlignItems.Center);
        grid.AddChild(Flex.Row(gap: Azimuth.S3).Children(
            EntryCard(Icons.FolderOpen, "Open a folder",
                "An existing collection — or any repo with .req files in it.",
                "Ctrl O", OpenFolder),
            EntryCard(Icons.Plus, "New collection",
                "Creates a folder, a first request, and an env file.",
                "Ctrl N", null)));
        grid.AddChild(Flex.Row(gap: Azimuth.S3).Children(
            EntryCard(Icons.ImportSpec, "Import OpenAPI",
                "A spec file or URL becomes a browsable endpoint map.",
                "Ctrl I", null),
            EntryCard(Icons.Discovery, "Discover an API",
                "Give a base URL; Meridian probes for specs and maps what it sees.",
                "3", () => SetView(MeridianView.Discovery), accent: true)));
        door.AddChild(grid);

        return door;
    }

    private static Element Rule(bool fadeLeft)
    {
        var near = Azimuth.Accent;
        var far = Color.Transparent;
        var rule = Box.New().Width(56).Height(1);
        rule.BackgroundGradient = fadeLeft
            ? Gradient.Horizontal(far, near)
            : Gradient.Horizontal(near, far);
        return rule;
    }

    private static Element BuildWordmarkLarge() => Wordmark(28f, Azimuth.Text);

    /// <summary>An entry card. A null <paramref name="onClick"/> means the feature
    /// hasn't landed: the sheet's 45% disabled treatment + an honest tooltip.</summary>
    private Element EntryCard(string glyph, string title, string desc, string key, Action? onClick, bool accent = false)
    {
        bool enabled = onClick != null;

        var card = Flex.Column(gap: Azimuth.S2)
            .Width(320).FlexShrink(0)
            .Padding(new Thickness(Azimuth.S4))
            .CornerRadius(Azimuth.Radius2)
            .Background(Azimuth.Surface)
            .BorderColor(accent ? Azimuth.Accent : Azimuth.Border).BorderWidth(1f)
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName(enabled ? title : $"{title} (not available yet)")
            .AutomationId("door-" + title.ToLowerInvariant().Replace(' ', '-'));

        var head = Flex.Row(gap: Azimuth.S2).AlignItems(AlignItems.Center);
        head.AddChild(Ui.Icon(glyph, 16f, enabled ? Azimuth.Accent300 : Azimuth.TextMuted));
        head.AddChild(Text.New(title).FontSize(13f).FontWeight(FontWeight.Medium).TextColor(Azimuth.Text));
        head.AddChild(Box.New().FlexGrow(1));
        head.AddChild(Ui.Kbd(key));
        card.AddChild(head);
        card.AddChild(Text.New(desc).FontSize(11.5f).TextColor(Azimuth.TextMuted));

        if (enabled)
        {
            card.Cursor(CursorType.Pointer);
            card.OnClick(_ => onClick!());
            card.HoverBg(Azimuth.Raised, Azimuth.Surface);
        }
        else
        {
            card.Opacity(0.45f);
            card.Tooltip("Arrives in a later phase — see IMPLEMENTATION-PLAN.md");
        }
        return card;
    }

    // ── Views 2–5: designed empty states until their phases land ─────────

    private Element BuildRunnerView() => EmptyView("Runner",
        "nothing to run yet",
        "Open a collection in Requests (1); runs stream in here, exactly as CI sees them.");

    private Element BuildDiscoveryView() => EmptyView("Discovery",
        "nothing discovered yet",
        "Import a spec or send requests against a base URL, and the endpoint map grows here.");

    private Element BuildHistoryView() => EmptyView("History",
        "no sends yet",
        "Everything you send lands here, newest first.");

    private Element BuildEnvironmentsView() => BuildEnvironmentsRealView();

    private static Element EmptyView(string title, string headline, string hint) =>
        Flex.Column(gap: Azimuth.S2).FlexGrow(1)
            .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center)
            .Children(
                Text.New(title).FontSize(15f).FontWeight(FontWeight.SemiBold).TextColor(Azimuth.TextDim),
                Text.New(headline).FontSize(12f).TextColor(Azimuth.TextMuted),
                Text.New(hint).FontSize(11.5f).TextColor(Azimuth.TextFaint));
}
