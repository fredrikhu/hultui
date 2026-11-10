using System.Reflection;
using System.Text;
using HultUI.Core.Animation;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Headless;
using Junction.GitLab;
using Junction.Tests.GitLab;
using Junction.Theme;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The title bar, the sidebar and the toolbar are chrome parts — child components the app
/// keeps while it rebuilds around them (JunctionApp.Chrome.cs). The risk is a value they show
/// changing while they are kept: a stale branch name, an old dirty count, a tab still lit.
///
/// <para>The oracle is a full build. Every check here compares the chrome on screen with the
/// chrome a forced rebuild of all three parts makes, after the selection moves that keep it
/// (which must not build it) and after each value it shows changes the way the app changes
/// it.</para>
/// </summary>
public class ChromePartTests
{
    // ── The view-only paths ──────────────────────────────────────────────

    /// <summary>Every path that rebuilds the view and keeps the chrome: the selection moves and
    /// cursors of History, Branches, Tags, Files and the Commit file list, the search boxes and
    /// the find bar. Each rebuilds the window, builds no chrome, and leaves the chrome exactly as
    /// a full build would make it. A character in the composer's fields builds no window at
    /// all.</summary>
    [Fact]
    public void ViewOnlyMoves_RebuildTheView_AndKeepTheChromeAFullBuildWouldMake()
    {
        Hosted(fx =>
        {
            var s = fx.Session;
            void ViewOnly(string name, Action act)
            {
                AssertChromeIsAFullBuild(fx, "before " + name);
                int builds = fx.App.Builds, chrome = fx.App.ChromeBuilds;
                act();
                s.PumpFrames(3);
                Assert.True(fx.App.Builds > builds, $"{name} did not rebuild the window");
                Assert.True(fx.App.ChromeBuilds == chrome, $"{name} built the chrome ({fx.App.ChromeBuilds - chrome} part builds)");
                AssertChromeIsAFullBuild(fx, "after " + name);
            }
            void Key(KeyCode k, KeyModifiers m = KeyModifiers.None) => s.Window.PressKey(k, m);

            // History.
            ViewOnly("History Down", () => Key(KeyCode.Down));
            ViewOnly("History Up", () => Key(KeyCode.Up));
            ViewOnly("History Shift+Down", () => Key(KeyCode.Down, KeyModifiers.Shift));
            ViewOnly("History Ctrl+Down", () => Key(KeyCode.Down, KeyModifiers.Ctrl));
            ViewOnly("History End", () => Key(KeyCode.End));
            ViewOnly("History Home", () => Key(KeyCode.Home));
            Focus(fx, "history-search");
            ViewOnly("History search typing", () => s.Window.TypeText("ch"));
            Unfocus(fx);

            // Branches.
            Switch(fx, KeyCode.Digit5);
            ViewOnly("Branches Down", () => Key(KeyCode.Down));
            ViewOnly("Branches Up", () => Key(KeyCode.Up));
            ViewOnly("Branches End", () => Key(KeyCode.End));

            // Tags.
            Switch(fx, KeyCode.Digit6);
            ViewOnly("Tags Down", () => Key(KeyCode.Down));
            ViewOnly("Tags Down again", () => Key(KeyCode.Down));
            ViewOnly("Tags Up", () => Key(KeyCode.Up));

            // Files: the cursor opens each file it lands on.
            Switch(fx, KeyCode.Digit7);
            ViewOnly("Files Down", () => Key(KeyCode.Down));
            ViewOnly("Files Down again", () => Key(KeyCode.Down));
            ViewOnly("Files End", () => Key(KeyCode.End));

            // Commit: the file list's cursor, the next commit, and the find bar.
            Switch(fx, KeyCode.Digit2);
            Key(KeyCode.Home); s.PumpFrames(3);
            Switch(fx, KeyCode.Digit3);
            ViewOnly("Commit file Down", () => Key(KeyCode.Down));
            ViewOnly("Commit file Down again", () => Key(KeyCode.Down));
            ViewOnly("Commit next commit", () => Key(KeyCode.Down, KeyModifiers.Ctrl));
            ViewOnly("Commit previous commit", () => Key(KeyCode.Up, KeyModifiers.Ctrl));
            ViewOnly("Commit find bar opens", () => Key(KeyCode.F, KeyModifiers.Ctrl));
            ViewOnly("Commit find typing", () => s.Window.TypeText("change"));
            Key(KeyCode.Escape); s.PumpFrames(3);
            Unfocus(fx);

            // Changes: the composer's two fields.
            Switch(fx, KeyCode.Digit4);
            foreach (var (field, text) in new[] { ("composer-summary", "Fix"), ("composer-description", "Body") })
            {
                Focus(fx, field);
                int builds = fx.App.Builds, chrome = fx.App.ChromeBuilds;
                s.Window.TypeText(text);
                s.PumpFrames(3);
                Assert.Equal(builds, fx.App.Builds);
                Assert.Equal(chrome, fx.App.ChromeBuilds);
                AssertChromeIsAFullBuild(fx, "after typing in " + field);
            }
        });
    }

