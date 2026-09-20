using HultUI.Core.Elements;

namespace HultUI.Core.Builders;

public static class NumericUpDown
{
    /// <summary>
    /// A number field with step buttons. Up/Down and the wheel step, PageUp/PageDown step
    /// ten times as far, Home/End go to the bounds, the buttons repeat while held, and a
    /// typed value commits on Enter or blur. Reads as a spin button with a range value.
    /// </summary>
    public static NumericUpDownElement New(double value = 0, double min = 0, double max = 100, double step = 1,
        [OriginFile] string? file = null, [OriginLine] int line = 0) =>
        NumericUpDownElement.New(value, min, max, step, file, line);
}
