using System.Reflection;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// What happens to a find's "scroll to the match" when it falls due before there is
/// anything to scroll.
///
/// <para>The reveal is deferred to a post-frame callback, because scrolling during Build
/// would run before the layout pass and clamp to zero. But the request is a single bool
/// cleared at the moment the callback is queued, and the callback itself gives up quietly
/// when the body is not mounted — so a request that arrives a frame early has nowhere to
/// land and is never retried. Two CI runs failed with a scroll that ended at 0px rather
/// than at the wrong place, which is the signature of a reveal that did not happen at all
/// rather than one that miscalculated.</para>
/// </summary>
public class FindRevealDeliveryTests
{
    private const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void ARevealThatFallsDueBeforeTheBodyExistsIsStillHonoured()
    {
        Hosted((session, app) =>
        {
            // The Files view has only just been reached: its tree load and the file's
            // content load are queued but have not landed, so the pane is a placeholder
            // and no TextArea has ever mounted — the state a reveal arrives in when the
            // file is still being read.
            Assert.Null(BodyRef(app).Current);

            var find = FindState(app);
            Member(find, "Open", true);
            Search(app).SetQuery("marker");
            Member(find, "PendingReveal", true);

            // Seeded before the tree lands: PickInitialTreeFile keeps an existing
            // selection it finds in the tree, and left to its own devices it would pick
            // the tree's first path — the seed file, whose text has no marker in it.
            typeof(JunctionApp).GetField("_filesSelPath", Priv)!.SetValue(app, "sample.cs");

            // Each pump lands one queued load: the tree, then the file's content. The
            // reveal falls due on every frame in between, with no body to receive it;
            // when the text finally mounts, the held request must come back round and
            // scroll. No PumpUntil — the frames are exact now that loads land
            // deterministically, which is the point: this test used to stage "no body
            // yet" by racing the thread pool, and lost under machine load.
            session.PumpFrames(6);

            var body = BodyRef(app).Current!;
            Assert.NotNull(body);
            Assert.True(body.Text.Length > 0, "the file's text never arrived");
            Assert.True(ScrollY(body) > 0,
                $"the body is still at the top ({ScrollY(body)}) — the reveal was dropped when "
                + "it arrived a frame before the text it was meant to scroll");
        });
    }

    [Fact]
    public void ARevealDueWhileTheNextFileLoads_IsNotSpentOnTheOldBody()
    {
        Hosted((session, app) =>
        {
            // Let everything land: the tree auto-selects README.md and its body mounts.
            session.PumpFrames(4);
            var oldBody = BodyRef(app).Current;
            Assert.NotNull(oldBody);

            var find = FindState(app);
            Member(find, "Open", true);
            Search(app).SetQuery("marker");   // nothing in README.md matches
            Member(find, "PendingReveal", true);

            // Selecting sample.cs rebuilds the pane as a "Loading…" placeholder for the
            // frame its content takes to land. The old TextArea is unmounted, but the
            // body ref still points at it — a reveal serviced against that ghost is
            // spent, and when the real text mounts a frame later nothing scrolls.
            //
            // Marshalled rather than called directly: Rerender's signal write rebuilds
            // synchronously, and a rebuild *between* frames queues the content load
            // early enough that the ghost reveal's post-frame callback runs after the
            // text has landed, where re-reading the ref self-heals it. Selecting inside
            // the frame — where a click handler runs in production — keeps the callback
            // ahead of the load, which is the window the bug lives in.
            HultUI.Application.InvokeOnMainThread(() => SelectFile(app, "sample.cs"));
            session.PumpFrames(6);

            var body = BodyRef(app).Current!;
            Assert.NotSame(oldBody, body);
            Assert.True(body.Text.Length > 100, "sample.cs's text never arrived");
            Assert.True(ScrollY(body) > 0,
                $"the body is still at the top ({ScrollY(body)}) — the reveal was spent on "
                + "the previous file's unmounted body while this one was loading");
        });
    }

    // ── harness ──────────────────────────────────────────────────────────

    private static void SelectFile(JunctionApp app, string path)
    {
        typeof(JunctionApp).GetField("_filesSelPath", Priv)!.SetValue(app, path);
        typeof(JunctionApp).GetMethod("Rerender", Priv)!.Invoke(app, null);
    }

    private static object FindState(JunctionApp app) =>
        typeof(JunctionApp).GetField("_filesFind", Priv)!.GetValue(app)!;

    private static TextSearch Search(JunctionApp app) =>
        (TextSearch)FindState(app).GetType().GetField("Search")!.GetValue(FindState(app))!;

    private static void Member(object target, string name, object value) =>
        target.GetType().GetField(name)!.SetValue(target, value);

    private static HultUI.Core.Reactivity.Ref<TextAreaElement> BodyRef(JunctionApp app) =>
        (HultUI.Core.Reactivity.Ref<TextAreaElement>)typeof(JunctionApp)
            .GetField("_fileBodyRef", Priv)!.GetValue(app)!;

    private static float ScrollY(TextAreaElement body) => body.ScrollOffsetY;

    private static void Hosted(Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        // Long enough that a real reveal has somewhere to scroll to.
        var lines = string.Join("\n", Enumerable.Range(1, 400).Select(i => $"// line {i}"));
        repo.Write("sample.cs", lines + "\n// the far-below marker\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "add a sample file");

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

            // Stop pumping the moment the view flips: the test needs to arrange its find
            // state before the Files view's queued loads land.
            session.Window.PressKey(KeyCode.Digit7);   // Files
            session.PumpUntil(() => app!.View == JunctionView.Files, TimeSpan.FromSeconds(20));

            body(session, app!);
        }
        finally { app?.Dispose(); }
    }
}
