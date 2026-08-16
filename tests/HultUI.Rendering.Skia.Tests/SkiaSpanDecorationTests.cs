using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Span-level text decorations (stage 1 of docs/plans/12-rich-text-spans.md).
/// Differential pixel tests: the same text is drawn with and without the
/// decorated span, and the difference must exist and be confined to the span's
/// character range — robust across fonts and platforms, since no absolute glyph
/// geometry is assumed.
/// </summary>
public class SkiaSpanDecorationTests
{
    private static readonly ResolvedInheritedStyle Style = new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: Color.Black, TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    [Fact]
    public void UnderlineSpan_AddsInkOnlyUnderTheSpanRange() =>
        AssertDecorationConfinedToSpan(TextDecoration.Underline);

    [Fact]
    public void LineThroughSpan_AddsInkOnlyAcrossTheSpanRange() =>
        AssertDecorationConfinedToSpan(TextDecoration.LineThrough);

    private static void AssertDecorationConfinedToSpan(TextDecoration decoration)
    {
        const string text = "0123456789";
        var plain = Draw(text, TextDecoration.None, out _, out _);
        var decorated = Draw(text, decoration, out float spanX0, out float spanX1);

        int changed = 0, outside = 0;
        for (int i = 0; i < plain.Length; i += 4)
        {
            if (plain[i] == decorated[i] && plain[i + 1] == decorated[i + 1]
                && plain[i + 2] == decorated[i + 2])
                continue;
            changed++;
            int x = i / 4 % 300;
            // A hair of antialiasing tolerance on either side of the range.
            if (x < spanX0 - 1.5f || x > spanX1 + 1.5f) outside++;
        }

        Assert.True(changed > 0, $"{decoration} drew nothing at all");
        Assert.True(outside == 0,
            $"{decoration} changed {outside} of {changed} pixels outside the span's x-range");
    }

    private static byte[] Draw(string text, TextDecoration decoration, out float spanX0, out float spanX1)
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(300, 60, 1f);

        var spans = new[] { new TextSpan(3, 4, Color.Red, default, decoration) };
        var ctx = surface.RenderContext;
        ctx.BeginFrame(300, 60, 1f, Color.White);
        ctx.DrawText(text, new Rect(0, 0, 280, 40), Style, TextOverflow.Clip, null, spans);
        ctx.EndFrame();
        surface.Present();

        spanX0 = surface.MeasureContext.MeasureText(text[..3], Style, 280, null).Width;
        spanX1 = surface.MeasureContext.MeasureText(text[..7], Style, 280, null).Width;
        return surface.ReadPixels(out _, out _, out _);
    }
}
