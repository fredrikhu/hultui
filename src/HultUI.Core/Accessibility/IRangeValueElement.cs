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
