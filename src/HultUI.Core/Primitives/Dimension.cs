namespace HultUI.Core.Primitives;

public readonly record struct Dimension
{
    private readonly float _valueAsPercentage;
    public float Value { get; }
    public DimensionUnit Unit { get; }

    private Dimension(float value, DimensionUnit unit)
    {
        Value = value;
        _valueAsPercentage = value / 100f;
        Unit = unit;
    }

    public static Dimension Px(float value) => new(value, DimensionUnit.Pixels);
    public static Dimension Percent(float value) => new(value, DimensionUnit.Percent);
    public static Dimension Auto => new(0, DimensionUnit.Auto);
    public static Dimension None => new(0, DimensionUnit.None);

    public bool IsAuto => Unit == DimensionUnit.Auto;
    public bool IsNone => Unit == DimensionUnit.None;

    /// <summary>
    /// Resolve this dimension against a reference size (the parent's available size).
    /// Returns null if Auto or None.
    /// </summary>
    public float? Resolve(float referenceSize) => Unit switch
    {
        // TODO: The other units need to be able to be resolved?
        DimensionUnit.Pixels => Value,
        DimensionUnit.Percent => _valueAsPercentage * referenceSize,
        _ => null
    };

    public static implicit operator Dimension(float px) => Px(px);
}

public enum DimensionUnit : byte
{
    Auto,
    Pixels,
    Percent,
    None
}
