using HultUI.Core.Layout;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// <see cref="IRenderContext.TextCaretXs"/> answers for every boundary of a line what
/// <see cref="IRenderContext.TextCaretX"/> answers for each, to the bit, from one layout lookup:
/// a text area's accessible text asks it once a row instead of once a character (plan 17, round 5).
/// Every kind of line the caret mapping knows: plain, spaced, monospace, merged clusters (emoji, a
/// family, a flag, combining marks, ligatures), Han, right to left and mixed, and the empty line.
/// </summary>
public class TextCaretXsTests
{
    private static readonly ResolvedInheritedStyle Base = new(
        FontFamily: "sans-serif", FontSize: 14f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly ResolvedInheritedStyle[] Styles =
    [
        Base,
        Base with { TextDirection = TextDirection.Rtl },
        Base with { TextDirection = TextDirection.Auto },
        Base with { LetterSpacing = 0.6f },
        Base with { FontSize = 12f, FontWeight = FontWeight.Bold },
        Base with { FontFamily = FontFamilies.Monospace, FontSize = 12f },
    ];

    // Numeric code points: the editing tools turn written-out escapes into the characters.
    private static string C(params int[] codePoints) => string.Concat(codePoints.Select(char.ConvertFromUtf32));

    private static readonly string[] Lines =
    [
        "",
        "plain words on a line, ten of them and a few more",
        "  Emoji:     " + C(0x1F600) + " " + C(0x1F44D, 0x1F3FD) + " " + C(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467) + " " + C(0x1F1F8, 0x1F1EA),
        "  Combining: e" + C(0x0301) + " a" + C(0x0308) + " n" + C(0x0303),
        "office affluent fjord, ligatures where the face has them",
        string.Concat(Enumerable.Repeat(C(0x6771, 0x4EAC, 0x306F, 0x65E5, 0x672C), 4)),
        "Hebrew " + C(0x05E9, 0x05DC, 0x05D5, 0x05DD) + " and " + C(0x0644, 0x0627, 0x0020, 0x0645, 0x0631, 0x062D, 0x0628, 0x0627) + " mixed",
    ];

    [Fact]
    public void EveryBoundary_IsWhatTextCaretXSays_ToTheBit()
    {
        var services = new SkiaTextServices();
        using var ctx = new SkiaRenderContext();
        ctx.SetPipeline(services.Pipeline);
        IRenderContext context = ctx;
        int compared = 0;
        foreach (var style in Styles)
            foreach (var line in Lines)
            {
                // One past the end too: an offset past the line is the line's end, as for TextCaretX.
                var xs = new float[line.Length + 2];
                context.TextCaretXs(line, style, xs);
                for (int k = 0; k < xs.Length; k++)
                {
                    float expected = context.TextCaretX(line, style, k);
                    Assert.True(BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(xs[k]),
                        $"'{line}' {style.TextDirection} {style.FontFamily} at {k}: {xs[k]} against {expected}");
                    compared++;
                }
            }
        Assert.True(compared > 1_000, $"{compared} boundaries compared");
    }
}
