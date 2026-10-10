using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Testing;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The framework's <see cref="CommandPalette"/>, which replaced three apps' own. Each case is one
/// of theirs, or one only some of them got right: Meridian's fuzzy ranking, Junction's scrolling
/// to the chosen row, Meridian's named dialog — and the selection Meridian let scroll out of view.
/// </summary>
public sealed class CommandPaletteTests
{
    private static readonly KeyChord Toggle = new(KeyCode.P, KeyModifiers.Ctrl);

    private static (HeadlessSession Session, CommandPalette Palette, List<string> Ran) Open(
        IEnumerable<PaletteCommand>? commands = null, Theme? theme = null, int? maxRows = null)
    {
        var session = HeadlessSession.Create(1000, 700);
        if (theme is not null) session.Application.Theme = theme;
        session.Window.SetRoot(Box.New());
        session.PumpFrame();
        var ran = new List<string>();
        var list = (commands ?? Standard(ran)).ToList();
        var palette = new CommandPalette(() => list) { ToggleKeys = [Toggle], MaxRows = maxRows ?? 100 };
        palette.Open(session.Window.Window);
        session.PumpFrame();
        return (session, palette, ran);
    }

    private static IEnumerable<PaletteCommand> Standard(List<string> ran) =>
    [
        new("Go to Requests", () => ran.Add("requests")) { Group = "Go", Keys = "1" },
        new("Go to Runner", () => ran.Add("runner")) { Group = "Go", Keys = "2" },
        new("Save request", () => ran.Add("save")) { Group = "Request", Keys = "Ctrl+S" },
        new("Checkout main", () => ran.Add("main")) { Group = "Branch", Detail = "refs/heads/main" },
        new("Checkout tidy", () => ran.Add("tidy")) { Group = "Branch", Detail = "feature/cleanup-runner" },
    ];

    [Fact]
    public void ItOpensAsANamedDialog_WithTheQueryFocused()
    {
        var (session, palette, _) = Open();
        using var _s = session;

        Assert.True(palette.IsOpen);
        Assert.NotNull(session.Window.FindByRole(AccessibilityRole.Dialog, "Command palette"));
        // One dialog, named — not the card inside an unnamed one Dialog.Show made of the host.
        Assert.Single(Walk(session.Window.AccessibilityTree), n => n.Role == AccessibilityRole.Dialog);
        Assert.True(session.Window.FindByAutomationId("palette-input")!.IsFocused);
        Assert.Equal(5, palette.Matches.Count);
    }

    [Fact]
    public void TypingNarrowsTheList_TitlesFirst_ThenWhatElseARowSays()
    {
        var (session, palette, _) = Open();
        using var _s = session;

        session.Window.TypeText("gtr");
        session.PumpFrame();
        Assert.Equal("Go to Runner", palette.Matches[0].Title);

        // "cleanup" is in no title, only in a branch's detail line — found, after any title.
        palette.Query = "runner";
        Assert.Equal(["Go to Runner", "Checkout tidy"], palette.Matches.Select(m => m.Title));
        palette.Query = "nothing like it";
        session.PumpFrame();
        Assert.Empty(palette.Matches);
        Assert.NotNull(session.Window.FindByAutomationId("palette-no-match"));
    }

    [Fact]
    public void EnterRunsTheChosenCommand_AfterClosing()
    {
        var (session, palette, ran) = Open();
        using var _s = session;
        bool closedFirst = false;
        palette.Closed += () => closedFirst = ran.Count == 0;

        session.Window.PressKey(KeyCode.Down);
        session.Window.PressKey(KeyCode.Down);
        session.Window.PressKey(KeyCode.Enter);

        Assert.Equal(["save"], ran);
        Assert.True(closedFirst, "the command ran before the palette closed");
        Assert.False(palette.IsOpen);
    }

    [Fact]
    public void EscAndTheToggleKeyClose_AtOnce()
    {
        var (session, palette, ran) = Open();
        using var _s = session;
        session.Window.PressKey(KeyCode.Escape);
        Assert.False(palette.IsOpen);   // not a frame later

        palette.Open(session.Window.Window);
        session.PumpFrame();
        // The dialog is modal, so the app's handler never sees the key that opened it.
        session.Window.PressKey(KeyCode.P, KeyModifiers.Ctrl);
        Assert.False(palette.IsOpen);
        Assert.Empty(ran);
    }

