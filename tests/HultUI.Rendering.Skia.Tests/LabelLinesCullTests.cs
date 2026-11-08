using System.Numerics;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// A label of several lines drawn through DrawText (a plain label: not selectable, no links) skips
/// the lines wholly outside its clip, as DrawParagraph does for a selectable one: a plain label in a
/// scroll view can be thousands of lines long (Meridian's Request tab shows the body it sent as one,
/// 1,898 lines drawn a frame for the ~190 on screen). What the clip shows is the pixels of every line
/// drawn: on a translated, scaled or rotated canvas and inside a layer, with lines the clip's edges
/// cut through and marks that hang out of a line's box into the clip.
/// </summary>
public class LabelLinesCullTests
{
    private static readonly ResolvedInheritedStyle Base = new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private const int Lines = 40;
    private const float Width = 400f, Height = 200f;

    // Forty lines of the same length, each with descenders; with marks, each ends in a capital under
    // six stacked acutes, which reach above the face's ascent and so out of the line's box (11 px
    // above it in Segoe UI at 16 px).
    private static string Label(bool marks) => string.Join('\n', Enumerable.Range(0, Lines).Select(i =>
        $"line {i:D2} of quickly jumping gray fjords" + (marks ? " A" + new string('́', 6) : "")));

    private static float LineHeight(SkiaRenderContext ctx, string label) =>
        ((PipelineTextParagraph)ctx.LayoutParagraph(label, Base, Width - 20)).TextLines[1].OffsetY;

    private static void Draw(SkiaRenderContext ctx, string label, float top, TextSpan[]? spans)
    {
        var bounds = new Rect(10, top, Width - 20, Lines * 30f);
        if (spans is null) ctx.DrawText(label, bounds, Base, TextOverflow.Clip, null);
        else ctx.DrawText(label, bounds, Base, TextOverflow.Clip, null, spans);
    }