    /// <summary>A character in a prompt's fields or the rebase reword builds nothing: the field shows
    /// what was typed, and only the confirm, the validator and Save read it. Each one built the
    /// whole window, chrome too (plan 17 round 10: 194 KB a character over Changes). What they read
    /// is what was typed.</summary>
    [Fact]
    public void APromptOrRewordCharacter_BuildsNothing_AndWhatReadsTheFieldReadsWhatWasTyped()
    {
        Hosted(fx =>
        {
            var s = fx.Session;
            void BuildsNothing(string what, Action keys)
            {
                int builds = fx.App.Builds, chrome = fx.App.ChromeBuilds;
                keys();
                s.PumpFrames(3);
                Assert.True(fx.App.Builds == builds, $"{what}: {fx.App.Builds - builds} builds");
                Assert.True(fx.App.ChromeBuilds == chrome, $"{what}: {fx.App.ChromeBuilds - chrome} chrome builds");
            }
            TextInputElement Field() => Assert.IsType<TextInputElement>(s.Window.Window.FocusedElement);

            // The new-branch prompt: the confirm creates the branch the field holds, and checks it out.
            Invoke(fx.App, "PromptNewBranch");
            s.PumpFrames(4);
            BuildsNothing("the prompt", () =>
            {
                s.Window.TypeText("topic/typex");
                s.Window.PressKey(KeyCode.Backspace);
                s.Window.TypeText("d");
            });
            var name = Field().Text;
            Assert.Contains("topic/typed", name);
            s.Window.PressKey(KeyCode.Enter);
            s.PumpUntil(() => fx.A.Git("rev-parse", "--abbrev-ref", "HEAD").Stdout.Trim() == name, Wait);
            Settle(fx);
            AssertChromeIsAFullBuild(fx, "after the branch the prompt made");

            // The new-worktree prompt's second field: the validator reads the branch typed there and
            // keeps the prompt open.
            Invoke(fx.App, "PromptNewWorktree");
            s.PumpFrames(4);
            var second = Descendants(Root(s)).First(e => e.Key == "dialog-input-2");
            second.Focus();
            s.PumpFrames(2);
            BuildsNothing("the second field", () => s.Window.TypeText("a..b"));
            s.Window.PressKey(KeyCode.Enter);
            s.PumpFrames(3);
            Assert.NotNull(typeof(JunctionApp).GetField("_dialog", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fx.App));
            Assert.Contains("..", Get<HultUI.Core.Builders.ToastHost>(fx.App, "_toasts").Message ?? "");
            s.Window.PressKey(KeyCode.Escape);
            s.PumpFrames(3);
            Settle(fx);

            // The rebase reword: Save keeps the message typed.
            Switch(fx, KeyCode.Digit2);
            var commits = (System.Collections.IList)Get<object>(Get<object>(Get<object>(fx.App, "_history"), "Value"), "Commits");
            Invoke(fx.App, "OpenRebase", commits[2]!);
            s.PumpUntil(() => Get<object?>(fx.App, "_rebase") is not null, Wait);
            s.PumpFrames(3);
            var rows = (System.Collections.IList)Get<object>(Get<object>(fx.App, "_rebase"), "Rows");
            Invoke(fx.App, "OpenRewordRow", rows[0]!);
            s.PumpFrames(4);
            var area = Assert.IsType<TextAreaElement>(s.Window.Window.FocusedElement);
            BuildsNothing("the reword", () => s.Window.TypeText(" reworded"));
            var message = area.Text;
            Assert.Contains("reworded", message);
            Invoke(fx.App, "SaveReword");
            s.PumpFrames(3);
            Assert.Equal(message, Get<string>(rows[0]!, "Reword"));
            Assert.True(HasText(Root(s), message), "the rebase card shows the message typed");
            AssertChromeIsAFullBuild(fx, "after the reword");
        });
    }

    /// <summary>A character in the add-repository folder field and the merge requests' filter
    /// builds the view, for the hint, the button and the rows kept, and not the chrome, which shows
    /// nothing of either (118.6 KB a character); nor does the folder check that lands.</summary>
    [Fact]
    public void AFolderOrFilterCharacter_BuildsTheViewAndNoChrome()
    {
        Hosted(fx =>
        {
            var s = fx.Session;
            Invoke(fx.App, "OpenAddRepo");
            s.PumpFrames(4);
            Assert.IsType<TextInputElement>(s.Window.Window.FocusedElement);
            int builds = fx.App.Builds, chrome = fx.App.ChromeBuilds;
            s.Window.TypeText("no-such-folder");
            s.PumpUntil(() => HasText(Root(s), "Folder not found"), Wait);
            Assert.True(fx.App.Builds > builds, "the hint needs a build");
            Assert.Equal(chrome, fx.App.ChromeBuilds);
            Assert.DoesNotContain(Descendants(Root(s)), e => e.Key == "modal-primary-on");

            foreach (var _ in "no-such-folder") s.Window.PressKey(KeyCode.Backspace);
            s.Window.TypeText(fx.B.Path);
            s.PumpUntil(() => HasText(Root(s), "Git repository found"), Wait);
            Assert.Contains(Descendants(Root(s)), e => e.Key == "modal-primary-on");
            Assert.Equal(chrome, fx.App.ChromeBuilds);
            AssertChromeIsAFullBuild(fx, "after the folder field");
        });

        HostedOnGitLab(new HostedAnswers(), (repo, session, app) =>
        {
            typeof(JunctionApp).GetMethod("OpenMergeRequests", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, [null]);
            session.PumpFrames(4);
            (typeof(JunctionApp).GetField("_mrSearchRef", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)
                as HultUI.Core.Reactivity.Ref<TextInputElement>)!.Current!.Focus();
            session.PumpFrames(2);
            var before = (Sig(Part(session, "titlebar")), Sig(Part(session, "toolbar")), Sig(Part(session, "sidebar-expanded")));
            int builds = app.Builds, chrome = app.ChromeBuilds;
            session.Window.TypeText("ab");
            session.PumpFrames(3);
            Assert.True(app.Builds - builds >= 2, $"{app.Builds - builds} builds for two characters");
            Assert.Equal(chrome, app.ChromeBuilds);
            Assert.True(Descendants(Root(session)).OfType<TextElement>().Any(t => t.Content.StartsWith("Nothing matches \"ab\"", StringComparison.Ordinal)),
                "the list says what it filtered by");
            FullRebuild(app);
            session.PumpFrames(3);
            Assert.Equal(before, (Sig(Part(session, "titlebar")), Sig(Part(session, "toolbar")), Sig(Part(session, "sidebar-expanded"))));
        });
    }

    /// <summary>A toast that comes builds everything, as the change it reports may be the chrome's;
    /// one that goes builds the view, where the toast was, and not the chrome (plan 17 round 10:
    /// a toast over Changes 366 → 227 KB, flash to gone).</summary>
    [Fact]
    public void AToastGoing_BuildsTheViewAndNoChrome()
    {
        Hosted(fx =>
        {
            var s = fx.Session;
            bool Shown() => Descendants(Root(s)).Any(e => e.Key == "toast");
            int chrome = fx.App.ChromeBuilds;
            Invoke(fx.App, "Flash", "Copied 1a2b3c4", null!);
            s.PumpFrames(2);
            Assert.True(Shown());
            Assert.True(fx.App.ChromeBuilds >= chrome + 3, $"{fx.App.ChromeBuilds - chrome} part builds for a toast that came");

            int builds = fx.App.Builds;
            chrome = fx.App.ChromeBuilds;
            s.PumpFrame(TimeSpan.FromSeconds(1.8));      // the hold ran out; fading
            s.PumpFrame(TimeSpan.FromMilliseconds(300));  // gone
            s.PumpFrames(2);
            Assert.False(Shown());
            Assert.True(fx.App.Builds > builds, "the toast went without a build");
            Assert.Equal(chrome, fx.App.ChromeBuilds);
            AssertChromeIsAFullBuild(fx, "after the toast");
        });
    }

