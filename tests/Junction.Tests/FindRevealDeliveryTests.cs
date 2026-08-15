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
            // Nothing selected, so the Files view shows its placeholder and there is no
            // TextArea to scroll — the state a reveal can arrive in when the file is still
            // being read off-thread.
            Assert.Null(BodyRef(app).Current);

            var find = FindState(app);
            Member(find, "Open", true);
            Search(app).SetQuery("marker");
            Member(find, "PendingReveal", true);

            // The frame in which the request falls due, with no body to receive it.
            session.PumpFrames(2);

            // Now the file arrives, exactly as an off-thread read completing would deliver it.
            SelectFile(app, "sample.cs");
            session.PumpUntil(() => BodyRef(app).Current is { } b && b.Text.Length > 0,
                TimeSpan.FromSeconds(30));
            session.PumpFrames(4);

            var body = BodyRef(app).Current!;
            Assert.True(ScrollY(body) > 0,
                $"the body is still at the top ({ScrollY(body)}) — the reveal was dropped when "
                + "it arrived a frame before the text it was meant to scroll");
        });
    }

    // ── harness ──────────────────────────────────────────────────────────

    private static object FindState(JunctionApp app) =>
        typeof(JunctionApp).GetField("_filesFind", Priv)!.GetValue(app)!;

    private static TextSearch Search(JunctionApp app) =>
        (TextSearch)FindState(app).GetType().GetField("Search")!.GetValue(FindState(app))!;

    private static void Member(object target, string name, object value) =>
        target.GetType().GetField(name)!.SetValue(target, value);

    private static HultUI.Core.Reactivity.Ref<TextAreaElement> BodyRef(JunctionApp app) =>
        (HultUI.Core.Reactivity.Ref<TextAreaElement>)typeof(JunctionApp)
            .GetField("_fileBodyRef", Priv)!.GetValue(app)!;

    private static float ScrollY(TextAreaElement body) =>
        (float)typeof(TextAreaElement).GetProperty("ScrollOffsetY", Priv)!.GetValue(body)!;

    private static void SelectFile(JunctionApp app, string path)
    {
        typeof(JunctionApp).GetField("_filesSelPath", Priv)!.SetValue(app, path);
        typeof(JunctionApp).GetMethod("Rerender", Priv)!.Invoke(app, null);
    }

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

            session.Window.PressKey(KeyCode.Digit7);   // Files
            session.PumpUntil(() => app!.View == JunctionView.Files, TimeSpan.FromSeconds(20));
            session.PumpFrames(2);

            body(session, app!);
        }
        finally { app?.Dispose(); }
    }
}
