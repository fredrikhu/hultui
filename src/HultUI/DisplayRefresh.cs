namespace HultUI;

/// <summary>
/// Which of a display's refresh rates a video's frame rate plays at without judder
/// (docs/design/TV-VIDEO.md §5): a whole multiple of it. A 23.976 fps film on a 59.94 Hz display is
/// shown three frames, two frames, three (3:2 pulldown), and pans stutter; at 23.976 Hz, or 119.88,
/// every frame is shown alike.
/// </summary>
internal static class DisplayRefresh
{
    /// <summary>How near a rate must be to a multiple of the video's: 0.05 %, which keeps 23.976
    /// apart from 24 (0.1 % apart), as a 23.976 fps film on a 24 Hz display skips a frame every
    /// 42 seconds.</summary>
    private const double Tolerance = 0.0005;

    /// <summary>
    /// The highest of <paramref name="rates"/> that is a whole multiple of
    /// <paramref name="framesPerSecond"/> (the highest keeps the interface smooth: 119.88 Hz over
    /// 23.976 Hz for a 23.976 fps film where the display has both), or null when none is.
    /// </summary>
    public static float? ForVideo(IEnumerable<float> rates, double framesPerSecond)
    {
        if (framesPerSecond <= 0 || double.IsNaN(framesPerSecond)) return null;
        float? best = null;
        foreach (var rate in rates)
        {
            if (rate <= 0) continue;
            double multiple = Math.Round(rate / framesPerSecond);
            if (multiple < 1) continue;
            if (Math.Abs(rate - multiple * framesPerSecond) / rate > Tolerance) continue;
            if (best is null || rate > best) best = rate;
        }
        return best;
    }
}
