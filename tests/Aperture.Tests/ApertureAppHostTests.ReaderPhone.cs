using HultUI.Core.Accessibility;
using HultUI.Media.Testing;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// What TalkBack on a Galaxy S25 found in Aperture's main views (plan 20 phase 6, 2026-11-27): a dot
/// that was a stop of its own, a rail that did not say which view is open, titles that were not
/// headings, a film's Back read after everything else on its page, Back from a film that started over
/// at the first card, and a filter chip whose double tap moved TalkBack to its neighbour.
/// </summary>
public partial class ApertureAppHostTests
{
    /// <summary>Whether the app's title bar is in the window: its wordmark is among the elements. Not
    /// read from the reader's tree, which leaves it out while the player covers it.</summary>
    private static bool TitleBarUp(ApertureApp app) =>
        AllOf(app.Mount()).OfType<HultUI.Core.Elements.TextElement>().Any(t => t.Content == "APERTURE");

    [Fact]
    public void TheTitleBarsDot_IsNoStop_AndTheRailSaysWhichViewIsOpen()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            Assert.DoesNotContain(Walk(session.Window.AccessibilityTree), n => n.Name == "·");
            Assert.True(session.Window.FindByAutomationId("rail-home")!.Node.IsSelected);
            Assert.False(session.Window.FindByAutomationId("rail-films")!.Node.IsSelected);

            session.Window.FindByAutomationId("rail-films")!.Click();
            for (int i = 0; i < 6; i++) session.PumpFrame();
            Assert.True(session.Window.FindByAutomationId("rail-films")!.Node.IsSelected);
            Assert.False(session.Window.FindByAutomationId("rail-home")!.Node.IsSelected);
        });
    }

    [Fact]
    public void ShelfAndPageTitles_AreHeadings()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            Assert.Contains(Walk(session.Window.AccessibilityTree),
                n => n.Name == "Continue watching" && n.Role == AccessibilityRole.Heading);

            session.Window.FindByAutomationId("rail-settings")!.Click();
            for (int i = 0; i < 6; i++) session.PumpFrame();
            Assert.Contains(Walk(session.Window.AccessibilityTree), n => n.Name == "Settings" && n.Role == AccessibilityRole.Heading);
        });
    }

    /// <summary>Back is the first stop on a film's page, as it is drawn: TalkBack read it after the
    /// verbs and the overview. And Back returns to the card that was opened, however it was opened:
    /// only the keys moved the cursor, so after a click or a reader's double tap Back started at the
    /// first card on screen.</summary>
    [Fact]
    public void AFilmsPage_ReadsBackFirst_AndBackReturnsToTheCardItWasOpenedFrom()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");
            // The last card in reading order: Back to the first would prove nothing.
            var cards = Walk(session.Window.AccessibilityTree)
                .Where(n => n.AutomationId?.StartsWith("card-", StringComparison.Ordinal) == true).ToList();
            Assert.True(cards.Count >= 3, session.Window.DumpAccessibilityTree());
            string opened = cards[^1].AutomationId!;
            session.Window.FindByAutomationId(opened)!.Click();
            for (int i = 0; i < 8; i++) session.PumpFrame();
            Assert.Equal(ApertureView.Detail, app.Current.View);

            var order = Walk(session.Window.AccessibilityTree).ToList();
            int back = order.FindIndex(n => n.AutomationId == "detail-back");
            int title = order.FindIndex(n => n.Role == AccessibilityRole.Heading);
            Assert.True(back >= 0 && title >= 0 && back < title, $"Back at {back}, the title at {title}");

            Assert.True(app.GoBack());
            for (int i = 0; i < 8; i++) session.PumpFrame();
            Assert.Equal(opened, session.Window.Window.FocusedElement?.AutomationId);
        });
    }

    /// <summary>A filter chip keeps its element when a filter goes on and Clear filters comes in
    /// front of it: it shifted every chip into the next one's element, and TalkBack, on Unwatched,
    /// read Genre after the double tap that turned it on. It says it is on as a toggle, not in its
    /// name.</summary>
    [Fact]
    public void AFilterChip_KeepsItsElement_WhenItTurnsOn_AndSaysItIsOn()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");
            var chip = session.Window.FindByAutomationId("filter-unwatched")!.Element;
            Assert.False(session.Window.FindByAutomationId("filter-unwatched")!.Node.IsToggled);

            session.Window.FindByAutomationId("filter-unwatched")!.Click();
            for (int i = 0; i < 6; i++) session.PumpFrame();
            Assert.True(app.GridHasFilters);
            var after = session.Window.FindByAutomationId("filter-unwatched")!;
            Assert.Same(chip, after.Element);
            Assert.True(after.Node.IsToggled);
            Assert.Equal("Unwatched", after.Node.Name);
        });
    }

    /// <summary>The player's arrow is named for what it draws.</summary>
    [Fact]
    public void ThePlayersArrow_IsNamedBack()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            app.Navigate(new Route(ApertureView.Detail, "m1", "Stalker"));
            PumpUntilShown(session, "Stalker is a fixture.");
            session.Window.FindByAutomationId("detail-play")!.Click();
            for (int i = 0; i < 6; i++) session.PumpFrame();
            var back = session.Window.FindByAutomationId("player-back");
            Assert.NotNull(back);
            Assert.StartsWith("Back", back!.Node.Name);
            // What the player covers is not reached: TalkBack swiped from it into the film's page.
            var ids = Walk(session.Window.AccessibilityTree).Select(n => n.AutomationId).ToList();
            Assert.DoesNotContain("detail-play", ids);
            Assert.DoesNotContain("detail-back", ids);
            Assert.DoesNotContain("titlebar-user", ids);
        }, backends: () => new FakeMediaBackend());
    }
}
