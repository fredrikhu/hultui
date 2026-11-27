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

    /// <summary>The sign-in card at a Galaxy S25's 360 DIP and a Pixel's 411: it was 420 wide, and
    /// its Connect button half off the screen's edge.</summary>
    [Theory]
    [InlineData(360)]
    [InlineData(411)]
    public void AtAPhonesSize_TheSignInCard_FitsTheScreen(int width)
    {
        Halide.UsePhoneSizes();
        try
        {
            Host(Library(), (session, app) =>
            {
                PumpUntilShown(session, "Films");
                app.SignOut(keepAccount: false);
                for (int i = 0; i < 6; i++) session.PumpFrame();

                Assert.True(Shows(session, "Sign in to your Jellyfin server."), session.Window.DumpAccessibilityTree());
                foreach (var id in new[] { "connect-address", "connect-password", "connect-submit" })
                {
                    var box = session.Window.FindByAutomationId(id)?.Node.BoundingRect
                        ?? throw new Xunit.Sdk.XunitException($"no {id}: {session.Window.DumpAccessibilityTree()}");
                    // Inside the card's padding, inside the page's margin: the card itself is not
                    // against the screen's edges (16 + 16).
                    Assert.True(box.X >= 2 * Halide.S4 - 0.5f && box.Right <= width - 2 * Halide.S4 + 0.5f,
                        $"{id} spans {box.X}..{box.Right} on a screen {width} wide");
                }
            }, width: width, height: 780);
        }
        finally { Halide.UseDesktopSizes(); }
    }

    /// <summary>Settings at a Galaxy S25's 360 DIP: Sign out and the request server's Connect ran
    /// off the right edge, a path's long file name ran past the card, and the quality chip read
    /// "10 Mb…". Everything stays on the screen, the chip whole. Also at 320, a small phone: the
    /// test's fonts are narrower than a phone's Roboto, so at 360 two of the three faults fit.</summary>
    [Theory]
    [InlineData(360)]
    [InlineData(320)]
    public void AtAPhonesSize_Settings_StaysOnTheScreen(int width)
    {
        Halide.UsePhoneSizes();
        try
        {
            Host(Library(), (session, app) =>
            {
                PumpUntilShown(session, "Films");
                app.SetMaxStreamingKbps(10_000);
                session.Window.FindByAutomationId("rail-settings")!.Click();
                for (int i = 0; i < 6; i++) session.PumpFrame();

                foreach (var id in new[] { "settings-refresh", "settings-signout", "settings-requests-connect", "settings-quality" })
                {
                    var box = session.Window.FindByAutomationId(id)?.Node.BoundingRect
                        ?? throw new Xunit.Sdk.XunitException($"no {id}: {session.Window.DumpAccessibilityTree()}");
                    Assert.True(box.Right <= width, $"{id} spans {box.X}..{box.Right} on a screen {width} wide");
                }
                // The longest value on the page: the season baseline's file, one unbroken name.
                var path = Walk(session.Window.AccessibilityTree).First(n => n.Name?.Contains("watch-", StringComparison.Ordinal) == true);
                Assert.True(path.BoundingRect.Right <= width, $"the path spans {path.BoundingRect.X}..{path.BoundingRect.Right}");
                // The chip is as wide as "10 Mbps" needs, not what a 130-wide label left it.
                var chip = session.Window.FindByAutomationId("settings-quality")!.Node.BoundingRect;
                Assert.True(chip.Width >= 60, $"the quality chip is {chip.Width} wide");
            }, width: width, height: 780);
        }
        finally { Halide.UseDesktopSizes(); }
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

    /// <summary>A filter turned on moves no filter chip: Clear filters comes in on the side away from
    /// where the chips are held. On a phone it came in front of the strip's chips and pushed them all
    /// right, Favourites, just turned on, partly out of view (a Galaxy S25). On the desktop the bar
    /// holds them at its right end, and Clear comes in front.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TurningAFilterOn_MovesNoFilterChip(bool phone)
    {
        void Body(HeadlessSession session, ApertureApp app)
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");
            string[] ids = ["filter-genre", "filter-unwatched", "filter-favourites"];
            var before = ids.Select(id => session.Window.FindByAutomationId(id)!.Node.BoundingRect.X).ToArray();
            Assert.Null(session.Window.FindByAutomationId("filter-clear"));

            session.Window.FindByAutomationId("filter-favourites")!.Click();
            for (int i = 0; i < 6; i++) session.PumpFrame();
            Assert.True(app.GridHasFilters);
            var after = ids.Select(id => session.Window.FindByAutomationId(id)!.Node.BoundingRect.X).ToArray();
            Assert.Equal(before, after);
            var clear = session.Window.FindByAutomationId("filter-clear")!.Node.BoundingRect;
            var favourites = session.Window.FindByAutomationId("filter-favourites")!.Node.BoundingRect;
            if (phone) Assert.True(clear.X >= favourites.Right, $"Clear at {clear.X}, Favourites ends at {favourites.Right}");
            else Assert.True(clear.Right <= before[0], $"Clear ends at {clear.Right}, Genre starts at {before[0]}");
        }

        if (phone) AtPhoneSize(Body);
        else Host(Library(), Body);
    }
}
