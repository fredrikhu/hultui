using Aperture.Core;
using Aperture.Theme;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace Aperture;

/// <summary>
/// Home: what you were watching, what comes next, and what has just arrived — in that
/// order, because that is the order of how likely you are to want it. A server with a
/// cold history shows only the latest rows, and an empty one says so rather than
/// presenting three blank headings.
/// </summary>
public sealed partial class ApertureApp
{
    private Element BuildHomeView()
    {
        EnsureLibraries();
        EnsureHomeRows();

        var scroll = ScrollView.Vertical().FlexGrow(1);
        // No FlexGrow inside a scroll view — see the note in the detail view.
        var page = Flex.Column(gap: Halide.S6)
            .Padding(new Thickness(Halide.S5));

        var resume = ResumeItems;
        var next = NextUpItems;
        var libraries = Libraries;

        bool anything = resume.Count > 0 || next.Count > 0 ||
                        libraries.Any(l => LatestIn(l.Id).Count > 0);

        if (!anything)
        {
            page.AddChild(HomeEmptyState());
            scroll.AddChild(page);
            return scroll;
        }

        // Home is several rows but one walk: the arrows run along a row and then into the
        // next, which is how every shelf on a television behaves. The flat list is built
        // in the order the rows are drawn, and each card is handed its place in it.
        var walk = new List<MediaItem>();
        walk.AddRange(resume);
        walk.AddRange(next);
        foreach (var library in libraries) walk.AddRange(LatestIn(library.Id));
        SetCursorList("home:" + walk.Count, walk, VisibleCards(Halide.PosterWidth));
        int cursorBase = 0;

        if (resume.Count > 0)
        {
            int start = cursorBase;
            page.AddChild(CardRow("Continue watching", resume.Count,
                i => StillCard(resume[i], cursorIndex: start + i), Halide.StillWidth, "row-resume"));
            cursorBase += resume.Count;
        }

        if (next.Count > 0)
        {
            int start = cursorBase;
            page.AddChild(CardRow("Next up", next.Count,
                i => PosterCard(next[i], cursorIndex: start + i), Halide.PosterWidth, "row-nextup"));
            cursorBase += next.Count;
        }

        foreach (var library in libraries)
        {
            var latest = LatestIn(library.Id);
            if (latest.Count == 0) continue;
            var id = library.Id;
            int start = cursorBase;
            page.AddChild(CardRow($"Latest in {library.Name}", latest.Count,
                i => PosterCard(latest[i], cursorIndex: start + i),
                Halide.PosterWidth, "row-latest-" + id, trailing: "open"));
            cursorBase += latest.Count;
        }

        scroll.AddChild(page);
        return scroll;
    }

    /// <summary>
    /// Nothing to show. Three reasons look identical from here — still loading, an empty
    /// server, a user with no libraries shared to them — so the state says which one it is
    /// rather than showing the same shrug for all three.
    /// </summary>
    private Element HomeEmptyState()
    {
        var column = Flex.Column(gap: Halide.S3)
            .Height(Dimension.Percent(100))
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .AutomationId("home-empty");

        if (HomeIsLoading)
        {
            column.AddChild(Ui.Icon(Icons.Loading, 22f, Halide.TextFaint));
            column.AddChild(Text.New("Asking the server…").FontSize(13f).TextColor(Halide.TextMuted));
            return column;
        }

        if (Libraries.Count == 0)
        {
            column.AddChild(Ui.Icon(Icons.OtherLibrary, 26f, Halide.TextFaint));
            column.AddChild(Text.New("No libraries here").FontSize(15f).FontWeight(FontWeight.SemiBold)
                .TextColor(Halide.TextDim));
            column.AddChild(Text.New($"{_library?.User.Name} has no libraries on {_library?.Server.Name}.")
                .FontSize(12f).TextColor(Halide.TextMuted));
            column.AddChild(Text.New("An administrator grants access under Dashboard → Users.")
                .FontSize(11.5f).TextColor(Halide.TextFaint));
            return column;
        }

        column.AddChild(Ui.Icon(Icons.Movies, 26f, Halide.TextFaint));
        column.AddChild(Text.New("Nothing watched yet").FontSize(15f).FontWeight(FontWeight.SemiBold)
            .TextColor(Halide.TextDim));
        column.AddChild(Text.New("Open a library from the rail and start something.")
            .FontSize(12f).TextColor(Halide.TextMuted));

        var row = Flex.Row(gap: Halide.S2).AlignItems(AlignItems.Center);
        foreach (var library in Libraries.Take(4))
        {
            var id = library.Id;
            var name = library.Name;
            row.AddChild(Ui.Button(ButtonVariant.Secondary, name,
                () => Navigate(new Route(ApertureView.Library, id, name)),
                "empty-open-" + id, Icons.ForLibrary(library.Kind)));
        }
        column.AddChild(row);
        return column;
    }
}
