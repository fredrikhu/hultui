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
        IEnumerable<PaletteCommand>? commands = null, Theme? theme = null, int? maxRows = null,
        Func<Element>? searchIcon = null)
    {
        var session = HeadlessSession.Create(1000, 700);
        if (theme is not null) session.Application.Theme = theme;
        session.Window.SetRoot(Box.New());
        session.PumpFrame();
        var ran = new List<string>();
        var list = (commands ?? Standard(ran)).ToList();
        var palette = new CommandPalette(() => list)
        {
            ToggleKeys = [Toggle], MaxRows = maxRows ?? 100, SearchIcon = searchIcon,
        };
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

        for (int i = 0; i < 120; i++) palette.Move(1);
        Assert.Equal(99, palette.SelectedIndex);   // only what is shown can be chosen
        session.PumpFrame();
        Assert.NotNull(session.Window.FindByAutomationId("palette-row-99"));
        Assert.Null(session.Window.FindByAutomationId("palette-row-100"));
        var scroll = FindScroll(session.Window.FindByAutomationId("palette")!.Element);
        scroll.ScrollOffsetY = scroll.ContentHeight;
        session.PumpFrame();
        Assert.NotNull(session.Window.FindByText("50 more — type to narrow them down"));
    }

    // ──── Only the rows on screen are elements ────
    //
    // A keystroke built every match up to MaxRows, 100 rows for the 8 to 11 the card shows: 335 KB
    // a key over 2,000 commands, and a reader was told the whole list removed and added. The list
    // is a virtual one now. The rows are what they were (buttons, not list items), so the reader's
    // tree holds the ones realized, as in any other virtual list, and they have to be there when
    // the tree is built: a list realizes rows as it is drawn, after the frame's accessibility phase.

    private static List<PaletteCommand> Mixed(int count, Action<int>? ran = null, Action? made = null) =>
        Enumerable.Range(0, count).Select(i => new PaletteCommand($"Command {i}", () => ran?.Invoke(i))
        {
            Detail = i % 3 == 0 ? $"refs/heads/{i}" : null,
            Icon = made is null ? null : () => { made(); return Box.New().Width(8).Height(8); },
        }).ToList();

    private static (HeadlessSession Session, CommandPalette Palette, HeadlessAccessibilityBridge Bridge) OpenHeard(
        IReadOnlyList<PaletteCommand> commands)
    {
        var session = HeadlessSession.Create(1000, 700);
        var bridge = session.Window.InstallAccessibilityBridge();
        session.Window.SetRoot(Box.New());
        session.PumpFrames(2);
        var palette = new CommandPalette(() => commands);
        palette.Open(session.Window.Window);
        session.PumpFrame();
        return (session, palette, bridge);
    }

    private static List<AccessibilityNode> Rows(AccessibilityNode? tree) =>
        Walk(tree!).Where(n => n.Role == AccessibilityRole.Button).ToList();

    private static void AssertFresh(HeadlessSession session, HeadlessAccessibilityBridge bridge)
    {
        var diff = AccessibilityTreeDiffer.Diff(bridge.CurrentTree, session.Window.Window.BuildAccessibilityTree());
        Assert.True(diff.IsEmpty, $"stale: +{diff.Added.Count} -{diff.Removed.Count} "
            + string.Join(", ", diff.PropertyChanged.Select(c => $"{c.PropertyName} of {c.Node.Role} '{c.Node.Name}'")));
    }

    [Fact]
    public void AKeystrokeMakesOnlyTheRowsOnScreen_AndAMoveOfTheChoiceNone()
    {
        int made = 0, cards = 0;
        var commands = Enumerable.Range(0, 2_000).Select(i => new PaletteCommand($"{(i % 2 == 0 ? "branch" : "commit")} {i}", () => { })
        {
            Icon = () => { made++; return Box.New().Width(8).Height(8); },
        });
        // The search icon is made with the card, so it counts the card's builds.
        var (session, palette, _) = Open(commands, searchIcon: () => { cards++; return Box.New().Width(12).Height(12); });
        using var _s = session;

        // Twelve rows and a bit fit; the list makes one or two more beside them.
        Assert.InRange(made, 12, 16);
        made = 0;
        session.Window.TypeText("b");
        session.PumpFrame();
        Assert.Equal(1_000, palette.Matches.Count);
        Assert.InRange(made, 12, 16);

        // The list draws the choice: no row is made (two were), only the card, and the chosen row
        // is still told chosen.
        made = cards = 0;
        session.Window.PressKey(KeyCode.Down);
        session.PumpFrame();
        Assert.Equal(1, palette.SelectedIndex);
        Assert.Equal((0, 1), (made, cards));
        Assert.Equal(true, session.Window.FindByAutomationId("palette-row-1")!.Element.AccessibleSelected);
        Assert.Equal(false, session.Window.FindByAutomationId("palette-row-0")!.Element.AccessibleSelected);
    }

    [Fact]
    public void SelectingARowFromAssistiveTechnology_RunsIt()
    {
        var ran = new List<int>();
        var (session, palette, _) = Open(Mixed(40, ran.Add));
        using var _s = session;
        var router = new HultUI.AccessibilityActionRouter(session.Window.Window);

        // The chosen row says it is selected already, which selecting leaves alone; selecting
        // another runs it, as a click does.
        router.SelectElement(session.Window.FindByAutomationId("palette-row-0")!.Element.Id);
        router.DrainPendingActions();
        Assert.Empty(ran);
        router.SelectElement(session.Window.FindByAutomationId("palette-row-3")!.Element.Id);
        router.DrainPendingActions();
        Assert.Equal([3], ran);
        Assert.False(palette.IsOpen);
    }

    [Fact]
    public void TheListDrawsTheChoiceAndTheTintUnderThePointer_AsTheRowsDid()
    {
        var look = new CommandPaletteTheme
        {
            SelectedBackground = new Color(40, 90, 200, 60), HoverBackground = new Color(200, 120, 30), RowCornerRadius = 5f,
        };
        var (session, _, _) = Open(Mixed(40), Theme.Light.With(look));
        using var _s = session;
        // A VirtualListElement of the palette's own row type: read through its type.
        var list = FindScroll(session.Window.FindByAutomationId("palette")!.Element);
        object? Read(string name) => list.GetType().GetProperty(name)!.GetValue(list);

        Assert.Equal(new RowHighlight(look.SelectedBackground.Value, new CornerRadius(5f)), Read("SelectionHighlight"));
        Assert.Equal(new RowHighlight(look.HoverBackground.Value, new CornerRadius(5f)), Read("HoverHighlight"));
        Assert.Equal(0, Read("SelectedIndex"));
        // No row draws a fill of its own, chosen or under the pointer.
        var row = session.Window.FindByAutomationId("palette-row-2")!.Element;
        var box = row.WindowBorderBox;
        session.Window.MoveMouse(box.X + box.Width / 2, box.Y + box.Height / 2);
        session.PumpFrame();
        for (int i = 0; i < 6; i++)
            Assert.Equal(Color.Transparent, ((BoxElement)session.Window.FindByAutomationId($"palette-row-{i}")!.Element).BackgroundColor);
    }

    [Fact]
    public void AReaderHasTheRows_OnTheFrameThePaletteOpens_AndOnTheFrameOfEachKey()
    {
        var (session, palette, bridge) = OpenHeard(Mixed(400));
        using var _s = session;
        int trees = session.Window.AccessibilityTreesBuilt;

        Assert.Contains(Rows(bridge.CurrentTree), n => n.Name == "Command 0");
        session.PumpFrame(TimeSpan.FromMilliseconds(200));

        foreach (var key in new[] { "1", "2", "3" })
        {
            session.Window.TypeText(key);
            session.PumpFrame();
            Assert.Contains(Rows(bridge.CurrentTree), n => n.Name == palette.Matches[0].Title);
            Assert.Equal(palette.Matches.Take(Rows(bridge.CurrentTree).Count).Select(c => c.Title),
                Rows(bridge.CurrentTree).Select(n => n.Name));
            AssertFresh(session, bridge);
        }
        session.Window.PressKey(KeyCode.Backspace);
        session.PumpFrame();
        Assert.Contains(Rows(bridge.CurrentTree), n => n.Name == palette.Matches[1].Title);
        AssertFresh(session, bridge);

        // Each key built the palette's part of the tree, not the window's.
        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
    }

    [Fact]
    public void TheKeyboardWalksPastTheFold_WithTheChosenRowOnScreenAndToldAsChosen()
    {
        var commands = Mixed(150);
        var (session, palette, bridge) = OpenHeard(commands);
        using var _s = session;
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        int trees = session.Window.AccessibilityTreesBuilt;
        var scroll = FindScroll(session.Window.FindByAutomationId("palette")!.Element);
        var look = new CommandPaletteTheme();

        for (int i = 1; i <= 99; i++)
        {
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrame();
            Assert.Equal(i, palette.SelectedIndex);

            var rows = Rows(bridge.CurrentTree);
            var chosen = Assert.Single(rows, n => n.IsSelected == true);
            Assert.Equal($"Command {i}", chosen.Name);
            Assert.False(chosen.IsOffscreen);
            Assert.InRange(rows.Count, 1, 16);
            Assert.All(rows.Where(n => n != chosen), n => Assert.False(n.IsSelected));

            // Where the summed heights say, and in the view.
            float top = look.ListPadding.Top + commands.Take(i).Sum(c => c.Detail is null ? look.RowHeight : look.DetailRowHeight);
            var view = scroll.Layout.AbsoluteContentBox;
            var box = session.Window.FindByAutomationId($"palette-row-{i}")!.Element.Layout.AbsoluteBorderBox;
            Assert.Equal(view.Y + top, box.Y, 0.5f);
            Assert.True(box.Y - scroll.ScrollOffsetY >= view.Y - 0.5f && box.Bottom - scroll.ScrollOffsetY <= view.Bottom + 0.5f,
                $"row {i} at {box.Y - scroll.ScrollOffsetY:0}, the list shows {view.Y:0}–{view.Bottom:0}");
        }
        Assert.DoesNotContain(Rows(bridge.CurrentTree), n => n.Name == "Command 0");

        session.Window.PressKey(KeyCode.Down);   // no wrap: it stops at the last row shown
        session.PumpFrame();
        Assert.Equal(99, palette.SelectedIndex);
        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);

        for (int i = 98; i >= 0; i--)
        {
            session.Window.PressKey(KeyCode.Up);
            session.PumpFrame();
            Assert.Equal($"Command {i}", Assert.Single(Rows(bridge.CurrentTree), n => n.IsSelected == true).Name);
        }
        Assert.Equal(0f, scroll.ScrollOffsetY);
        AssertFresh(session, bridge);
    }

    [Fact]
    public void RowsJoinTheReadersTree_AsTheyAreScrolledTo()
    {
        var (session, _, bridge) = OpenHeard(Mixed(150));
        using var _s = session;
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        Assert.DoesNotContain(Rows(bridge.CurrentTree), n => n.Name == "Command 40");

        var scroll = FindScroll(session.Window.FindByAutomationId("palette")!.Element);
        var box = scroll.Layout.AbsoluteContentBox;
        for (int turn = 0; turn < 3 && scroll.ScrollOffsetY < 1500; turn++)
            session.Window.Wheel(box.X + 50, box.Y + 50, 0, -10);
        session.PumpFrame();
        session.PumpFrame(TimeSpan.FromMilliseconds(200));

        Assert.True(scroll.ScrollOffsetY > 0);
        Assert.Contains(Rows(bridge.CurrentTree), n => n.Name == session.Window.FindByAutomationId("palette-row-40")?.Element.AccessibleName);
        Assert.DoesNotContain(Rows(bridge.CurrentTree), n => n.Name == "Command 0");
        AssertFresh(session, bridge);
    }

    [Fact]
    public void ANarrowedListGoesBackToTheTop_AndShrinksTheCard_AndComesBack()
    {
        var (session, palette, _) = Open(Mixed(150));
        using var _s = session;
        var scroll = FindScroll(session.Window.FindByAutomationId("palette")!.Element);
        float full = Card(session).Layout.AbsoluteBorderBox.Height;

        for (int i = 0; i < 40; i++) session.Window.PressKey(KeyCode.Down);
        session.PumpFrame();
        Assert.True(scroll.ScrollOffsetY > 0);

        session.Window.TypeText("7");
        session.PumpFrame();
        Assert.Equal(0, palette.SelectedIndex);
        Assert.Equal(0f, scroll.ScrollOffsetY);
        Assert.Equal(palette.Matches[0].Title, session.Window.FindByAutomationId("palette-row-0")!.Element.AccessibleName);

        session.Window.PressKey(KeyCode.Backspace);
        session.Window.TypeText("149");
        session.PumpFrame();
        Assert.Equal(["Command 149"], palette.Matches.Select(c => c.Title));
        Assert.True(Card(session).Layout.AbsoluteBorderBox.Height < 200f);
        Assert.Null(session.Window.FindByAutomationId("palette-row-1"));

        for (int i = 0; i < 3; i++) session.Window.PressKey(KeyCode.Backspace);
        session.PumpFrame();
        Assert.Equal(full, Card(session).Layout.AbsoluteBorderBox.Height, 0.5f);
        Assert.NotNull(session.Window.FindByAutomationId("palette-row-5"));
    }

    [Fact]
    public void NoMatchesAndBack_LeavesAListThatWorks()
    {
        var ran = new List<int>();
        var (session, palette, _) = Open(Mixed(150, ran.Add));
        using var _s = session;

        session.Window.TypeText("zz");
        session.PumpFrame();
        Assert.NotNull(session.Window.FindByAutomationId("palette-no-match"));

        session.Window.PressKey(KeyCode.Backspace);
        session.Window.PressKey(KeyCode.Backspace);
        session.PumpFrame();
        Assert.Null(session.Window.FindByAutomationId("palette-no-match"));
        for (int i = 0; i < 20; i++) session.Window.PressKey(KeyCode.Down);
        session.PumpFrame();
        Assert.True(FindScroll(session.Window.FindByAutomationId("palette")!.Element).ScrollOffsetY > 0);
        session.Window.PressKey(KeyCode.Enter);
        Assert.Equal([20], ran);
        Assert.False(palette.IsOpen);
    }

    [Fact]
    public void AClickRunsARowFarDownTheList()
    {
        var ran = new List<int>();
        var (session, palette, _) = Open(Mixed(150, ran.Add));
        using var _s = session;
        for (int i = 0; i < 30; i++) session.Window.PressKey(KeyCode.Down);
        session.PumpFrame();

        session.Window.Click(session.Window.FindByAutomationId("palette-row-30")!.Element);

        Assert.Equal([30], ran);
        Assert.False(palette.IsOpen);
    }

    [Fact]
    public void TheListKeepsItsPadding_ItsRowsTheirHeights_AndTheLineUnderThemItsOwn()
    {
        var look = new CommandPaletteTheme
        {
            ListPadding = new Thickness(10, 4, 14, 8), RowHeight = 28, DetailRowHeight = 44, Width = 500, MaxHeight = 600,
        };
        var commands = Mixed(50);
        var (session, _, _) = Open(commands, Theme.Light.With(look), maxRows: 6);
        using var _s = session;
        var scroll = FindScroll(session.Window.FindByAutomationId("palette")!.Element);
        var view = scroll.Layout.AbsoluteBorderBox;

        float y = view.Y + 10;
        for (int i = 0; i < 6; i++)
        {
            var box = session.Window.FindByAutomationId($"palette-row-{i}")!.Element.Layout.AbsoluteBorderBox;
            float height = commands[i].Detail is null ? 28 : 44;
            Assert.Equal((view.X + 8, y, view.Width - 12, height), (box.X, box.Y, box.Width, box.Height));
            y += height;
        }

        // Its height comes from measuring its text before it is made: as tall as it lays out, to
        // the pixel, or the rows above would sit wrong against the end of a scrolled list.
        var line = session.Window.FindByText("44 more — type to narrow them down")!.Element.Layout.AbsoluteBorderBox;
        Assert.Equal(y, line.Y, 0.01f);
        Assert.Equal(line.Bottom + 14, view.Y + scroll.ContentHeight, 0.01f);
        Assert.Equal(view.Height, scroll.ContentHeight, 0.01f);   // and the list is as tall as its rows
    }

    [Fact]
    public void EachRowIsItselfAPlaceInTheList_ItsMarginsThePadding()
    {
        // The list lays a row's margins out inside its place, so a row needs no box around it to
        // hold the list's padding — a box, and its style, with every row made.
        var look = new CommandPaletteTheme { ListPadding = new Thickness(10, 4, 14, 8), MaxHeight = 600 };
        var (session, _, _) = Open(Mixed(50), Theme.Light.With(look), maxRows: 6);
        using var _s = session;
        var list = FindScroll(session.Window.FindByAutomationId("palette")!.Element);

        var places = list.Children.Skip(1).ToList();   // the first is the list's spacer
        Assert.Equal(7, places.Count);
        for (int i = 0; i < 6; i++) Assert.Equal($"palette-row-{i}", places[i].AutomationId);
        Assert.Equal("44 more — type to narrow them down", Assert.IsType<TextElement>(places[6]).Content);
    }

    [Fact]
    public void TheLineUnderTheList_WrapsAsItWouldInACardTooNarrowForIt()
    {
        var (session, _, _) = Open(Mixed(50), Theme.Light.With(new CommandPaletteTheme { Width = 160 }), maxRows: 2);
        using var _s = session;
        var scroll = FindScroll(session.Window.FindByAutomationId("palette")!.Element);
        var line = session.Window.FindByText("48 more — type to narrow them down")!.Element.Layout.AbsoluteBorderBox;
        Assert.True(line.Height > 40f, $"{line.Height:0.##} is one line");
        Assert.Equal(line.Bottom + new CommandPaletteTheme().ListPadding.Bottom, scroll.Layout.AbsoluteBorderBox.Y + scroll.ContentHeight, 0.01f);
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