    /// <summary>Clicks and keys that change only the view build it and no chrome part: a line or a
    /// hunk picked for staging and the picks cleared, a prompt opened and closed without confirming
    /// (Escape, Cancel, a click beside it), a popover closed with Escape, the search key of History,
    /// Branches, Tags, Files and the merge requests, the merge requests' filter cleared with Escape,
    /// and a folder closed and opened in Files and in a commit's files.
    /// Each built the title bar, the toolbar and the sidebar too (plan 17 round 10: 118.6 KB a
    /// click). A prompt confirmed still builds the chrome, as what it did may be the chrome's: here
    /// a branch made and checked out, which the title bar shows.</summary>
    [Fact]
    public void ViewOnlyClicks_BuildTheViewAndNoChrome()
    {
        Hosted(fx =>
        {
            var s = fx.Session;
            void ViewOnly(string name, Func<Action> act)
            {
                // A second click this soon would be a double-click: let the clock run past it.
                s.PumpFrame(TimeSpan.FromSeconds(1));
                s.PumpFrames(2);
                var run = act();
                int builds = fx.App.Builds, chrome = fx.App.ChromeBuilds;
                run();
                s.PumpFrames(3);
                Assert.True(fx.App.Builds > builds, $"{name} did not rebuild the window");
                Assert.True(fx.App.ChromeBuilds == chrome, $"{name} built the chrome ({fx.App.ChromeBuilds - chrome} part builds)");
                AssertChromeIsAFullBuild(fx, "after " + name);
            }
            Action Click(Element e) => () => s.Window.Click(e);
            Action Key(KeyCode k) => () => s.Window.PressKey(k);

            // Changes: a line of the diff and its hunk picked for staging, and the picks cleared.
            // The watcher reports the write 300 ms later and the window rebuilds, chrome and all:
            // that has to land before anything is counted.
            int gen = DataGen(fx.App, fx.A.Path);
            fx.A.Write("three.txt", "three\nfour\nfive\n");
            s.PumpUntil(() => DataGen(fx.App, fx.A.Path) > gen, Wait);
            Switch(fx, KeyCode.Digit4);
            s.PumpUntil(() => DiffRow(fx, "four") is not null, Wait);
            Settle(fx);
            ViewOnly("a line picked", () => Click(DiffRow(fx, "four")!));
            Assert.Single(Get<HashSet<Junction.Git.GitDiffLine>>(fx.App, "_stageSel"));
            ViewOnly("the line let go", () => Click(DiffRow(fx, "four")!));
            ViewOnly("the hunk picked", () => Click(DiffRow(fx, "@@")!));
            Assert.Equal(2, Get<HashSet<Junction.Git.GitDiffLine>>(fx.App, "_stageSel").Count);
            ViewOnly("the hunk let go", () => Click(DiffRow(fx, "@@")!));
            ViewOnly("a line picked again", () => Click(DiffRow(fx, "five")!));
            ViewOnly("Clear", () => Click(ButtonWith(fx, "Clear")));
            Assert.Empty(Get<HashSet<Junction.Git.GitDiffLine>>(fx.App, "_stageSel"));

            // A prompt opened and closed without confirming, three ways.
            bool Open() => Get<object?>(fx.App, "_dialog") is not null;
            ViewOnly("a prompt opens", () => () => Invoke(fx.App, "PromptNewBranch"));
            Assert.True(Open());
            ViewOnly("Escape closes it", () => Key(KeyCode.Escape));
            Assert.False(Open());
            ViewOnly("a prompt opens again", () => () => Invoke(fx.App, "PromptNewBranch"));
            ViewOnly("Cancel closes it", () => Click(ButtonWith(fx, "Cancel")));
            Assert.False(Open());
            ViewOnly("a tag prompt opens", () => () => Invoke(fx.App, "PromptNewTag"));
            ViewOnly("a click beside it closes it", () => () => s.Window.Click(6, 890));
            Assert.False(Open());

            // A popover from the toolbar (opening it builds the chrome: the menu is the toolbar's),
            // closed with Escape.
            s.PumpFrame(TimeSpan.FromSeconds(1));
            s.Window.Click(Descendants(Part(fx, "toolbar")).First(e => e.AccessibleName == "Stash"));
            s.PumpFrames(3);
            Assert.NotNull(Get<object?>(fx.App, "_popover"));
            ViewOnly("Escape closes the popover", () => Key(KeyCode.Escape));
            Assert.Null(Get<object?>(fx.App, "_popover"));

            // The search keys.
            foreach (var view in new[] { KeyCode.Digit2, KeyCode.Digit5, KeyCode.Digit6, KeyCode.Digit7 })
            {
                Switch(fx, view);
                ViewOnly($"/ in {fx.App.View}", () => Key(KeyCode.Slash));
                Assert.IsType<TextInputElement>(s.Window.Window.FocusedElement);
                Unfocus(fx);
            }

            // A folder closed and opened: in Files, and in the Commit tab's file list, on the newest
            // commit (the one with a folder).
            // Selected outright: the fixture's commits share a second, and the branches and tags on
            // the one before put that one first in History about half the time.
            Invoke(fx.App, "SetCommitSelection", fx.A.Head);
            Invoke(fx.App, "Rerender");
            s.PumpFrames(3);
            Switch(fx, KeyCode.Digit7);
            s.PumpUntil(() => TryFolder(fx, "src") is not null, Wait);
            bool SrcFilesShown() => Descendants(Root(s)).Any(e => e.Key is string k && k.StartsWith("f:src/", StringComparison.Ordinal));
            if (!SrcFilesShown()) { s.Window.Click(Folder(fx, "src")); s.PumpFrames(3); }
            Assert.True(SrcFilesShown());
            ViewOnly("a folder closes in Files", () => Click(Folder(fx, "src")));
            Assert.False(SrcFilesShown());
            ViewOnly("it opens again", () => Click(Folder(fx, "src")));
            Assert.True(SrcFilesShown());
            Switch(fx, KeyCode.Digit3);
            ViewOnly("a folder closes in the commit's files", () => Click(Folder(fx, "src")));
            ViewOnly("it opens again", () => Click(Folder(fx, "src")));

            // A prompt confirmed: the branch it made is checked out, and the title bar says so.
            Switch(fx, KeyCode.Digit4);
            int chrome = fx.App.ChromeBuilds;
            Invoke(fx.App, "PromptNewBranch");
            s.PumpFrames(3);
            s.Window.PressKey(KeyCode.A, KeyModifiers.Ctrl);
            s.Window.TypeText("walked");
            s.Window.PressKey(KeyCode.Enter);
            s.PumpUntil(() => HasText(Part(fx, "titlebar"), "walked"), Wait);
            Assert.True(fx.App.ChromeBuilds > chrome, "the confirmed prompt built no chrome");
            Settle(fx);
            AssertChromeIsAFullBuild(fx, "after the confirmed prompt");
        });

        // The merge requests: Escape clearing the filter, and the key that focuses it.
        HostedOnGitLab(new HostedAnswers(), (repo, session, app) =>
        {
            typeof(JunctionApp).GetMethod("OpenMergeRequests", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, [null]);
            session.PumpFrames(4);
            var field = (typeof(JunctionApp).GetField("_mrSearchRef", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)
                as HultUI.Core.Reactivity.Ref<TextInputElement>)!.Current!;
            field.Focus();
            session.PumpFrames(2);
            session.Window.TypeText("ab");
            session.PumpFrames(3);
            void ViewOnly(string name, Action act)
            {
                int builds = app.Builds, chrome = app.ChromeBuilds;
                act();
                session.PumpFrames(3);
                Assert.True(app.Builds > builds, $"{name} did not rebuild the window");
                Assert.True(app.ChromeBuilds == chrome, $"{name} built the chrome ({app.ChromeBuilds - chrome} part builds)");
            }
            ViewOnly("Escape clearing the filter", () => session.Window.PressKey(KeyCode.Escape));
            Assert.Equal("", field.Text);
            Assert.Equal("", Get<string>(app, "_mrQuery"));
            session.Window.Window.ClearFocus();
            session.PumpFrames(2);
            ViewOnly("/ in the merge requests", () => session.Window.PressKey(KeyCode.Slash));
            Assert.Equal("mr-search", session.Window.Window.FocusedElement?.AutomationId);
            var kept = (Sig(Part(session, "titlebar")), Sig(Part(session, "toolbar")), Sig(Part(session, "sidebar-expanded")));
            FullRebuild(app);
            session.PumpFrames(3);
            Assert.Equal(kept, (Sig(Part(session, "titlebar")), Sig(Part(session, "toolbar")), Sig(Part(session, "sidebar-expanded"))));
        });
    }

