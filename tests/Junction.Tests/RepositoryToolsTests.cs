using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The toolbar's repository-tools menu — the chrome-level home for the four panels
/// (worktrees, submodules, remotes, reflog) that used to be reachable only by opening
/// the command palette and typing their name.
///
/// <para>Two things are worth guarding here. First, that each row opens <i>its own</i>
/// panel: the rows are built from one list into one container, which is the shape where a
/// reconcile by position instead of by key ends up firing the previous row's action —
/// clicking "Remotes…" and getting worktrees. Second, that the menu survives the compact
/// breakpoint down to the 760 dip window floor, since a discovery affordance that
/// disappears on a small window has not solved the discovery problem.</para>
/// </summary>
public class RepositoryToolsTests
{
    private static readonly string[] AllPanelFields =
        ["_worktreesOpen", "_submodulesOpen", "_remotesOpen", "_reflogOpen"];

    // label in the menu · the app field the panel's open state lives in · its heading
    [Theory]
    [InlineData("Worktrees…", "_worktreesOpen", "Worktrees")]
    [InlineData("Submodules…", "_submodulesOpen", "Submodules")]
    [InlineData("Remotes…", "_remotesOpen", "Remotes")]
    [InlineData("Reflog (recover work)…", "_reflogOpen", "Reflog")]
    public void EachPanelOpensFromTheToolbarMenu(string label, string field, string heading)
    {
        Hosted(1440, 900, (repo, session, app) =>
        {
            OpenToolsMenu(session);
            ClickMenuRow(session, label);

            // The panel it names, and no other: the failure this guards against is a row
            // running its neighbour's action, which looks like the app ignoring the click.
            Assert.True(Flag(app, field), $"clicking \"{label}\" did not open its panel");
            foreach (var other in AllPanelFields.Where(f => f != field))
                Assert.False(Flag(app, other), $"clicking \"{label}\" also opened {other}");

            session.PumpFrames(3);
            Assert.True(Contains(session, heading), $"the {heading} panel did not render");
        });
    }

    [Fact]
    public void TheMenuStillWorksAtTheWindowFloor()
    {
        // 760x540 is the hard minimum Program.cs sets, and it is well below the compact
        // breakpoint — the toolbar is icon-only there. The button carries no label to
        // shrink away, so it (and everything behind it) has to survive intact.
        Hosted(760, 540, (repo, session, app) =>
        {
            Assert.True(Walk(session.Window.AccessibilityTree)
                    .Any(n => n.Role == AccessibilityRole.Button && n.Name == "Repository tools"),
                "the repository-tools button is not in the accessibility tree at 760x540:\n"
                + session.Window.DumpAccessibilityTree());

            OpenToolsMenu(session);
            // Every entry is on screen at the floor, not just the first: the menu is
            // right-aligned under a button that sits near the window edge here.
            foreach (var label in new[] { "Worktrees…", "Submodules…", "Remotes…", "Reflog (recover work)…" })
                Assert.True(Contains(session, label), $"\"{label}\" is missing from the menu at 760x540");

            ClickMenuRow(session, "Remotes…");
            Assert.True(Flag(app, "_remotesOpen"), "the remotes panel did not open at 760x540");
        });
    }

    [Fact]
    public void TheToolbarFitsItsRowAtEverySize()
    {
        // The compact breakpoint exists because the labelled toolbar stops fitting; if it
        // is set too low, the widths in between lay out a row that runs past its own right
        // edge and pushes the trailing buttons off the window. That is what had happened
        // when the view tabs went from five to seven, and it is invisible to every test
        // that only asks whether the app still renders.
        //
        // Sizes either side of the breakpoint (1140 dip of content + 266 sidebar = a 1406
        // window), plus the floor.
        Hosted(1440, 900, (repo, session, app) =>
        {
            foreach (var width in new[] { 760, 900, 1100, 1300, 1400, 1410, 1440, 1600 })
            {
                session.Window.SetClientSize(width, 800);
                session.PumpFrames(4);

                var toolbar = Toolbar(session);
                var row = LayoutProbe.Inner(toolbar);
                foreach (var child in toolbar.Children)
                {
                    var box = LayoutProbe.Painted(child);
                    Assert.True(box.Right <= row.Right + 0.5f,
                        $"at {width}x800 the toolbar overflows its row by {box.Right - row.Right:0.0} dip "
                        + "— the compact breakpoint is below the width the labelled toolbar needs");
                }
            }
        });
    }

