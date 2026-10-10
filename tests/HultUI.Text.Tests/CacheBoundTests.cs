using HultUI.Core.Layout;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// An application keeps one pipeline for its whole life, so its caches keep what they hold after
/// the text that filled them is gone. Each is bounded by an estimate of the bytes its entries
/// keep alive as well as by a count: a long document laid out line by line — what a TextArea
/// does when it opens one — must leave no more than the bounds behind, while a long list of
/// ordinary labels still comes out of the cache whole.
/// </summary>
public class CacheBoundTests
{
    private static readonly ResolvedInheritedStyle Style = new(
        FontFamily: "TestFont", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    // 3,000 lines of ~1,000 characters, 16 bytes a glyph: ~48 MB of glyphs each way. A spaced
    // line is many words and one shaping; a line of numbers and commas is one "word" too.
    private static IEnumerable<string> Document()
    {
        var rng = new Random(5);
        for (int i = 0; i < 3_000; i++)
        {
            var line = new System.Text.StringBuilder();
            line.Append(i).Append(i % 2 == 0 ? ' ' : ',');
            while (line.Length < 1_000)
                line.Append((char)('a' + rng.Next(26))).Append(rng.Next(1_000)).Append(i % 2 == 0 ? ' ' : ',');
            yield return line.ToString();
        }
    }

    [Fact]
    public void ALongDocument_LeavesEachCacheWithinItsBound()
    {
        var pipeline = new TextPipeline(new StubFontCollection(), new StubTextShaper());
        foreach (var line in Document())
        {
            pipeline.Layout(line, Style, float.PositiveInfinity, 1);   // measured whole: shape and word caches
            pipeline.Layout(line, Style, 640f, null);                  // wrapped: the layout cache
        }

        Check(pipeline.ShapeCacheUse, TextPipeline.ShapeCacheBytes, 3_000, "shape");
        Check(pipeline.WordCacheUse, TextPipeline.WordCacheBytes, 8_192, "word");
        Check(pipeline.LayoutCacheUse, TextPipeline.LayoutCacheBytes, 2_048, "layout");

        // Within the bound, near it, and the bound is what stopped it: by the entry counts alone
        // (every line laid out, or the 8,192 and 2,048 the word and layout caches may hold)
        // each would hold ten times its bound or more.
        static void Check((int Entries, long Bytes) use, long bound, int entries, string name)
        {
            Assert.True(use.Bytes <= bound, $"{name} cache holds {use.Bytes:N0} bytes, over its {bound:N0}");
            Assert.True(use.Bytes > bound / 2, $"{name} cache holds only {use.Bytes:N0} bytes of {bound:N0}");
            Assert.True(use.Entries < entries, $"{name} cache kept {use.Entries:N0} entries");
        }
    }

    [Fact]
    public void AnEntrysWeight_IsAtLeastTheGlyphsItKeeps()
    {
        // The estimate is what the bound counts: a line of 1,000 glyphs must weigh 16 KB or
        // more in each cache it lands in, or a document could fill memory under the bound.
        var pipeline = new TextPipeline(new StubFontCollection(), new StubTextShaper());
        string line = string.Concat(Enumerable.Repeat("word ", 199)) + "words";
        string csv = string.Join(',', Enumerable.Range(0, 250).Select(i => i.ToString("000")));

        pipeline.Layout(line, Style, float.PositiveInfinity, 1);
        Assert.InRange(pipeline.ShapeCacheUse.Bytes, 16 * line.Length, 24 * line.Length + 1_000);

        pipeline.Layout(line, Style, 640f, null);
        Assert.InRange(pipeline.LayoutCacheUse.Bytes, 16 * line.Length, 24 * line.Length + 4_000);

        long words = pipeline.WordCacheUse.Bytes;
        pipeline.Layout(csv, Style, float.PositiveInfinity, 1);
        Assert.InRange(pipeline.WordCacheUse.Bytes - words, 16 * csv.Length, 20 * csv.Length + 1_000);

        // The layout cache's other two ways in: styled text, and text on the BiDi path.
        long layouts = pipeline.LayoutCacheUse.Bytes;
        var bold = new[] { new HultUI.Core.Rendering.TextSpan(0, 4, HultUI.Core.Primitives.Color.Black, Weight: FontWeight.Bold) };
        pipeline.Layout(line, Style, 640f, null, TextOverflow.Clip, bold);
        Assert.InRange(pipeline.LayoutCacheUse.Bytes - layouts, 16 * line.Length, 24 * line.Length + 4_000);

        layouts = pipeline.LayoutCacheUse.Bytes;
        string hebrew = string.Concat(Enumerable.Repeat(((char)0x05E9).ToString() + (char)0x05DC + (char)0x05D5 + (char)0x05DD + " ", 200));
        pipeline.Layout(hebrew, Style, 640f, null);
        Assert.InRange(pipeline.LayoutCacheUse.Bytes - layouts, 16 * hebrew.Length - 4_000, 24 * hebrew.Length + 4_000);
    }

    [Fact]
    public void FiveThousandLabels_StillComeOutOfTheCache_AfterADocument()
    {
        // The largest list the perf suite lays out in one go is 5,000 rows. With the caches
        // filled to their bounds by a document first, every label of such a list must still be
        // there for the next pass: the same layout back, nothing shaped.
        var shaper = new CountingShaper();
        var pipeline = new TextPipeline(new StubFontCollection(), shaper);
        foreach (var line in Document().Take(1_000))
            pipeline.Layout(line, Style, float.PositiveInfinity, 1);
        Assert.True(pipeline.ShapeCacheUse.Bytes > TextPipeline.ShapeCacheBytes / 2);

        var labels = Enumerable.Range(0, 5_000).Select(i => $"Unique label {i} with a few more words").ToArray();
        var first = labels.Select(l => pipeline.Layout(l, Style, float.PositiveInfinity, 1)).ToArray();
        int shaped = shaper.Calls;

        for (int i = 0; i < labels.Length; i++)
            Assert.Same(first[i], pipeline.Layout(labels[i], Style, float.PositiveInfinity, 1));
        Assert.Equal(shaped, shaper.Calls);
    }

    [Fact]
    public void ADocumentOfOrdinaryLines_StaysWholeForTheNextKeystroke()
    {
        // A TextArea measures every line that fits again on each keystroke, and finds it in the
        // shape cache. As many lines as the count bound allows, of 55 characters, must all
        // still be there the next time round.
        var pipeline = new TextPipeline(new StubFontCollection(), new StubTextShaper());
        var lines = Enumerable.Range(0, 16_384)
            .Select(i => $"    \"field_{i:00000}\": \"value {i * 7919 % 1_000_000:000000} with some words here\",")
            .ToArray();
        Assert.All(lines, l => Assert.Equal(55, l.Length));
        var first = lines.Select(l => pipeline.Layout(l, Style, float.PositiveInfinity, 1)).ToArray();

        for (int i = 0; i < lines.Length; i++)
            Assert.Same(first[i], pipeline.Layout(lines[i], Style, float.PositiveInfinity, 1));
    }

    [Fact]
    public void ADocumentInOneLabel_StaysCachedBesideTheLabelsAroundIt()
    {
        // A whole document in one wrapped label lays out to more than the layout cache's bound.
        // It counts for half of it, so the labels drawn around it every frame do not push it
        // out — or it them, frame after frame.
        var pipeline = new TextPipeline(new StubFontCollection(), new StubTextShaper());
        string document = string.Join('\n', Document().Take(300));
        var labels = Enumerable.Range(0, 200).Select(i => $"Label {i} that wraps at a narrow width").ToArray();
        var whole = pipeline.Layout(document, Style, 640f, null);
        Assert.True(pipeline.LayoutCacheUse.Bytes >= TextPipeline.LayoutCacheBytes / 2);

        TextLayout[]? previous = null;
        for (int frame = 0; frame < 3; frame++)
        {
            Assert.Same(whole, pipeline.Layout(document, Style, 640f, null));
            var laidOut = labels.Select(l => pipeline.Layout(l, Style, 120f, null)).ToArray();
            if (previous is not null)
                for (int i = 0; i < labels.Length; i++)
                    Assert.Same(previous[i], laidOut[i]);
            previous = laidOut;
        }
        Assert.True(pipeline.LayoutCacheUse.Bytes <= TextPipeline.LayoutCacheBytes);
    }

    [Fact]
    public void ADocumentsWidths_LeaveTheWidthCacheWithinItsBound()
    {
        // Each line measured twice, the most a TextArea measures one: kept from the second time.
        var pipeline = new TextPipeline(new StubFontCollection(), new StubTextShaper());
        foreach (var text in Document())
        {
            pipeline.MeasureWidth(text, Style);
            pipeline.MeasureWidth(text, Style);
        }
        var use = pipeline.WidthCacheUse;
        Assert.True(use.Bytes <= TextPipeline.WidthCacheBytes, $"width cache holds {use.Bytes:N0} bytes");
        Assert.True(use.Bytes > TextPipeline.WidthCacheBytes / 2, $"width cache holds only {use.Bytes:N0} bytes");
        Assert.True(use.Entries < 3_000, $"width cache kept {use.Entries:N0} entries");

        // An entry weighs at least the characters of its key.
        var one = new TextPipeline(new StubFontCollection(), new StubTextShaper());
        string line = Document().First();
        one.MeasureWidth(line, Style);
        one.MeasureWidth(line, Style);
        Assert.InRange(one.WidthCacheUse.Bytes, 2 * line.Length, 2 * line.Length + 400);
    }

    /// <summary>
    /// A width is kept while the cache has room. Once it is full only a line measured lately is
    /// — most lines of a document are measured once — so it keeps the lines that repeat, among
    /// any number that do not. A line found in it is not itemized again, which is all its fonts
    /// are asked for.
    /// </summary>
    [Fact]
    public void AFullWidthCache_KeepsTheLinesThatRepeat_AmongAnyNumberThatDoNot()
    {
        var fonts = new CountingFonts();
        var pipeline = new TextPipeline(fonts, new StubTextShaper());
        pipeline.MeasureWidth("measured once, with room", Style);
        Assert.Equal(1, pipeline.WidthCacheUse.Entries);

        // Full: a document of long lines, each measured once.
        foreach (var text in Document())
            pipeline.MeasureWidth(text, Style);
        Assert.True(pipeline.WidthCacheUse.Bytes > TextPipeline.WidthCacheBytes - 4_000);

        var repeated = Enumerable.Range(0, 50).Select(i => $"      \"field_{i}\": false,").ToArray();
        var rng = new Random(9);
        for (int i = 0; i < 19_000; i++)
            pipeline.MeasureWidth(i % 4 == 0 ? repeated[rng.Next(repeated.Length)] : $"      \"id\": {i * 7919},", Style);
        int asked = fonts.Resolves;
        foreach (var line in repeated)
            pipeline.MeasureWidth(line, Style);
        Assert.Equal(asked, fonts.Resolves);

        // Longer than any room an eviction leaves: not kept the first time, kept the second.
        string fresh = string.Concat(Enumerable.Repeat("measured once when full ", 90));
        pipeline.MeasureWidth(fresh, Style);
        asked = fonts.Resolves;
        pipeline.MeasureWidth(fresh, Style);
        Assert.True(fonts.Resolves > asked, "a line measured once was kept in a full cache");
        asked = fonts.Resolves;
        pipeline.MeasureWidth(fresh, Style);
        Assert.Equal(asked, fonts.Resolves);
    }

    [Fact]
    public void TheWidthCache_IsForgotten_WhenTheUICultureChanges()
    {
        // A width is of the faces the culture picks for Han characters without kana.
        var culture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            var fonts = new CountingFonts();
            var pipeline = new TextPipeline(fonts, new StubTextShaper());
            pipeline.MeasureWidth("東京", Style);
            pipeline.MeasureWidth("東京", Style);
            Assert.Equal(1, pipeline.WidthCacheUse.Entries);
            int asked = fonts.Resolves;
            pipeline.MeasureWidth("東京", Style);
            Assert.Equal(asked, fonts.Resolves);   // found

            System.Globalization.CultureInfo.CurrentUICulture = culture.Name == "ja-JP" ? new("zh-CN") : new("ja-JP");
            pipeline.MeasureWidth("東京", Style);
            Assert.True(fonts.Resolves > asked, "the width from the other culture was found");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = culture;
        }
    }

    private sealed class CountingFonts : IFontCollection
    {
        private readonly StubFontCollection _inner = new();
        public int Resolves;

        public IResolvedFont Resolve(string familyName, FontWeight weight, FontSlant slant)
        {
            Resolves++;
            return _inner.Resolve(familyName, weight, slant);
        }

        public IResolvedFont? MatchCharacter(int codepoint, FontWeight weight, FontSlant slant) =>
            _inner.MatchCharacter(codepoint, weight, slant);
    }

    private sealed class CountingShaper : ITextShaper
    {
        private readonly StubTextShaper _inner = new();
        public int Calls;

        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
        {
            Calls++;
            return _inner.Shape(text, font, fontSize, rtl);
        }
    }
}
