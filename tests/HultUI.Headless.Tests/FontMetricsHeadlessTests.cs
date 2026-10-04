using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Headless;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Font metrics and glyph coverage reach code the way it asks for them: <see cref="TextMeasure"/>
/// at build time and the render context while drawing, recorded or not, with the same answers.
/// </summary>
public class FontMetricsHeadlessTests
{
    private static readonly ResolvedInheritedStyle Mono = TextMeasure.StyleFor(13f, FontFamilies.Monospace);

    private sealed class Probe : Core.Elements.Element
    {
        public FontMetrics? Metrics;
        public bool? HasEmoji, HasA;

        public override void Render(IRenderContext ctx)
        {
            Metrics = ctx.MeasureFont(Mono);
            HasEmoji = ctx.HasGlyph(Mono, 0x1F600);
            HasA = ctx.HasGlyph(Mono, 'A');
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildTimeAndRenderTime_AgreeWithTheFont(bool recording)
    {
        using var session = HeadlessSession.Create(200, 100,
            options: recording ? new HeadlessOptions { RecordDrawCalls = true } : null);
        var probe = new Probe();
        probe.Box.Width = 10;
        probe.Box.Height = 10;
        session.Window.SetRoot(probe);
        session.PumpFrame();

        var built = TextMeasure.MeasureFont(Mono);
        Assert.NotNull(built);
        Assert.True(built.Value.IsFixedPitch, built.Value.FamilyName);
        Assert.Equal(built, probe.Metrics);

        // The recording context forwards the queries to the real one, not the interface's
        // defaults, which would say every glyph is there.
        Assert.False(probe.HasEmoji);
        Assert.True(probe.HasA);
        Assert.False(TextMeasure.HasGlyph(Mono, 0x1F600));
    }
}
