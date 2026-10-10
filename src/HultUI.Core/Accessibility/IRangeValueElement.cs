namespace HultUI.Core.Accessibility;

/// <summary>
/// Implemented by elements that hold a numeric value in a range (e.g. a slider),
/// so the accessibility layer can expose the UIA RangeValue pattern — reporting
/// the current value/bounds and letting assistive tech set it.
/// </summary>
public interface IRangeValueElement
{
    double Value { get; }
    double Minimum { get; }
    double Maximum { get; }
    double SmallChange { get; }
    double LargeChange { get; }
    bool IsReadOnly { get; }

    /// <summary>Set the value from assistive tech (clamped to the range).</summary>
    void SetAccessibleValue(double value);
}

/// <summary>
/// A value in a range — a slider's, a progress bar's, a number field's — as an accessibility tree
/// was built with it (<see cref="AccessibilityNode.Range"/>): what UIA's RangeValue pattern and
/// AT-SPI's Value interface report, and what the differ compares to tell a change.
/// </summary>
public readonly record struct AccessibleRange(
    double Value, double Minimum, double Maximum, double SmallChange, double LargeChange, bool IsReadOnly)
{
    /// <summary>The range of <paramref name="element"/> as it is now.</summary>
    public static AccessibleRange Of(IRangeValueElement element) =>
        new(element.Value, element.Minimum, element.Maximum, element.SmallChange, element.LargeChange, element.IsReadOnly);
}
