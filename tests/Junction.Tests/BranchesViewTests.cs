using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The Branches view, hosted headlessly against a real repository.
/// <para>
/// The remote-name chip ("☁ origin") sits in the same shrinkable title row as the branch
/// name. Without an explicit no-shrink the chip used to compress against a long title and
/// wrap its own label onto two lines ("origi/n") — Ui.Chip now carries FlexShrink(0) itself.
/// </para>
/// </summary>
public class BranchesViewTests
{
    [Fact]
    public void TheRemoteChipKeepsItsLabelOnOneLineBesideALongTitle()
    {
        Hosted(repo =>
        {
            repo.Git("remote", "add", "origin", "https://example.invalid/repo.git");
            repo.Git("update-ref", "refs/remotes/origin/dev", "HEAD");
            // The name from the report: a prefix-less ticket leaf, long enough that the
            // title cell runs out of room at 1440px and the chips beside it get squeezed.
            repo.Git("update-ref", "refs/remotes/origin/PROJ-3057-image-resize", "HEAD");
        }, (repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit5);
            Assert.Equal(JunctionView.Branches, app.View);

            // The table is virtualized and its rows are realized during layout, so wait
            // for both chips to exist rather than pumping a fixed number of frames.
            List<TextElement> chips = [];
            try
            {
                session.PumpUntil(() =>
                {
                    chips = LayoutProbe.Descendants<TextElement>(Root(session),
                        t => t.Content == "origin" && t.Layout.AbsoluteBorderBox.Height > 0);
                    return chips.Count == 2;
                }, TimeSpan.FromSeconds(20));
            }
            catch (TimeoutException e)
            {
                throw new Xunit.Sdk.XunitException(
                    $"expected two remote chips on screen, found {chips.Count}.\n{e.Message}");
            }

            // The chip beside the short title ("dev") has all the room it needs, so it is
            // the reference; the chip beside the long title must lay out to the same size.
            // Text metrics differ across OSes, so equal boxes — not fixed pixel bounds —
            // is the claim. Height catches the label wrapping onto a second line, width
            // catches it being truncated instead of shown whole.
            var a = chips[0].Layout.AbsoluteBorderBox;
            var b = chips[1].Layout.AbsoluteBorderBox;
            Assert.True(Math.Abs(a.Height - b.Height) < 0.5f,
                $"the two remote chips laid out at different heights ({a.Height:0.0}px vs {b.Height:0.0}px) — " +
                "the one beside the long title wrapped its label");
            Assert.True(Math.Abs(a.Width - b.Width) < 0.5f,
                $"the two remote chips laid out at different widths ({a.Width:0.0}px vs {b.Width:0.0}px) — " +
                "the one beside the long title truncated its label");
        });
    }

    // ── harness (same shape as TagsViewTests) ────────────────────────────
    private static void Hosted(Action<TempRepo> arrange, Action<TempRepo, HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        arrange(repo);

        using var session = HeadlessSession.Create(1440, 900);
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
            session.PumpFrame();
            body(repo, session, app);
        }
        finally { app?.Dispose(); }
    }

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window);
}
