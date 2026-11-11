using System.Runtime.CompilerServices;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Rendering;
using HultUI.Headless;
using Meridian.Core;
using Meridian.Theme;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// The last <see cref="MeridianApp.KeptBodies"/> finished response bodies — text and colour — are
/// kept by tab, response and pretty/raw. A tab switched back to, or a pretty/raw toggle back,
/// shows its coloured body in the first frame and hands no job to a worker. Until round 11 of
/// plan 17 each showing formatted and tokenized the body again (51 MB on all threads a switch to
/// a 3,641-line HTML body) and drew an empty pane, then plain text, then colour.
/// </summary>
public class KeptResponseBodyTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-kept-").FullName;

    public KeptResponseBodyTests()
    {
        // Port 1 refuses: a send fails at once, with no response.
        File.WriteAllText(Path.Combine(_root, "one.req"), "GET http://127.0.0.1:1/one\n");
        File.WriteAllText(Path.Combine(_root, "two.req"), "GET http://127.0.0.1:1/two\n");
        File.WriteAllText(Path.Combine(_root, "three.req"), "GET http://127.0.0.1:1/three\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Hosted(Action<HeadlessSession, MeridianApp> body)
    {
        using var session = HeadlessSession.Create(1440, 900);
        var app = new MeridianApp(session.Application, session.Window.Window, viewerCapBytes: 8 * 1024 * 1024);
        try
        {
            session.Application.SetRoot(app.Mount());
            app.OpenWorkspace(_root);
            session.PumpFrame();
            body(session, app);
        }
        finally
        {
            app.Dispose();
        }
    }

    /// <summary>Opens the request's tab and lands <paramref name="body"/> as its response; the
    /// body is shown, coloured, when this returns.</summary>
    private static void OpenWithResponse(HeadlessSession session, MeridianApp app, string request, string body)
    {
        Click(session, "req-" + request);
        app.ActiveTab!.Result = SendResult.Ok(new HttpResponseData(200, "OK", [("Content-Type", "application/json")],
            "application/json", new ResponseBody(BodyKind.Text, body, [], body.Length, false, null),
            new PhaseTimings(default, default, default, default, default, default, false, "HTTP/1.1", null),
            "http://127.0.0.1/" + request));
        MarkDirty(app);
        session.PumpFrames(3);
        Assert.True(Body(session) is { Spans.Count: > 0 }, "the body should be shown, coloured");
    }

    private static void Click(HeadlessSession session, string automationId)
    {
        var el = session.Window.FindByAutomationId(automationId);
        Assert.True(el != null, $"#{automationId} not found");
        el!.Click();
        session.PumpFrames(2);
    }

    /// <summary>A click and the one frame that answers it.</summary>
    private static void ClickOneFrame(HeadlessSession session, string automationId)
    {
        var el = session.Window.FindByAutomationId(automationId);
        Assert.True(el != null, $"#{automationId} not found");
        el!.Click();
        session.PumpFrame();
    }

    private static TextAreaElement? Body(HeadlessSession session) =>
        session.Window.FindByAutomationId("response-body")?.Element as TextAreaElement;

    private static void MarkDirty(MeridianApp app) =>
        typeof(MeridianApp).GetMethod("MarkDirty",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(app, null);

    // 10 items is a short body (coloured with its text), 150 a long one (plain first, then coloured).
    private static string Json(int items, int seed) => ResponseBodyWorkerTests.Json(items, seed);

    /// <summary>A switch back to a tab shows its body, coloured, in the frame that answers the
    /// click, and hands no job to a worker — for a long body too, which used to come plain first.</summary>
    [Theory]
    [InlineData(10)]
    [InlineData(150)]
    public void ASwitchBackShowsTheKeptBodyColouredAtOnce(int items)
    {
        Hosted((session, app) =>
        {
            OpenWithResponse(session, app, "one", Json(items, seed: 1));
            var text = Body(session)!.Text;
            var spans = Body(session)!.Spans;
            OpenWithResponse(session, app, "two", Json(10, seed: 2));
            Assert.Equal(2, app.KeptBodyCount);

            int jobs = app.ResponseWorkerJobs;
            ClickOneFrame(session, "request-tab-0");

            var body = Body(session);
            Assert.True(body != null, "the body should be shown in the first frame");
            Assert.Equal(text, body!.Text);
            Assert.Same(spans, body.Spans);
            Assert.Equal(jobs, app.ResponseWorkerJobs);

            // And back again, the same.
            ClickOneFrame(session, "request-tab-1");
            Assert.True(Body(session) is { Spans.Count: > 0 }, "the other tab's body, coloured");
            Assert.Equal(jobs, app.ResponseWorkerJobs);
        });
    }

    /// <summary>One find bar serves every tab. A switch to a tab with a kept body binds the search
    /// to it in the build that shows it, and a close lets go of the binding and binds the next tab's
    /// body: the count is the shown body's on the frame that shows it (FindBar.Service), not the
    /// count of the tab before.</summary>
    [Fact]
    public void WithFindOpen_ASwitchAndACloseCountTheShownTabsBody()
    {
        Hosted((session, app) =>
        {
            OpenWithResponse(session, app, "one", """{"a":"needle","b":"needle"}""");
            OpenWithResponse(session, app, "two", """{"c":"needle"}""");
            session.Window.PressKey(KeyCode.F, KeyModifiers.Ctrl);
            session.PumpFrames(2);
            session.Window.TypeText("needle");
            session.PumpFrames(3);
            Assert.Equal("1 of 1", Status(session));

            ClickOneFrame(session, "request-tab-0");
            Assert.Equal("1 of 2", Status(session));

            session.Window.PressKey(KeyCode.W, KeyModifiers.Ctrl);
            session.PumpFrame();
            Assert.Equal(1, app.OpenTabCount);
            Assert.Equal("1 of 1", Status(session));
        });

        static string Status(HeadlessSession session) =>
            ((TextElement)session.Window.FindByAutomationId("find-status")!.Element).Content;
    }

    /// <summary>Pretty, raw, pretty: the third showing is the first one's body, with no job.</summary>
    [Fact]
    public void AToggleBackShowsTheKeptBody()
    {
        Hosted((session, app) =>
        {
            OpenWithResponse(session, app, "one", Json(150, seed: 3));
            var pretty = Body(session)!.Spans;
            Click(session, "body-raw");
            session.PumpFrames(2);
            Assert.DoesNotContain('\n', Body(session)!.Text);

            int jobs = app.ResponseWorkerJobs;
            ClickOneFrame(session, "body-pretty");
            Assert.Same(pretty, Body(session)!.Spans);
            Assert.Equal(jobs, app.ResponseWorkerJobs);
        });
    }

    /// <summary>A fifth finished body lets the least recently shown go; showing a kept one makes
    /// it the most recent.</summary>
    [Fact]
    public void TheLeastRecentlyShownOfFiveIsDropped()
    {
        Hosted((session, app) =>
        {
            // one pretty, one raw, two pretty, two raw: four kept.
            OpenWithResponse(session, app, "one", Json(10, seed: 4));
            Click(session, "body-raw");
            session.PumpFrames(2);
            OpenWithResponse(session, app, "two", Json(10, seed: 5));
            Click(session, "body-raw");
            session.PumpFrames(2);
            Assert.Equal(MeridianApp.KeptBodies, app.KeptBodyCount);

            // The fifth, three pretty, lets one pretty go.
            OpenWithResponse(session, app, "three", Json(10, seed: 6));
            Assert.Equal(MeridianApp.KeptBodies, app.KeptBodyCount);

            // Tab one comes back raw, as it was left: kept.
            int jobs = app.ResponseWorkerJobs;
            ClickOneFrame(session, "request-tab-0");
            Assert.DoesNotContain('\n', Body(session)!.Text);
            Assert.Equal(jobs, app.ResponseWorkerJobs);

            // Its pretty body was the oldest: formatted again.
            Click(session, "body-pretty");
            session.PumpFrames(2);
            Assert.Equal(jobs + 1, app.ResponseWorkerJobs);
            Assert.Contains('\n', Body(session)!.Text);
            Assert.Equal(MeridianApp.KeptBodies, app.KeptBodyCount);
        });
    }

    /// <summary>A send that replaces the tab's response drops the old response's bodies, even
    /// with the body not on screen to see it.</summary>
    [Fact]
    public void ANewSendDropsTheTabsKeptBodies()
    {
        Hosted((session, app) =>
        {
            OpenWithResponse(session, app, "one", Json(10, seed: 7));
            Click(session, "body-raw");
            session.PumpFrames(2);
            Assert.Equal(2, app.KeptBodyCount);
            var old = app.ActiveTab!.Result;

            Click(session, "resptab-headers");
            Click(session, "send");
            session.PumpUntil(() => app.ActiveTab!.Result != old && app.ActiveTab.Sending == null,
                TimeSpan.FromSeconds(10));
            session.PumpFrames(2);
            Assert.Equal(0, app.KeptBodyCount);
        });
    }

    /// <summary>A response landed by any other way drops the old one's bodies when the pane next
    /// asks for one.</summary>
    [Fact]
    public void ANewerResponseDropsTheOldOnesBodies()
    {
        Hosted((session, app) =>
        {
            OpenWithResponse(session, app, "one", Json(10, seed: 8));
            Click(session, "body-raw");
            session.PumpFrames(2);
            Click(session, "body-pretty");
            Assert.Equal(2, app.KeptBodyCount);

            OpenWithResponse(session, app, "one", Json(10, seed: 9));
            Assert.Contains("\"id\": 90000", Body(session)!.Text);
            Assert.Equal(1, app.KeptBodyCount);
        });
    }

    /// <summary>A body's colours belong to the theme they were made under: a new one drops them
    /// all, and the next showing formats again.</summary>
    [Fact]
    public void AThemeChangeDropsTheKeptBodies()
    {
        Hosted((session, app) =>
        {
            OpenWithResponse(session, app, "one", Json(10, seed: 10));
            OpenWithResponse(session, app, "two", Json(10, seed: 11));
            Assert.Equal(2, app.KeptBodyCount);

            session.Application.Theme = Azimuth.ToHultTheme();
            int jobs = app.ResponseWorkerJobs;
            Click(session, "request-tab-0");
            session.PumpFrames(2);
            Assert.True(Body(session) is { Spans.Count: > 0 });
            Assert.Equal(jobs + 1, app.ResponseWorkerJobs);
            Assert.Equal(1, app.KeptBodyCount);
        });
    }

    /// <summary>Closing a tab drops its kept bodies; closing them all lets every body go — kept,
    /// on screen and in the loaders.</summary>
    [Fact]
    public void ClosedTabsLetTheirBodiesGo()
    {
        Hosted((session, app) =>
        {
            var gone = ShowFourAndWatch(session, app);
            Assert.Equal(MeridianApp.KeptBodies, app.KeptBodyCount);

            session.Window.PressKey(KeyCode.W, KeyModifiers.Ctrl);
            session.PumpFrames(2);
            Assert.Equal(2, app.KeptBodyCount);

            session.Window.PressKey(KeyCode.W, KeyModifiers.Ctrl);
            session.PumpFrames(3);
            Assert.Equal(0, app.OpenTabCount);
            Assert.Equal(0, app.KeptBodyCount);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            for (int i = 0; i < gone.Length; i++)
                Assert.False(gone[i].TryGetTarget(out _), $"body {i} is still held");
        });
    }

    /// <summary>Two tabs, each shown pretty and raw: four kept bodies, watched by their spans (the
    /// kept view's own list).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<IReadOnlyList<TextSpan>>[] ShowFourAndWatch(HeadlessSession session, MeridianApp app)
    {
        var watched = new List<WeakReference<IReadOnlyList<TextSpan>>>();
        foreach (var (request, seed) in new[] { ("one", 12), ("two", 13) })
        {
            OpenWithResponse(session, app, request, Json(150, seed));
            watched.Add(new(Body(session)!.Spans!));
            Click(session, "body-raw");
            session.PumpFrames(2);
            watched.Add(new(Body(session)!.Spans!));
        }
        return [.. watched];
    }
}
