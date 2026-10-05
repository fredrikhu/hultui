using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The change list's section headers are held at the top of the list while their section
/// scrolls under them, so Unstage all / Stage all stay in reach in a long list — and still work
/// where they are drawn.
/// </summary>
public class ChangesStickyHeaderTests
{
    [Fact]
    public void TheStagedHeaderStaysInReach_WhileItsFilesScrollUnderIt()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        for (int i = 0; i < 30; i++) repo.Write($"staged-{i:00}.txt", $"{i}\n");
        repo.Git("add", "-A");
        for (int i = 0; i < 5; i++) repo.Write($"loose-{i}.txt", $"{i}\n");

        using var session = HeadlessSession.Create(1100, 700);
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
            session.PumpFrame();
            session.Window.PressKey(KeyCode.Digit4);
            session.PumpUntil(() => session.Window.FindByRole(AccessibilityRole.Button, "Unstage all") is not null,
                TimeSpan.FromSeconds(20));
            session.PumpFrames(2);

            var unstageAll = session.Window.FindByRole(AccessibilityRole.Button, "Unstage all")!.Element;
            var list = Ancestor<ScrollViewElement>(unstageAll)!;
            float top = list.Layout.AbsoluteContentBox.Y;
            float before = unstageAll.ScreenBorderBox.Y;

            list.ScrollOffsetY = 400;
            session.PumpFrames(2);

            // Still where it was, at the top of the list, rows having gone by under it.
            Assert.Equal(before, unstageAll.ScreenBorderBox.Y, 0.5f);
            Assert.True(unstageAll.ScreenBorderBox.Y - top < 30f);

            // And a press there is a press on it.
            var box = unstageAll.ScreenBorderBox;
            session.Window.MouseDown(box.X + box.Width / 2, box.Y + box.Height / 2);
            session.Window.MouseUp(box.X + box.Width / 2, box.Y + box.Height / 2);
            session.PumpUntil(() => repo.Repo.GetStagedChanges().Count == 0, TimeSpan.FromSeconds(20));
        }
        finally { app.Dispose(); }
    }

    private static T? Ancestor<T>(Element element) where T : Element
    {
        for (var e = element.Parent; e is not null; e = e.Parent)
            if (e is T match) return match;
        return null;
    }
}
