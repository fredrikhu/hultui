using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The Tags view, driven headlessly against a real repository: the tab reaches it, it lists
/// what is under refs/tags, and the search box filters. Assertions go through the
/// accessibility tree, so anything they can find is also reachable by a screen reader.
/// </summary>
public class TagsViewTests
{
    [Fact]
    public void TheTagsTabListsTheRepositorysTags()
    {
        Hosted(repo =>
        {
            repo.Git("tag", "v1.0.0");
            repo.Git("tag", "-a", "v2.0.0", "-m", "the annotated one");
        }, (repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit6);
            Assert.Equal(JunctionView.Tags, app.View);

            PumpUntilVisible(session, "v2.0.0");
            Assert.True(Contains(session, "v1.0.0"), "the lightweight tag is missing from the list");
            // The annotation message is shown under the name, and the commit beside it.
            Assert.True(Contains(session, "the annotated one"), "the tag message is not on screen");
            Assert.True(Contains(session, "initial"), "the commit the tag points at is not on screen");
        });
    }

    [Fact]
    public void TheKindOfEachTagIsSaidOutright()
    {
        Hosted(repo =>
        {
            repo.Git("tag", "plain");
            repo.Git("tag", "-a", "annotated-one", "-m", "m");
        }, (repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit6);
            PumpUntilVisible(session, "annotated-one");

            Assert.True(Contains(session, "lightweight"), "the lightweight tag is not labelled");
            Assert.True(Contains(session, "annotated"), "the annotated tag is not labelled");
        });
    }

    [Fact]
    public void TheSearchBoxFiltersTheList()
    {
        Hosted(repo =>
        {
            repo.Git("tag", "v1.0.0");
            repo.Git("tag", "release-candidate");
        }, (repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit6);
            PumpUntilVisible(session, "release-candidate");

            SetField(app, "_tagQuery", "v1");
            MarkDirty(app);
            session.PumpFrames(3);

            Assert.True(Contains(session, "v1.0.0"));
            Assert.False(Contains(session, "release-candidate"), "the filter let a non-matching tag through");
        });
    }

    /// <summary>The same wildcard search the branch box has, on the semantics
    /// <c>git tag --list &lt;pattern&gt;</c> uses: anchored at both ends, <c>*</c> crossing
    /// the separators, <c>?</c> standing for one character.</summary>
    [Fact]
    public void AWildcardQueryMatchesTagNamesTheWayGitTagListDoes()
    {
        Hosted(repo =>
        {
            repo.Git("tag", "v1.0.0");
            repo.Git("tag", "v2.0.0");
            repo.Git("tag", "release/2024-06");
        }, (repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit6);
            PumpUntilVisible(session, "release/2024-06");

            SetField(app, "_tagQuery", "v1.*");
            MarkDirty(app);
            session.PumpFrames(3);
            Assert.True(Contains(session, "v1.0.0"), "the pattern hid the tag it matches");
            Assert.False(Contains(session, "v2.0.0"), "the pattern kept a tag outside it");
            Assert.False(Contains(session, "release/2024-06"), "the pattern kept a tag outside it");

            // Anchored, so a pattern reaching into a folder has to say so.
            SetField(app, "_tagQuery", "release/*");
            MarkDirty(app);
            session.PumpFrames(3);
            Assert.True(Contains(session, "release/2024-06"), "the pattern did not reach into the folder");
            Assert.False(Contains(session, "v1.0.0"), "'release/*' matched a tag outside that folder");

            // '?' is exactly one character — a whole date is not one.
            SetField(app, "_tagQuery", "release/2024-0?");
            MarkDirty(app);
            session.PumpFrames(3);
            Assert.True(Contains(session, "release/2024-06"));
            SetField(app, "_tagQuery", "release/2024-?");
            MarkDirty(app);
            session.PumpFrames(3);
            Assert.False(Contains(session, "release/2024-06"), "'?' swallowed more than one character");
        });
    }

    [Fact]
    public void ARepositoryWithNoTagsSaysSo()
    {
        Hosted(_ => { }, (repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit6);
            PumpUntilVisible(session, "no tags");
        });
    }

    /// <summary>The tab renumbering that came with this view: Tags took 6 and Files moved
    /// to 7. Both are asserted so a future reshuffle has to be deliberate.</summary>
    [Theory]
    [InlineData(KeyCode.Digit5, JunctionView.Branches)]
    [InlineData(KeyCode.Digit6, JunctionView.Tags)]
    [InlineData(KeyCode.Digit7, JunctionView.Files)]
    public void TheNumberKeysReachTheirViews(KeyCode key, JunctionView expected)
    {
        Hosted(_ => { }, (repo, session, app) =>
        {
            session.Window.PressKey(key);
            session.PumpFrames(10);
            Assert.Equal(expected, app.View);
        });
    }

    [Fact]
    public void TheTagsTabIsReachableByName()
    {
        Hosted(_ => { }, (repo, session, app) =>
        {
            var buttons = Walk(session.Window.AccessibilityTree)
                .Where(n => n.Role == AccessibilityRole.Button)
                .Select(n => n.Name ?? "");
            Assert.Contains(buttons, b => b.StartsWith("Tags", StringComparison.Ordinal));
        });
    }

    /// <summary>Selecting a tag points the rest of the app at the commit it names, the same
    /// way selecting a branch points it at the tip — so Commit and Files follow along.</summary>
    [Fact]
    public void SelectingATagSelectsTheCommitItPointsAt()
    {
        string tagged = "";
        Hosted(repo =>
        {
            tagged = repo.Head;
            repo.Git("tag", "v1.0.0");
            repo.Write("later.txt", "x\n");
            repo.Git("add", "-A");
            repo.Git("commit", "-m", "after the tag");
        }, (repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit6);
            PumpUntilVisible(session, "v1.0.0");

            session.Window.PressKey(KeyCode.Down);   // no-op at the top, but it commits a selection
            session.PumpFrames(2);

            Assert.Equal(tagged, GetField<string?>(app, "_selectedCommitHash"));
        });
    }

    /// <summary>A key in the Tags list rebuilds the window once, and the list scrolls to keep
    /// the selected row on screen. MoveTagSel rebuilt twice — once through SelectTag, then
    /// again for nothing — and the one rebuild left has to be the one the scroll follows.</summary>
    [Fact]
    public void ATagsKeyRebuildsOnce_AndTheListFollowsTheSelection()
    {
        Hosted(repo =>
        {
            for (int i = 0; i < 40; i++) repo.Git("tag", $"v{i:00}");
        }, (repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit6);
            PumpUntilVisible(session, "v39");
            session.PumpFrames(3);

            for (int i = 1; i <= 30; i++)
            {
                int builds = app.Builds;
                session.Window.PressKey(KeyCode.Down);
                session.PumpFrames(2);
                Assert.Equal(1, app.Builds - builds);

                var list = HultUI.Headless.LayoutProbe.Descendants<HultUI.Core.Elements.VirtualListElement<HultUI.Core.Builders.TreeNode<Junction.Git.GitTag>>>(
                    Root(session), l => l.Layout.AbsoluteContentBox.Height > 0).Single();
                Assert.Equal(i, list.SelectedIndex);
                float top = i * list.ItemHeight, bottom = top + list.ItemHeight;
                float viewTop = list.ScrollOffsetY, viewBottom = viewTop + list.Layout.AbsoluteContentBox.Height;
                Assert.True(top >= viewTop - 0.5f && bottom <= viewBottom + 0.5f,
                    $"row {i} ({top}–{bottom}) is off the list's view ({viewTop}–{viewBottom})");
            }
        });
    }

    private static HultUI.Core.Elements.Element Root(HeadlessSession session) =>
        (HultUI.Core.Elements.Element)typeof(HultUI.Window).GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window)!;

    // ── harness ──────────────────────────────────────────────────────────
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

    private static void PumpUntilVisible(HeadlessSession session, string text)
    {
        try
        {
            session.PumpUntil(() => Contains(session, text), TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException e)
        {
            throw new Xunit.Sdk.XunitException(
                $"\"{text}\" never appeared in the accessibility tree.\n{e.Message}\n" +
                session.Window.DumpAccessibilityTree());
        }
    }

    private static bool Contains(HeadlessSession session, string text) =>
        Walk(session.Window.AccessibilityTree).Any(n =>
            n.Name?.Contains(text, StringComparison.OrdinalIgnoreCase) == true
            || n.Value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }

    private static void SetField(JunctionApp app, string name, object? value) =>
        typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, value);

    private static T GetField<T>(JunctionApp app, string name) =>
        (T)typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;

    private static void MarkDirty(JunctionApp app) =>
        typeof(JunctionApp).GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(app, null);
}
