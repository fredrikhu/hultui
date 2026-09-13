using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using SkiaSharp;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// A hovered context-menu row must not paint over the menu's own border. Nocturne gives the
/// menu no horizontal padding, so the hover fill used to start at the border box's edge and
/// the frame lost a 1px notch beside every row the pointer touched — on the History menu,
/// the Branches menu, every menu. The check is on pixels: the border column beside a
/// hovered row is the same colour as beside a row that is not.
/// </summary>
public class ContextMenuBorderTests
{
    [Fact]
    public void AHoveredRowLeavesTheBorderColumnAlone()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Git("commit", "--allow-empty", "-m", "second");
        using var session = HeadlessSession.Create(1440, 900);
        var app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
        {
            RepositoryPaths = [repo.Path],
            ActiveRepoPath = repo.Path,
            Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
            ActiveWorkspaceId = "w1",
        });
        try
        {
            session.Application.SetRoot(app.Mount());
            session.PumpUntil(() => LayoutProbe.Descendants<TextElement>(Root(session), t => t.Content == "second").Count > 0,
                TimeSpan.FromSeconds(15));
            session.PumpFrames(2);

            // Right-click the first History row; the commit menu opens as an overlay.
            session.Window.Click(700, 172, MouseButton.Right);
            session.PumpFrames(2);
            Assert.NotEmpty(session.Window.OverlayContents);
            // The menu opens with its top-left at the pointer; its own layout box is
            // overlay-relative, so the click point is the anchor here.
            const float menuX = 700f, menuY = 172f;
            float hoveredY = menuY + 20, otherY = menuY + 220;

            session.Window.MoveMouse(menuX + 40, hoveredY);
            session.PumpFrames(2);

            using var image = session.Window.CapturePixels();
            using var pixels = image.PeekPixels();
            SKColor At(float x, float y) => pixels.GetPixelColor((int)MathF.Round(x), (int)MathF.Round(y));

            // The border column: the first pixel left of the menu's content that is not the
            // app ground beside the unhovered row.
            var ground = At(menuX - 3, otherY);
            int borderCol = Enumerable.Range((int)menuX - 2, 6).First(x => At(x, otherY) != ground);
            Assert.Equal(At(borderCol, otherY), At(borderCol, hoveredY));

            // And the hover did paint: a few pixels in, the two rows differ.
            Assert.NotEqual(At(borderCol + 3, otherY), At(borderCol + 3, hoveredY));

        }
        finally { app.Dispose(); }
    }

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window)
            .GetField("_root", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(session.Window.Window);
}