    // ── What the chrome shows ────────────────────────────────────────────

    /// <summary>View: the toolbar's lit tab. A tab switch writes the View signal, which the
    /// toolbar reads itself — no MarkDirty is involved.</summary>
    [Fact]
    public void TheLitTab_FollowsTheView()
    {
        Hosted(fx =>
        {
            Assert.Equal(Nocturne.Accent, Tab(fx, "History").BorderColor);
            Assert.NotEqual(Nocturne.Accent, Tab(fx, "Branches").BorderColor);
            Switch(fx, KeyCode.Digit5);
            Assert.Equal(Nocturne.Accent, Tab(fx, "Branches").BorderColor);
            Assert.NotEqual(Nocturne.Accent, Tab(fx, "History").BorderColor);
            KeyViewOnly(fx);
            Assert.Equal(Nocturne.Accent, Tab(fx, "Branches").BorderColor);
            AssertChromeIsAFullBuild(fx);
        });
    }

    /// <summary>The active repository: its name in the title bar, its row lit in the sidebar.</summary>
    [Fact]
    public void TheRepositoryOnScreen_FollowsASwitch()
    {
        Hosted(fx =>
        {
            Assert.True(HasText(Part(fx, "titlebar"), Name(fx.A)));
            fx.Session.Window.PressKey(KeyCode.Down, KeyModifiers.Alt);
            fx.Session.PumpUntil(() => HasText(Part(fx, "titlebar"), Name(fx.B)), Wait);
            Assert.False(HasText(Part(fx, "titlebar"), Name(fx.A)));
            Assert.Equal(Nocturne.Accent900, RepoRow(fx, fx.B.Path).BackgroundColor);
            Assert.NotEqual(Nocturne.Accent900, RepoRow(fx, fx.A.Path).BackgroundColor);
            Settle(fx);
            KeyViewOnly(fx);
            Assert.True(HasText(Part(fx, "titlebar"), Name(fx.B)));
            AssertChromeIsAFullBuild(fx);
        });
    }

    /// <summary>The active workspace: its name in the title bar, its row lit in the sidebar.</summary>
    [Fact]
    public void TheWorkspaceOnScreen_FollowsASwitch()
    {
        Hosted(fx =>
        {
            Assert.True(HasText(Part(fx, "titlebar"), "First"));
            fx.Session.Window.PressKey(KeyCode.Digit2, KeyModifiers.Ctrl);
            fx.Session.PumpUntil(() => HasText(Part(fx, "titlebar"), "Second"), Wait);
            Assert.False(HasText(Part(fx, "titlebar"), "First"));
            Settle(fx);
            KeyViewOnly(fx);
            Assert.True(HasText(Part(fx, "titlebar"), "Second"));
            AssertChromeIsAFullBuild(fx);
        });
    }

    /// <summary>The repository's summary — its branch in the title bar, its dirty count on the
    /// Changes tab, the dot on its sidebar row — follows the repository on disk, through the
    /// watcher's rebuild and the summary's own loader, with view-only keys in between.</summary>
    [Fact]
    public void TheBranchAndTheDirtyCount_FollowTheRepositoryOnDisk()
    {
        Hosted(fx =>
        {
            Assert.True(HasText(Part(fx, "titlebar"), "main"));
            Assert.False(HasTooltip(Part(fx, "sidebar-expanded"), "Uncommitted changes"));

            fx.A.Write("dirty.txt", "x\n");
            KeyViewOnly(fx);
            fx.Session.PumpUntil(() => ChangesBadge(fx) == "1", Wait);
            fx.Session.PumpUntil(() => HasTooltip(Part(fx, "sidebar-expanded"), "Uncommitted changes"), Wait);

            fx.A.Git("checkout", "-q", "alpha");
            KeyViewOnly(fx);
            fx.Session.PumpUntil(() => HasText(Part(fx, "titlebar"), "alpha"), Wait);
            Settle(fx);
            KeyViewOnly(fx);
            Assert.True(HasText(Part(fx, "titlebar"), "alpha"));
            AssertChromeIsAFullBuild(fx);
        });
    }

    /// <summary>Another repository's summary is read by the sidebar alone: when it changes on
    /// disk its row's dot comes and goes with nothing on screen but the sidebar asking.</summary>
    [Fact]
    public void AnotherRepositorysRow_FollowsItsSummary()
    {
        Hosted(fx =>
        {
            Assert.False(HasTooltip(RepoRow(fx, fx.B.Path), "Uncommitted changes"));
            fx.B.Write("dirty.txt", "x\n");
            fx.Session.PumpUntil(() => HasTooltip(RepoRow(fx, fx.B.Path), "Uncommitted changes"), Wait);
            Settle(fx);
            KeyViewOnly(fx);
            Assert.True(HasTooltip(RepoRow(fx, fx.B.Path), "Uncommitted changes"));
            AssertChromeIsAFullBuild(fx);
        });
    }

