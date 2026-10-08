using System.Reflection;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using SkiaSharp;
using Xunit;

namespace HultUI.Headless.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// The windows of an application share one set of text services, which the platform disposes
/// when the application ends. Before plan 17's round 1 each window built its own and nothing
/// disposed it: the shaper's HarfBuzz font per face held a stream over the font file that no
/// finalizer frees, so every window closed left one per face behind (600 headless open/close
/// cycles: +4 such streams, +44 KB of private bytes and +1 KB of managed heap per cycle), and
/// every window repeated the others' fonts and text caches.
///
/// <para>The leak is counted where it was found: SkiaSharp's handle table, which holds every
/// live Skia object, after a full collection. This assembly runs one test at a time, so
/// nothing else adds to it meanwhile.</para>
/// </summary>
public class SharedTextServicesTests
{
    private static Element Labels(string first) => Flex.Column().Padding(8).Children(
        Text.New(first),
        Text.New("The quick brown fox jumps over the lazy dog"),
        Text.New("Bold heading").FontWeight(FontWeight.Bold),
        Text.New("a1b2c3d 0x1F2E commit").FontFamily(FontFamilies.Monospace),
        Text.New("Italic aside").Italic());

    private static IMeasureContext MeasureContextOf(HeadlessWindow window) =>
        window.Window.PlatformWindow.Surface.MeasureContext;

    /// <summary>Streams over font files that Skia objects still hold: one per HarfBuzz font a
    /// shaper keeps, and nothing else in a headless session holds one.</summary>
    private static int LiveFontStreams()
    {
        for (int i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        var table = typeof(SKObject).Assembly.GetType("SkiaSharp.HandleDictionary")
            ?.GetField("instances", BindingFlags.Static | BindingFlags.NonPublic)
            ?.GetValue(null) as System.Collections.IDictionary
            ?? throw new InvalidOperationException("SkiaSharp's handle table moved; update this test.");
        int count = 0;
        lock (table)
        {
            foreach (System.Collections.DictionaryEntry entry in table)
            {
                var target = entry.Value is WeakReference weak ? weak.Target : entry.Value;
                if (target is SKStreamAsset) count++;
            }
        }
        return count;
    }

    private static byte[] Pixels(HeadlessWindow window)
    {
        using var image = window.CapturePixels();
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.Bytes;
    }

    [Fact]
    public void Every_window_of_a_session_measures_with_the_same_text_services()
    {
        using var session = HeadlessSession.Create(300, 200);
        var second = session.CreateWindow("second", 300, 200);
        session.Application.OpenInspector();
        session.PumpFrame();

        var inspector = session.Application.Windows.Single(w => w.IsDevToolsWindow);
        Assert.Same(MeasureContextOf(session.Window), MeasureContextOf(second));
        Assert.Same(MeasureContextOf(session.Window), inspector.PlatformWindow.Surface.MeasureContext);
    }

    [Fact]
    public void Text_draws_the_same_in_two_windows_and_closing_one_leaves_the_other_whole()
    {
        using var session = HeadlessSession.Create(320, 160);
        session.Window.SetRoot(Labels("First window"));
        session.PumpFrame();
        var primary = Pixels(session.Window);
        float width = TextMeasure.MeasureWidth("The quick brown fox", 14f);
        int streams = LiveFontStreams();
        Assert.NotEqual(0, streams);

        var second = session.CreateWindow("second", 320, 160);
        second.SetRoot(Labels("First window"));
        session.PumpFrame();
        Assert.Equal(primary, Pixels(second));

        second.Window.Close();
        session.PumpFrame();
        Assert.False(second.Window.IsOpen);

        // The shared HarfBuzz fonts are still there, still used: closing a window disposed
        // nothing the primary draws with.
        Assert.Equal(streams, LiveFontStreams());
        session.PumpFrame(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60), forceRender: true);
        Assert.Equal(primary, Pixels(session.Window));
        Assert.Equal(width, TextMeasure.MeasureWidth("The quick brown fox", 14f));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]   // pinned fonts (golden images) share a set of their own
    public void Opening_and_closing_windows_leaves_no_font_streams_behind(bool pinned)
    {
        using var session = HeadlessSession.Create(320, 160,
            options: pinned ? HeadlessOptions.Golden() : null);
        session.Window.SetRoot(Labels("Primary"));
        session.PumpFrame();

        void Cycle(int i)
        {
            var window = session.CreateWindow($"w{i}", 320, 160);
            window.SetRoot(Labels($"Window {i}"));
            session.PumpFrame();
            window.Window.Close();
            session.PumpFrame();
        }

        Cycle(0);
        int streams = LiveFontStreams();
        for (int i = 1; i <= 20; i++) Cycle(i);

        Assert.Equal(streams, LiveFontStreams());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ending_the_session_gives_the_shared_fonts_back(bool pinned)
    {
        int before = LiveFontStreams();
        var session = HeadlessSession.Create(320, 160,
            options: pinned ? HeadlessOptions.Golden() : null);
        session.Window.SetRoot(Labels("Primary"));
        var second = session.CreateWindow("second", 320, 160);
        second.SetRoot(Labels("Second"));
        session.PumpFrame();
        Assert.True(LiveFontStreams() > before);

        session.Dispose();

        Assert.Equal(before, LiveFontStreams());
    }
}
