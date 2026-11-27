using System.Text.RegularExpressions;
using HultUI.Core.Accessibility;
using HultUI.Headless;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// Aperture at a tablet's size: the desktop's card sizes (a screen whose shorter side is 600 DIP
/// or more), on a screen narrower than a desktop window.
/// </summary>
public partial class ApertureAppHostTests
{
    private static readonly string[] LibraryBarChips =
        ["filter-genre", "filter-unwatched", "filter-favourites", "sort-name", "sort-added",
         "sort-year", "sort-rating", "library-view", "library-refresh"];

    private static AccessibilityNode LibraryCount(HeadlessSession session) =>
        Walk(session.Window.AccessibilityTree).FirstOrDefault(n => n.Name is { } name && Regex.IsMatch(name, @"^\d+ of \d+$"))
        ?? throw new Xunit.Sdk.XunitException($"no count: {session.Window.DumpAccessibilityTree()}");

    /// <summary>The library's bar at a Galaxy Tab S4's 711 DIP, upright: its one row squeezed every
    /// chip, some to no text at all and the rest to an ellipsis, and the count "18 of 18" broke after
    /// its first number. Below the width the row needs, the chips go to a strip of their own under
    /// the title, as on a phone, each as wide as in a desktop window, and the count keeps one line.</summary>
    [Theory]
    [InlineData(711, 1138)]
    [InlineData(800, 1280)]
    public void AtATabletsWidth_TheLibraryBar_KeepsEveryChipWhole(int width, int height)
    {
        var wide = new Dictionary<string, float>();
        float countHeight = 0;
        void Open(HeadlessSession session)
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");
        }

        Host(Library(), (session, app) =>
        {
            Open(session);
            foreach (var id in LibraryBarChips)
                wide[id] = session.Window.FindByAutomationId(id)!.Node.BoundingRect.Width;
            countHeight = LibraryCount(session).BoundingRect.Height;
        });

        Host(Library(), (session, app) =>
        {
            Open(session);
            foreach (var id in LibraryBarChips)
            {
                float w = session.Window.FindByAutomationId(id)!.Node.BoundingRect.Width;
                Assert.True(w >= wide[id] - 0.5f, $"{id} is {w} wide at {width}, {wide[id]} in a desktop window");
            }
            var count = LibraryCount(session).BoundingRect;
            Assert.True(count.Height <= countHeight + 0.5f, $"the count is {count.Height} high at {width}, {countHeight} in a desktop window");
            Assert.True(count.Right <= width, $"the count runs to {count.Right} on a screen {width} wide");
        }, width: width, height: height);
    }

    /// <summary>A window narrowed past the width the bar's row needs moves the chips to their strip,
    /// and widened again, back into the row: the bar is read off the window's size, so a resize
    /// across that width builds the library again (ApertureApp.WindowFit.cs). Both widths have four
    /// posters across, so nothing else asks for that build.</summary>
    [Fact]
    public void AWindowNarrowedPastTheBar_MovesTheChipsToTheirStrip_AndBack()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");
            float Row(string id)
            {
                var box = session.Window.FindByAutomationId(id)!.Node.BoundingRect;
                return box.Y + box.Height / 2;
            }
            Assert.Equal(Row("filter-genre"), Row("library-refresh"), 1f);
            int across = app.VisibleCards(Aperture.Theme.Halide.PosterWidth);

            session.Window.SetClientSize(1000, 900);
            session.PumpFrames(4);
            Assert.Equal(across, app.VisibleCards(Aperture.Theme.Halide.PosterWidth));
            Assert.True(Row("filter-genre") > Row("library-refresh") + 8, "the chips stayed in the title's row");

            session.Window.SetClientSize(1012, 900);
            session.PumpFrames(4);
            Assert.Equal(Row("filter-genre"), Row("library-refresh"), 1f);
        }, width: 1012, height: 900);
    }
}
