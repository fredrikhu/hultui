using System.Reflection;
using System.Text;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// In a face the render context proves monospace for printable ASCII
/// (<see cref="IRenderContext.MonospaceAsciiAdvance"/>), a TextArea takes a hard line of those
/// characters as fitting when one character more would still fit, without measuring it: a
/// 19,000-line JSON response opened measured all 19,000 lines to find that they fit. Any other
/// line — a tab, a character outside printable ASCII, one that fits by less than a character,
/// one too long — is measured as before, and the rows are the measured path's, line for line.
/// </summary>
public class TextAreaAsciiFitsTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    // Every printable ASCII character Advance wide, a tab four, anything else one and a half; a
    // record of the lines measured whole.
    private sealed class Mono(bool proven) : NullRenderContext, IRenderContext
    {
        public const float Advance = 7f;
        public readonly List<string> Measured = [];

        public static float Width(ReadOnlySpan<char> text)
        {
            float w = 0;
            foreach (char c in text) w += c == '\t' ? 4 * Advance : c is >= ' ' and <= '~' ? Advance : 1.5f * Advance;
            return w;
        }

        public override Size MeasureText(string text, ResolvedInheritedStyle style) => new(Width(text), style.FontSize);

        public float MeasureTextWidth(ReadOnlySpan<char> text, ResolvedInheritedStyle style)
        {
            Measured.Add(text.ToString());
            return Width(text);
        }

        public float MonospaceAsciiAdvance(ResolvedInheritedStyle style) => proven ? Advance : 0f;
    }

    private TextAreaElement Mount(string text, IRenderContext ctx, bool fitsByCount = true)
    {
        var area = TextAreaElement.New(text);
        area.FitsByCount = fitsByCount;
        area.Inherited.FontSize = 12;   // a first guess of 6.6 a character: every line here is tried whole
        area.Box.Width = 700;
        area.Box.Height = 300;
        var root = BoxElement.New();
        root.Box.Width = 900;
        root.Box.Height = 400;
        root.AddChild(area);
        StyleResolver.Resolve(root, Theme.Light.ToResolvedStyle());
        _layout.PerformLayout(root, 900, 400);
        // The context a frame would give it, with no frame: no pass at the area's own width.
        typeof(TextAreaElement).GetField("_renderCtx", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(area, ctx);
        return area;
    }

    private static string Ascii(int length, int seed)
    {
        var chars = new char[length];
        for (int i = 0; i < length; i++) chars[i] = (char)(' ' + (i * 31 + seed) % 95);
        return new string(chars);
    }

    [Fact]
    public void ALineOfPrintableAsciiThatFitsWithACharacterToSpare_IsNotMeasured()
    {
        string[] lines =
        [
            "short",
            Ascii(98, 1),
            Ascii(99, 2),                 // 100 characters' room: one to spare
            Ascii(100, 3),                // exactly the width: measured, fits
            Ascii(60, 4) + "\t" + "x",    // a tab
            Ascii(30, 5) + "café",   // outside printable ASCII
            Ascii(40, 6) + "\r",          // a carriage return left at a line's end
            "",
        ];
        var ctx = new Mono(proven: true);
        var area = Mount(string.Join('\n', lines), ctx);
        var style = area.ResolvedInherited;
        var starts = area.GetLineStarts(700f, style);

        Assert.Equal([lines[3], lines[4], lines[5], lines[6]], ctx.Measured);
        Assert.Equal(lines.Length, starts.Count);   // every line fits: one row each

        var reference = Mount(string.Join('\n', lines), new Mono(proven: true), fitsByCount: false);
        Assert.Equal(reference.GetLineStarts(700f, style), starts);
    }

    [Fact]
    public void AFaceNotProvenMonospace_MeasuresEveryLine()
    {
        var lines = Enumerable.Range(0, 40).Select(i => Ascii(10 + i, i)).ToArray();
        var ctx = new Mono(proven: false);
        var area = Mount(string.Join('\n', lines), ctx);
        area.GetLineStarts(700f, area.ResolvedInherited);
        Assert.Equal(lines, ctx.Measured);
    }

    /// <summary>A line taken as fitting is not measured, so it has no width kept: when a
    /// narrower width leaves it no character to spare, it is measured then.</summary>
    [Fact]
    public void ALineTakenAsFitting_IsMeasuredWhenANarrowerWidthLeavesNoCharacterToSpare()
    {
        string[] lines = [Ascii(50, 1), Ascii(80, 2), Ascii(20, 3)];
        var ctx = new Mono(proven: true);
        var area = Mount(string.Join('\n', lines), ctx);
        var style = area.ResolvedInherited;
        area.GetLineStarts(700f, style);
        Assert.Empty(ctx.Measured);
        area.GetLineStarts(7f * 51, style);           // 51 characters' room: line 1 fits, by count
        Assert.Empty(ctx.Measured);
        area.GetLineStarts(7f * 50.5f, style);        // 50.5: no character to spare
        Assert.Equal([lines[0]], ctx.Measured);
    }

    /// <summary>
    /// The rows are the measured path's: random documents of printable ASCII lines of every
    /// length, some with a tab or a character outside printable ASCII, at widths that land
    /// exactly on a character's edge, a hair either side of one, and between.
    /// </summary>
    [Fact]
    public void TheRows_AreTheMeasuredPathsAtEveryWidth()
    {
        var rng = new Random(11);
        var sb = new StringBuilder();
        for (int i = 0; i < 400; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(Ascii(rng.Next(0, 140), i));
            if (rng.Next(10) == 0) sb.Append('\t').Append(Ascii(rng.Next(1, 20), i + 1));
            if (rng.Next(12) == 0) sb.Append('é');
            if (rng.Next(6) == 0) sb.Append(' ').Append(Ascii(rng.Next(1, 30), i + 2));
        }
        string text = sb.ToString();
        var area = Mount(text, new Mono(proven: true));
        var reference = Mount(text, new Mono(proven: true), fitsByCount: false);
        var style = area.ResolvedInherited;
        int compared = 0;
        foreach (float chars in new[] { 3f, 9.5f, 20f, 40f, 40.01f, 39.99f, 63f, 64f, 64.5f, 99f, 100f, 101f, 140f, 141f })
        {
            float width = chars * Mono.Advance;
            var expected = reference.GetLineStarts(width, style);
            var actual = area.GetLineStarts(width, style);
            Assert.True(expected.SequenceEqual(actual), $"at {chars} characters' width the rows differ");
            compared++;
        }
        Assert.Equal(14, compared);
    }
}