    /// <summary>The win: of forty lines under a clip three lines tall, at most seven reach Skia (the
    /// three, and a line's height of slack each way); every one of them without the cull.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AClippedLabel_HandsSkiaOnlyTheLinesItsClipReaches(bool spanned)
    {
        using var text = new SkiaTextServices();
        using var surface = new SkiaRasterRenderSurface(text);
        surface.Resize((int)Width, (int)Height, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        string label = Label(marks: false);
        float lineHeight = LineHeight(ctx, label);
        // One colour over the whole label: a stretch a line, the line's own glyphs.
        TextSpan[]? spans = spanned ? [new TextSpan(0, label.Length, new Color(200, 30, 30, 255))] : null;

        long Glyphs(bool cull)
        {
            ctx.CullsTextLines = cull;
            long before = ctx.GlyphsDrawn;
            ctx.BeginFrame(Width, Height, 1f, Color.White);
            ctx.PushClip(new Rect(0, 0, Width, 3 * lineHeight));
            Draw(ctx, label, -10 * lineHeight, spans);
            ctx.Pop();
            ctx.EndFrame();
            return ctx.GlyphsDrawn - before;
        }

        long every = Glyphs(cull: false);
        Assert.True(every > 0 && every % Lines == 0, $"{every} glyphs is not the same number on each of {Lines} lines");
        long perLine = every / Lines;
        long drawn = Glyphs(cull: true);
        Assert.Equal(0, drawn % perLine);
        // Lines 10 to 12 are in the clip (the pixel test below holds them to the reference).
        Assert.InRange(drawn / perLine, 3, 7);
    }

    /// <summary>A label of one line reads no clip: it is drawn wherever it is, as before, and the
    /// element renderer's own cull is what skips it.</summary>
    [Fact]
    public void ALabelOfOneLine_IsDrawnOutsideItsClip()
    {
        using var text = new SkiaTextServices();
        using var surface = new SkiaRasterRenderSurface(text);
        surface.Resize((int)Width, (int)Height, 1f);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        ctx.BeginFrame(Width, Height, 1f, Color.White);
        ctx.PushClip(new Rect(0, 0, Width, 20));
        ctx.DrawText("one line far below its clip", new Rect(10, 150, 380, 30), Base, TextOverflow.Clip, null);
        ctx.Pop();
        ctx.EndFrame();
        Assert.True(ctx.GlyphsDrawn > 0, "a label of one line was culled by the clip");
    }

    public enum Space { Plain, Translated, Scaled, Rotated, InALayer, InABoundedLayer }

    public static TheoryData<Space, bool, bool> Cases()
    {
        var data = new TheoryData<Space, bool, bool>();
        foreach (var space in Enum.GetValues<Space>())
        foreach (bool spanned in new[] { false, true })
        foreach (bool marks in new[] { false, true })
            data.Add(space, spanned, marks);
        return data;
    }

    /// <summary>
    /// What the clip shows is the pixels of every line drawn. The clip starts 2 px inside line 5's
    /// box and ends 2 px above line 8's, so the clip's edges cut lines 5 and 7; with marks, line 8
    /// is wholly below the clip but its marks hang up into it, which only the slack keeps (in the
    /// plain cases: a colour stretch clips its glyphs to their line's box, so spanned marks do not
    /// reach the clip, and a rotated clip's local bounds reach line 8 anyway).
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void WhatTheClipShows_IsThePixelsOfEveryLineDrawn(Space space, bool spanned, bool marks)
    {
        using var text = new SkiaTextServices();
        using var surface = new SkiaRasterRenderSurface(text);
        float scale = space == Space.Scaled ? 1.5f : 1f;
        surface.Resize((int)(Width * scale), (int)(Height * scale), scale);
        var ctx = (SkiaRenderContext)surface.RenderContext;
        string label = Label(marks);
        TextSpan[]? spans = spanned
            ?
            [
                new TextSpan(0, label.Length, new Color(30, 30, 160, 255)),
                new TextSpan(label.IndexOf("line 05", StringComparison.Ordinal) + 11, 7, Color.Black, Weight: FontWeight.Bold),
            ]
            : null;
        // Where the lines are, bold included: the same layout DrawText makes.
        var lines = ((PipelineTextParagraph)ctx.LayoutParagraph(label, Base, Width - 20, spans)).TextLines;
        float top = 7.3f - lines[4].OffsetY;
        var clip = new Rect(0, top + lines[5].OffsetY + 2, Width, lines[8].OffsetY - lines[5].OffsetY - 4);
        float labelHeight = lines[^1].OffsetY + lines[^1].Height;

        byte[] Frame(bool cull)
        {
            ctx.CullsTextLines = cull;
            ctx.BeginFrame(Width, Height, scale, Color.White);
            if (space == Space.Translated) ctx.PushTranslate(13.5f, -37.25f);
            if (space == Space.Rotated) ctx.PushTransform(Matrix3x2.CreateRotation(0.3f, new Vector2(200, 100)));
            ctx.PushClip(clip);
            if (space == Space.InALayer) ctx.PushOpacity(0.6f);
            if (space == Space.InABoundedLayer) ctx.PushOpacity(0.6f, new Rect(10, top, Width - 20, labelHeight));
            Draw(ctx, label, top, spans);
            if (space is Space.InALayer or Space.InABoundedLayer) ctx.Pop();
            ctx.Pop();
            if (space is Space.Translated or Space.Rotated) ctx.Pop();
            ctx.EndFrame();
            return surface.ReadPixels(out _, out _, out _);
        }

        byte[] every = Frame(cull: false);
        int ink = 0;
        for (int i = 0; i < every.Length; i += 4)
            if (every[i] != 255 || every[i + 1] != 255 || every[i + 2] != 255) ink++;
        Assert.True(ink > 200, $"only {ink} pixels of ink in the clip");
        Assert.Equal(every, Frame(cull: true));
    }
}
