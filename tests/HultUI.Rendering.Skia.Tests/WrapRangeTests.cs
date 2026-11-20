using System.Text;
using HultUI.Core.Layout;
using HultUI.Core.Styling;
using HultUI.Text;
using HultUI.Text.Tests;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// A wrapped plain layout is kept for the widths at which the break pass takes the decisions it took,
/// and a layout at any of them is the kept one (TextPipeline.WrapRange). It must be the layout the
/// reference (<c>KeepsWrapRanges = false</c>) makes at that width, to the bit, through HarfBuzz and the
/// system's fonts: over a resize drag in whole, fractional and 4 px steps, and at each kept range's
/// ends and just past them. The corpus is a trim of what the change was proven on (this repository's
/// TODO.md and ledger archive in Vesper Commander's wrapped reader, and Chinese, Japanese and Korean
/// text, here made from code points).
/// </summary>
public class WrapRangeTests
{
    private static readonly ResolvedInheritedStyle Base = new(
        FontFamily: "sans-serif", FontSize: 13f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    internal static readonly ResolvedInheritedStyle[] Styles =
    [
        Base with { FontFamily = FontFamilies.Monospace, FontSize = 11.5f },   // Vesper's reader
        Base,
        Base with { FontWeight = FontWeight.Bold, FontSize = 15f, LetterSpacing = 0.3f },
        Base with { FontFamily = "serif", FontSize = 17.5f, LineHeight = 26f },
    ];

    // In this order through one pipeline: two lines truncated between two passes of the same two lines
    // clipped, whose wraps are kept under a key that says Clip (a truncated layout is not kept, and a
    // kept one does not answer for one), and one clipped line (cut at a character, not kept).
    private static readonly (int? MaxLines, TextOverflow Overflow)[] Modes =
    [
        (null, TextOverflow.Clip),
        (2, TextOverflow.Clip),
        (2, TextOverflow.Ellipsis),
        (2, TextOverflow.Clip),
        (1, TextOverflow.Clip),
        (3, TextOverflow.Clip),
    ];

    // Lines of TODO.md and of the ledger archive, as Vesper's reader measures them (one a row).
    internal static readonly string[] Latin =
    [
        """- **Repeating tweens** — `TweenBuilder.Repeat(n)` / `.Loop()` / `.Yoyo()`. The cycle and its phase are derived from elapsed time rather than accumulated per cycle, so a repeat holds its period: a restart that dropped the frame's overshoot would quantise every cycle up to a whole number of frames, and a spinner built that way visibly slows down and drifts. A finite repeat ends on its end value; a loop never renders the terminal frame at all. Junction's Fetch/Pull/Push buttons are the first consumer.""",
        """- **IME composition** — `SDL_EVENT_TEXT_EDITING` plumbed through `EventTranslator → TextEditingEvent`. `TextInputElement` and `TextAreaElement` render the preedit inline at the caret with a thin underline across the whole preedit and a thick underline on the active IME segment. `SDL_SetTextInputArea` is called each frame on the focused input so OS candidate windows snap to the caret; `SDL_ClearComposition` fires on blur. Custom candidate-list rendering is deliberately skipped — the OS-native window is used instead. New `ITextInputHost` abstraction in `HultUI.Core.Platform`; `SDL3TextInputHost` is the implementation. 9 unit tests covering the state machine.""",
        """| 2 | fc00b75, a9cc75f, 46a1219, 9b801d1 | text-textarea-measure-lines | Width per hard line measured without layout (`IRenderContext.MeasureTextWidth`, bit-equal; 4 MB width cache with a weight); last frame's line strings reused; shape bound 24 → 8 MB. JSON open −21 % JIT / −20 % AOT (median −35 %), 6,599 → 1,903 KB; typing in 2,000 lines −22 %, 465 → 222 KB; undo-redo −28 %; steady TextArea repaint 2.2 KB → 0; 10,000-line JSON keystroke 1.66 ms even at 8 MB (was 25 ms at 8 MB). Meridian first response frame 12.4 → 5.8 MB. Retained after closing the JSON 9.6 → 4.3 MB. JSON wheel +3.4 KB (lines shaped when drawn). Line starts identical over ~2 M lines. AOT TableView sort +5…7 % unproven placement. |""",
        """| 3 | 3730052 | a11y-selection-move-patch | Regression fix (7dc7e0e): a selection move marks IsSelected on the changed item nodes of the applied tree and hands the bridge a patch (`IAccessibilityBridge.ApplyPatchedProperties`, default = apply as a diff; UIA and AT-SPI override) instead of forcing a rebuild; falls back to a rebuild for no tree, a list mounted in the same handler, count change, composite rows, crossing 2,000. Event streams identical on 7dc7e0e's 12 scripts and two walks, except UIA VerticalScrollPercent 28 → 16 in a 200-item scrolling walk (one per refresh, not per key). Junction History AOT, UIA listening, 80 keys at 60 ms: a11y trees 82 → 50, a11y bytes per key 293.6 → 179 KB (−40 %), UI bytes 553 → 438 KB (−21 %). Walk row 380.6 → 227.6 KB/op, min −8…−15 % JIT/AOT; 0 slower. 9 + 3 tests, 7 mutations caught. |""",
        "C:\\Users\\someone\\source\\repos\\a-path-with-no-break-opportunities-at-all-in-it-anywhere\\that-runs-on-and-on\\File.cs and then words",
        "first line\nsecond line is longer than the first one and wraps where the first does not",
    ];

    // Chinese, Japanese (with Latin words and full-width punctuation) and Korean (with spaces), made from
    // code points: the plain path's CJK, cut anywhere between characters.
    private static IEnumerable<string> Cjk()
    {
        var rng = new Random(15);
        string Pick(int from, int to) => char.ConvertFromUtf32(from + rng.Next(to - from));
        for (int kind = 0; kind < 3; kind++)
        for (int n = 0; n < 2; n++)
        {
            var sb = new StringBuilder();
            int length = 300 + rng.Next(500);
            while (sb.Length < length)
            {
                double r = rng.NextDouble();
                switch (kind)
                {
                    case 0:
                        sb.Append(r < 0.9 ? Pick(0x4E00, 0x4E00 + 3000) : r < 0.95 ? "\uFF0C" : "\u3002");
                        break;
                    case 1:
                        if (r < 0.45) sb.Append(Pick(0x4E00, 0x4E00 + 3000));
                        else if (r < 0.85) sb.Append(Pick(0x3041, 0x3094));
                        else if (r < 0.92) sb.Append(Pick(0x30A1, 0x30F7));
                        else if (r < 0.96) sb.Append('\u3001');
                        else sb.Append(" TextArea ");
                        break;
                    default:
                        for (int k = 1 + rng.Next(5); k > 0; k--) sb.Append(Pick(0xAC00, 0xD7A4));
                        sb.Append(' ');
                        break;
                }
            }
            yield return sb.ToString();
        }
    }

    internal static readonly string[] Texts = [.. Latin, .. Cjk()];

    [Fact]
    public void AKeptWrap_IsTheReferenceLayout_AtEveryWidthOfADrag()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        int compared = 0, kept = 0, wrapped = 0;
        foreach (var style in Styles)
        {
            // Pipelines per style, shared by every mode and text, as an application's is: the texts'
            // wraps kept under Clip are there when the same texts are truncated.
            var keeps = new TextPipeline(fonts, shaper);
            var reference = new TextPipeline(fonts, shaper) { KeepsWrapRanges = false };
            foreach (var (maxLines, overflow) in Modes)
            {
                int modeHits = keeps.WrapRangeHits;
                foreach (float width in Drag(640f))
                {
                    int before = keeps.WrapRangeHits;
                    foreach (var text in Texts)
                    {
                        var expected = reference.Layout(text, style, width, maxLines, overflow);
                        var actual = keeps.Layout(text, style, width, maxLines, overflow);
                        LayoutAssert.Same(expected, actual, $"\"{text[..20]}\" w={width:R} lines={maxLines} {overflow} {style}");
                        compared++;
                        if (expected.Lines.Count > 1 && overflow == TextOverflow.Clip) wrapped++;
                    }
                    kept += keeps.WrapRangeHits - before;
                }
                if (overflow == TextOverflow.Ellipsis)
                    Assert.Equal(modeHits, keeps.WrapRangeHits);
            }
        }
        Assert.True(compared > 2_000, $"compared only {compared} layouts");
        // Most of a drag's steps keep a paragraph's lines (the drag below is 41 widths, 32 of them 1 px or
        // less from the last); with nothing kept the comparisons above would hold the reference to itself.
        Assert.True(kept * 2 > wrapped, $"kept {kept} of {wrapped} wrapped layouts");
    }