    [Fact]
    public void AClickRunsTheRow()
    {
        var (session, palette, ran) = Open();
        using var _s = session;
        session.Window.Click(session.Window.FindByAutomationId("palette-row-3")!.Element);
        Assert.Equal(["main"], ran);
        Assert.False(palette.IsOpen);
    }

    [Fact]
    public void TheChosenRowStaysInView()
    {
        // Meridian's palette let the selection walk off the bottom of its list.
        var many = Enumerable.Range(0, 60).Select(i => new PaletteCommand($"Command {i:00}", () => { }));
        var (session, palette, _) = Open(many);
        using var _s = session;
        var scroll = FindScroll(session.Window.FindByAutomationId("palette")!.Element);   // the dialog

        for (int i = 0; i < 30; i++) session.Window.PressKey(KeyCode.Down);
        session.PumpFrame();

        Assert.Equal(30, palette.SelectedIndex);
        var row = session.Window.FindByAutomationId("palette-row-30")!.Element.Layout.AbsoluteBorderBox;
        var view = scroll.Layout.AbsoluteContentBox;
        float top = row.Y - scroll.ScrollOffsetY, bottom = top + row.Height;
        Assert.True(scroll.ScrollOffsetY > 0, "the list never scrolled");
        Assert.True(top >= view.Y - 0.5f && bottom <= view.Y + view.Height + 0.5f,
            $"row 30 at {top:0}–{bottom:0}, list shows {view.Y:0}–{view.Y + view.Height:0}");

        session.Window.PressKey(KeyCode.PageUp);
        session.Window.PressKey(KeyCode.PageUp);
        session.Window.PressKey(KeyCode.PageUp);
        session.Window.PressKey(KeyCode.PageUp);
        session.PumpFrame();
        Assert.Equal(0, palette.SelectedIndex);
        Assert.Equal(0f, scroll.ScrollOffsetY);
    }

    [Fact]
    public void TheCardFitsAShortList_AndStopsAtItsHeightForALongOne()
    {
        float shortCard;
        var (session, _, _) = Open();
        using (session) shortCard = Card(session).Layout.AbsoluteBorderBox.Height;
        Assert.True(shortCard < 300f, $"five rows made a {shortCard:0}px card");

        var many = Enumerable.Range(0, 60).Select(i => new PaletteCommand($"Command {i:00}", () => { }));
        var (longSession, _, _) = Open(many);
        using (longSession)
            Assert.Equal(new CommandPaletteTheme().MaxHeight, Card(longSession).Layout.AbsoluteBorderBox.Height, 0.5f);
    }

    private static Element Card(HeadlessSession session) =>
        LayoutProbe.Descendants<BoxElement>(session.Window.FindByAutomationId("palette")!.Element,
            e => e.AutomationId == "palette-card").Single();

    [Fact]
    public void ALongListShowsItsHead_AndSaysHowManyMore()
    {
        var many = Enumerable.Range(0, 150).Select(i => new PaletteCommand($"Branch {i}", () => { }));
        var (session, palette, _) = Open(many, maxRows: 100);
        using var _s = session;
        Assert.NotNull(session.Window.FindByAutomationId("palette-row-99"));
        Assert.Null(session.Window.FindByAutomationId("palette-row-100"));
        Assert.NotNull(session.Window.FindByText("50 more — type to narrow them down"));

        for (int i = 0; i < 120; i++) palette.Move(1);
        Assert.Equal(99, palette.SelectedIndex);   // only what is shown can be chosen
    }

    [Fact]
    public void TheListIsTakenWhenItOpens_NotOnEveryKey()
    {
        using var session = HeadlessSession.Create(800, 600);
        session.Window.SetRoot(Box.New());
        session.PumpFrame();
        int asked = 0;
        var palette = new CommandPalette(() => { asked++; return [new PaletteCommand("Push", () => { })]; });
        palette.Open(session.Window.Window);
        session.PumpFrame();
        session.Window.TypeText("pus");
        session.PumpFrame();
        Assert.Equal(1, asked);
    }