    /// <summary>The compact breakpoint: the title bar drops the branch and the search label,
    /// the toolbar its labels; and back.</summary>
    [Fact]
    public void TheChromeGoesCompact_AndBack()
    {
        Hosted(fx =>
        {
            Assert.True(HasText(Part(fx, "titlebar"), "Search & commands"));
            Assert.True(HasText(Part(fx, "toolbar"), "Fetch"));

            fx.Session.Window.SetClientSize(1000, 900);
            fx.Session.PumpFrames(4);
            Assert.False(HasText(Part(fx, "titlebar"), "Search & commands"));
            Assert.False(HasText(Part(fx, "toolbar"), "Fetch"));
            KeyViewOnly(fx);
            Assert.False(HasText(Part(fx, "toolbar"), "Fetch"));
            AssertChromeIsAFullBuild(fx);

            fx.Session.Window.SetClientSize(1440, 900);
            fx.Session.PumpFrames(4);
            Assert.True(HasText(Part(fx, "titlebar"), "Search & commands"));
            Assert.True(HasText(Part(fx, "toolbar"), "Fetch"));
            AssertChromeIsAFullBuild(fx);
        });
    }

    /// <summary>The sidebar's shape: a toggle replaces the expanded list with the rail and back,
    /// each a part of its own.</summary>
    [Fact]
    public void TheSidebarCollapsesToTheRail_AndComesBack()
    {
        Hosted(fx =>
        {
            Assert.NotNull(TryPart(fx, "sidebar-expanded"));
            fx.Session.Window.PressKey(KeyCode.Backtick);
            fx.Session.PumpFrames(3);
            Assert.Null(TryPart(fx, "sidebar-expanded"));
            var rail = Part(fx, "sidebar-rail");
            Assert.Contains(Descendants(rail), e => e.Key == "rail:" + fx.A.Path + "#a");
            KeyViewOnly(fx);
            AssertChromeIsAFullBuild(fx);

            fx.Session.Window.PressKey(KeyCode.Backtick);
            fx.Session.PumpFrames(3);
            Assert.Null(TryPart(fx, "sidebar-rail"));
            Assert.Equal(Nocturne.Accent900, RepoRow(fx, fx.A.Path).BackgroundColor);
            AssertChromeIsAFullBuild(fx);
        });
    }

