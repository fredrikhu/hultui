namespace HultUI.Core.Primitives;

public readonly record struct Color(byte R, byte G, byte B, byte A)
{
    public Color(byte r, byte g, byte b) : this(r, g, b, 255) { }

    public static Color Transparent => new(0, 0, 0, 0);
    public static Color Black => new(0, 0, 0);
    public static Color White => new(255, 255, 255);
    public static Color Red => new(255, 0, 0);
    public static Color Green => new(0, 128, 0);
    public static Color Blue => new(0, 0, 255);
    public static Color Gray => new(128, 128, 128);

    public static Color FromHex(string hex)
    {
        var span = hex.AsSpan();
        if (span.Length > 0 && span[0] == '#')
            span = span[1..];

        return span.Length switch
        {
            6 => new(
                Convert.ToByte(span[..2].ToString(), 16),
                Convert.ToByte(span[2..4].ToString(), 16),
                Convert.ToByte(span[4..6].ToString(), 16)),
            8 => new(
                Convert.ToByte(span[..2].ToString(), 16),
                Convert.ToByte(span[2..4].ToString(), 16),
                Convert.ToByte(span[4..6].ToString(), 16),
                Convert.ToByte(span[6..8].ToString(), 16)),
            _ => throw new ArgumentException($"Invalid hex color: {hex}")
        };
    }
}
