using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Headless.Recording;
using Xunit;

namespace HultUI.Headless.Tests;

using Text = HultUI.Core.Builders.Text;

public class RenderLogTests
{
    /// <summary>The card used by most tests here: a heading above a filled, rounded button.</summary>
    private static Element BuildCard(float padding = 20f) =>
        Flex.Column(gap: 16).Padding(padding).Children(
            Text.New("Title").FontSize(20f).FontWeight(FontWeight.Bold),
            Box.New().Padding(12).Background(Color.FromHex("#4F46E5")).CornerRadius(8f)
                .TextColor(Color.White).FontWeight(FontWeight.Bold)
                .Children(Text.New("Click me")));

    [Fact]
    public void DisplayList_MatchesBaseline()
    {
        using var session = HeadlessSession.Create(width: 300, height: 160,
            options: HeadlessOptions.DeterministicRecording());
        session.Window.SetRoot(BuildCard());
        session.PumpFrame();

        // The whole point of display-list snapshots: the baseline is readable,
        // so a diff shows what changed rather than handing you two images.
        const string expected = """
            BeginFrame 300x160 scale=1 clear=#FFFFFF
              DrawText "Title" (20,20 260x24) size=20 font=sans-serif weight=Bold color=#000000
              DrawRoundedRect (20,60 260x43.2) r=8 fill=#4F46E5
              DrawText "Click me" (32,72 236x19.2) size=16 font=sans-serif weight=Bold color=#FFFFFF
            EndFrame
            """;

        AssertBaseline(expected, session.Window.RenderLog);
    }

    /// <summary>
    /// Compares a log against an inline baseline.
    /// </summary>
    /// <remarks>
    /// The baseline is normalised because a raw string literal carries the
    /// *source file's* line endings: with git's autocrlf the same file is LF on
    /// Linux and CRLF on Windows, so an un-normalised literal passes on one and
    /// fails on the other. <see cref="RenderLog.ToString"/> always emits \n.
    /// </remarks>
    private static void AssertBaseline(string expected, RenderLog log)
        => Assert.Equal(expected.ReplaceLineEndings("\n"), log.ToString().TrimEnd('\n'));

    [Fact]
    public void LayoutRegression_ShowsAsADiff()
    {
        // A padding change is exactly the kind of unintended regression a
        // semantic test can't see — the counter still reads right, the button
        // is still findable, but everything moved.
        string Snapshot(float padding)
        {
            using var session = HeadlessSession.Create(width: 300, height: 160,
                options: HeadlessOptions.DeterministicRecording());
            session.Window.SetRoot(BuildCard(padding));
            session.PumpFrame();
            return session.Window.RenderLog.ToString();
        }

        var before = Snapshot(20f);
        var after = Snapshot(24f);

        Assert.NotEqual(before, after);
        Assert.Contains("(20,20 260x24)", before);
        Assert.Contains("(24,24 252x24)", after);
    }

    [Fact]
    public void SameTreeTwice_ProducesIdenticalLogs()
    {
        // Stability is what makes a baseline trustworthy: no ordering wobble,
        // no float noise between runs.
        string Snapshot()
        {
            using var session = HeadlessSession.Create(width: 300, height: 160,
                options: HeadlessOptions.DeterministicRecording());
            session.Window.SetRoot(BuildCard());
            session.PumpFrame();
            return session.Window.RenderLog.ToString();
        }

        Assert.Equal(Snapshot(), Snapshot());
    }

    [Fact]
    public void FixedMetrics_DoNotDependOnSystemFonts()
    {
        // Text geometry is derived purely from font size, so a baseline written
        // on one OS compares clean on another. (Real metrics would vary with
        // whatever font the platform resolved.)
        using var session = HeadlessSession.Create(width: 400, height: 200,
            options: HeadlessOptions.DeterministicRecording());
        // FlexStart on both axes so the text box is its measured size rather
        // than being stretched to the container.
        session.Window.SetRoot(Flex.Row().Padding(10).AlignItems(AlignItems.FlexStart).Children(
            Text.New("ABCD").FontSize(10f)));
        session.PumpFrame();

        // 4 chars x (10 * 0.5) = 20 wide; line height 10 * 1.2 = 12. Both derive
        // from font size alone — no system font involved.
        var log = session.Window.RenderLog.ToString();
        Assert.True(log.Contains("(10,10 20x12)"), log);
    }

    [Fact]
    public void ClipsAndTransforms_NestTheLog()
    {
        using var session = HeadlessSession.Create(width: 200, height: 200,
            options: HeadlessOptions.DeterministicRecording());
        session.Window.SetRoot(ScrollView.New().Height(100).Children(
            Box.New().Height(400).Background(Color.Red)));
        session.PumpFrame();

        var ops = session.Window.RenderLog.Ops;
        var push = ops.FirstOrDefault(o => o.Name.StartsWith("PushClip")
                                        || o.Name.StartsWith("PushRoundedClip"));
        Assert.NotNull(push);

        // Content drawn inside the clip is indented past it, and the stack balances.
        Assert.Contains(ops, o => o.Name.StartsWith("Draw") && o.Depth > push.Depth);
        Assert.Equal(ops.Count(o => o.Name.StartsWith("Push")), ops.Count(o => o.Name == "Pop"));
    }

    [Fact]
    public void Named_FiltersOps()
    {
        using var session = HeadlessSession.Create(width: 300, height: 160,
            options: HeadlessOptions.DeterministicRecording());
        session.Window.SetRoot(BuildCard());
        session.PumpFrame();

        var texts = session.Window.RenderLog.Named("DrawText").ToList();
        Assert.Equal(2, texts.Count);
        Assert.Contains(texts, o => o.Args.Contains("\"Click me\""));
    }

    [Fact]
    public void Clear_ScopesTheLogToOneFrame()
    {
        using var session = HeadlessSession.Create(width: 200, height: 100,
            options: HeadlessOptions.DeterministicRecording());
        var label = Text.New("first");
        session.Window.SetRoot(Flex.Column().Padding(10).Children(label));
        session.PumpFrame();

        session.Window.RenderLog.Clear();
        label.Content = "second";
        session.PumpFrame();

        var log = session.Window.RenderLog.ToString();
        Assert.Contains("\"second\"", log);
        Assert.DoesNotContain("\"first\"", log);
    }

    [Fact]
    public void RecordingCoexistsWithPixelCapture()
    {
        // Recording decorates the raster surface rather than replacing it, so
        // the frame still renders for real.
        using var session = HeadlessSession.Create(width: 50, height: 50,
            options: new HeadlessOptions { RecordDrawCalls = true });
        session.Window.SetRoot(Box.New().Background(Color.Red));
        session.PumpFrame();

        Assert.NotEmpty(session.Window.RenderLog.Ops);

        using var image = session.Window.CapturePixels();
        using var bitmap = SkiaSharp.SKBitmap.FromImage(image);
        Assert.Equal(255, bitmap.GetPixel(25, 25).Red);
    }

    [Fact]
    public void RenderLog_ThrowsWhenRecordingIsOff()
    {
        using var session = HeadlessSession.Create();
        session.Window.SetRoot(Box.New());
        session.PumpFrame();

        var ex = Assert.Throws<InvalidOperationException>(() => session.Window.RenderLog);
        Assert.Contains("RecordDrawCalls", ex.Message);
    }
}