    /// <summary>The remote command running: the toolbar's busy button, kept through view-only
    /// keys, with its icon turning on screen through the Ref — the live icon in the kept
    /// toolbar, not one a discarded build made.</summary>
    [Fact]
    public void TheRunningCommand_ShowsOnItsButton_AndItsIconTurnsOnScreen()
    {
        Hosted(fx =>
        {
            Invoke(fx.App, "BeginRemoteOp", RemoteOp("Fetch"));
            fx.Session.PumpFrames(2);
            Assert.True(HasText(Part(fx, "toolbar"), "Fetching…"));
            KeyViewOnly(fx);
            Assert.True(HasText(Part(fx, "toolbar"), "Fetching…"));

            var icon = typeof(JunctionApp).GetField("_remoteOpIcon", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(fx.App) is HultUI.Core.Reactivity.Ref<Element> r ? r.Current : null;
            Assert.NotNull(icon);
            Assert.False(icon!.IsDetached);
            Assert.Contains(icon, Descendants(Part(fx, "toolbar")));
            AnimationManager.Tick(0.4f);
            fx.Session.PumpFrame();
            Assert.True(icon.Transform is { RotationDegrees: > 1f }, "the icon on screen did not turn");

            Invoke(fx.App, "EndRemoteOp");
            fx.Session.PumpFrames(2);
            Assert.False(HasText(Part(fx, "toolbar"), "Fetching…"));
            Assert.True(HasText(Part(fx, "toolbar"), "Fetch"));
            AssertChromeIsAFullBuild(fx);
        });
    }

    /// <summary>Dragging a sidebar row: the dimmed row and the gap marker while it moves, the
    /// new order once it lands.</summary>
    [Fact]
    public void ARowDraggedInTheSidebar_DimsMovesAndLands()
    {
        Hosted(fx =>
        {
            var s = fx.Session;
            var a = RepoRow(fx, fx.A.Path).Layout.AbsoluteBorderBox;
            var b = RepoRow(fx, fx.B.Path).Layout.AbsoluteBorderBox;
            float x = b.X + b.Width / 2f;
            s.Window.MouseDown(x, b.Y + b.Height / 2f);
            s.Window.MoveMouse(x, b.Y + b.Height / 2f - 20f);
            s.PumpFrame();
            s.Window.MoveMouse(x, a.Y + a.Height * 0.25f);
            s.PumpFrame();
            Assert.Contains(Descendants(Part(fx, "sidebar-expanded")), e => e.Key == "repo:" + fx.B.Path + "#drag" && e.Opacity < 0.5f);

            var settled = RepoRow(fx, fx.A.Path).Layout.AbsoluteBorderBox;
            s.Window.MouseUp(x, settled.Y + settled.Height * 0.25f);
            s.PumpFrames(3);
            var keys = Descendants(Part(fx, "sidebar-expanded")).Select(e => e.Key).Where(k => k?.StartsWith("repo:") == true).ToList();
            Assert.Equal(["repo:" + fx.B.Path, "repo:" + fx.A.Path], keys);
            Settle(fx);
            AssertChromeIsAFullBuild(fx);
        });
    }

    /// <summary>The settings the sidebar and title bar read, edited the way the app edits them:
    /// a workspace renamed, a repository's badge colour, a repository taken out of the
    /// workspace.</summary>
    [Fact]
    public void EditedSettings_ReachTheChrome()
    {
        Hosted(fx =>
        {
            // Rename the active workspace through its edit form.
            Invoke(fx.App, "OpenEditWorkspace", "w1");
            fx.Session.PumpFrames(2);
            var form = typeof(JunctionApp).GetField("_modal", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fx.App)!;
            form.GetType().GetField("Name")!.SetValue(form, "Renamed");
            Invoke(fx.App, "SubmitModal");
            fx.Session.PumpUntil(() => HasText(Part(fx, "titlebar"), "Renamed"), Wait);
            Assert.True(HasText(Part(fx, "sidebar-expanded"), "Renamed"));
            Settle(fx);
            AssertChromeIsAFullBuild(fx);

            // Another badge colour for the second repository.
            var tileBefore = Sig(RepoRow(fx, fx.B.Path));
            Invoke(fx.App, "OpenEditRepo", fx.B.Path);
            fx.Session.PumpFrames(2);
            form = typeof(JunctionApp).GetField("_modal", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fx.App)!;
            var color = (string)form.GetType().GetField("Color")!.GetValue(form)!;
            form.GetType().GetField("Color")!.SetValue(form, color == "rose" ? "teal" : "rose");
            Invoke(fx.App, "SubmitModal");
            fx.Session.PumpFrames(3);
            Assert.NotEqual(tileBefore, Sig(RepoRow(fx, fx.B.Path)));
            Settle(fx);
            AssertChromeIsAFullBuild(fx);

            // Out of the workspace: the row goes.
            Invoke(fx.App, "ToggleRepoInWorkspace", fx.B.Path, "w1");
            fx.Session.PumpFrames(3);
            Assert.Null(TryRepoRow(fx, fx.B.Path));
            Settle(fx);
            KeyViewOnly(fx);
            Assert.Null(TryRepoRow(fx, fx.B.Path));
            AssertChromeIsAFullBuild(fx);
        });
    }

    /// <summary>A colour scheme switch repaints the chrome in the new scheme.</summary>
    [Fact]
    public void ASchemeSwitch_RepaintsTheChrome()
    {
        Hosted(fx =>
        {
            try
            {
                var before = ChromeSigs(fx);
                Invoke(fx.App, "UseTheme", "solarized-light");
                fx.Session.PumpFrames(3);
                Assert.NotEqual(before, ChromeSigs(fx));
                KeyViewOnly(fx);
                AssertChromeIsAFullBuild(fx);
            }
            finally
            {
                Nocturne.Use("nocturne");
                Ui.InvalidateIconCache();
            }
        });
    }

    /// <summary>A display scale change retires the icon rasters cut for the old scale, so the
    /// chrome has to be built again at the new one (see b10a34c).</summary>
    [Fact]
    public void ADisplayScaleChange_BuildsTheChromeAgain()
    {
        Hosted(fx =>
        {
            try
            {
                int chrome = fx.App.ChromeBuilds;
                fx.Session.Window.SetScale(1.5f);
                fx.Session.PumpFrames(3);
                Assert.True(fx.App.ChromeBuilds >= chrome + 3, $"{fx.App.ChromeBuilds - chrome} part builds after the scale change");
                AssertChromeIsAFullBuild(fx);
                chrome = fx.App.ChromeBuilds;
                fx.Session.Window.SetScale(1f);
                fx.Session.PumpFrames(3);
                Assert.True(fx.App.ChromeBuilds >= chrome + 3);
            }
            finally { Ui.DisplayScale = 1f; }
        });
    }

    /// <summary>What GitLab says about the repository: the mark by its name, the to-do bell and
    /// the head pipeline. Each arrives by its own loader, which the part reads, and changes
    /// with a refresh (F5), which rebuilds everything.</summary>
    [Fact]
    public void WhatGitLabSays_ReachesTheChrome()
    {
        var gl = new HostedAnswers();
        HostedOnGitLab(gl, (repo, session, app) =>
        {
            Assert.NotNull(ByIdIn(Part(session, "titlebar"), "gitlab-mark"));
            Assert.NotNull(ByIdIn(Part(session, "sidebar-expanded"), "gitlab-mark"));
            session.PumpUntil(() => ByIdIn(Part(session, "titlebar"), "todo-bell") is not null, Wait);
            Assert.Null(HeadPipeline(session));

            gl.Todos = "[]";
            gl.Pipelines = true;
            session.Window.PressKey(KeyCode.F5);
            session.PumpUntil(() => HeadPipeline(session) is not null, Wait);
            session.PumpUntil(() => ByIdIn(Part(session, "titlebar"), "todo-bell") is null, Wait);
            session.PumpFrames(30);
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(3);
            Assert.NotNull(HeadPipeline(session));
            var before = (Sig(Part(session, "titlebar")), Sig(Part(session, "toolbar")), Sig(Part(session, "sidebar-expanded")));
            FullRebuild(app);
            session.PumpFrames(3);
            Assert.Equal(before, (Sig(Part(session, "titlebar")), Sig(Part(session, "toolbar")), Sig(Part(session, "sidebar-expanded"))));
        });
    }

    /// <summary>A part's host takes its root's place in the layout: the title bar 40 high and
    /// the toolbar 52 across the window, the sidebar 266 wide (58 as the rail) down the whole
    /// body — also in a window too narrow for its content, where a host left to shrink gave
    /// the sidebar's width to the view.</summary>
    [Theory]
    [InlineData(1440)]
    [InlineData(700)]
    public void ThePartsLieWhereTheChromeDid(int width)
    {
        Hosted(fx =>
        {
            fx.Session.Window.SetClientSize(width, 900);
            fx.Session.PumpFrames(4);
            var title = Part(fx, "titlebar").Layout.AbsoluteBorderBox;
            var side = Part(fx, "sidebar-expanded").Layout.AbsoluteBorderBox;
            var tools = Part(fx, "toolbar").Layout.AbsoluteBorderBox;
            Assert.Equal((0f, 0f, (float)width, 40f), (title.X, title.Y, title.Width, title.Height));
            Assert.Equal((0f, 40f, 266f, 860f), (side.X, side.Y, side.Width, side.Height));
            Assert.Equal((266f, 40f, 52f), (tools.X, tools.Y, tools.Height));
            Assert.Equal(width - 266f, tools.Width);

            fx.Session.Window.PressKey(KeyCode.Backtick);
            fx.Session.PumpFrames(3);
            var rail = Part(fx, "sidebar-rail").Layout.AbsoluteBorderBox;
            Assert.Equal((0f, 40f, 58f, 860f), (rail.X, rail.Y, rail.Width, rail.Height));
        });
    }

    // ── The oracle ───────────────────────────────────────────────────────

    /// <summary>The chrome on screen equals the chrome a forced build of every part makes.</summary>
    private static void AssertChromeIsAFullBuild(Fixture fx, string? when = null)
    {
        var kept = ChromeSigs(fx);
        int chrome = fx.App.ChromeBuilds;
        FullRebuild(fx.App);
        fx.Session.PumpFrames(2);
        Assert.True(fx.App.ChromeBuilds >= chrome + 3, $"the forced rebuild built {fx.App.ChromeBuilds - chrome} parts");
        var full = ChromeSigs(fx);
        if (kept != full)
            throw new Xunit.Sdk.XunitException($"the kept chrome is not what a full build makes{(when is null ? "" : " (" + when + ")")}:\n"
                + FirstDifference(kept, full));
    }

    private static string FirstDifference((string, string, string) a, (string, string, string) b)
    {
        foreach (var (x, y, name) in new[] { (a.Item1, b.Item1, "title bar"), (a.Item2, b.Item2, "toolbar"), (a.Item3, b.Item3, "sidebar") })
        {
            if (x == y) continue;
            var xl = x.Split('\n'); var yl = y.Split('\n');
            for (int i = 0; i < Math.Max(xl.Length, yl.Length); i++)
                if (i >= xl.Length || i >= yl.Length || xl[i] != yl[i])
                    return $"{name}, line {i}:\n  kept: {(i < xl.Length ? xl[i] : "<none>")}\n  full: {(i < yl.Length ? yl[i] : "<none>")}";
        }
        return "";
    }

    private static (string, string, string) ChromeSigs(Fixture fx) =>
        (Sig(Part(fx, "titlebar")), Sig(Part(fx, "toolbar")),
         Sig(TryPart(fx, "sidebar-expanded") ?? Part(fx, "sidebar-rail")));

    /// <summary>What a subtree shows: element kinds, keys, text, colours, tooltips, names,
    /// opacity and enabled state, in tree order.</summary>
    private static string Sig(Element e)
    {
        var sb = new StringBuilder();
        void Walk(Element el, int depth)
        {
            sb.Append(' ', depth).Append(el.GetType().Name);
            if (el is not ComponentHost && el.Key is { } k) sb.Append(" key=").Append(k);
            if (el is TextElement t) sb.Append(" text=").Append(t.Content);
            if (el is BoxElement b) sb.Append(" bg=").Append(b.BackgroundColor).Append(" border=").Append(b.BorderColor);
            if (el.TooltipText is { } tip) sb.Append(" tip=").Append(tip);
            if (el.AccessibleName is { } n) sb.Append(" name=").Append(n);
            if (el.AutomationId is { } id) sb.Append(" id=").Append(id);
            if (el.Inherited.TextColor is { } c) sb.Append(" fg=").Append(c);
            if (el is ButtonElement btn) sb.Append(" enabled=").Append(btn.IsEnabled);
            sb.Append(" op=").Append(el.Opacity.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append('\n');
            foreach (var child in el.Children) Walk(child, depth + 1);
        }
        Walk(e, 0);
        return sb.ToString();
    }

    private static void FullRebuild(JunctionApp app) =>
        typeof(JunctionApp).GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes)!
            .Invoke(app, null);

    // ── Harness ──────────────────────────────────────────────────────────

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private sealed record Fixture(TempRepo A, TempRepo B, HeadlessSession Session, JunctionApp App);

    /// <summary>Two repositories in workspace "First" (the first on screen, with commits,
    /// branches, tags and a last commit of three files), the second alone in "Second".</summary>
    private static void Hosted(Action<Fixture> body)
    {
        using var a = TempRepo.CreateWithInitialCommit();
        using var b = TempRepo.CreateWithInitialCommit();
        for (int i = 0; i < 4; i++)
        {
            a.Write($"f{i}.txt", $"{i}\n");
            a.Git("add", "-A");
            a.Git("commit", "-q", "-m", $"change {i}");
        }
        foreach (var br in new[] { "alpha", "beta", "gamma" }) a.Git("branch", br);
        foreach (var t in new[] { "v1", "v2", "v3" }) a.Git("tag", t);
        a.Write("src/one.txt", "one\n");
        a.Write("src/two.txt", "two\n");
        a.Write("three.txt", "three\n");
        a.Git("add", "-A");
        a.Git("commit", "-q", "-m", "three files");

        using var session = HeadlessSession.Create(1440, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
            {
                RepositoryPaths = [a.Path, b.Path],
                ActiveRepoPath = a.Path,
                Workspaces =
                [
                    new WorkspaceData { Id = "w1", Name = "First", RepoPaths = [a.Path, b.Path] },
                    new WorkspaceData { Id = "w2", Name = "Second", RepoPaths = [b.Path] },
                ],
                ActiveWorkspaceId = "w1",
            });
            session.Application.SetRoot(app.Mount());
            var fx = new Fixture(a, b, session, app);
            session.PumpUntil(() => HasText(Part(fx, "titlebar"), "main") && TryRepoRow(fx, b.Path) is not null, Wait);
            Settle(fx);
            body(fx);
        }
        finally { app?.Dispose(); }
    }

