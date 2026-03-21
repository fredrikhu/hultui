namespace HultUI.Core.Primitives;

public readonly record struct Dimension
{
    public float Value { get; }
    public DimensionUnit Unit { get; }

    private Dimension(float value, DimensionUnit unit)
    {
        Value = value;
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
        DimensionUnit.Pixels => Value,
        DimensionUnit.Percent => Value / 100f * referenceSize,
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
