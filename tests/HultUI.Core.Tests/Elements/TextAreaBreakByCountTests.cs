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
/// (<see cref="IRenderContext.MonospaceAsciiAdvance"/>), a row of a line too long to fit breaks
/// where its count says — floor(width / advance) characters, walked back to the last space —
/// instead of searching measured prefixes: a 1 MB minified script built a prefix table and
/// shaped words for each of its 12,525 rows. A row whose first n + 1 characters are not all
/// printable ASCII, or a width within the proof's rounding of a multiple of the advance, is
/// searched as before, and the rows are the measured path's either way.
/// </summary>
public class TextAreaBreakByCountTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    // Every printable ASCII character Advance wide, anything else one and a half; a record of
    // the prefixes the search measured (MeasureSubstring asks MeasureText).
    private sealed class Mono : NullRenderContext, IRenderContext
    {
        public const float Advance = 7f;
        public readonly List<string> Searched = [];

        private static float Width(ReadOnlySpan<char> text)
        {
            float w = 0;
            foreach (char c in text) w += c is >= ' ' and <= '~' ? Advance : 1.5f * Advance;
            return w;
        }

        public override Size MeasureText(string text, ResolvedInheritedStyle style)
        {
            Searched.Add(text);
            return new(Width(text), style.FontSize);
        }

        public float MeasureTextWidth(ReadOnlySpan<char> text, ResolvedInheritedStyle style) => Width(text);

        public float MonospaceAsciiAdvance(ResolvedInheritedStyle style) => Advance;
    }

    private TextAreaElement Mount(string text, IRenderContext ctx, bool fitsByCount = true)
    {
        var area = TextAreaElement.New(text);
        area.FitsByCount = fitsByCount;
        area.Inherited.FontSize = 12;
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

    private static readonly FieldInfo LinesValid =
        typeof(TextAreaElement).GetField("_linesValid", BindingFlags.NonPublic | BindingFlags.Instance)!;

    // The rows at `width`, wrapped for it: GetLineStarts hands back the rows it has for any width
    // within half a pixel of theirs, so a width a hair from the last would get the last one's.
    private static int[] Rows(TextAreaElement area, float width, ResolvedInheritedStyle style)
    {
        LinesValid.SetValue(area, false);
        return area.GetLineStarts(width, style).ToArray();
    }

    private static string Ascii(int length, int seed)
    {
        var chars = new char[length];
        for (int i = 0; i < length; i++) chars[i] = (char)(' ' + (i * 31 + seed) % 95);
        return new string(chars);
    }

    // Words of 1 to 14 characters with one space between, and now and then a run of 150 with
    // none, which breaks between characters.
    private static string Code(Random rng, int length)
    {
        var sb = new StringBuilder(length + 160);
        while (sb.Length < length)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(Ascii(rng.Next(12) == 0 ? 150 : rng.Next(1, 15), rng.Next(95)).Replace(' ', '_'));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Long lines of printable ASCII: at widths between multiples of the advance every row breaks
    /// by its count and nothing is measured; on a multiple and a hair either side of one the rows
    /// are searched. The rows are the measured path's at every width.
    /// </summary>
    [Fact]
    public void AWrappedAsciiRow_BreaksWhereTheMeasuredPathBreaks_OnAndNearMultiplesOfTheAdvance()
    {
        var rng = new Random(6);
        string text = string.Join('\n', Code(rng, 3_000), Code(rng, 800), "short", Code(rng, 5_000), "");
        var ctx = new Mono();
        var area = Mount(text, ctx);
        var reference = Mount(text, new Mono(), fitsByCount: false);
        var style = area.ResolvedInherited;

        int byCount = 0, searched = 0;
        // From 7 characters on, a hundredth of a pixel is inside the rounding the proof allows.
        foreach (float chars in new[] { 7f, 20f, 40f, 63f, 64f, 99f, 100f, 101f, 149f, 150f, 151f, 160f })
            foreach (float nudge in new[] { -0.5f, -0.01f, -0.001f, -0.0001f, 0f, 0.0001f, 0.001f, 0.01f, 0.5f })
            {
                float width = chars * Mono.Advance + nudge;
                var expected = Rows(reference, width, style);
                ctx.Searched.Clear();
                var actual = Rows(area, width, style);
                Assert.True(expected.AsSpan().SequenceEqual(actual), $"at {width} ({chars} characters' width {nudge:+0.####;-0.####;0}) the rows differ");
                Assert.True(expected.Length > text.Length / (chars + 1) / 2, $"at {width} too few rows wrapped");
                if (Math.Abs(nudge) == 0.5f)
                {
                    // Half a character from any multiple: every row by its count.
                    Assert.True(ctx.Searched.Count == 0, $"at {width} {ctx.Searched.Count} prefixes were measured");
                    byCount++;
                }
                else
                {
                    // Within the rounding of a multiple: the search, as before.
                    Assert.True(ctx.Searched.Count > 0, $"at {width}, a hair from a multiple, nothing was measured");
                    searched++;
                }
            }
        Assert.Equal(24, byCount);
        Assert.Equal(84, searched);
    }

    /// <summary>
    /// A row breaks by its count only when the n + 1 characters that settle it are printable
    /// ASCII: an é among them — anywhere up to and including the (n + 1)th — sends the row to
    /// the search; one after them leaves the row to its count and the next row, which holds it,
    /// is searched.
    /// </summary>
    [Fact]
    public void ARowWithANonAsciiCharacterInItsFirstNPlusOne_TakesTheSearch()
    {
        const int n = 60;
        float width = n * Mono.Advance + 3f;   // 60 characters and three sevenths: by count, 60
        foreach (var (at, searchedFromTheStart) in new[] { (10, true), (n - 1, true), (n, true), (n + 1, false), (n + 30, false) })
        {
            string text = Ascii(at, 3).Replace(' ', '_') + "é" + Ascii(400, 5);
            var ctx = new Mono();
            var area = Mount(text, ctx);
            var style = area.ResolvedInherited;
            var rows = area.GetLineStarts(width, style);
            var reference = Mount(text, new Mono(), fitsByCount: false).GetLineStarts(width, style);
            Assert.True(reference.SequenceEqual(rows), $"with é at {at} the rows differ");

            // The first row's search measures prefixes of the line from its start; no other row starts there.
            bool firstRowSearched = ctx.Searched.Any(s => s.Length > 1 && text.StartsWith(s, StringComparison.Ordinal));
            Assert.True(firstRowSearched == searchedFromTheStart,
                $"with é at {at} the first row was {(firstRowSearched ? "" : "not ")}searched");
            Assert.True(ctx.Searched.Count > 0, $"with é at {at} the row that holds it was not searched");
        }
    }

    /// <summary>A width that is not a number, zero, negative or under one advance never breaks
    /// by count (the search keeps its one-character floor; a count of none would never move on)
    /// and wraps as the measured path does, without throwing.</summary>
    [Fact]
    public void ANaNZeroOrSubCharacterWidth_TakesTheSearch_AndDoesNotThrow()
    {
        string text = Ascii(300, 1) + "\n" + Ascii(50, 2);
        var area = Mount(text, new Mono());
        var reference = Mount(text, new Mono(), fitsByCount: false);
        var style = area.ResolvedInherited;
        var breakByCount = typeof(TextAreaElement).GetMethod("BreakByCount", BindingFlags.NonPublic | BindingFlags.Instance)!;
        float[] widths = [float.NaN, 0f, -0f, -50f, float.NegativeInfinity, 0.5f * Mono.Advance, 0.999f * Mono.Advance];

        // Asked first: a count of none under the guard's reverse would wrap forever below.
        foreach (float width in widths)
            Assert.Equal(-1, (int)breakByCount.Invoke(area, [0, 300, width, Mono.Advance])!);
        Assert.Equal(-1, (int)breakByCount.Invoke(area, [0, 300, 7f * 40, float.NaN])!);

        foreach (float width in widths)
            Assert.True(Rows(reference, width, style).AsSpan().SequenceEqual(Rows(area, width, style)), $"at {width} the rows differ");
        // One advance and a hair: by count, one character a row.
        Assert.Equal(1, (int)breakByCount.Invoke(area, [0, 300, 1.25f * Mono.Advance, Mono.Advance])!);
    }
}
