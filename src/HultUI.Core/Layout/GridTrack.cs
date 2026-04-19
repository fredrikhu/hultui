namespace HultUI.Core.Layout;

public enum GridTrackKind : byte
{
    Fixed,
    Percent,
    Auto,
    Fr,
}

public readonly record struct GridTrack(GridTrackKind Kind, float Value)
{
    public static GridTrack Fixed(float px) => new(GridTrackKind.Fixed, px);
    public static GridTrack Percent(float pct) => new(GridTrackKind.Percent, pct);
    public static GridTrack Auto => new(GridTrackKind.Auto, 0f);
    public static GridTrack Fr(float weight) => new(GridTrackKind.Fr, weight);

    public bool IsAuto => Kind == GridTrackKind.Auto;
    public bool IsFr => Kind == GridTrackKind.Fr;
    public bool IsIntrinsic => Kind == GridTrackKind.Auto;
}
