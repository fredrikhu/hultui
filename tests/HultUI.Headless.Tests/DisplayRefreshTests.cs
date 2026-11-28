using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="DisplayRefresh.ForVideo"/>: the display rate a film plays at without judder
/// (docs/design/TV-VIDEO.md §5). The rates are a Shield TV's at 4K, as Android reports them.
/// </summary>
public class DisplayRefreshTests
{
    private static readonly float[] Shield4K = [29.97003f, 23.976025f, 50f, 30.000002f, 25f, 24.000002f, 60.000004f, 59.94006f];

    [Theory]
    [InlineData(24000.0 / 1001, 23.976025f)]   // a film: not 24, nor 59.94 (3:2)
    [InlineData(24.0, 24.000002f)]
    [InlineData(25.0, 50f)]                     // the highest multiple
    [InlineData(30000.0 / 1001, 59.94006f)]
    [InlineData(50.0, 50f)]
    [InlineData(60000.0 / 1001, 59.94006f)]
    [InlineData(60.0, 60.000004f)]
    public void AFilmsRate_GetsTheHighestMultipleTheDisplayHas(double fps, float expected) =>
        Assert.Equal(expected, DisplayRefresh.ForVideo(Shield4K, fps));

    [Fact]
    public void ARateTheDisplayCannotShowWithoutJudder_IsNone()
    {
        Assert.Null(DisplayRefresh.ForVideo([59.94006f, 60.000004f], 24000.0 / 1001));
        Assert.Null(DisplayRefresh.ForVideo(Shield4K, 15.5));
        Assert.Null(DisplayRefresh.ForVideo(Shield4K, 0));
    }

    [Fact]
    public void A120HzPanel_ShowsAFilmAtFiveTimesItsRate() =>
        Assert.Equal(119.88012f, DisplayRefresh.ForVideo([59.94006f, 119.88012f, 23.976025f], 24000.0 / 1001));
}