    [Fact]
    public void KeyChipsAndThePaletteTakeTheirTheme()
    {
        var kbd = new Color(10, 20, 30);
        var card = new Color(40, 50, 60);
        var theme = Theme.Dark
            .With(new KbdTheme { Background = kbd })
            .With(new CommandPaletteTheme { Background = card, Width = 480 });
        var (session, _, _) = Open(theme: theme);
        using var _s = session;

        var host = session.Window.FindByAutomationId("palette")!.Element;
        var panel = LayoutProbe.Descendants<BoxElement>(host, e => e.AutomationId == "palette-card").Single();
        Assert.Equal(card, panel.BackgroundColor);
        Assert.Equal(480f, panel.Layout.AbsoluteBorderBox.Width, 0.5f);
        var chip = LayoutProbe.Descendants<KbdElement>(panel).First();
        Assert.Equal(kbd, chip.BackgroundColor);
    }

    [Fact]
    public void Filter_BreaksTiesByTheShorterTitle_ThenByListOrder()
    {
        PaletteCommand C(string title) => new(title, () => { });
        var commands = new[] { C("Save all environments"), C("Save"), C("Save as"), C("Sabre") };

        Assert.Equal(["Save", "Save as", "Save all environments", "Sabre"],
            CommandPalette.Filter(commands, "save").Concat(CommandPalette.Filter(commands, "sabre")).Distinct()
                .Select(c => c.Title));
        Assert.Same(commands, CommandPalette.Filter(commands, "  "));
        Assert.Equal(-1, FuzzyMatch.Score("Go to Runner", "rg"));
        Assert.True(FuzzyMatch.Score("Go to Runner", "run") < FuzzyMatch.Score("Go to Runner", "gtr"));
    }

    // ──── The ranking, against the first implementation ────
    //
    // Filter used to join every unmatched command's text afresh on each keystroke, match a
    // title-only command's title twice, and rank through a three-key LINQ sort: 232 KB a query
    // over 2,000 commands with groups and details. It now keeps the joined text for as long as
    // the palette is open and sorts an array. The answers must not move, item for item.

    [Fact]
    public void Filter_RanksExactlyAsTheFirstImplementationDid()
    {
        int ties = 0, pastTitle = 0;
        for (int seed = 1; seed <= 6; seed++)
        {
            var commands = Corpus(300, seed);
            var index = IndexOf(commands);
            var kept = new string?[commands.Count];   // one per open palette, filled as needed
            foreach (var query in Queries)
            {
                var expected = Indices(ReferenceFilter(commands, query), index);
                Assert.Equal(expected, Indices(CommandPalette.Filter(commands, query), index));
                Assert.Equal(expected, Indices(CommandPalette.Filter(commands, query, kept), index));

                var scores = commands.Select(c => ReferenceScore(c, query.Trim())).ToList();
                var matched = scores.Where(s => s >= 0).ToList();
                ties += matched.Count - matched.Distinct().Count();
                pastTitle += matched.Count(s => s >= 1_000_000);
            }
        }
        // The corpus must exercise what the ranking orders by, or the comparison above proves little.
        Assert.True(ties > 1_000, $"only {ties} tied scores");
        Assert.True(pastTitle > 1_000, $"only {pastTitle} matches past the title");
    }

    [Fact]
    public void AnOpenPalette_RanksAsTheFirstImplementationDid_AndForgetsTheListItClosedOn()
    {
        var first = Corpus(200, 7);
        var second = Corpus(200, 8);   // as many commands, other words: a kept text would show
        var current = first;
        using var session = HeadlessSession.Create(1000, 700);
        session.Window.SetRoot(Box.New());
        session.PumpFrame();
        var palette = new CommandPalette(() => current);
        palette.Query = "save";   // closed: nothing to match, and nothing kept for the list it opens on
        Assert.Empty(palette.Matches);

        foreach (var commands in new[] { first, second })
        {
            current = commands;
            palette.Open(session.Window.Window);
            session.PumpFrame();
            var index = IndexOf(commands);
            foreach (var query in Queries)
            {
                palette.Query = query;
                Assert.Equal(Indices(ReferenceFilter(commands, query), index), Indices(palette.Matches, index));
            }
            palette.Close();
            session.PumpFrame();
            Assert.False(palette.IsOpen);
        }
    }

