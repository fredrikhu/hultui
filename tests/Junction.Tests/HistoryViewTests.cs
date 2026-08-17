using System.Globalization;
using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The History list, hosted headlessly against a real repository.
/// </summary>
public class HistoryViewTests
{
    /// <summary>
    /// A commit's timestamp and the tooltip over it must not say the same thing twice.
    /// <para>
    /// The label shows the author's own wall clock to the minute, in whatever offset the
    /// commit was written at, without naming that offset — so the two things a reader can
    /// still be missing are the seconds and the offset itself. Both live in the tooltip.
    /// It previously repeated the same instant in a second format, which answered neither.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTimestampTooltipAddsTheSecondsAndOffsetTheLabelOmits()
    {
        // A commit deliberately not in the machine's own offset, and at a second that is
        // not :00 — an offset equal to the local one, or a whole minute, would let a label
        // that quietly dropped either still match.
        const string authored = "2026-03-04 05:06:07 +0530";
        Hosted(repo =>
        {
            repo.Write("a.txt", "x\n");
            repo.Git("add", "-A");
            repo.Git("-c", "user.name=A", "-c", "user.email=a@b.invalid",
                "commit", "-m", "timestamped", "--date", authored);
        }, (repo, session, app) =>
        {
            var date = DateTimeOffset.Parse(
                repo.Git("log", "-1", "--format=%aI").Stdout.Trim(),
                CultureInfo.InvariantCulture, DateTimeStyles.None);
            Assert.Equal(TimeSpan.FromMinutes(330), date.Offset);   // the fixture, not the machine

            session.Window.PressKey(KeyCode.Digit2);
            Assert.Equal(JunctionView.History, app.View);

            var label = date.ToString("g");
            TextElement? stamp = null;
            try
            {
                session.PumpUntil(() =>
                {
                    stamp = LayoutProbe.Descendants<TextElement>(Root(session), t => t.Content == label)
                        .FirstOrDefault();
                    return stamp != null;
                }, TimeSpan.FromSeconds(20));
            }
            catch (TimeoutException)
            {
                throw new Xunit.Sdk.XunitException(
                    $"no commit row showed the timestamp \"{label}\".\n" +
                    session.Window.DumpAccessibilityTree());
            }

            var tooltip = stamp!.TooltipText;
            Assert.False(string.IsNullOrEmpty(tooltip), "the timestamp carries no tooltip");
            Assert.NotEqual(label, tooltip);
            // The seconds and the offset, named outright rather than inferred from a shape:
            // a tooltip that merely differs from the label is what the old one already did.
            Assert.Contains(":07", tooltip);
            Assert.Contains("+05:30", tooltip);
        });
    }

    /// <summary>
    /// "Only &lt;branch&gt;" in the include bar: one click narrows the log to the branch that
    /// is checked out, and a second widens it back. Setting that up a branch at a time from
    /// the Branches tab's eye toggles was the only way to say it.
    /// </summary>
    [Fact]
    public void TheOnlyCurrentBranchButtonNarrowsTheLogToTheCheckedOutBranchAndBack()
    {
        Hosted(repo =>
        {
            repo.Git("checkout", "-b", "side");
            repo.Write("s.txt", "s\n");
            repo.Git("add", "-A");
            repo.Git("commit", "-m", "off to one side");
            repo.Git("checkout", "main");
            repo.Write("m.txt", "m\n");
            repo.Git("add", "-A");
            repo.Git("commit", "-m", "on the main line");
        }, (repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit2);
            Assert.Equal(JunctionView.History, app.View);

            // Every branch by default, so the side branch's commit is on screen.
            PumpUntil(session, () => Shows(session, "off to one side"),
                "the log did not start out showing every branch");

            Click(app, "ToggleCurrentBranchOnly");
            // Both halves of the claim in one wait: the log reloads asynchronously, and an
            // empty list mid-reload satisfies "the side commit is gone" all by itself.
            PumpUntil(session, () => !Shows(session, "off to one side") && Shows(session, "on the main line"),
                "filtering to the current branch did not leave exactly its own commits");

            Click(app, "ToggleCurrentBranchOnly");
            PumpUntil(session, () => Shows(session, "off to one side"),
                "pressing the button again did not widen the log back to every branch");
        });
    }

    private static void Click(JunctionApp app, string method) =>
        typeof(JunctionApp).GetMethod(method,
            BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);

    private static bool Shows(HeadlessSession session, string subject) =>
        LayoutProbe.Descendants<TextElement>(Root(session),
            t => t.Content == subject && t.Layout.AbsoluteBorderBox.Height > 0).Count > 0;

    private static void PumpUntil(HeadlessSession session, Func<bool> condition, string message)
    {
        try
        {
            session.PumpUntil(condition, TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException e)
        {
            throw new Xunit.Sdk.XunitException($"{message}.\n{e.Message}\n"
                + session.Window.DumpAccessibilityTree());
        }
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
