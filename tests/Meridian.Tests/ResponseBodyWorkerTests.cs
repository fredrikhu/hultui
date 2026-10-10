using System.Text;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Rendering;
using HultUI.Headless;
using HultUI.Syntax;
using Meridian.Core;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// The response body is pretty-printed, split and tokenized on the thread pool, not in the
/// build that shows it. An 18,884-line JSON response used to hold the UI thread for ~110 ms
/// before its first frame. Under a headless session the pool hop is deterministic: the text
/// lands one frame after the build that asked for it, and a long body's colour one frame later.
/// </summary>
public class ResponseBodyWorkerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-body-").FullName;

    public ResponseBodyWorkerTests() =>
        File.WriteAllText(Path.Combine(_root, "ok.req"), "GET http://127.0.0.1:1/ok\n");

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
            session.Window.FindByAutomationId("req-ok")!.Click();
            session.PumpFrames(2);
            body(session, app);
        }
        finally
        {
            app.Dispose();
        }
    }

    /// <summary>The response lands, as the send's continuation lands it: the result set, then a
    /// rebuild.</summary>
    private static void Arrive(MeridianApp app, string json)
    {
        app.ActiveTab!.Result = SendResult.Ok(new HttpResponseData(200, "OK", [("Content-Type", "application/json")],
            "application/json", new ResponseBody(BodyKind.Text, json, [], json.Length, false, null),
            new PhaseTimings(default, default, default, default, default, default, false, "HTTP/1.1", null),
            "http://127.0.0.1/big"));
        typeof(MeridianApp).GetMethod("MarkDirty",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(app, null);
    }

    private static TextAreaElement? Body(HeadlessSession session) =>
        session.Window.FindByAutomationId("response-body")?.Element as TextAreaElement;

    /// <summary>The spans the build used to make on the UI thread: the tokenize of the shown
    /// text, flattened by its '\n' line starts.</summary>
    private static List<TextSpan> SynchronousSpans(string text)
    {
        var perLine = SyntaxHighlighter.TokenizeLines("response.json", text);
        Assert.NotNull(perLine);
        var lines = text.Split('\n');
        var flat = new List<TextSpan>();
        int offset = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (i < perLine!.Length)
                foreach (var s in perLine[i])
                    flat.Add(new TextSpan(offset + s.Start, s.Length, s.Color, s.Background));
            offset += lines[i].Length + 1;
        }
        return flat;
    }

    private static void AssertSameSpans(IReadOnlyList<TextSpan> expected, IReadOnlyList<TextSpan>? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Count, actual!.Count);
        for (int i = 0; i < expected.Count; i++)
            Assert.True(expected[i] == actual[i], $"span {i}: expected {expected[i]}, got {actual[i]}");
    }

    /// <summary>~16 lines an item: 10 items is a short body (coloured with its text), 150 a long
    /// one (plain first, then coloured).</summary>
    [Theory]
    [InlineData(10)]
    [InlineData(150)]
    public void TheBodyIsTokenizedOnAWorkerNeverOnTheUiThread(int items)
    {
        Hosted((session, app) =>
        {
            int before = SyntaxHighlighter.RegistryUsesOnThisThread;
            Arrive(app, Json(items, seed: 0));
            session.PumpFrames(2);

            var body = Body(session);
            Assert.True(body is { Spans.Count: > 0 }, "the body should be shown, coloured");
            Assert.Equal(before, SyntaxHighlighter.RegistryUsesOnThisThread);
        });
    }

    [Theory]
    [InlineData(10)]
    [InlineData(150)]
    public void TheSpansAreTheOnesTheBuildUsedToMake(int items)
    {
        Hosted((session, app) =>
        {
            Arrive(app, Json(items, seed: 1));
            session.PumpFrames(2);

            var body = Body(session)!;
            Assert.True(body.Text.Count(c => c == '\n') + 1 > (items == 10 ? 100 : 2_000), "pretty-printed");
            AssertSameSpans(SynchronousSpans(body.Text), body.Spans);
        });
    }

    /// <summary>A long body is drawn plain as soon as it is split, and coloured a frame after
    /// the tokenize lands; a short one comes with its colour.</summary>
    [Fact]
    public void ALongBodyShowsPlainFirstThenColoured()
    {
        Hosted((session, app) =>
        {
            Arrive(app, Json(150, seed: 2));
            session.PumpFrame();
            Assert.True(Body(session) is { Spans: null } plain && plain.Text.Length > 0, "the text, plain");

            session.PumpFrame();
            Assert.True(Body(session) is { Spans.Count: > 0 }, "then its colour");
        });
    }

    /// <summary>A newer response stops the tokenize of the one it replaced, and the colour
    /// found for the old text never lands on the new one.</summary>
    [Fact]
    public void ANewerResponseStopsTheWorkForTheOldOne()
    {
        Hosted((session, app) =>
        {
            // The first response's text is in and its tokenize asked for, not yet run.
            Arrive(app, Json(150, seed: 3));
            session.PumpFrame();
            var first = app.ResponseWorkToken;
            Assert.False(first.IsCancellationRequested);

            Arrive(app, Json(160, seed: 4));
            Assert.True(first.IsCancellationRequested, "the replaced response's tokenize was not stopped");

            session.PumpFrames(2);
            var body = Body(session)!;
            Assert.Contains("\"id\": 40000", body.Text);
            AssertSameSpans(SynchronousSpans(body.Text), body.Spans);
        });
    }

    /// <summary>The colour that landed for the previous response is not drawn over the next
    /// one's text while that is being tokenized: it shows plain until its own spans land.</summary>
    [Fact]
    public void TheOldColourIsNeverDrawnOverTheNewText()
    {
        Hosted((session, app) =>
        {
            Arrive(app, Json(150, seed: 5));
            session.PumpFrames(2);
            Assert.True(Body(session) is { Spans.Count: > 0 });

            Arrive(app, Json(150, seed: 6));
            session.PumpFrame();
            var body = Body(session)!;
            Assert.Contains("\"id\": 60000", body.Text);
            Assert.Null(body.Spans);

            session.PumpFrame();
            AssertSameSpans(SynchronousSpans(Body(session)!.Text), Body(session)!.Spans);
        });
    }

    /// <summary>Closing the tab stops its body's tokenize.</summary>
    [Fact]
    public void ClosingTheTabStopsItsWork()
    {
        Hosted((session, app) =>
        {
            Arrive(app, Json(150, seed: 7));
            session.PumpFrame();
            var work = app.ResponseWorkToken;
            Assert.False(work.IsCancellationRequested);

            session.Window.PressKey(KeyCode.W, KeyModifiers.Ctrl);
            session.PumpFrames(2);

            Assert.Equal(0, app.OpenTabCount);
            Assert.True(work.IsCancellationRequested, "the closed tab's tokenize was not stopped");
        });
    }

    /// <summary>While the next response's text is formatted, the tab's previous answer stays up,
    /// greyed as during a send, rather than the pane going blank for a frame.</summary>
    [Fact]
    public void ThePreviousAnswerStaysUpWhileTheNextIsFormatted()
    {
        Hosted((session, app) =>
        {
            Arrive(app, Json(10, seed: 8));
            session.PumpFrames(2);
            string first = Body(session)!.Text;

            Arrive(app, Json(10, seed: 9));
            Assert.Equal(first, Body(session)?.Text);

            session.PumpFrame();
            Assert.Contains("\"id\": 90000", Body(session)!.Text);
        });
    }

    /// <summary>Compact JSON the way the survey's server sent it; pretty-printed it is about 16
    /// lines an item.</summary>
    internal static string Json(int count, int seed)
    {
        var rng = new Random(7 + seed);
        var sb = new StringBuilder("{\"count\":").Append(count).Append(",\"items\":[");
        string[] tags = ["alpha", "beta", "gamma"];
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(i + seed * 10_000).Append(",\"name\":\"customer-").Append(i.ToString("00000"))
                .Append("\",\"email\":\"user").Append(i).Append("@example.test\",\"balance\":")
                .Append((rng.NextDouble() * 5500 - 500).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
                .Append(",\"active\":").Append(i % 3 != 0 ? "true" : "false").Append(",\"tags\":[");
            for (int t = 0; t <= i % 3; t++) sb.Append(t > 0 ? "," : "").Append('"').Append(tags[t]).Append('"');
            sb.Append("],\"address\":{\"street\":\"").Append(i).Append(" Main St\",\"city\":\"Springfield\",\"zip\":\"")
                .Append(10000 + i).Append("\"}}");
        }
        return sb.Append("]}").ToString();
    }
}
