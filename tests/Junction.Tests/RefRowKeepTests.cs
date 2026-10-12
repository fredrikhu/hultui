using System.Reflection;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Headless;
using Junction.Git;
using Junction.GitLab;
using Junction.Tests.GitLab;
using Junction.Theme;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The Branches and Tags tables keep a row until its item or its caret changes (the item-rows
/// <c>RenderWith</c>), so a key makes the two rows it moved between instead of every row on
/// screen. Their items are the tree's nodes, kept between builds while the refs, the query, the
/// flat/tree choice and the open folders are unchanged; a branch row's item also carries its
/// compare pick and base/head tag. Everything else a row shows is in a shape that makes the rows
/// again. One test per input: each fails with that input left out.
/// </summary>
// Serial: the colour-scheme and display-scale tests change process-wide state (the palette and
// the glyph caches) that other sessions draw with.
[Collection("JunctionDiff")]
public class RefRowKeepTests
{
    // ── Branches: what a key keeps ───────────────────────────────────────

    [Fact]
    public void APlainMove_MakesTheTwoBranchRowsItMovedBetween_AndKeepsTheRest()
    {
        Hosted((session, app) =>
        {
            Keys(session, KeyCode.Down);
            var before = BranchRows(session);
            int made = app.BranchRowsMade;

            Keys(session, KeyCode.Down);

            Assert.Equal(2, app.BranchRowsMade - made);
            var after = BranchRows(session);
            Assert.Equal(before.Count, after.Count);
            Assert.Equal(before.Count - 2, before.Zip(after).Count(p => ReferenceEquals(p.First, p.Second)));
            Assert.Equal(Nocturne.Accent900, after[2].BackgroundColor);   // focused and picked
            Assert.NotEqual(Nocturne.Accent900, after[1].BackgroundColor);
        });
    }

    /// <summary>Ctrl+Space takes the focused branch out of the compare picks: its caret does not
    /// move, so only its item (the pick) says it changed.</summary>
    [Fact]
    public void UnpickingTheFocusedBranch_MakesItsRowAgain()
    {
        Hosted((session, app) =>
        {
            Keys(session, KeyCode.Down);
            Assert.Equal(Nocturne.Accent900, BranchRows(session)[1].BackgroundColor);
            int made = app.BranchRowsMade;

            Keys(session, KeyCode.Space, KeyModifiers.Ctrl);

            Assert.Equal(1, app.BranchRowsMade - made);
            Assert.Equal(Nocturne.Neutral900, BranchRows(session)[1].BackgroundColor);
        });
    }

    /// <summary>A second pick starts a comparison: the first pick, which neither moves nor
    /// changes its pick, is tagged base or head by its item.</summary>
    [Fact]
    public void ASecondPick_TagsTheFirstPickedRowAsBaseOrHead()
    {
        Hosted((session, app) =>
        {
            Keys(session, KeyCode.Down);
            Keys(session, KeyCode.Down, KeyModifiers.Ctrl);
            Assert.False(HasText(BranchRows(session)[1], "base", "head"));

            Keys(session, KeyCode.Space, KeyModifiers.Ctrl);

            var rows = BranchRows(session);
            Assert.True(HasText(rows[1], "base", "head"), "the first pick is not tagged base or head");
            Assert.True(HasText(rows[2], "base", "head"), "the second pick is not tagged base or head");
        });
    }

    // ── Branches: the shape ──────────────────────────────────────────────

    /// <summary>A compact table (a window under 934 px of content) gives Checkout an icon in
    /// place of its word; the rows on screen are made again in it.</summary>
    [Fact]
    public void ACompactTable_MakesTheBranchRowsCompact()
    {
        Hosted((session, app) =>
        {
            Assert.True(LeafBranchRows(session).All(r => HasText(r, "Checkout")));

            session.Window.SetClientSize(1000, 900);
            session.PumpFrames(4);

            Assert.True(LeafBranchRows(session).All(r => !HasText(r, "Checkout")), "a kept row still says Checkout");
        });
    }

    /// <summary>A narrow table (under 724 px) drops LAST COMMIT: from compact to narrow, only
    /// the narrow flag changes.</summary>
    [Fact]
    public void ANarrowTable_DropsLastCommitFromTheKeptBranchRows()
    {
        Hosted((session, app) =>
        {
            session.Window.SetClientSize(1000, 900);
            session.PumpFrames(4);
            Assert.True(BranchRows(session).Any(r => HasText(r, "initial")));

            session.Window.SetClientSize(860, 900);
            session.PumpFrames(4);

            Assert.False(BranchRows(session).Any(r => HasText(r, "initial")), "a kept row still shows LAST COMMIT");
        });
    }

    /// <summary>Including one branch in History excludes every other: their eyes close.</summary>
    [Fact]
    public void IncludingABranch_ClosesTheOtherRowsEyes()
    {
        Hosted((session, app) =>
        {
            Assert.All(BranchRows(session).Where(r => IncludeKey(r) is not null),
                r => Assert.EndsWith("#i", IncludeKey(r)));

            Keys(session, KeyCode.I);   // the focused row, main

            var rows = BranchRows(session);
            Assert.EndsWith("#e", IncludeKey(rows[0]));
            Assert.All(rows.Skip(1).Where(r => IncludeKey(r) is not null), r => Assert.EndsWith("#x", IncludeKey(r)));
        });
    }

    /// <summary>
    /// The IN HISTORY set is changed in place, so the shape compares a copy of it. Including a
    /// branch also reloads the refs, which brings new nodes; in a session that lands on the
    /// key's own frame (the test above cannot tell the two apart), in the app a git call or
    /// several later, with the eyes on screen stale until then. So the shape is asked directly.
    /// </summary>
    [Fact]
    public void TheIncludeSet_IsInTheBranchRowShape()
    {
        Hosted((session, app) =>
        {
            Assert.False(ShapeChanged(app), "nothing changed since the last build");
            var set = (HashSet<string>)Invoke(app, "IncludeSet", (string)Field(app, "_activeRepoPath")!)!;

            set.Add("alpha");

            Assert.True(ShapeChanged(app), "a branch included in place did not change the shape");
            Assert.False(ShapeChanged(app));
        });
    }

    private static bool ShapeChanged(JunctionApp app) => (bool)Invoke(app, "BranchRowShapeChanged")!;

    [Fact]
    public void AThemeSwitch_MakesTheBranchRowsInTheNewColours()
    {
        Hosted((session, app) =>
        {
            try
            {
                var before = BranchRows(session)[0].BackgroundColor;
                Assert.Equal(Nocturne.Neutral900, before);

                Nocturne.Use("solarized-light");
                Rebuild(app);
                session.PumpFrames(3);

                var after = BranchRows(session)[0].BackgroundColor;
                Assert.Equal(Nocturne.Neutral900, after);
                Assert.NotEqual(before, after);
            }
            finally
            {
                Nocturne.Use("nocturne");
            }
        });
    }

    /// <summary>A move to a screen of another scale retires the glyph rasters the rows hold (and
    /// the next change frees them): the rows on screen are made again with new ones.</summary>
    [Fact]
    public void ADisplayScaleChange_MakesTheBranchRowsAgain()
    {
        Hosted((session, app) =>
        {
            try
            {
                int onScreen = BranchRows(session).Count;
                int made = app.BranchRowsMade;

                session.Window.SetScale(1.5f);
                session.PumpFrames(3);

                Assert.True(app.BranchRowsMade - made >= onScreen, $"{app.BranchRowsMade - made} rows made of {onScreen}");
            }
            finally
            {
                session.Window.SetScale(1f);
                session.PumpFrames(2);
            }
        });
    }

    [Fact]
    public void ADisplayScaleChange_MakesTheTagRowsAgain()
    {
        Hosted((session, app) =>
        {
            try
            {
                int onScreen = TagRows(session).Count;
                int made = app.TagRowsMade;

                session.Window.SetScale(1.5f);
                session.PumpFrames(3);

                Assert.True(app.TagRowsMade - made >= onScreen, $"{app.TagRowsMade - made} rows made of {onScreen}");
            }
            finally
            {
                session.Window.SetScale(1f);
                session.PumpFrames(2);
            }
        }, tags: true);
    }

    /// <summary>Pipelines land after the rows are made: the kept rows take the glyph. The
    /// project and the merge requests fail to refresh, so they keep their objects: only the
    /// pipelines changed.</summary>
    [Fact]
    public void PipelinesThatLandLater_ShowOnTheKeptBranchRows()
    {
        var gitlab = new GitLabSwitches();
        HostedOnGitLab(gitlab, (repo, session, app) =>
        {
            Assert.False(BranchRows(session).Any(r => HasId(r, "pipeline:running")));

            gitlab.Project = gitlab.MergeRequests = Answer.Fail;
            gitlab.Pipelines = Answer.Data;
            RefreshHostedAndWait(session, app, repo, () => PipelineCount(app, repo.Path) > 0);

            Assert.True(BranchRows(session).Any(r => HasId(r, "pipeline:running")), "no row shows the pipeline");
        });
    }

    /// <summary>The project's protection rules land after the rows are made: main's kept row
    /// takes the lock.</summary>
    [Fact]
    public void ProtectionThatLandsLater_LocksTheKeptMainRow()
    {
        var gitlab = new GitLabSwitches();
        HostedOnGitLab(gitlab, (repo, session, app) =>
        {
            Assert.False(BranchRows(session).Any(IsLocked));

            gitlab.Pipelines = gitlab.MergeRequests = Answer.Fail;
            gitlab.Project = Answer.Data;
            RefreshHostedAndWait(session, app, repo, () => ProtectionCount(app, repo.Path) > 0);

            Assert.True(IsLocked(BranchRows(session)[0]), "main's row has no lock");
        });
    }

    /// <summary>A merge request that lands after the rows are made: its branch's kept row takes
    /// the chip and no longer offers to create one.</summary>
    [Fact]
    public void AMergeRequestThatLandsLater_ShowsOnItsKeptBranchRow()
    {
        var gitlab = new GitLabSwitches();
        HostedOnGitLab(gitlab, (repo, session, app) =>
        {
            Assert.False(BranchRows(session).Any(r => HasId(r, "mr-chip")));
            int offers = BranchRows(session).Count(r => HasTooltip(r, "Create merge request (m)"));
            Assert.True(offers >= 2, $"{offers} rows offer a merge request");

            gitlab.Project = gitlab.Pipelines = Answer.Fail;
            gitlab.MergeRequests = Answer.Data;
            RefreshHostedAndWait(session, app, repo, () => MergeRequestCount(app, repo.Path) > 0);

            var rows = BranchRows(session);
            Assert.True(rows.Any(r => HasId(r, "mr-chip")), "no row shows the merge request");
            Assert.Equal(offers - 1, rows.Count(r => HasTooltip(r, "Create merge request (m)")));
        });
    }

    // ── Branches: the kept nodes ─────────────────────────────────────────

    [Fact]
    public void ClosingAFolder_TakesItsBranchesOffTheList()
    {
        Hosted((session, app) =>
        {
            var rows = BranchRows(session);
            int folder = rows.FindIndex(r => HasText(r, "fix"));
            Assert.True(folder > 0);
            int count = BranchNodes(app).Count;
            for (int i = 0; i < folder; i++) Keys(session, KeyCode.Down);

            Keys(session, KeyCode.Left);

            Assert.Equal(count - 2, BranchNodes(app).Count);
            Assert.False(BranchRows(session).Any(r => HasText(r, "three")), "the folder's branches are still listed");
        });
    }

    [Fact]
    public void AQuery_FiltersTheKeptBranchRows()
    {
        Hosted((session, app) =>
        {
            Assert.True(BranchRows(session).Any(r => HasText(r, "alpha")));

            session.Window.PressKey(KeyCode.Slash);
            session.PumpFrames(2);
            session.Window.TypeText("gam");
            session.PumpFrames(3);

            var rows = BranchRows(session);
            Assert.False(rows.Any(r => HasText(r, "alpha")), "alpha survived the query");
            Assert.True(rows.Any(r => HasText(r, "gamma")));
        });
    }

    [Fact]
    public void TheFlatList_HasNoFolderRows()
    {
        Hosted((session, app) =>
        {
            Assert.Contains(BranchNodes(app), n => n.IsFolder);

            Keys(session, KeyCode.T);

            Assert.DoesNotContain(BranchNodes(app), n => n.IsFolder);
            Assert.False(BranchRows(session).Any(r => HasText(r, "topic")), "a folder row survived the flat list");
            Keys(session, KeyCode.T);
        });
    }

    /// <summary>A branch made outside the app reloads the refs: a new list, so new nodes.</summary>
    [Fact]
    public void ABranchMadeOutside_AppearsInTheTable()
    {
        TempRepo? fixture = null;
        Hosted((session, app) =>
        {
            Assert.False(BranchRows(session).Any(r => HasText(r, "zeta")));

            fixture!.Git("branch", "zeta");
            session.PumpUntil(() => BranchNodes(app).Any(n => n.Path == "zeta"), TimeSpan.FromSeconds(20));
            session.PumpFrames(3);

            Assert.True(BranchRows(session).Any(r => HasText(r, "zeta")), "the new branch is not on screen");
        }, repo => fixture = repo);
    }

    /// <summary>The items' labels are one delegate across builds, so the list keeps them.</summary>
    [Fact]
    public void TheBranchLabel_IsOneDelegateAcrossBuilds()
    {
        Hosted((session, app) =>
        {
            var label = BranchList(session).AccessibleLabel;
            Keys(session, KeyCode.Down);
            Assert.Same(label, BranchList(session).AccessibleLabel);
        });
    }

    // ── Tags ─────────────────────────────────────────────────────────────

    [Fact]
    public void APlainMove_MakesTheTwoTagRowsItMovedBetween_AndKeepsTheRest()
    {
        Hosted((session, app) =>
        {
            var before = TagRows(session);
            int made = app.TagRowsMade;

            Keys(session, KeyCode.Down);

            Assert.Equal(2, app.TagRowsMade - made);
            var after = TagRows(session);
            Assert.Equal(before.Count, after.Count);
            Assert.Equal(before.Count - 2, before.Zip(after).Count(p => ReferenceEquals(p.First, p.Second)));
            Assert.Equal(Nocturne.Neutral900, after[1].BackgroundColor);
        }, tags: true);
    }

    [Fact]
    public void ACompactTable_MakesTheTagRowsCompact()
    {
        Hosted((session, app) =>
        {
            Assert.True(LeafTagRows(session).All(r => HasText(r, "Checkout")));

            session.Window.SetClientSize(1000, 900);
            session.PumpFrames(4);

            Assert.True(LeafTagRows(session).All(r => !HasText(r, "Checkout")), "a kept row still says Checkout");
        }, tags: true);
    }

    [Fact]
    public void ANarrowTable_DropsCommitFromTheKeptTagRows()
    {
        Hosted((session, app) =>
        {
            session.Window.SetClientSize(1000, 900);
            session.PumpFrames(4);
            Assert.True(LeafTagRows(session).All(r => HasText(r, "initial")));

            session.Window.SetClientSize(860, 900);
            session.PumpFrames(4);

            Assert.False(LeafTagRows(session).Any(r => HasText(r, "initial")), "a kept row still shows COMMIT");
        }, tags: true);
    }

    [Fact]
    public void AThemeSwitch_MakesTheTagRowsInTheNewColours()
    {
        Hosted((session, app) =>
        {
            try
            {
                var before = TagRows(session)[0].BackgroundColor;
                Assert.Equal(Nocturne.Neutral900, before);

                Nocturne.Use("solarized-light");
                Rebuild(app);
                session.PumpFrames(3);

                var after = TagRows(session)[0].BackgroundColor;
                Assert.Equal(Nocturne.Neutral900, after);
                Assert.NotEqual(before, after);
            }
            finally
            {
                Nocturne.Use("nocturne");
            }
        }, tags: true);
    }

    [Fact]
    public void TheTagLabel_IsOneDelegateAcrossBuilds()
    {
        Hosted((session, app) =>
        {
            var label = TagList(session).AccessibleLabel;
            Keys(session, KeyCode.Down);
            Assert.Same(label, TagList(session).AccessibleLabel);
        }, tags: true);
    }

    // ── Hosting ──────────────────────────────────────────────────────────

    /// <summary>main and eight branches (two folders of two) at one commit, five tags (one
    /// folder of two), Branches or Tags open at 1400 × 900.</summary>
    private static void Hosted(Action<HeadlessSession, JunctionApp> body, Action<TempRepo>? seeRepo = null, bool tags = false)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        foreach (var b in new[] { "alpha", "beta", "gamma", "delta", "topic/one", "topic/two", "fix/three", "fix/four" })
            repo.Git("branch", b);
        foreach (var t in new[] { "v1.0", "v1.1", "v1.2", "rel/2.0", "rel/2.1" })
            repo.Git("tag", t);
        seeRepo?.Invoke(repo);

        using var session = HeadlessSession.Create(1400, 900);
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
            session.Window.PressKey(tags ? KeyCode.Digit6 : KeyCode.Digit5);
            session.PumpUntil(() => (tags ? TagRows(session).Count : BranchRows(session).Count) >= 6, TimeSpan.FromSeconds(20));
            session.PumpFrames(3);
            body(session, app);
        }
        finally { app?.Dispose(); }
    }

    private const string Api = "/api/v4";

    private enum Answer { Empty, Fail, Data }

    /// <summary>What the fake GitLab answers: the project with no protection rules, no pipelines
    /// and no merge requests, until a switch is thrown. A failed refresh keeps the object it had
    /// (a kept row sees no change), so a test that fails the others changes one thing.</summary>
    private sealed class GitLabSwitches
    {
        public volatile Answer Project, Pipelines, MergeRequests;
    }

    private static HttpResponseMessage Respond(Answer answer, string empty, Func<string> data) => answer switch
    {
        Answer.Fail => FakeGitLabHandler.Json("{\"message\":\"500 Internal Server Error\"}", 500),
        Answer.Data => FakeGitLabHandler.Json(data()),
        _ => FakeGitLabHandler.Json(empty),
    };

    private static void HostedOnGitLab(GitLabSwitches gitlab, Action<TempRepo, HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Git("remote", "add", "origin", "https://gitlab.com/acme/core-api.git");
        repo.Git("branch", "feature/2.4.0/payments/PC-1042-refund-splitting");
        repo.Git("branch", "alpha");
        string head = repo.Head;
        var handler = new FakeGitLabHandler()
            .Map($"GET {Api}/projects/acme%2Fcore-api",
                _ => Respond(gitlab.Project, FakeGitLabHandler.Recording("project"), () => FakeGitLabHandler.Recording("project")))
            .Map($"GET {Api}/projects/7781/protected_branches?per_page=100&page=1",
                _ => Respond(gitlab.Project, "[]", () => FakeGitLabHandler.Recording("protected_branches")))
            .Map($"GET {Api}/projects/7781/merge_requests?state=opened&order_by=updated_at&sort=desc&with_merge_status_recheck=true&per_page=100&page=1",
                _ => Respond(gitlab.MergeRequests, "[]", () => FakeGitLabHandler.Recording("merge_requests_page1")))
            .Map($"GET {Api}/projects/7781/pipelines?order_by=updated_at&sort=desc&per_page=100&page=1",
                _ => Respond(gitlab.Pipelines, "[]", () =>
                    $"[{{\"id\":4021,\"iid\":118,\"project_id\":7781,\"sha\":\"{head}\",\"ref\":\"main\",\"status\":\"running\",\"web_url\":\"https://gitlab.com/acme/core-api/-/pipelines/4021\",\"created_at\":\"2026-09-12T09:44:00.000Z\"}}]"));

        var store = new InMemoryCredentialStore();
        store.Write(GitLabAccounts.Service("gitlab.com"), GitLabAccounts.TokenAccount, "glpat-test");
        var settings = new JunctionSettingsData
        {
            RepositoryPaths = [repo.Path],
            ActiveRepoPath = repo.Path,
            Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
            ActiveWorkspaceId = "w1",
        };
        settings.GitLabAccounts.Add(new GitLabAccountData
        {
            Host = "gitlab.com", WebBaseUrl = "https://gitlab.com", UserId = 42, Username = "dosei", Name = "Dana Osei",
            Kind = GitLabTokenKind.Pat, Scopes = ["api"], ExpiresAt = DateTimeOffset.UtcNow.AddDays(200),
        });

        using var session = HeadlessSession.Create(1440, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, settings,
                credentialStore: store, gitLabHandler: _ => handler);
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();
            session.PumpUntil(() => Descendant(session, e => e.AutomationId == "gitlab-mark") is not null, TimeSpan.FromSeconds(15));
            session.Window.PressKey(KeyCode.Digit5);
            session.PumpUntil(() => BranchRows(session).Count >= 3 && ProjectLoaded(app, repo.Path), TimeSpan.FromSeconds(15));
            session.PumpFrames(5);
            body(repo, session, app);
        }
        finally { app?.Dispose(); }
    }

    /// <summary>Asks for the hosted data again and pumps until it has landed.</summary>
    private static void RefreshHostedAndWait(HeadlessSession session, JunctionApp app, TempRepo repo, Func<bool> landed)
    {
        app.RefreshHosted(repo.Path);
        session.PumpUntil(landed, TimeSpan.FromSeconds(15));
        session.PumpFrames(3);
    }

    // ── Reaching the app's state ─────────────────────────────────────────

    private static object? Invoke(JunctionApp app, string method, params object[] args) =>
        typeof(JunctionApp).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, args);

    private static object? Field(JunctionApp app, string name) =>
        typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app);

    private static object? HostedValue(JunctionApp app, string loader, string path)
    {
        var hosted = Invoke(app, loader, path)!;
        return hosted.GetType().GetProperty("Value")!.GetValue(hosted);
    }

    private static bool ProjectLoaded(JunctionApp app, string path) => HostedValue(app, "ProjectFor", path) is not null;

    private static int PipelineCount(JunctionApp app, string path) =>
        ((System.Collections.ICollection)HostedValue(app, "PipelinesFor", path)!).Count;

    private static int MergeRequestCount(JunctionApp app, string path) =>
        ((System.Collections.ICollection)HostedValue(app, "MergeRequestsFor", path)!).Count;

    private static int ProtectionCount(JunctionApp app, string path)
    {
        var info = HostedValue(app, "ProjectFor", path);
        return info is null ? 0 : ((System.Collections.ICollection)info.GetType().GetProperty("Protected")!.GetValue(info)!).Count;
    }

    private static List<TreeNode<GitBranch>> BranchNodes(JunctionApp app) =>
        (List<TreeNode<GitBranch>>)Invoke(app, "BranchRows")!;

    private static void Rebuild(JunctionApp app)
    {
        for (var t = typeof(JunctionApp); t != null; t = t.BaseType)
            if (t.GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance) is { } m)
            { m.Invoke(app, null); return; }
        throw new Xunit.Sdk.XunitException("no MarkDirty to call");
    }

    private static void Keys(HeadlessSession session, KeyCode key, KeyModifiers mods = KeyModifiers.None)
    {
        session.Window.PressKey(key, mods);
        session.PumpFrames(3);
    }

    // ── Reading the screen ───────────────────────────────────────────────

    private static VirtualListElement<JunctionApp.BranchRowItem> BranchList(HeadlessSession session) =>
        LayoutProbe.Descendants<VirtualListElement<JunctionApp.BranchRowItem>>(Root(session),
            l => l.Layout.AbsoluteContentBox.Height > 0).Single();

    private static VirtualListElement<TreeNode<GitTag>> TagList(HeadlessSession session) =>
        LayoutProbe.Descendants<VirtualListElement<TreeNode<GitTag>>>(Root(session),
            l => l.Layout.AbsoluteContentBox.Height > 0).Single();

    /// <summary>The realized rows, top to bottom (the list's first child is its spacer).</summary>
    private static List<BoxElement> BranchRows(HeadlessSession session) =>
        LayoutProbe.Descendants<VirtualListElement<JunctionApp.BranchRowItem>>(Root(session),
            l => l.Layout.AbsoluteContentBox.Height > 0).SingleOrDefault() is { } list
            ? list.Children.Skip(1).Cast<BoxElement>().ToList()
            : [];

    private static List<BoxElement> TagRows(HeadlessSession session) =>
        LayoutProbe.Descendants<VirtualListElement<TreeNode<GitTag>>>(Root(session),
            l => l.Layout.AbsoluteContentBox.Height > 0).SingleOrDefault() is { } list
            ? list.Children.Skip(1).Cast<BoxElement>().ToList()
            : [];

    /// <summary>The branch rows that are branches, not folders: the ones with an IN HISTORY eye.</summary>
    private static List<BoxElement> LeafBranchRows(HeadlessSession session) =>
        BranchRows(session).Where(r => IncludeKey(r) is not null).ToList();

    /// <summary>The tag rows that are tags, not folders: the ones that say what kind they are.</summary>
    private static List<BoxElement> LeafTagRows(HeadlessSession session) =>
        TagRows(session).Where(r => HasText(r, "lightweight", "annotated")).ToList();

    private static bool HasText(Element row, params string[] texts) =>
        LayoutProbe.Descendants<TextElement>(row, t => texts.Contains(t.Content)).Count > 0;

    private static bool HasId(Element row, string id) =>
        LayoutProbe.Descendants<Element>(row, e => e.AutomationId == id).Count > 0;

    private static bool HasTooltip(Element row, string tooltip) =>
        LayoutProbe.Descendants<Element>(row, e => e.TooltipText == tooltip).Count > 0;

    private static bool IsLocked(Element row) =>
        LayoutProbe.Descendants<Element>(row, e => e.TooltipText?.Contains(" is protected — ", StringComparison.Ordinal) == true).Count > 0;

    private static string? IncludeKey(Element row) =>
        LayoutProbe.Descendants<Element>(row, e => e.Key?.StartsWith("inc:", StringComparison.Ordinal) == true)
            .FirstOrDefault()?.Key;

    private static Element? Descendant(HeadlessSession session, Func<Element, bool> match) =>
        LayoutProbe.Descendants<Element>(Root(session), e => match(e)).FirstOrDefault();

    private static Element Root(HeadlessSession session) =>
        (Element)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window)!;
}
