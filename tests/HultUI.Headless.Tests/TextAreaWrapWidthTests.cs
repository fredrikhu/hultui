using System.Reflection;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A wrapped <see cref="TextAreaElement"/> breaks its lines to the width it was actually
/// given.
///
/// <para>Nothing covered this. The area owns its own wrapping — it computes line starts during
/// layout and caches them against the width it wrapped at — which is exactly why it is immune
/// to the measured-height faults that bit the two apps that wrap rows themselves. But "it
/// wraps itself" is only reassuring while the width it wraps to is the width it has: a wrap
/// computed a few pixels too generous draws past the right edge, and a read-only area has no
/// sideways scrolling to reach the overflow with.</para>
///
/// <para>Verified by injecting the fault it is meant to catch — adding 14px to the wrap width
/// inside the control — and confirming that with it in place every suite in the repository
/// still passed, this one included, until these tests existed.</para>
/// </summary>
public class TextAreaWrapWidthTests
{
    private const float Width = 260f;

    [Theory]
    [InlineData("prose", "the row has to grow with it or the text is clipped and that reads as a fault rather than a narrow pane, which is why this exists at every length")]
    [InlineData("one unbreakable token", "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    [InlineData("a token amid prose", "start xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx end")]
    [InlineData("hard newlines", "short\nthe row has to grow with it or the text is clipped and that reads as a fault\nshort")]
    public void EveryWrappedLineFitsTheWidthItWasGiven(string name, string text)
    {
        using var session = HeadlessSession.Create(width: 500, height: 400);
        var area = Mount(session, text);

        var style = area.ResolvedInherited;
        float content = area.Layout.AbsoluteContentBox.Width;
        Assert.True(content > 0, "the area was never laid out");

        var lines = WrappedLines(area, text);
        Assert.True(lines.Count > 1, $"{name}: the text laid out as one line — it is not a wrap test");

        foreach (var line in lines)
        {
            // Trailing whitespace is allowed to hang past the edge, as it is everywhere in
            // this pipeline: it paints nothing.
            float drawn = TextMeasure.MeasureWidth(line.TrimEnd(), style);
            Assert.True(drawn <= content + 0.5f,
                $"{name}: a line measured {drawn:0.0}px in a {content:0.0}px area — it draws past the edge");
        }
    }

    /// <summary>Narrowing re-wraps rather than serving the cached line starts, which is the
    /// other half of "the width it wraps to is the width it has". The cache is keyed on that
    /// width; one that stopped noticing a change would look exactly like the bug above.</summary>
    [Fact]
    public void NarrowingTheAreaRewrapsToTheNewWidth()
    {
        const string Text = "the row has to grow with it or the text is clipped and that reads "
            + "as a fault rather than a narrow pane, which is why this exists at every length";

        using var session = HeadlessSession.Create(width: 500, height: 400);
        // Filling the window rather than a fixed row: the point is that the wrap width
        // follows the width the area actually has, so the container must not pin it.
        var area = Mount(session, Text, fill: true);
        int wide = WrappedLines(area, Text).Count;

        session.Window.SetClientSize(300, 400);
        session.PumpFrames(3);
        session.Window.CapturePixels().Dispose();

        int narrow = WrappedLines(area, Text).Count;
        Assert.True(narrow > wide,
            $"the text took {wide} lines at 500px and {narrow} at 300px — it did not re-wrap");
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private static TextAreaElement Mount(HeadlessSession session, string text, bool fill = false)
    {
        var area = TextArea.New(text);
        area.ReadOnly = true;
        area.FontFamily("Consolas").FontSize(12f).FlexGrow(1);
        // A row of a fixed width, so the wrap width is a number this test knows rather than
        // whatever the window happened to leave over — except where the test is about the
        // width changing, which needs the area to follow the window.
        var row = Flex.Row(gap: 0).Height(300).Children(area);
        if (!fill) row.Width(Width);
        session.Window.SetRoot(Flex.Column(gap: 0).Children(row));
        session.PumpFrames(2);
        session.Window.CapturePixels().Dispose();   // the line starts are computed during layout
        return area;
    }

    /// <summary>The lines the control actually broke the text into, read from its own cache —
    /// the same list it scrolls and hit-tests against, so this cannot disagree with what it
    /// draws.</summary>
    private static List<string> WrappedLines(TextAreaElement area, string text)
    {
        var starts = (List<int>?)typeof(TextAreaElement)
            .GetField("_lineStarts", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(area);
        Assert.True(starts is { Count: > 0 }, "the area has not wrapped yet");

        var lines = new List<string>();
        for (int i = 0; i < starts!.Count; i++)
        {
            int from = starts[i];
            int to = i + 1 < starts.Count ? starts[i + 1] : text.Length;
            if (to > from) lines.Add(text[from..to].TrimEnd('\n'));
        }
        return lines;
    }
}