    [Fact]
    public void TheOlderEntryPointsStillReachThePanels()
    {
        // The menu is an addition, not a replacement: the palette entries and the Branches
        // header's Worktrees button are how people who already found them get there.
        Hosted(1440, 900, (repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.K, KeyModifiers.Ctrl);
            session.PumpFrames(3);
            foreach (var label in new[] { "Worktrees…", "Submodules…", "Remotes…", "Reflog (recover lost commits)…" })
                Assert.True(Contains(session, label), $"the palette no longer offers \"{label}\"");
            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(2);

            session.Window.PressKey(KeyCode.Digit5);
            session.PumpFrames(3);
            // The header button is an icon + label row; the label's parent is the button.
            var worktrees = LayoutProbe.Descendants<TextElement>(Root(session),
                t => t.Content == "Worktrees").FirstOrDefault();
            Assert.True(worktrees?.Parent is not null,
                "the Branches header no longer has a Worktrees button");

            session.Window.Click(worktrees!.Parent!);
            session.PumpFrames(3);
            Assert.True(Flag(app, "_worktreesOpen"),
                "the Branches header's Worktrees button no longer opens the panel");
        });
    }

    [Fact]
    public void TheMenuNamesTheToolsWhenNoRepositoryIsOpen()
    {
        // An empty workspace has nothing for the panels to show. The rows stay, greyed —
        // dropping them would answer "does this app do worktrees?" with silence.
        using var session = HeadlessSession.Create(1440, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
            {
                RepositoryPaths = [],
                ActiveRepoPath = "",
                Workspaces = [new WorkspaceData { Id = "w1", Name = "Empty", RepoPaths = [] }],
                ActiveWorkspaceId = "w1",
            });
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();

            OpenToolsMenu(session);
            Assert.True(Contains(session, "Worktrees…"), "the tools are not listed without a repository");

            ClickMenuRow(session, "Worktrees…");
            Assert.False(Flag(app, "_worktreesOpen"), "an unavailable row still opened its panel");
            // …and the menu is still up, so the click read as inert rather than as a dismissal.
            Assert.True(Contains(session, "Submodules…"), "an unavailable row dismissed the menu");
        }
        finally { app?.Dispose(); }
    }

    // ── driving the menu ─────────────────────────────────────────────────

    private static void OpenToolsMenu(HeadlessSession session)
    {
        var button = LayoutProbe.Descendants<BoxElement>(Root(session),
            e => e.AccessibleName == "Repository tools").FirstOrDefault();
        Assert.True(button is not null, "the repository-tools button is not in the toolbar");
        session.Window.Click(button!);
        session.PumpFrames(2);
    }

    /// <summary>Clicks the menu row with the given label. Rows are keyed by their label,
    /// which is also how the reconciler tells them apart.</summary>
    private static void ClickMenuRow(HeadlessSession session, string label)
    {
        var row = LayoutProbe.Descendants<BoxElement>(Root(session), e => e.Key == label).FirstOrDefault();
        Assert.True(row is not null, $"the menu has no \"{label}\" row");
        session.Window.Click(row!);
        session.PumpFrames(3);
    }

    private static BoxElement Toolbar(HeadlessSession session) =>
        LayoutProbe.Descendants<BoxElement>(Root(session), e => e.Key == "toolbar").First();

    private static bool Flag(JunctionApp app, string field) =>
        (bool)typeof(JunctionApp)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;

    // ── harness (same shape as BranchesViewTests) ────────────────────────

    private static void Hosted(int width, int height, Action<TempRepo, HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        using var session = HeadlessSession.Create(width, height);
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

    private static bool Contains(HeadlessSession session, string text) =>
        Walk(session.Window.AccessibilityTree).Any(n =>
            n.Name?.Contains(text, StringComparison.Ordinal) == true
            || n.Value?.Contains(text, StringComparison.Ordinal) == true);

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }
}
