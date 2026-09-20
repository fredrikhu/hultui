using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class ProgressBar
{
    /// <summary>
    /// A horizontal progress bar that fills the width it is given. The value is a
    /// fraction of <paramref name="max"/>; chain <c>.Indeterminate()</c> for activity
    /// with no known fraction.
    /// </summary>
    public static ProgressBarElement New(double value = 0, double max = 1, [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        ProgressBarElement.New(value, max, file, line);
}
