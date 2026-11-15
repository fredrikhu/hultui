using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Under 1,406 logical px (1,140 of content beside the sidebar) Junction's chrome goes compact: the view tabs, the
/// search box, Fetch, Pull and Push, the diff's Unified, Split and Wrap, the ref views' Tree and List and History's
/// "Only main" drop their texts and keep their icons. A button is named from its texts and a tooltip is no name, so
/// a reader heard each of them there as a button named '' (Changes, Pull and Push by their count alone), and each
/// crossing of the breakpoint renamed them: a Name raise each, eleven in the Commit view. The icon-only builds now
/// name themselves as the labelled builds read, so the names are the same on both sides and a crossing renames none.
/// A node's <see cref="AccessibilityNode.Name"/> is what UIA's Name and AT-SPI's accessible-name publish, and a Name
/// in the diff is what each bridge raises.
/// </summary>
public class CompactChromeNameTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    // 1,440 − 266 of sidebar is 1,174 of content, over the 1,140 the compact chrome starts under; 1,400 − 266 is 1,134.
    private const int Wide = 1440, Compact = 1400;

    private static readonly string[] Tabs = ["Overview", "History", "Commit", "Changes", "Branches", "Tags", "Files"];

    /// <summary>What the labelled chrome reads, as its texts name it: one file changed, one commit behind its
    /// upstream and one ahead (the fixture), and the buttons the view adds.</summary>
    private static string[] Expected(JunctionView view, string pull = "Pull 1") =>
    [
        "Search & commands " + Shortcuts.Map["palette"].Keys[0],
        "Overview 1", "History 2", "Commit 3", "Changes 4 1", "Branches 5", "Tags 6", "Files 7",
        "Fetch", pull, "Push 1",
        .. view switch
        {
            JunctionView.History => new[] { "Only main" },
            JunctionView.Commit => new[] { "Unified", "Split", "Wrap" },
            JunctionView.Branches => new[] { "Tree", "List" },
            _ => Array.Empty<string>(),
        },
    ];

    [Theory]
    [InlineData(JunctionView.History)]
    [InlineData(JunctionView.Commit)]
    [InlineData(JunctionView.Branches)]
    public void BelowTheBreakpoint_TheIconOnlyButtonsKeepTheirNames_AndACrossingRenamesNone(JunctionView view)
    {
        Hosted(view, (session, app, bridge) =>
        {
            var expected = Expected(view);
            Assert.Equal(expected, Names(session, bridge));

            Cross(session, app, bridge, Compact, expected);
            // It is the compact chrome: the labels are gone from the screen.
            Assert.DoesNotContain(Descendants(Root(session)), e => e is TextElement { Content: "Fetch" or "Overview" });

            Cross(session, app, bridge, Wide, expected);
            Assert.Contains(Descendants(Root(session)), e => e is TextElement { Content: "Fetch" });
        });
    }

    /// <summary>While Pull runs its button reads "Pulling…" without its count, and Fetch and Push, refused
    /// meanwhile, keep theirs: on both sides of the breakpoint.</summary>
    [Fact]
    public void WhileACommandRuns_ItsButtonsKeepTheirNamesAcrossTheBreakpoint()
    {
        Hosted(JunctionView.History, (session, app, bridge) =>
        {
            var remoteOp = typeof(JunctionApp).GetNestedType("RemoteOp", BindingFlags.NonPublic)!;
            typeof(JunctionApp).GetMethod("BeginRemoteOp", Any)!.Invoke(app, [Enum.Parse(remoteOp, "Pull")]);
            try
            {
                session.PumpFrames(3);
                var expected = Expected(JunctionView.History, pull: "Pulling…");
                Assert.Equal(expected, Names(session, bridge));
                Cross(session, app, bridge, Compact, expected);
                Cross(session, app, bridge, Wide, expected);
            }
            finally { typeof(JunctionApp).GetMethod("EndRemoteOp", Any)!.Invoke(app, null); }
        });
    }

    /// <summary>Resizes across the breakpoint and checks the names it leaves and that no node was renamed.</summary>
    private static void Cross(HeadlessSession session, JunctionApp app, HeadlessAccessibilityBridge bridge, int width,
        string[] expected)
    {
        var before = Walk(bridge.CurrentTree!).ToDictionary(n => n.ElementId, n => n.Name);
        bridge.ClearDiffs();
        session.Window.SetClientSize(width, 900);
        session.PumpFrames(4);
        Settle(session, app);
        var renames = bridge.Diffs.SelectMany(d => d.PropertyChanged)
            .Where(c => c.PropertyName == AccessibilityProperties.Name)
            .Select(c => $"'{before.GetValueOrDefault(c.Node.ElementId)}' → '{c.Node.Name}'")
            .ToList();
        Assert.True(bridge.Diffs.Count > 0, $"the resize to {width} told the reader nothing");
        Assert.True(renames.Count == 0, $"the resize to {width} renamed {renames.Count}: {string.Join(", ", renames)}");
        Assert.Equal(expected, Names(session, bridge));
    }

    /// <summary>The names the reader has for the chrome's buttons and the view's, each found by its element.</summary>
    private static List<string?> Names(HeadlessSession session, HeadlessAccessibilityBridge bridge)
    {
        var all = Descendants(Root(session)).ToList();
        var nodes = Walk(bridge.CurrentTree!).ToDictionary(n => n.ElementId);
        var found = new List<Element?> { all.FirstOrDefault(e => e.TooltipText?.StartsWith("Search & commands (") == true) };
        found.AddRange(Tabs.Select(t => all.FirstOrDefault(e => e.TooltipText?.StartsWith(t + " (") == true)));
        found.AddRange(new[] { "fetch", "pull#busy", "pull", "push" }
            .Select(k => all.FirstOrDefault(e => e.Key == k)).Where(e => e is not null));
        found.AddRange(new[] { "Unified (", "Split (", "Wrap (" }
            .Select(m => all.FirstOrDefault(e => e.TooltipText?.StartsWith(m) == true)).Where(e => e is not null));
        found.AddRange(all.Where(e => e.Key?.StartsWith("refview:") == true || e.Key?.StartsWith("onlycurrent:") == true));
        return found.Select(e => e is null ? "(no element)" : nodes.TryGetValue(e.Id, out var n) ? n.Name : "(no node)").ToList();
    }

    // ── Harness ──────────────────────────────────────────────────────────

    /// <summary>A repository one commit ahead of its upstream and one behind, with a file changed, open at
    /// <paramref name="view"/> in a wide window with a reader listening.</summary>
    private static void Hosted(JunctionView view, Action<HeadlessSession, JunctionApp, HeadlessAccessibilityBridge> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("theirs.txt", "theirs\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-q", "-m", "theirs");
        repo.Git("remote", "add", "origin", "../nowhere.git");
        repo.Git("update-ref", "refs/remotes/origin/main", "HEAD");
        repo.Git("reset", "-q", "--hard", "HEAD~1");
        repo.Git("branch", "-q", "--set-upstream-to=origin/main");
        repo.Write("ours.txt", "ours\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-q", "-m", "ours");
        repo.Write("changed.txt", "changed\n");

        using var session = HeadlessSession.Create(Wide, 900);
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
            var bridge = session.Window.InstallAccessibilityBridge();
            session.Application.SetRoot(app.Mount());
            session.PumpUntil(() => bridge.CurrentTree is { } tree && Walk(tree).Any(n => n.Name == "Push 1"), Wait);
            Settle(session, app);
            session.Window.Window.ClearFocus();
            session.Window.PressKey(view switch
            {
                JunctionView.History => KeyCode.Digit2,
                JunctionView.Commit => KeyCode.Digit3,
                JunctionView.Branches => KeyCode.Digit5,
                _ => throw new ArgumentOutOfRangeException(nameof(view)),
            });
            session.PumpFrames(3);
            Settle(session, app);
            Assert.Equal(view, app.View);
            body(session, app, bridge);
        }
        finally { app?.Dispose(); }
    }

    /// <summary>Pumps until the window has built nothing for a while, so the loads have landed.</summary>
    private static void Settle(HeadlessSession session, JunctionApp app)
    {
        int quiet = 0, last = app.Builds + app.ChromeBuilds;
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (quiet < 30 && DateTime.UtcNow < until)
        {
            session.PumpFrame();
            Thread.Sleep(5);
            int now = app.Builds + app.ChromeBuilds;
            quiet = now == last ? quiet + 1 : 0;
            last = now;
        }
    }

    private static Element Root(HeadlessSession session) =>
        (Element)typeof(HultUI.Window).GetField("_root", Any)!.GetValue(session.Window.Window)!;

    private static IEnumerable<Element> Descendants(Element e)
    {
        foreach (var c in e.Children)
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var n in Walk(child)) yield return n;
    }
}