    [Fact]
    public void AKeptRange_HoldsToItsEnds_AndNoFurther()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        int ranges = 0, probes = 0;
        foreach (var (maxLines, _) in Modes.Where(m => m.Overflow == TextOverflow.Clip && m.MaxLines != 1).Distinct())
        foreach (var style in Styles)
        foreach (float start in new[] { 640f, 517.33f })
        {
            var reference = new TextPipeline(fonts, shaper) { KeepsWrapRanges = false };
            foreach (var text in Texts)
            {
                // A pipeline of its own per text: a range is the latest of its text's.
                var keeps = new TextPipeline(fonts, shaper);
                var first = keeps.Layout(text, style, start, maxLines);
                if (!keeps.KeptWrapRange(text, style, maxLines, out float from, out float below)) continue;
                ranges++;
                Assert.True(from <= start && start < below, $"[{from:R}, {below:R}) leaves out {start:R}");
                string at = $"\"{text[..20]}\" [{from:R}, {below:R}) lines={maxLines} {style}";

                // Inside: the kept layout itself, which is the reference's there.
                float mid = float.IsPositiveInfinity(below) ? from + 100f : from + (below - from) / 2f;
                if (mid >= below) mid = from;   // a range an ulp wide
                foreach (float t in new[] { from, MathF.BitDecrement(below), mid })
                {
                    int hits = keeps.WrapRangeHits;
                    var x = keeps.Layout(text, style, t, maxLines);
                    Assert.Same(first, x);
                    Assert.Equal(hits + 1, keeps.WrapRangeHits);
                    LayoutAssert.Same(reference.Layout(text, style, t, maxLines), x, $"{at} at {t:R}");
                    probes++;
                }
                // Just past either end: laid out again, the reference's too.
                foreach (float t in new[] { MathF.BitDecrement(from), below })
                {
                    if (float.IsInfinity(t)) continue;
                    var x = keeps.Layout(text, style, t, maxLines);
                    Assert.NotSame(first, x);
                    LayoutAssert.Same(reference.Layout(text, style, t, maxLines), x, $"{at} past an end at {t:R}");
                    probes++;
                }
            }
        }
        Assert.True(ranges > 100, $"only {ranges} ranges kept");
        Assert.True(probes > 5 * 100, $"only {probes} probes");
    }

    [Fact]
    public void AWidthInsideTheKeptRange_IsAnsweredWithoutALayout()
    {
        var pipeline = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper());
        var style = Styles[0];
        string paragraph = Latin[1];
        var first = pipeline.Layout(paragraph, style, 600f, null);
        Assert.True(first.Lines.Count > 1);
        Assert.True(pipeline.KeptWrapRange(paragraph, style, null, out float from, out float below));

        // A drag a quarter pixel at a time, as long as the lines stay: no layout is made; the one kept is
        // the answer.
        int hits = pipeline.WrapRangeHits, steps = 0;
        for (float w = 600f - 0.25f; w >= from; w -= 0.25f, steps++)
            Assert.Same(first, pipeline.Layout(paragraph, style, w, null));
        Assert.Same(first, pipeline.Layout(paragraph, style, MathF.BitDecrement(below), null));
        Assert.Equal(hits + steps + 1, pipeline.WrapRangeHits);

        // Past it the text is broken again, and that layout is kept for its own range.
        var narrower = pipeline.Layout(paragraph, style, MathF.BitDecrement(from), null);
        Assert.NotSame(first, narrower);
        Assert.True(pipeline.KeptWrapRange(paragraph, style, null, out _, out float narrowerBelow));
        Assert.True(narrowerBelow <= from);
    }

    // A resize drag: whole, fractional (a physical pixel at 150 %) and 4 px steps narrower, 2.5 px steps
    // wider, a jump and back.
    internal static IEnumerable<float> Drag(float start)
    {
        float w = start;
        yield return w;
        for (int i = 0; i < 10; i++) yield return w -= 1f;
        for (int i = 0; i < 12; i++) yield return w -= 0.6666667f;
        for (int i = 0; i < 6; i++) yield return w -= 4f;
        for (int i = 0; i < 8; i++) yield return w += 2.5f;
        yield return start - 200f;
        yield return start - 200.5f;
        yield return start - 201f;
        yield return start;
    }
}
