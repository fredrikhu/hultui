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

    // ── The BiDi and styled memos ──

    // Paragraphs of about `length` characters, a third of their words Hebrew (or, styled, Latin
    // with a bold first word), each laid out at 2,000, 2,000 and 1,990 pixels (lines of some 200
    // characters): an entry at its second layout, lines remembered at the third.
    private static void LayOutParagraphs(TextPipeline pipeline, int count, int length, bool styled, int seed = 5)
    {
        var rng = new Random(seed);
        var style = styled ? Style : Style with { TextDirection = TextDirection.Rtl };
        var bold = new[] { new HultUI.Core.Rendering.TextSpan(0, 5, HultUI.Core.Primitives.Color.Black, Weight: FontWeight.Bold) };
        for (int i = 0; i < count; i++)
        {
            var sb = new System.Text.StringBuilder();
            while (sb.Length < length)
            {
                bool hebrew = !styled && rng.Next(3) == 0;
                for (int k = 2 + rng.Next(7); k > 0; k--)
                    sb.Append(hebrew ? (char)(0x05D0 + rng.Next(27)) : (char)('a' + rng.Next(26)));
                sb.Append(' ');
            }
            string text = sb.ToString();
            foreach (float width in new[] { 2_000f, 2_000f, 1_990f })
                pipeline.Layout(text, style, width, null, TextOverflow.Clip, styled ? bold : null);
        }
    }

    /// <summary>
    /// What the BiDi and styled paths keep of a string between widths — its entry (levels,
    /// breaks, segment widths, the line of the whole text) and the lines it was cut into — is
    /// bounded by bytes too. By their counts alone, 4,096 paragraphs of 2,000 characters kept
    /// ~58 MB of entries.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LongParagraphs_LeaveTheMemosWithinTheirBounds(bool styled)
    {
        var pipeline = new TextPipeline(new StubFontCollection(), new StubTextShaper());
        LayOutParagraphs(pipeline, 1_500, 2_000, styled);

        Check(pipeline.MemoCacheUse, TextPipeline.MemoCacheBytes, 1_500, "memo");
        Check(pipeline.MemoLineCacheUse, TextPipeline.MemoLineCacheBytes, 4_096, "remembered line");

        // Within the bound, near it, and the bound is what stopped it: 1,500 entries and 4,096
        // lines fit the counts.
        static void Check((int Entries, long Bytes) use, long bound, int entries, string name)
        {
            Assert.True(use.Bytes <= bound, $"{name} cache holds {use.Bytes:N0} bytes, over its {bound:N0}");
            Assert.True(use.Bytes > bound / 2, $"{name} cache holds only {use.Bytes:N0} bytes of {bound:N0}");
            Assert.True(use.Entries < entries, $"{name} cache kept {use.Entries:N0} entries");
        }
    }

    [Fact]
    public void AMemosWeight_IsAtLeastWhatItKeeps()
    {
        // An entry keeps its key's text (2 bytes a character) and, on the BiDi path, a level a
        // character; a remembered line 16 bytes a glyph. Weighed less, the bound would let more
        // than it says stay.
        var pipeline = new TextPipeline(new StubFontCollection(), new StubTextShaper());
        var rtl = Style with { TextDirection = TextDirection.Rtl };
        string hebrew = string.Concat(Enumerable.Repeat(((char)0x05E9).ToString() + (char)0x05DC + (char)0x05D5 + (char)0x05DD + " ", 400));
        foreach (float width in new[] { 640f, 630f })
            pipeline.Layout(hebrew, rtl, width, null);
        Assert.Equal(1, pipeline.MemoCacheUse.Entries);
        Assert.InRange(pipeline.MemoCacheUse.Bytes, 3 * hebrew.Length, 12 * hebrew.Length + 2_000);
        Assert.InRange(pipeline.MemoLineCacheUse.Bytes, 16 * hebrew.Length - 4_000, 22 * hebrew.Length + 20_000);

        // The line of the whole text, kept on the entry once a width fits it all, counts too.
        long before = pipeline.MemoCacheUse.Bytes;
        pipeline.Layout(hebrew, rtl, float.PositiveInfinity, null);
        Assert.InRange(pipeline.MemoCacheUse.Bytes - before, 16 * hebrew.Length, 22 * hebrew.Length + 2_000);
    }

    /// <summary>
    /// A width or a line remembered for a range of a string is keyed by its entry's number, not
    /// the entry: an entry the cache has let go is gone, though ranges of it are still there.
    /// Keyed by the entry, each kept its entry alive — levels, breaks, segment widths and the
    /// line of the whole text — for as long as any of its ranges stayed.
    /// </summary>
    [Fact]
    public void AnEntryTheCacheLetGo_IsNotKeptAliveByItsLines()
    {
        var pipeline = new TextPipeline(new StubFontCollection(), new StubTextShaper());
        var entry = FirstEntryOf(pipeline, () => LayOutParagraphs(pipeline, 1, 2_000, styled: false));
        Assert.True(pipeline.MemoLineCacheUse.Entries > 10, "the paragraph's lines were not remembered");

        // 4,096 short labels, each laid out twice, take every entry's place; their lines are
        // their whole texts, kept on their entries, so the paragraph's lines stay remembered.
        int lines = pipeline.MemoLineCacheUse.Entries;
        var rtl = Style with { TextDirection = TextDirection.Rtl };
        for (int i = 0; i < 4_200; i++)
        {
            string label = $"{(char)(0x05D0 + i % 27)}{(char)(0x05D0 + i / 27 % 27)} {i}";
            pipeline.Layout(label, rtl, 600f, null);
            pipeline.Layout(label, rtl, 590f, null);
        }
        Assert.Equal(lines, pipeline.MemoLineCacheUse.Entries);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(entry.IsAlive, "the paragraph's entry was let go by the cache but is still alive");
    }

    /// <summary>
    /// After the last entry number, the numbers start over, and nothing remembered under a number
    /// is found for the entry that has it next: every entry, width and line goes first.
    /// </summary>
    [Fact]
    public void AfterTheLastEntryNumber_NothingRememberedIsFoundForAnotherString()
    {
        var pipeline = new TextPipeline(new StubFontCollection(), new StubTextShaper());
        var reference = new TextPipeline(new StubFontCollection(), new StubTextShaper()) { ReuseAcrossWidths = false };
        var rtl = Style with { TextDirection = TextDirection.Rtl };
        string Hebrew(int seed) => string.Concat(Enumerable.Range(0, 60).Select(i => (char)(0x05D0 + (i * seed + i / 5) % 27) + (i % 5 == 4 ? " " : "")));
        string first = Hebrew(3), next = Hebrew(7), last = Hebrew(11);
        foreach (float width in new[] { 300f, 250f, 200f })
            pipeline.Layout(first, rtl, width, null);           // entry 1, its lines remembered

        typeof(TextPipeline).GetField("_lastMemoId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(pipeline, int.MaxValue - 1);
        foreach (var text in new[] { next, last })
            foreach (float width in new[] { 300f, 250f, 200f })
            {
                var laidOut = pipeline.Layout(text, rtl, width, null);
                var expected = reference.Layout(text, rtl, width, null);
                static string Of(TextLayout layout) =>
                    string.Join('|', layout.Lines.Select(l => $"{l.Start}+{l.Length}:{string.Concat(l.Runs.Select(r => r.Text))}"));
                Assert.Equal(Of(expected), Of(laidOut));
            }
        Assert.Equal(1, pipeline.MemoCacheUse.Entries);   // the last one, numbered 1 again
    }

    // The entry the pipeline's first memo is, weakly: found by reflection, in a frame of its own
    // so no local keeps it.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference FirstEntryOf(TextPipeline pipeline, Action fill)
    {
        fill();
        const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var cache = typeof(TextPipeline).GetField("_memos", Private)!.GetValue(pipeline)!;
        var map = (System.Collections.IDictionary)cache.GetType().GetField("_map", Private)!.GetValue(cache)!;
        foreach (System.Collections.DictionaryEntry e in map)
        {
            var node = e.Value!;
            var entry = node.GetType().GetProperty("Value")!.GetValue(node)!;
            return new WeakReference(entry.GetType().GetProperty("Value")!.GetValue(entry));
        }
        throw new InvalidOperationException("no memo was made");
    }

    /// <summary>
    /// A thousand wrapped labels on the BiDi path, laid out again at two widths, as a list that
    /// is resized: once each has its entry and its lines at those widths, laying them out again
    /// shapes nothing, with the layouts themselves gone from the layout cache. The bounds sit
    /// above what such use fills (and what the perf suite's 5,000-block rich text document
    /// fills, opened twice and resized: see MemoCacheBytes).
    /// </summary>
    [Fact]
    public void WrappedLabelsLaidOutAgain_ShapeNothing()
    {
        var shaper = new CountingShaper();
        var pipeline = new TextPipeline(new StubFontCollection(), shaper);
        // Lines of ~80 characters, a run a word or two: ~2.5 KB each as weighed, a little more
        // than the rich text document's lines, so 4,000 of them weigh ~10 MB.
        string Segment(int i) => $"commit {i:000} {(char)0x05E9}{(char)0x05DC}{(char)0x05D5}{(char)0x05DD} merged by {(char)0x05D0}{(char)0x05D1}{(char)0x05D2} into main";
        var labels = Enumerable.Range(0, 1_000).Select(i => string.Join(' ', Enumerable.Range(0, 4).Select(k => Segment(i + k)))).ToArray();
        void LayOut(string label, float width) => Assert.Equal(2, pipeline.Layout(label, Style, width, null).Lines.Count);
        void Pass(float width)
        {
            ForgetLayouts(pipeline);
            foreach (var label in labels)
                LayOut(label, width);
        }

        // Each label at both widths in a row, so its entry is made at its second layout: with
        // other labels in between, one could take its note first (string hashes differ from run
        // to run) and it would get none. Then the first width again, for its lines.
        foreach (var label in labels)
        {
            LayOut(label, 900f);
            LayOut(label, 850f);
        }
        Pass(900f);

        int shaped = shaper.Calls;
        Pass(900f);
        Pass(850f);
        Assert.Equal(shaped, shaper.Calls);
        Assert.Equal(labels.Length, pipeline.MemoCacheUse.Entries);
        Assert.Equal(4 * labels.Length, pipeline.MemoLineCacheUse.Entries);
        Assert.True(pipeline.MemoLineCacheUse.Bytes > 8L << 20, $"the lines weigh only {pipeline.MemoLineCacheUse.Bytes:N0} bytes");
    }

    private static void ForgetLayouts(TextPipeline pipeline)
    {
        var cache = typeof(TextPipeline).GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(pipeline)!;
        cache.GetType().GetMethod("Clear")!.Invoke(cache, null);
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