    [Fact]
    public void Filter_AllocatesOnlyTheListItReturns()
    {
        // Titles only: nothing past the title to match, so nothing to join.
        var titles = Enumerable.Range(0, 2_000)
            .Select(i => new PaletteCommand($"{(i % 3 == 0 ? "Save" : "Open")} document {i}", () => { })).ToList();
        int saves = CommandPalette.Filter(titles, "sav").Count;
        Assert.Equal(667, saves);
        double plain = Allocations.PerCall(10, () => CommandPalette.Filter(titles, "sav"), goal: ListBytes(saves));
        Assert.True(plain <= ListBytes(saves), $"Filter over 2,000 titles allocated {plain:F0} B; its list is {ListBytes(saves)} B");

        // Groups and details: an open palette joins each command's text once, not per keystroke.
        var grouped = titles.Select((c, i) => c with { Group = i % 2 == 0 ? "File" : "Edit", Detail = $"C:\\docs\\folder {i}" }).ToList();
        var kept = new string?[grouped.Count];
        int folders = CommandPalette.Filter(grouped, "fold", kept).Count;
        Assert.Equal(2_000, folders);   // every one past its title
        double again = Allocations.PerCall(10, () => CommandPalette.Filter(grouped, "fold", kept), goal: ListBytes(folders));
        Assert.True(again <= ListBytes(folders), $"a second query allocated {again:F0} B; its list is {ListBytes(folders)} B");
    }

    /// <summary>A List of <paramref name="count"/> references sized to them: the object and its array.</summary>
    private static long ListBytes(int count) => 32 + 24 + 8L * count;

    /// <summary>The ranking as it was first written, LINQ and all.</summary>
    private static List<PaletteCommand> ReferenceFilter(IReadOnlyList<PaletteCommand> commands, string query)
    {
        query = query.Trim();
        if (query.Length == 0) return commands.ToList();
        return commands
            .Select((command, index) => (command, index, score: ReferenceScore(command, query)))
            .Where(x => x.score >= 0)
            .OrderBy(x => x.score)
            .ThenBy(x => x.command.Title.Length)
            .ThenBy(x => x.index)
            .Select(x => x.command)
            .ToList();
    }

    private static int ReferenceScore(PaletteCommand command, string query)
    {
        int score = FuzzyMatch.Score(command.Title, query);
        if (score >= 0) return score;
        var rest = string.Join(" ", new[] { command.Title, command.Group, command.Detail, command.Keywords }
            .Where(s => !string.IsNullOrEmpty(s)));
        score = FuzzyMatch.Score(rest, query);
        return score < 0 ? -1 : 1_000_000 + score;
    }

    /// <summary>Commands from a few words, so titles tie on score and on length, with a group,
    /// detail and keywords each missing, empty or set at random, and some titles empty.</summary>
    private static List<PaletteCommand> Corpus(int count, int seed)
    {
        string[] words = ["Save", "save", "all", "Go", "to", "Runner", "requests", "Branch", "main", "tidy", "Sabre",
            "x", "Open", "recent", "Ünïcode", "go to"];
        string?[] extras = [null, "", "Go", "Branch", "refs/heads/main", "feature/cleanup-runner", "Request",
            "zeta quux", "Ctrl+S", "Ünï"];
        var rng = new Random(seed);
        var commands = new List<PaletteCommand>(count);
        for (int i = 0; i < count; i++)
        {
            var title = string.Join(" ", Enumerable.Range(0, rng.Next(0, 4)).Select(_ => words[rng.Next(words.Length)]));
            commands.Add(new PaletteCommand(title, () => { })
            {
                Group = extras[rng.Next(extras.Length)],
                Detail = extras[rng.Next(extras.Length)],
                Keywords = extras[rng.Next(extras.Length)],
            });
        }
        return commands;
    }

    private static readonly string[] Queries =
    [
        "", "  ", "s", "save", "SAVE", " save ", "sa", "gtr", "go to", "g t r", "main", "refs", "heads/main",
        "zeta", "quux", "zq", "ctrl", "cleanup", "x", "ü", "ÜNÏ", "zzzz", "rq", "a", "e", "o", "br", "tidy main", "req",
    ];

    // By identity: commands are records, and two built alike are equal.
    private static Dictionary<PaletteCommand, int> IndexOf(List<PaletteCommand> commands)
    {
        var index = new Dictionary<PaletteCommand, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < commands.Count; i++) index[commands[i]] = i;
        return index;
    }

    private static int[] Indices(IReadOnlyList<PaletteCommand> matches, Dictionary<PaletteCommand, int> index) =>
        matches.Select(c => index[c]).ToArray();

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node) =>
        node.Children.SelectMany(Walk).Prepend(node);

    private static ScrollViewElement FindScroll(Element root) =>
        LayoutProbe.Descendants<ScrollViewElement>(root).First();
}
