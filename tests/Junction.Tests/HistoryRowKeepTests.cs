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
// Serial: the colour-scheme and display-scale tests change process-wide state (the palette and
// the glyph caches) that other sessions draw with.
[Collection("JunctionDiff")]
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

    /// <summary>A move to a screen of another scale retires the glyph rasters the chips hold
    /// (and the next change frees them): the rows on screen are made again with new ones.</summary>
    [Fact]
    public void ADisplayScaleChange_MakesTheRowsAgain()
    {
        Hosted((session, app) =>
        {
            try
            {
                int onScreen = Rows(session).Count;
                int made = app.HistoryRowsMade;

                session.Window.SetScale(1.5f);
                session.PumpFrames(3);

                Assert.True(app.HistoryRowsMade - made >= onScreen, $"{app.HistoryRowsMade - made} rows made of {onScreen}");
            }
            finally
            {
                session.Window.SetScale(1f);
                session.PumpFrames(2);
            }
        });
    }

    /// <summary>The include bar and the footer show nothing a move changes: a key hands both back
    /// to the build as they were — the same elements, not built. What they do show reaches them:
    /// a widened selection makes the footer offer the compare, a character in the search box
    /// builds the bar again (and the box keeps its focus and its text). Each time, the two are
    /// what a fresh build of them makes.</summary>
    [Fact]
    public void TheIncludeBarAndTheFooter_AreKeptAcrossKeys_AndFollowWhatTheyShow()
    {
        Hosted((session, app) =>
        {
            var bar = Part(session, "include-bar");
            var footer = Part(session, "history-footer");
            int made = app.HistoryPartsMade;
            for (int i = 0; i < 4; i++)
            {
                session.Window.PressKey(KeyCode.Down);
                session.PumpFrames(2);
            }
            Assert.Equal(made, app.HistoryPartsMade);
            Assert.Same(bar, Part(session, "include-bar"));
            Assert.Same(footer, Part(session, "history-footer"));
            AssertPartsAreAFreshBuild(session, app);

            // Shift+Down selects two: the footer is built again, and offers to compare them.
            made = app.HistoryPartsMade;
            session.Window.PressKey(KeyCode.Down, KeyModifiers.Shift);
            session.PumpFrames(2);
            Assert.Equal(made + 1, app.HistoryPartsMade);
            Assert.Contains(LayoutProbe.Descendants<TextElement>(Part(session, "history-footer"), _ => true),
                t => t.Content == "Compare 2 commits");
            AssertPartsAreAFreshBuild(session, app);

            // The search box: each character builds the bar again, into the same box.
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(2);
            session.Window.PressKey(KeyCode.Slash);
            session.PumpFrames(2);
            var box = Assert.IsType<TextInputElement>(session.Window.Window.FocusedElement);
            foreach (var ch in "Commit 1")
            {
                made = app.HistoryPartsMade;
                session.Window.TypeText(ch.ToString());
                session.PumpFrames(2);
                Assert.True(app.HistoryPartsMade > made, $"'{ch}' did not build the include bar");
            }
            Assert.Same(box, session.Window.Window.FocusedElement);
            Assert.Equal("Commit 1", box.Text);
            AssertPartsAreAFreshBuild(session, app);
        });
    }

    private static Element Part(HeadlessSession session, string key) =>
        LayoutProbe.Descendants<Element>(Root(session), e => e.Key == key).Single();

    /// <summary>The kept include bar and footer show what a build of them makes: the parts are
    /// told they were not built last time, the window rebuilt, and the two compared.</summary>
    private static void AssertPartsAreAFreshBuild(HeadlessSession session, JunctionApp app)
    {
        var kept = Sig(Part(session, "include-bar")) + Sig(Part(session, "history-footer"));
        int made = app.HistoryPartsMade;
        foreach (var name in new[] { "_includeBarPart", "_footerPart" })
        {
            var part = typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
            part.GetType().GetField("Build")!.SetValue(part, -1);
        }
        Rebuild(app);
        session.PumpFrames(2);
        Assert.Equal(made + 2, app.HistoryPartsMade);
        Assert.Equal(kept, Sig(Part(session, "include-bar")) + Sig(Part(session, "history-footer")));
    }

    /// <summary>What a subtree shows: element kinds, keys, text, colours, tooltips, names and
    /// enabled state, in tree order.</summary>
    private static string Sig(Element e)
    {
        var sb = new System.Text.StringBuilder();
        void Walk(Element el, int depth)
        {
            sb.Append(' ', depth).Append(el.GetType().Name);
            if (el.Key is { } k) sb.Append(" key=").Append(k);
            if (el is TextElement t) sb.Append(" text=").Append(t.Content);
            if (el is TextInputElement input) sb.Append(" input=").Append(input.Text);
            if (el is BoxElement b) sb.Append(" bg=").Append(b.BackgroundColor).Append(" border=").Append(b.BorderColor);
            if (el.TooltipText is { } tip) sb.Append(" tip=").Append(tip);
            if (el.AccessibleName is { } n) sb.Append(" name=").Append(n);
            if (el.Inherited.TextColor is { } c) sb.Append(" fg=").Append(c);
            if (el is ButtonElement btn) sb.Append(" enabled=").Append(btn.IsEnabled);
            sb.Append('\n');
            foreach (var child in el.Children) Walk(child, depth + 1);
        }
        Walk(e, 0);
        return sb.ToString();
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