    /// <summary>Pumps until the window has rebuilt nothing for a while: the loads the start, a
    /// switch or a disk change set off have landed (and the watcher's echoes of git's own lock
    /// files have passed), so what a test compares is not still moving.</summary>
    private static void Settle(Fixture fx) => Settle(fx.Session, fx.App);

    private static void Settle(HeadlessSession s, JunctionApp app)
    {
        int quiet = 0, last = app.Builds + app.ChromeBuilds;
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (quiet < 30 && DateTime.UtcNow < until)
        {
            s.PumpFrame();
            Thread.Sleep(5);
            int now = app.Builds + app.ChromeBuilds;
            quiet = now == last ? quiet + 1 : 0;
            last = now;
        }
    }

    /// <summary>How often the app has reloaded a repository's data: its watcher's reports and
    /// its own git actions each bump it.</summary>
    private static int DataGen(JunctionApp app, string path) =>
        ((Dictionary<string, int>)typeof(JunctionApp).GetField("_dataGen", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!).GetValueOrDefault(path);

    private static void Switch(Fixture fx, KeyCode view)
    {
        Unfocus(fx);
        fx.Session.Window.PressKey(view);
        fx.Session.PumpFrames(3);
        Settle(fx);
    }

    /// <summary>View-only keys: a selection move down and back in the list on screen (History,
    /// where the fixture opens, when the view has no list).</summary>
    private static void KeyViewOnly(Fixture fx)
    {
        if (fx.App.View is not (JunctionView.History or JunctionView.Branches or JunctionView.Tags))
            Switch(fx, KeyCode.Digit2);
        fx.Session.Window.PressKey(KeyCode.Down);
        fx.Session.PumpFrames(3);
        fx.Session.Window.PressKey(KeyCode.Up);
        fx.Session.PumpFrames(3);
    }

    private static void Focus(Fixture fx, string key)
    {
        var field = key == "history-search"
            ? (typeof(JunctionApp).GetField("_historySearchRef", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fx.App)
                as HultUI.Core.Reactivity.Ref<TextInputElement>)?.Current
            : Descendants(Root(fx.Session)).FirstOrDefault(e => e.Key == key);
        if (field is null) throw new Xunit.Sdk.XunitException($"no field \"{key}\" on screen");
        field.Focus();
        fx.Session.PumpFrames(2);
    }

    private static void Unfocus(Fixture fx)
    {
        fx.Session.Window.Window.ClearFocus();
        fx.Session.PumpFrames(2);
    }

    private static Element Root(HeadlessSession session) =>
        (Element)typeof(HultUI.Window).GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window)!;

