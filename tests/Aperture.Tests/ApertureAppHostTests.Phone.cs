using Aperture.Theme;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Headless;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// Aperture at a phone's size (plan 20 phase 5), with a phone's card sizes on
/// (<see cref="Halide.UsePhoneSizes"/>, which the Android build turns on for a screen under 600 DIP):
/// two posters across where one went, a film's page with no poster beside its title, and the
/// library's chips in a strip of their own. The Android-only parts (no caption buttons, no key hints)
/// read the platform, so a desktop test cannot reach them.
/// </summary>
public partial class ApertureAppHostTests
{
    private static void AtPhoneSize(Action<HeadlessSession, ApertureApp> body)
    {
        Halide.UsePhoneSizes();
        try { Host(Library(), body, width: 412, height: 915); }
        finally { Halide.UseDesktopSizes(); }
    }

    private static IEnumerable<AccessibilityNode> WithId(HeadlessSession session, string prefix) =>
        Walk(session.Window.AccessibilityTree).Where(n => n.AutomationId?.StartsWith(prefix, StringComparison.Ordinal) == true);

    [Fact]
    public void AtAPhonesSize_TwoPostersGoAcross_WhereOneDid()
    {
        Host(Library(), (session, app) => Assert.Equal(1, app.VisibleCards(Halide.PosterWidth)), width: 412, height: 915);
        AtPhoneSize((session, app) => Assert.Equal(2, app.VisibleCards(Halide.PosterWidth)));
    }

    [Fact]
    public void AtAPhonesSize_AFilmsPage_HasNoPosterBesideItsTitle()
    {
        AtPhoneSize((session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");
            session.Window.FindByAutomationId("card-m1")!.Click();
            for (int i = 0; i < 6; i++) session.PumpFrame();
            PumpUntilShown(session, "Stalker is a fixture.");

            var play = session.Window.FindByAutomationId("detail-play")!;
            // The title's block, and so Play, starts at the page's edge, not beside a poster 150 wide.
            Assert.True(play.Node.BoundingRect.X < Halide.RailWidth + Halide.S4 + 8, $"Play at {play.Node.BoundingRect.X}");
            Assert.True(play.Node.BoundingRect.Right <= 412, "Play runs off the screen");
        });
    }

    [Fact]
    public void AtAPhonesSize_TheLibrarysChips_AreAStripOfTheirOwn()
    {
        AtPhoneSize((session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");

            var genre = session.Window.FindByAutomationId("filter-genre")!;
            var refresh = session.Window.FindByAutomationId("library-refresh")!;
            // Under the title's row, not beside it.
            Assert.True(genre.Node.BoundingRect.Y > refresh.Node.BoundingRect.Bottom - 1,
                $"genre at {genre.Node.BoundingRect.Y}, the title's row ends at {refresh.Node.BoundingRect.Bottom}");
            // In a strip that scrolls sideways.
            Assert.Contains(Walk(session.Window.AccessibilityTree), n => n.Role == AccessibilityRole.ScrollView
                && n.Children.Count > 0 && n.ScrollExtent is { } e && e.Content.Width > e.Viewport.Width);
        });
    }
}
