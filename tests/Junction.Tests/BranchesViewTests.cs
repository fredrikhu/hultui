using System.Reflection;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Headless;
using Junction.Git;
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
        // Flat, because that is where the chip lives: a row inside the "origin" folder
        // drops it, the folder above having already said which remote these refs are on.
        Hosted(tree: false, repo =>
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
            // The tree groups both refs under a folder row that is also labelled
            // "origin" — the chip labels are the small ones, so the font size tells the
            // two apart without depending on the tree being on or off.
            List<TextElement> chips = [];
            try
            {
                session.PumpUntil(() =>
                {
                    chips = LayoutProbe.Descendants<TextElement>(Root(session),
                        t => t.Content == "origin" && t.Layout.AbsoluteBorderBox.Height > 0
                             && t.Inherited.FontSize is < 11f);
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

    [Fact]
    public void TheBranchListsScrollbarSitsAgainstTheRightEdgeOfThePanel()
    {
        // A scroll view draws its scrollbar inside its *padding* box, so the list's own
        // side padding used to leave the bar floating 26px in from the panel edge with a
        // strip of nothing beside it. The rows carry that gutter themselves now.
        Hosted(repo =>
        {
            for (var i = 0; i < 40; i++) repo.Git("branch", $"feature/b{i:00}");
        }, (repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit5);
            var list = PumpForList(session);

            var content = list.Layout.AbsoluteContentBox;
            var border = list.Layout.AbsoluteBorderBox;
            Assert.True(Math.Abs(border.Right - content.Right) < 0.5f,
                $"the scrollbar track ends {border.Right - content.Right:0.0}px short of the list's " +
                "right edge — the list has side padding again");
        });
    }

    [Fact]
    public void TheRowsColumnsLineUpWithTheHeadersNamingThem()
    {
        // Same claim from the table's side: the header row spans the panel, so a list that
        // insets its rows lays their columns out in a narrower box and every one of them
        // drifts left of its heading. The ahead/behind cell is fixed-width and centred at
        // both ends, so its "↑n / ↓n" separator and the centred heading share one axis.
        Hosted(repo => repo.Git("branch", "feature/login"), (repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit5);
            PumpForList(session);

            var heading = LayoutProbe.Descendants<TextElement>(Root(session),
                t => t.Content == "AHEAD / BEHIND" && t.Layout.AbsoluteBorderBox.Height > 0).Single();
            var separator = LayoutProbe.Descendants<TextElement>(Root(session),
                t => t.Content == "/" && t.Layout.AbsoluteBorderBox.Height > 0).First();

            var drift = Center(separator) - Center(heading);
            Assert.True(Math.Abs(drift) < 6f,
                $"the ahead/behind numbers sit {drift:0}px off the heading above them — the rows and " +
                "the table header are laid out in different boxes");
        });

        static float Center(Element e) => e.Layout.AbsoluteBorderBox.X + e.Layout.AbsoluteBorderBox.Width / 2f;
    }

    [Fact]
    public void AWildcardQueryMatchesRefNamesTheWayGitBranchListDoes()
    {
        Hosted(repo =>
        {
            repo.Git("branch", "feature/login");
            repo.Git("branch", "hotfix/login-crash");
        }, (repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit5);
            PumpForList(session);

            SetField(app, "_branchQuery", "feature/*");
            MarkDirty(app);
            session.PumpFrames(3);

            Assert.True(Shows(session, "login"), "the pattern hid the branch it matches");
            Assert.False(Shows(session, "login-crash"),
                "the pattern kept a branch outside it — the query is still a substring search");

            // Anchored at both ends, so the two ends of the pattern mean something.
            SetField(app, "_branchQuery", "*crash");
            MarkDirty(app);
            session.PumpFrames(3);
            Assert.True(Shows(session, "login-crash"), "a leading star did not match");
            Assert.False(Shows(session, "login"), "'*crash' matched a branch that does not end in it");
        });
    }

    [Theory]
    [InlineData(1440, true)]
    [InlineData(1440, false)]
    [InlineData(1200, true)]
    [InlineData(1200, false)]
    [InlineData(1100, true)]
    [InlineData(1100, false)]
    [InlineData(1000, true)]
    [InlineData(1000, false)]
    public void TheNameCellKeepsItsNameAndTagsInsideItsColumn(int width, bool tree)
    {
        // At 1000 px the BRANCH column is narrower than a tree row's indent, glyph and
        // "current" tag. The tag never shrank (FlexShrink(0)), so it stuck out over LAST
        // COMMIT by its own width; a deep ref's indent and glyph did the same. Now the cell
        // clips, the name truncates first, and a tag that does not fit whole is left out:
        // whole or not at all — never truncated to an empty outline or cut at the edge.
        Hosted(tree, repo =>
        {
            repo.Git("checkout", "-b", "claude/happy-wiles-1e3e0e");
            // Folders with two children at every level, so the tree cannot fold any of them
            // into its parent: the sync leaves sit three indents deep, and at 1000 px their
            // glyph alone starts past the column's edge.
            repo.Git("branch", "team/platform/sync/q4-hardening-of-the-sync-path");
            repo.Git("branch", "team/platform/sync/q4-retry-budget");
            repo.Git("branch", "team/platform/retry/backoff");
            repo.Git("branch", "team/web/login-copy");
        }, (repo, session, app) =>
        {
            session.Window.SetClientSize(width, 760);
            session.PumpFrames(2);
            session.Window.PressKey(KeyCode.Digit5);
            PumpForList(session);
            session.PumpFrames(2);

            var subjects = LayoutProbe.Descendants<TextElement>(Root(session),
                t => t.Content == "initial" && t.Layout.AbsoluteBorderBox.Height > 0);
            Assert.True(subjects.Count >= 6, $"expected the six branch rows on screen, found {subjects.Count}");
            foreach (var subject in subjects)
            {
                var lastCommit = subject.Parent!;
                var row = lastCommit.Parent!;
                float edge = lastCommit.WindowBorderBox.X;
                foreach (var e in LayoutProbe.Descendants<Element>(row.Children[0]))
                {
                    var v = Visible(e);
                    Assert.True(v.Width <= 0.5f || v.Height <= 0.5f || v.X + v.Width <= edge + 0.5f,
                        $"{Describe(e)} draws to x={v.X + v.Width:0.#}, past the BRANCH column's edge at x={edge:0.#}");
                }
            }

            var tagLabel = LayoutProbe.Descendants<TextElement>(Root(session),
                t => t.Content == "current" && t.Inherited.FontSize is < 11f).Single();
            var tag = tagLabel.Parent!;
            var shown = Visible(tag);
            bool hidden = shown.Width <= 0.5f || shown.Height <= 0.5f;
            float natural = TextMeasure.MeasureWidth("current", 9f);
            if (!hidden)
            {
                var whole = tag.WindowBorderBox;
                Assert.True(Math.Abs(shown.Width - whole.Width) < 0.5f && Math.Abs(shown.Height - whole.Height) < 0.5f,
                    $"the current tag is cut: {shown.Width:0.#}x{shown.Height:0.#} of {whole.Width:0.#}x{whole.Height:0.#} shows");
                Assert.True(tagLabel.Layout.Width >= natural - 0.5f,
                    $"the current tag's label is truncated: {tagLabel.Layout.Width:0.#} of {natural:0.#}px");
            }
            // And it goes only when it has to: where the name's row has room for the whole tag
            // (its label, padding 8 + 8, border 1 + 1, the 6px gap), the name truncates instead.
            float room = tag.Parent!.Layout.Width;
            if (room >= natural + 18f + 6f + 1f)
                Assert.False(hidden, $"the current tag is hidden in a {room:0.#}px row that has room for it");
            if (width >= 1440)
            {
                Assert.False(hidden, "the current tag is hidden at 1440 px");
                // Where the name fits whole, the tag sits right after its text, not at the
                // column's end. (The text, not the label's box: a box grown to fill the row
                // would put the tag at the end and still touch it.)
                var name = LayoutProbe.Descendants<TextElement>(tag.Parent!, t => t.Content == "happy-wiles-1e3e0e").Single();
                float text = TextMeasure.MeasureWidth(name.Content, 12.5f, Fonts.Mono);
                float gap = tag.WindowBorderBox.X - (name.WindowBorderBox.X + text);
                Assert.True(gap < 8f, $"the current tag sits {gap:0.#}px after the end of the name");
            }
        });
    }

    /// <summary>Where an element can draw: its window box, cut by every ancestor that clips.</summary>
    private static Rect Visible(Element e)
    {
        Rect r = e.WindowBorderBox;
        for (var a = e.Parent; a is not null; a = a.Parent)
        {
            Rect c = a.WindowBorderBox;
            float x0 = r.X, x1 = r.X + r.Width, y0 = r.Y, y1 = r.Y + r.Height;
            if (a.Box.OverflowX != Overflow.Visible) { x0 = Math.Max(x0, c.X); x1 = Math.Min(x1, c.X + c.Width); }
            if (a.Box.OverflowY != Overflow.Visible) { y0 = Math.Max(y0, c.Y); y1 = Math.Min(y1, c.Y + c.Height); }
            r = new Rect(x0, y0, Math.Max(0f, x1 - x0), Math.Max(0f, y1 - y0));
        }
        return r;
    }

    private static string Describe(Element e) => e is TextElement t ? $"\"{t.Content}\"" : e.GetType().Name;

    /// <summary>Waits for the virtualized table to realize its rows and hands back the list.</summary>
    private static Element PumpForList(HeadlessSession session)
    {
        List<Element> found = [];
        try
        {
            session.PumpUntil(() =>
            {
                found = LayoutProbe.Descendants<VirtualListElement<TreeNode<GitBranch>>>(Root(session),
                        l => l.Layout.AbsoluteContentBox.Height > 0)
                    .Cast<Element>().ToList();
                return found.Count == 1 && found[0].Children.Count > 1;
            }, TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException e)
        {
            throw new Xunit.Sdk.XunitException($"the branch table never realized its rows.\n{e.Message}");
        }
        return found[0];
    }

    /// <summary>Whether a realized row is labelled exactly this. The table is virtualized,
    /// so it is a claim about what is on screen, not about the underlying list — and it is
    /// exact because "login" is a substring of "login-crash", which is the very distinction
    /// an anchored pattern is being asked to make.</summary>
    private static bool Shows(HeadlessSession session, string text) =>
        LayoutProbe.Descendants<TextElement>(Root(session),
            t => t.Content == text && t.Layout.AbsoluteBorderBox.Height > 0).Count > 0;

    private static void SetField(JunctionApp app, string name, object? value) =>
        typeof(JunctionApp).GetField(name,
            BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, value);

    private static void MarkDirty(JunctionApp app) =>
        typeof(JunctionApp).GetMethod("MarkDirty",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.Invoke(app, null);

    // ── harness (same shape as TagsViewTests) ────────────────────────────
    private static void Hosted(Action<TempRepo> arrange, Action<TempRepo, HeadlessSession, JunctionApp> body) =>
        Hosted(tree: true, arrange, body);

    private static void Hosted(bool tree, Action<TempRepo> arrange,
        Action<TempRepo, HeadlessSession, JunctionApp> body)
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
                RefTreeView = tree,
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
