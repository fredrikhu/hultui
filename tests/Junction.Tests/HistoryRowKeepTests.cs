using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Headless;
using Junction.Theme;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// History's list keeps a row until its commit or its focus changes (the item-rows
/// <c>RenderWith</c>), so a key walk makes two rows a key instead of every row on screen. What
/// else a row shows — the wider selection, the theme, the window width that drops the ref
/// chips — is told to the list by making the rows again. These pin both halves.
/// </summary>
public class HistoryRowKeepTests
{
    [Fact]
    public void APlainMove_MakesTheOldAndTheNewFocusedRow_AndKeepsTheRest()
    {
        Hosted((session, app) =>
        {
            var before = Rows(session);
            int made = app.HistoryRowsMade;

            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(3);

            Assert.Equal(2, app.HistoryRowsMade - made);
            var after = Rows(session);
            Assert.Equal(before.Count, after.Count);
            int kept = before.Zip(after).Count(p => ReferenceEquals(p.First, p.Second));
            Assert.Equal(before.Count - 2, kept);
            Assert.Equal(Nocturne.Accent900, after[1].BackgroundColor);   // the commit moved to
            Assert.NotEqual(Nocturne.Accent900, after[0].BackgroundColor);
        });
    }

    /// <summary>Shift+Down widens the selection: rows are coloured by membership now, which the
    /// list does not see, so every row is made again and both selected commits draw selected.</summary>
    [Fact]
    public void WideningTheSelection_MakesEveryRowAgain()
    {
        Hosted((session, app) =>
        {
            int onScreen = Rows(session).Count;
            int made = app.HistoryRowsMade;

            session.Window.PressKey(KeyCode.Down, KeyModifiers.Shift);
            session.PumpFrames(3);

            Assert.True(app.HistoryRowsMade - made >= onScreen, $"{app.HistoryRowsMade - made} rows made of {onScreen}");
            var rows = Rows(session);
            Assert.Equal(Nocturne.Accent900, rows[0].BackgroundColor);
            Assert.Equal(Nocturne.Accent900, rows[1].BackgroundColor);

            // And back to one commit: the rows that were selected by membership are made again.
            made = app.HistoryRowsMade;
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(3);
            Assert.True(app.HistoryRowsMade - made >= onScreen);
            rows = Rows(session);
            Assert.NotEqual(Nocturne.Accent900, rows[0].BackgroundColor);
            Assert.NotEqual(Nocturne.Accent900, rows[1].BackgroundColor);
            Assert.Equal(Nocturne.Accent900, rows[2].BackgroundColor);
        });
    }

    [Fact]
    public void AThemeSwitch_MakesTheRowsInTheNewColours()
    {
        Hosted((session, app) =>
        {
            try
            {
                var before = Rows(session)[0];
                Assert.Equal(Nocturne.Accent900, before.BackgroundColor);

                // The app's own palette, as UseTheme switches it, and the rebuild that follows.
                Nocturne.Use("gruvbox-dark");
                Rebuild(app);
                session.PumpFrames(3);

                var after = Rows(session)[0];
                Assert.NotSame(before, after);
                Assert.Equal(Nocturne.Accent900, after.BackgroundColor);
                Assert.NotEqual(before.BackgroundColor, after.BackgroundColor);
            }
            finally
            {
                Nocturne.Use("nocturne");
            }
        });
    }

    /// <summary>A narrow window drops the ref chips from the rows: a width the rows show and the
    /// list does not see.</summary>
    [Fact]
    public void ANarrowWindow_DropsTheRefChipsFromTheKeptRows()
    {
        Hosted((session, app) =>
        {
            Assert.True(HasHeadChip(session), "the newest commit shows no HEAD chip");

            session.Window.SetClientSize(900, 900);
            session.PumpFrames(4);

            Assert.False(HasHeadChip(session), "the HEAD chip is still drawn in a narrow window");
        }, width: 1800);
    }

    private static bool HasHeadChip(HeadlessSession session) =>
        LayoutProbe.Descendants<TextElement>(Rows(session)[0], t => t.Content == "HEAD").Any();

    /// <summary>The History rows on screen, top to bottom.</summary>
    private static List<BoxElement> Rows(HeadlessSession session)
    {
        var list = LayoutProbe.Descendants<VirtualListElement<Git.GitCommit>>(Root(session), _ => true).Single();
        var rows = new List<BoxElement>();
        for (int i = 1; i < list.Children.Count; i++)
        {
            var row = (BoxElement)list.Children[i];
            if (row.Layout.AbsoluteY - list.ScrollOffsetY >= list.Layout.AbsoluteContentBox.Y - 1)
                rows.Add(row);
        }
        return rows;
    }

    private static void Hosted(Action<HeadlessSession, JunctionApp> body, int width = 1400)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        for (int i = 0; i < 12; i++)
            repo.Git("commit", "--allow-empty", "-m", $"Commit {i}");

        using var session = HeadlessSession.Create(width, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
            {
                RepositoryPaths = [repo.Path],
                ActiveRepoPath = repo.Path,
                Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
                ActiveWorkspaceId = "w1",
            });
            session.Application.SetRoot(app.Mount());
            session.PumpFrames(3);

            session.Window.PressKey(KeyCode.Digit2);   // History
            session.PumpUntil(() => Rows(session).Count > 10, TimeSpan.FromSeconds(30));
            session.PumpFrames(3);

            body(session, app);
        }
        finally { app?.Dispose(); }
    }

    private static void Rebuild(JunctionApp app)
    {
        for (var t = typeof(JunctionApp); t != null; t = t.BaseType)
            if (t.GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance) is { } m)
            { m.Invoke(app, null); return; }
        throw new Xunit.Sdk.XunitException("no MarkDirty to call");
    }

    private static Element Root(HeadlessSession session) =>
        (Element)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window)!;
}
