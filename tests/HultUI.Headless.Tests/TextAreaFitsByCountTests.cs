using System.Reflection;
using System.Text;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using SkiaSharp;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A TextArea in a face proven monospace for printable ASCII takes a line of it as fitting,
/// without measuring it, when one character more would fit
/// (<see cref="IRenderContext.MonospaceAsciiAdvance"/>). With real fonts: the rows, the caret and
/// the selection are the ones measuring every line gives, at widths that land on a character's
/// edge and a hair either side of one, for lines of every length, with tabs, carriage returns
/// and characters outside printable ASCII among them.
/// </summary>
public class TextAreaFitsByCountTests
{
    private static string Document()
    {
        var sb = new StringBuilder();
        const string ascii = "!\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~ ";
        for (int n = 1; n <= 160; n++)
        {
            for (int i = 0; i < n; i++) sb.Append(ascii[(i * 7 + n) % ascii.Length]);
            if (n % 9 == 0) sb.Append('\t').Append("tab");
            if (n % 13 == 0) sb.Append(" café — 東");
            if (n % 17 == 0) sb.Append('\r');
            sb.Append('\n');
            sb.Append("  \"key").Append(n).Append("\": [1, 2.5, \"=> != -> www fi\", null],\n");
        }
        return sb.ToString();
    }

    // Two windows of one session (one application a process), the same text in each: one area
    // fitting by count, one measuring every line. Pinned: the bundled DejaVu faces, drawn as
    // outlines, the same pixels from draw to draw (system glyph masks can differ between two draws
    // in one process once Skia purges its strikes).
    private static (HeadlessSession Session, HeadlessWindow Second, TextAreaElement Counted, TextAreaElement Measured)
        Open(string text, int width, bool pinned = false)
    {
        var session = HeadlessSession.Create(width, 600, options: pinned ? HeadlessOptions.Golden() : null);
        var second = session.CreateWindow(width: width, height: 600);
        var counted = Area(text, fitsByCount: true);
        var measured = Area(text, fitsByCount: false);
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Padding(8).Children(counted));
        second.SetRoot(Flex.Column().FlexGrow(1).Padding(8).Children(measured));
        session.PumpFrame();
        return (session, second, counted, measured);
    }

    private static TextAreaElement Area(string text, bool fitsByCount)
    {
        var area = TextArea.New(text).FlexGrow(1);
        area.FitsByCount = fitsByCount;
        area.FontFamily(FontFamilies.Monospace).FontSize(12f).LineHeight(1.5f);
        return area;
    }

    private static IRenderContext ContextOf(TextAreaElement area) => (IRenderContext)typeof(TextAreaElement)
        .GetField("_renderCtx", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(area)!;

    [Fact]
    public void TheRows_AreTheMeasuredPaths_AtEveryWidth()
    {
        string text = Document();
        var (session, _, counted, measured) = Open(text, 900);
        using var owned = session;
        var style = counted.ResolvedInherited;
        float advance = ContextOf(counted).MonospaceAsciiAdvance(style);
        Assert.True(advance > 0f, "the platform's monospace face was not proven monospace");

        int compared = 0, wrapped = 0;
        foreach (float chars in new[] { 1f, 7f, 20f, 39.5f, 40f, 41f, 64f, 80f, 99f, 100f, 101f, 120f, 159f, 160f, 161f, 200f })
            foreach (float nudge in new[] { -0.01f, 0f, 0.01f })
            {
                float width = chars * advance + nudge;
                var expected = measured.GetLineStarts(width, style);
                var actual = counted.GetLineStarts(width, style);
                Assert.True(expected.SequenceEqual(actual), $"at {width} ({chars} characters' width {nudge:+0.00;-0.00;0}) the rows differ");
                compared++;
                wrapped += expected.Count;
            }
        Assert.Equal(48, compared);
        Assert.True(wrapped > 48 * 330, $"only {wrapped} rows: too few lines wrapped");
    }

    /// <summary>The frames are the same: the rows, the caret and a selection over several
    /// rows, at three widths, one of them a character's edge.</summary>
    [Fact]
    public void TheFrames_AreTheMeasuredPaths()
    {
        string text = Document();
        foreach (int width in new[] { 300, 517, 900 })
        {
            var (session, second, counted, measured) = Open(text, width, pinned: true);
            using var owned = session;
            Assert.True(ContextOf(counted).MonospaceAsciiAdvance(counted.ResolvedInherited) > 0f,
                "the pinned monospace face was not proven monospace");
            foreach (var (window, area) in new[] { (session.Window, counted), (second, measured) })
            {
                window.Click(area);
                session.PumpFrame();
                area.CursorPosition = text.IndexOf('\n', 2_000) + 5;
                session.PumpFrame();
                for (int i = 0; i < 4; i++) window.PressKey(KeyCode.Down, KeyModifiers.Shift);
                session.PumpFrame();
            }
            Assert.True(counted.HasSelection && counted.SelectionStart == measured.SelectionStart
                && counted.SelectionEnd == measured.SelectionEnd, "the two areas' selections differ");
            using var a = session.Window.CapturePixels();
            using var b = second.CapturePixels();
            using var pa = SKBitmap.FromImage(a);
            using var pb = SKBitmap.FromImage(b);
            Assert.True(pa.GetPixelSpan().SequenceEqual(pb.GetPixelSpan()), $"the frames differ at {width} px");
        }
    }
}