    private static Element? TryPart(Fixture fx, string key) => TryPart(fx.Session, key);

    private static Element? TryPart(HeadlessSession session, string key) =>
        Descendants(Root(session)).FirstOrDefault(e => e.Key == key && e.Parent is ComponentHost);

    private static Element Part(Fixture fx, string key) => Part(fx.Session, key);

    private static Element Part(HeadlessSession session, string key) =>
        TryPart(session, key) ?? throw new Xunit.Sdk.XunitException($"no chrome part \"{key}\" on screen");

    private static BoxElement? TryRepoRow(Fixture fx, string path) =>
        TryPart(fx, "sidebar-expanded") is { } side
            ? Descendants(side).OfType<BoxElement>().FirstOrDefault(e => e.Key == "repo:" + path || e.Key == "repo:" + path + "#drag")
            : null;

    private static BoxElement RepoRow(Fixture fx, string path) =>
        TryRepoRow(fx, path) ?? throw new Xunit.Sdk.XunitException($"no sidebar row for {path}");

    private static BoxElement Tab(Fixture fx, string label) =>
        Descendants(Part(fx, "toolbar")).OfType<BoxElement>()
            .First(e => e.TooltipText?.StartsWith(label + " (") == true);

    private static string? ChangesBadge(Fixture fx)
    {
        var tab = Tab(fx, "Changes");
        // The badge is the tab's last child when there is one: a filled box holding the count
        // (the shortcut number before it is an outlined one).
        return tab.Children[^1] is BoxElement { BackgroundColor.A: > 0 } badge && badge.Children.Count == 1
            && badge.Children[0] is TextElement t ? t.Content : null;
    }

    private static string Name(TempRepo repo) => System.IO.Path.GetFileName(repo.Path.TrimEnd('/', '\\'));

    /// <summary>The realized diff row with a code text starting with <paramref name="text"/> ("@@" for
    /// the hunk's header), or null while the diff is not on screen.</summary>
    private static Element? DiffRow(Fixture fx, string text) =>
        Get<HultUI.Core.Reactivity.Ref<Element>>(fx.App, "_diffBodyRef").Current is { IsHosted: true } list
            ? list.Children.Skip(1).FirstOrDefault(r => Descendants(r).OfType<TextElement>().Any(t => t.Content.StartsWith(text, StringComparison.Ordinal)))
            : null;

    private static ButtonElement ButtonWith(Fixture fx, string text) =>
        Descendants(Root(fx.Session)).OfType<ButtonElement>().First(b => Descendants(b).OfType<TextElement>().Any(t => t.Content == text));

    /// <summary>A folder's row in the Files tree or the Commit tab's file list.</summary>
    private static Element Folder(Fixture fx, string path) =>
        TryFolder(fx, path) ?? throw new Xunit.Sdk.XunitException($"no folder row \"{path}\" in {fx.App.View}; rows: "
            + string.Join(", ", Descendants(Root(fx.Session)).Select(e => e.Key).OfType<string>().Where(k => k.StartsWith("d:") || k.StartsWith("f:")).Take(12)));

    private static Element? TryFolder(Fixture fx, string path) =>
        Descendants(Root(fx.Session)).FirstOrDefault(e => e.Key is string k && (k == "d:" + path || k.StartsWith("d:" + path + "#", StringComparison.Ordinal)));

    private static bool HasText(Element root, string text) =>
        Descendants(root).OfType<TextElement>().Any(t => t.Content == text);

    private static bool HasTooltip(Element root, string tip) =>
        Descendants(root).Any(e => e.TooltipText == tip);

    private static Element? ByIdIn(Element root, string id) => Descendants(root).FirstOrDefault(e => e.AutomationId == id);

    /// <summary>The toolbar's head pipeline button, named for the pipeline it shows.</summary>
    private static Element? HeadPipeline(HeadlessSession session) =>
        Descendants(Part(session, "toolbar")).FirstOrDefault(e => e.AccessibleName?.StartsWith("Pipeline #4021") == true);

    private static IEnumerable<Element> Descendants(Element element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var d in Descendants(child))
                yield return d;
    }

    private static object RemoteOp(string name) =>
        Enum.Parse(typeof(JunctionApp).GetNestedType("RemoteOp", BindingFlags.NonPublic)!, name);

    private static void Invoke(JunctionApp app, string method, params object[] args) =>
        typeof(JunctionApp).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, args);

    /// <summary>A field or property of anything, public or not.</summary>
    private static T Get<T>(object target, string name)
    {
        const BindingFlags any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        var type = target.GetType();
        return (T)(type.GetField(name, any)?.GetValue(target) ?? type.GetProperty(name, any)?.GetValue(target))!;
    }

    // ── On GitLab ────────────────────────────────────────────────────────

    private const string Api = "/api/v4";

    private sealed class HostedAnswers
    {
        public volatile string Todos = FakeGitLabHandler.Recording("todos");
        public volatile bool Pipelines;
    }

    private static void HostedOnGitLab(HostedAnswers gl, Action<TempRepo, HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Git("remote", "add", "origin", "https://gitlab.com/acme/core-api.git");
        var head = repo.Head;
        var handler = new FakeGitLabHandler()
            .Map($"GET {Api}/projects/acme%2Fcore-api", "project")
            .Map($"GET {Api}/projects/7781/protected_branches?per_page=100&page=1", "protected_branches")
            .MapJson($"GET {Api}/projects/7781/merge_requests?state=opened&order_by=updated_at&sort=desc&with_merge_status_recheck=true&per_page=100&page=1", "[]")
            .Map($"GET {Api}/todos?state=pending&per_page=100&page=1", _ => FakeGitLabHandler.Json(gl.Todos))
            .Map($"GET {Api}/projects/7781/pipelines?order_by=updated_at&sort=desc&per_page=100&page=1", _ => FakeGitLabHandler.Json(
                gl.Pipelines
                    ? $"[{{\"id\":4021,\"iid\":118,\"project_id\":7781,\"sha\":\"{head}\",\"ref\":\"main\",\"status\":\"running\",\"web_url\":\"https://gitlab.com/acme/core-api/-/pipelines/4021\",\"created_at\":\"2026-09-12T09:44:00.000Z\"}}]"
                    : "[]"));
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
            session.PumpUntil(() => ByIdIn(Root(session), "gitlab-mark") is not null, Wait);
            // The mark is the project's load; the to-dos, the pipelines and the summary may still
            // be landing, each building the chrome. The body counts builds from here.
            Settle(session, app);
            body(repo, session, app);
        }
        finally { app?.Dispose(); }
    }
}
