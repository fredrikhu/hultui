using System.Reflection;
using System.Text;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Reactivity;
using HultUI.Headless;
using Junction.Theme;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The Changes view's composer is a child component the app keeps while it rebuilds around it
/// (ComposerPart, JunctionApp.Changes.cs), and a character in its fields builds it alone. The
/// risk is what it shows moving while it is kept: a commit button that still says what it said
/// before a file was staged, Amend pouring in an old message.
///
/// <para>The oracle is a full build, as in <see cref="ChromePartTests"/>: each check compares
/// the composer on screen with the one a forced rebuild makes, after each thing it shows changes
/// the way the app changes it.</para>
/// </summary>
public class ComposerPartTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    /// <summary>A character builds the composer and nothing else: no window build, no chrome,
    /// every element of the lists the one it was, whatever the number of changed files. It
    /// built the whole view (1.3 MB a character with 200 files).</summary>
    [Fact]
    public void ACharacter_BuildsTheComposerAlone_AndKeepsTheLists()
    {
        Hosted((repo, session, app) =>
        {
            var summary = Ref<TextInputElement>(app, "_summaryRef").Current!;
            session.Window.Click(summary);
            session.PumpFrames(2);
            var list = ListElements(app);
            Assert.True(list.Count > 100, $"{list.Count} elements in the lists");

            foreach (var ch in "Fix it")
            {
                int builds = app.Builds, chrome = app.ChromeBuilds, composer = app.ComposerBuilds;
                session.Window.TypeText(ch.ToString());
                session.PumpFrames(2);
                Assert.Equal(0, app.Builds - builds);
                Assert.Equal(0, app.ChromeBuilds - chrome);
                Assert.Equal(1, app.ComposerBuilds - composer);
            }

            Assert.Equal("Fix it", Get<string>(app, "_commitSummary"));
            Assert.Same(summary, Ref<TextInputElement>(app, "_summaryRef").Current);
            Assert.Same(summary, session.Window.Window.FocusedElement);
            Assert.Equal(6, summary.CursorPosition);
            Assert.Equal(list, ListElements(app));
            AssertIsAFullBuild(session, app, "summary typed");

            // The host took the composer's place in the column: a long list does not squeeze it.
            var root = Composer(session);
            Assert.True(root.Parent!.Layout.AbsoluteBorderBox.Height >= root.Layout.AbsoluteBorderBox.Height,
                $"the host is {root.Parent!.Layout.AbsoluteBorderBox.Height:0} px high, the composer {root.Layout.AbsoluteBorderBox.Height:0}");

            var description = Ref<TextAreaElement>(app, "_descRef").Current!;
            session.Window.Click(description);
            session.PumpFrames(2);
            int windowBuilds = app.Builds;
            session.Window.TypeText("Body");
            session.PumpFrames(2);
            Assert.Equal(windowBuilds, app.Builds);
            Assert.Equal("Body", Get<string>(app, "_commitDescription"));
            AssertIsAFullBuild(session, app, "description typed");
        });
    }

    /// <summary>A view-only build, a row selected, leaves the composer as it is.</summary>
    [Fact]
    public void AViewOnlyBuild_KeepsTheComposer()
    {
        Hosted((repo, session, app) =>
        {
            int composer = app.ComposerBuilds, builds = app.Builds;
            Invoke(app, "RerenderView");
            session.PumpFrames(2);
            Assert.True(app.Builds > builds, "the view did not build");
            Assert.Equal(composer, app.ComposerBuilds);
            AssertIsAFullBuild(session, app, "view-only build");
        });
    }

    /// <summary>What the composer shows moves with each way it can: the commit button with what
    /// is staged and with files that appear on their own, Amend and Sign with their boxes, the
    /// fields with a commit, the amend box's message with a HEAD rewritten behind the app's
    /// back, and all of it with the scheme and a visit to another view.</summary>
    [Fact]
    public void WhatTheComposerShows_FollowsEveryWayItChanges()
    {
        Hosted((repo, session, app) =>
        {
            string Button() => LayoutProbe.Descendants<TextElement>(Composer(session),
                t => t.Content.StartsWith("Commit", StringComparison.Ordinal) || t.Content.StartsWith("Amend last commit w", StringComparison.Ordinal)
                     || t.Content.StartsWith("Amend last commit (", StringComparison.Ordinal)).Single().Content;
            Assert.StartsWith("Commit all 40 changes", Button());

            // A file that appears on disk: the load lands with no full rebuild behind it.
            // On the frame the list counts it, the button does.
            repo.Write("late.txt", "late\n");
            Invoke(app, "RefreshAll");
            session.PumpUntil(() =>
            {
                bool listed = LayoutProbe.Descendants<TextElement>(Ref<ScrollViewElement>(app, "_changeScrollRef").Current!,
                    t => t.Content == "CHANGES · 41").Any();
                if (listed) Assert.StartsWith("Commit all 41 changes", Button());
                return listed;
            }, Wait);
            AssertIsAFullBuild(session, app, "a file appeared");

            Invoke(app, "StageAll");
            session.PumpFrames(2);
            Assert.StartsWith("Commit 41 staged files", Button());
            session.PumpUntil(() => Settled(session, app), Wait);
            AssertIsAFullBuild(session, app, "staged");
            Invoke(app, "UnstageAll");
            session.PumpFrames(2);
            Assert.StartsWith("Commit all 41 changes", Button());
            session.PumpUntil(() => Settled(session, app), Wait);

            // The sign box, through its own click.
            session.Window.FindByText("Sign commit (GPG/SSH)")!.Click();
            session.PumpFrames(2);
            Assert.EndsWith("(signed)", Button());
            AssertIsAFullBuild(session, app, "signed");
            session.Window.FindByText("Sign commit (GPG/SSH)")!.Click();
            session.PumpFrames(2);

            // HEAD rewritten behind the app's back: nothing the button says moves, and the amend
            // box must still pour in the new message.
            repo.Git("commit", "--amend", "-q", "-m", "The new subject");
            Invoke(app, "RefreshAll");
            session.PumpUntil(() => Settled(session, app), Wait);
            session.Window.FindByText("Amend last commit")!.Click();
            session.PumpFrames(2);
            Assert.Equal("The new subject", Get<string>(app, "_commitSummary"));
            Assert.Equal("The new subject", Ref<TextInputElement>(app, "_summaryRef").Current!.Text);
            Assert.StartsWith("Amend last commit with 41 changes", Button());
            AssertIsAFullBuild(session, app, "amend poured in");
            session.Window.FindByText("Amend last commit")!.Click();
            session.PumpFrames(2);
            Assert.Equal("", Ref<TextInputElement>(app, "_summaryRef").Current!.Text);

            // Text typed, another view and back: it is still there, in a new composer.
            session.Window.Click(Ref<TextInputElement>(app, "_summaryRef").Current!);
            session.PumpFrames(2);
            session.Window.TypeText("Kept");
            session.PumpFrames(2);
            session.Window.Window.ClearFocus();
            session.PumpFrames(2);
            session.Window.PressKey(KeyCode.Digit2);
            session.PumpFrames(4);
            session.Window.PressKey(KeyCode.Digit4);
            session.PumpUntil(() => Ref<TextInputElement>(app, "_summaryRef").Current is { Text: "Kept" } field && session.Window.Window.IsInTree(field), Wait);
            session.PumpFrames(2);
            AssertIsAFullBuild(session, app, "away and back");

            // The commit clears the fields and the list empties.
            session.Window.Click(Ref<TextInputElement>(app, "_summaryRef").Current!);
            session.PumpFrames(2);
            session.Window.PressKey(KeyCode.Enter, KeyModifiers.Ctrl);
            session.PumpUntil(() => Get<string>(app, "_commitSummary").Length == 0 && Button().StartsWith("Commit 0 staged", StringComparison.Ordinal), Wait);
            Assert.Equal("", Ref<TextInputElement>(app, "_summaryRef").Current!.Text);
            AssertIsAFullBuild(session, app, "committed");

            // A scheme: everything is drawn in new colours.
            var accent = Nocturne.Accent;
            Invoke(app, "UseTheme", "solarized-light");
            session.PumpFrames(2);
            Assert.NotEqual(accent, Nocturne.Accent);
            AssertIsAFullBuild(session, app, "scheme");
            Invoke(app, "UseTheme", "nocturne");
            session.PumpFrames(2);
        });
    }

    // ── The oracle ───────────────────────────────────────────────────────

    private static void AssertIsAFullBuild(HeadlessSession session, JunctionApp app, string when)
    {
        var kept = Sig(Composer(session));
        int composer = app.ComposerBuilds;
        typeof(JunctionApp).GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes)!.Invoke(app, null);
        session.PumpFrames(2);
        Assert.True(app.ComposerBuilds > composer, "the forced rebuild did not build the composer");
        Assert.True(kept == Sig(Composer(session)), $"the kept composer is not what a full build makes ({when}):\n{kept}\n--- full ---\n{Sig(Composer(session))}");
    }

    private static string Sig(Element e)
    {
        var sb = new StringBuilder();
        void Walk(Element el, int depth)
        {
            sb.Append(' ', depth).Append(el.GetType().Name);
            if (el.Key is { } k) sb.Append(" key=").Append(k);
            if (el is TextElement t) sb.Append(" text=").Append(t.Content);
            if (el is TextInputElement i) sb.Append(" input=").Append(i.Text);
            if (el is TextAreaElement a) sb.Append(" area=").Append(a.Text);
            if (el is CheckboxElement c) sb.Append(" checked=").Append(c.IsChecked);
            if (el is BoxElement b) sb.Append(" bg=").Append(b.BackgroundColor).Append(" border=").Append(b.BorderColor);
            if (el.AccessibleName is { } n) sb.Append(" name=").Append(n);
            if (el.Inherited.TextColor is { } fg) sb.Append(" fg=").Append(fg);
            sb.Append('\n');
            foreach (var child in el.Children) Walk(child, depth + 1);
        }
        Walk(e, 0);
        return sb.ToString();
    }

    // ── Harness ──────────────────────────────────────────────────────────

    /// <summary>Forty changed files, more than the column shows, the Changes view on screen.</summary>
    private static void Hosted(Action<TempRepo, HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        for (int i = 0; i < 40; i++) repo.Write($"src/f{i:00}.txt", $"{i}\n");
        using var session = HeadlessSession.Create(1200, 800);
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
            session.Window.PressKey(KeyCode.Digit4);
            var ready = app;
            session.PumpUntil(() => Ref<TextInputElement>(ready, "_summaryRef").Current is not null
                && Get<int>(ready, "_changeSelFileCount") >= 0 && ListElements(ready).Count > 100, Wait);
            session.PumpUntil(() => Settled(session, ready), Wait);
            body(repo, session, app);
        }
        finally { app?.Dispose(); }
    }

    private static int _lastBuilds = -1, _quiet;

    /// <summary>True once the window has gone twenty frames without building.</summary>
    private static bool Settled(HeadlessSession session, JunctionApp app)
    {
        session.PumpFrame();
        Thread.Sleep(5);
        _quiet = app.Builds == _lastBuilds ? _quiet + 1 : 0;
        _lastBuilds = app.Builds;
        if (_quiet < 20) return false;
        _quiet = 0;
        return true;
    }

    private static Element Composer(HeadlessSession session)
    {
        var root = (Element)typeof(HultUI.Window).GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window)!;
        return LayoutProbe.Descendants<ComponentHost>(root, h => h.Component.GetType().Name == "ComposerPart").Single().Children[0];
    }

    private static List<Element> ListElements(JunctionApp app)
    {
        var list = new List<Element>();
        void Walk(Element e) { list.Add(e); foreach (var c in e.Children) Walk(c); }
        if (Ref<ScrollViewElement>(app, "_changeScrollRef").Current is { } scroll) Walk(scroll);
        return list;
    }

    private static Ref<T> Ref<T>(JunctionApp app, string field) where T : class => Get<Ref<T>>(app, field);

    private static T Get<T>(JunctionApp app, string field) =>
        (T)typeof(JunctionApp).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;

    private static void Invoke(JunctionApp app, string name, params object?[] args) =>
        typeof(JunctionApp).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(app, args);
}
