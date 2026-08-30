using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The slider's colours are themable.
///
/// <para>It was the last interactive control that was not: it read <c>theme.Primary</c> and
/// <c>theme.Border</c> directly, so an app whose card borders are a hairline and whose slider
/// track is a mid grey — Fluent's, and it is not unusual — had to choose which one to get
/// right. Asserted against the recorded draw calls rather than against private fields,
/// because what matters is the colour that reaches the renderer.</para>
/// </summary>
public class SliderThemeTests
{
    private static string Draw(Theme theme, double value = 40)
    {
        using var session = HeadlessSession.Create(300, 80, options: new HeadlessOptions { RecordDrawCalls = true });
        session.Application.Theme = theme;

        var slider = Slider.New(value, 0, 100).Width(200f);
        session.Application.SetRoot(Box.New().Padding(20f).Children(slider));
        session.PumpFrames(2);

        session.Window.RenderLog.Clear();
        session.PumpFrame(session.DefaultFrameDelta, forceRender: true);
        return session.Window.RenderLog.ToString();
    }

    [Fact]
    public void AThemedSliderPaintsTheThemesColours()
    {
        var log = Draw(Theme.Light.With(new SliderTheme
        {
            TrackColor = Color.FromHex("#112233"),
            FillColor = Color.FromHex("#445566"),
            ThumbColor = Color.FromHex("#778899"),
            ThumbBorderColor = Color.FromHex("#AABBCC"),
        }));

        Assert.Contains("#112233", log);
        Assert.Contains("#445566", log);
        Assert.Contains("#778899", log);
        Assert.Contains("#AABBCC", log);
    }

    /// <summary>
    /// The part that matters more than the feature: a slider under a theme that says nothing
    /// about sliders paints exactly what it painted before this extension existed — track from
    /// <c>Border</c>, fill from <c>Primary</c>, a white thumb ringed in the fill colour.
    /// </summary>
    [Fact]
    public void AnUnthemedSliderIsUnchanged()
    {
        var log = Draw(Theme.Light);

        Assert.Contains(Hex(Theme.Light.Border), log);
        Assert.Contains(Hex(Theme.Light.Primary), log);
        Assert.Contains("#FFFFFF", log);
    }

    [Fact]
    public void ThePartialOverrideFallsBackForTheRest()
    {
        // Only the track is named. Fill must still come from the palette, which is the
        // difference between "a theme extension" and "a second palette you must fill in".
        var log = Draw(Theme.Dark.With(new SliderTheme { TrackColor = Color.FromHex("#123456") }));

        Assert.Contains("#123456", log);
        Assert.Contains(Hex(Theme.Dark.Primary), log);
    }

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
