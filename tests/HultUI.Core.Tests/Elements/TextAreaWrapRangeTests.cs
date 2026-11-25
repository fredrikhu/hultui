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
/// A hard line that wraps keeps the widths its rows hold for — those at which each row's longest
/// fitting prefix, searched or by its count, walks back to the same space (or, with none, is the
/// same) — and at another width inside them takes its rows from the last width's instead of
/// being searched again. A 1 px resize drag searched every wrapped line
/// of a document at each step, though 92–97 % of them kept their rows. These hold a resize inside
/// every line's range to searching none, the rows to a fresh pass's, through edits that move the
/// lines kept, and a line whose prefixes measure narrower as they grow near its break to keeping
/// no range.
/// </summary>
public class TextAreaWrapRangeTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    // Whole-pixel widths that differ from letter to letter (a space 3, a to z 4 to 8, anything else
    // 6), so a prefix's width is the sum of its characters' and grows with its length, and a width
    // a quarter of a pixel off a whole one is inside every searched row's range. Except: Ψ before ψ
    // measures 3 and ψ 5, where Ψ alone measures 12 — the prefix ending in ψ is narrower than the
    // one ending in Ψ, as Arabic joining forms or a kerning pair can make it. With an advance,
    // printable ASCII is proven monospace at it and anything else measures one and a half.
    private sealed class Widths(float advance) : NullRenderContext, IRenderContext
    {
        public int Prefixes;

        public float Width(ReadOnlySpan<char> text)
        {
            float w = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                w += advance > 0
                    ? c is >= ' ' and <= '~' ? advance : 1.5f * advance
                    : c == ' ' ? 3 : c is >= 'a' and <= 'z' ? 4 + (c - 'a') % 5
                    : c == 'Ψ' ? (i + 1 < text.Length && text[i + 1] == 'ψ' ? 3 : 12)
                    : c == 'ψ' ? 5 : 6;
            }
            return w;
        }

        public override Size MeasureText(string text, ResolvedInheritedStyle style) => new(Width(text), style.FontSize);

        public ITextPrefixWidths? MeasureTextPrefixes(string text, int start, int length, ResolvedInheritedStyle style)
        {
            Prefixes++;
            return new Table(this, text.Substring(start, length));
        }

        private sealed class Table(Widths owner, string text) : ITextPrefixWidths
        {
            public int Length => text.Length;
            public float Width(int length) => owner.Width(text.AsSpan(0, length));
        }

        public float MeasureTextWidth(ReadOnlySpan<char> text, ResolvedInheritedStyle style) => Width(text);

        public float MonospaceAsciiAdvance(ResolvedInheritedStyle style) => advance;
    }

    private TextAreaElement Mount(string text, IRenderContext ctx, bool ranges = true)
    {
        var area = TextAreaElement.New(text);
        area.KeepsWrapRanges = ranges;
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
    private static int[] Rows(TextAreaElement area, float width)
    {
        LinesValid.SetValue(area, false);
        return area.GetLineStarts(width, area.ResolvedInherited).ToArray();
    }

    // Whether hard line `line` keeps a range of widths.
    private static bool KeepsARange(TextAreaElement area, int line)
    {
        var ranges = (Array)typeof(TextAreaElement).GetField("_lineRanges", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(area)!;
        object range = ranges.GetValue(line)!;
        float lo = (float)range.GetType().GetProperty("Lo")!.GetValue(range)!;
        float hi = (float)range.GetType().GetProperty("Hi")!.GetValue(range)!;
        return lo < hi;
    }

    // Paragraphs of words, now and then an é (which a monospace count does not take), and short
    // lines between that fit.
    private static string Document(int seed)
    {
        var rng = new Random(seed);
        var sb = new StringBuilder();
        for (int p = 0; p < 40; p++)
        {
            int length = rng.Next(200, 1_600);
            int start = sb.Length;
            while (sb.Length - start < length)
            {
                for (int k = rng.Next(1, 12); k > 0; k--) sb.Append((char)('a' + rng.Next(26)));
                if (rng.Next(30) == 0) sb.Append('é');
                sb.Append(' ');
            }
            sb.Append('\n').Append("short line ").Append(p).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// A resize to a width inside every line's range — a quarter of a pixel, between the same two
    /// whole widths — searches no line, and the rows are a fresh pass's; then a drag of a quarter
    /// pixel a step (a 1 px drag at 150 % moves the width two thirds of one) searches
    /// fewer lines than a pass keeping no ranges does, with the same rows at every width. With
    /// printable ASCII proven monospace too, where rows break by their count and those holding an é
    /// are searched.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(7f)]
    public void AResizeInsideEveryLinesRange_SearchesNoLineAgain(float advance)
    {
        string text = Document(3);
        var area = Mount(text, new Widths(advance));
        var fresh = Mount(text, new Widths(advance), ranges: false);

        Rows(area, 400.25f);   // the open: no range kept
        Rows(area, 397.25f);   // the first resize: every wrapped line searched, each keeping its range
        int searched = area.LinesWrapped;
        Assert.Equal(80, searched);   // 40 paragraphs, twice

        // Prefix widths are whole (or half) pixels: nothing measures between 397.25 and 397.375.
        var rows = Rows(area, 397.375f);
        Assert.Equal(searched, area.LinesWrapped);
        Assert.Equal(Rows(fresh, 397.375f), rows);
        Assert.True(rows.Length > 400, $"only {rows.Length} rows");

        int ranged = 0, all = 0;
        for (float width = 397.125f; width > 387f; width -= 0.25f)
        {
            int before = area.LinesWrapped, freshBefore = fresh.LinesWrapped;
            Assert.Equal(Rows(fresh, width), Rows(area, width));
            ranged += area.LinesWrapped - before;
            all += fresh.LinesWrapped - freshBefore;
        }
        Assert.True(ranged < all / 2, $"the drag searched {ranged} lines against {all} keeping no ranges");
    }

    /// <summary>
    /// An edit keeps the ranges of the lines it did not touch, with their rows: those after it move
    /// by the characters it added. A resize inside every range then searches only the line the
    /// edit touched (an edit's pass keeps no range: typing pays for none), and one that comes with
    /// an edit only the lines that edit touched; the rows are a fresh pass's.
    /// </summary>
    [Fact]
    public void AnEdit_KeepsTheOtherLinesRanges_WithTheirRowsMoved()
    {
        string text = Document(5);
        var area = Mount(text, new Widths(0f));
        var fresh = Mount(text, new Widths(0f), ranges: false);
        Rows(area, 500.25f);
        Rows(area, 497.25f);

        // A word typed into the 21st paragraph, at the same width: that line searched again.
        int at = 0;
        for (int line = 0; line < 40; line++) at = text.IndexOf('\n', at) + 1;
        at += 3;
        string edited = text.Insert(at, "inserted words ");
        area.Text = edited;
        fresh.Text = edited;
        int before = area.LinesWrapped;
        Rows(area, 497.25f);
        Assert.Equal(1, area.LinesWrapped - before);

        before = area.LinesWrapped;
        Assert.Equal(Rows(fresh, 497.375f), Rows(area, 497.375f));
        Assert.Equal(1, area.LinesWrapped - before);
        before = area.LinesWrapped;
        Assert.Equal(Rows(fresh, 497.5f), Rows(area, 497.5f));
        Assert.Equal(0, area.LinesWrapped - before);

        // Another edit with a resize in the same pass: a line added before the 31st paragraph.
        int at2 = 0;
        for (int line = 0; line < 60; line++) at2 = edited.IndexOf('\n', at2) + 1;
        string edited2 = edited.Insert(at2, "a new short line\n");
        area.Text = edited2;
        fresh.Text = edited2;
        before = area.LinesWrapped;
        Assert.Equal(Rows(fresh, 497.625f), Rows(area, 497.625f));
        Assert.Equal(0, area.LinesWrapped - before);   // the line added fits

        // And the rows of a narrower width still a fresh pass's, line by line searched or taken.
        for (float width = 496.5f; width > 470f; width -= 1f)
            Assert.Equal(Rows(fresh, width), Rows(area, width));
    }

    /// <summary>
    /// Where a prefix one cluster longer measures narrower next to the break — Ψψ — a search from
    /// another guess could stop at the other place where a prefix fits and the next does not: the
    /// line keeps no range and is searched again at the next width, while a line like it without
    /// the pair keeps its range. The rows are a fresh pass's either way.
    /// </summary>
    [Fact]
    public void ALineWhosePrefixesShrinkNearTheBreak_KeepsNoRange()
    {
        // 'a' measures 4: 99 of them, Ψψ, then 30 more. Prefixes: 396 (99), 408 (Ψ alone), 404 (Ψψ),
        // 408, 412, ... At 406: 99 fits and 100 does not, and 101 fits and 102 does not.
        string run = new('a', 99);
        string text = run + "Ψψ" + new string('a', 30) + "\n" + run + "bb" + new string('a', 30);
        var ctx = new Widths(0f);
        var area = Mount(text, ctx);
        var fresh = Mount(text, new Widths(0f), ranges: false);
        Rows(area, 405.75f);
        Assert.Equal(Rows(fresh, 406.25f), Rows(area, 406.25f));
        Assert.False(KeepsARange(area, 0), "the line with Ψψ at its break kept a range");
        Assert.True(KeepsARange(area, 1), "the line without it kept none");

        int before = area.LinesWrapped;
        Assert.Equal(Rows(fresh, 406.5f), Rows(area, 406.5f));
        Assert.Equal(1, area.LinesWrapped - before);
    }

    /// <summary>The reference path keeps no range and searches every line that wraps at every
    /// width, as before.</summary>
    [Fact]
    public void WithoutRanges_EveryWrappedLineIsSearchedAtEveryWidth()
    {
        string text = Document(7);
        var fresh = Mount(text, new Widths(0f), ranges: false);
        Rows(fresh, 400.25f);
        int once = fresh.LinesWrapped;
        Rows(fresh, 400.375f);
        Assert.Equal(2 * once, fresh.LinesWrapped);
    }
}
