using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class Spinner
{
    /// <summary>
    /// A busy indicator: a ring of dots turning for as long as it is on screen. Give it
    /// an <c>AccessibleName</c> saying what is being waited for; it reads as an
    /// indeterminate progress bar.
    /// </summary>
    public static SpinnerElement New(float? size = null, [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        SpinnerElement.New(size, file, line);
}
