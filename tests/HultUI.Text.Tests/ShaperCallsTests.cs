using HultUI.Core.Layout;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// <see cref="TextPipeline.ShaperCalls"/> counts every string the pipeline hands its shaper
/// (docs/plans/19-run-shaping.md, item 4): words, the digit table's and the marks' proofs,
/// right-to-left runs, ellipsized lines, styled spans, prefix tables; and nothing for text the
/// caches already hold.
/// </summary>
public class ShaperCallsTests
{
    private static readonly ResolvedInheritedStyle Style = new(
        FontFamily: "TestFont", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    [Fact]
    public void ShaperCalls_CountsEveryStringHandedToTheShaper()
    {
        var shaper = new CountingShaper();
        var pipeline = new TextPipeline(new StubFontCollection(), shaper);
        Assert.Equal(0, pipeline.ShaperCalls);
        void Check(string what) =>
            Assert.True(shaper.Calls == pipeline.ShaperCalls, $"{what}: {pipeline.ShaperCalls} counted, {shaper.Calls} made");

        pipeline.Layout("hello world", Style, 10_000f, null);
        Check("words");
        pipeline.Layout("Item 1000000 ohxec — 62439 units, -7 at 12:30", Style, 10_000f, null);
        Check("numbers, with the digit table's proof and the marks'");
        Assert.True(shaper.Calls > 200, $"only {shaper.Calls} calls: the proofs did not run");
        pipeline.Layout("abc שלום עולם def and more words here", Style, 120f, null);
        Check("right to left, wrapped");
        pipeline.Layout("a long line of words to truncate somewhere", Style, 120f, 1, TextOverflow.Ellipsis);
        Check("truncated");
        var bold = new[] { new TextSpan(2, 4, HultUI.Core.Primitives.Color.Black, Weight: FontWeight.Bold, FontSize: 20f) };
        pipeline.Layout("a styled sentence of words", Style, 90f, null, TextOverflow.Clip, bold);
        Check("styled");
        pipeline.MeasurePrefixes("internationalization of everything", 0, 34, Style);
        Check("a prefix table");
    }

    [Fact]
    public void TextTheCachesHold_CountsNothing()
    {
        var pipeline = new TextPipeline(new StubFontCollection(), new StubTextShaper());
        string[] texts = ["hello world", "Item 1000000 — 62439 units", "abc שלום עולם def"];
        foreach (string text in texts)
            pipeline.Layout(text, Style, 10_000f, null);
        long calls = pipeline.ShaperCalls;

        foreach (string text in texts)
            pipeline.Layout(text, Style, 10_000f, null);
        pipeline.Layout("world hello", Style, 10_000f, null);   // words seen before, in a new string
        Assert.Equal(calls, pipeline.ShaperCalls);

        pipeline.Layout("hello unseen", Style, 10_000f, null);
        Assert.Equal(calls + 1, pipeline.ShaperCalls);           // the one new word
    }

    private sealed class CountingShaper : ITextShaper
    {
        private readonly StubTextShaper _inner = new();
        public long Calls;

        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
        {
            Calls++;
            return _inner.Shape(text, font, fontSize, rtl);
        }
    }
}
