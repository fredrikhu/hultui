namespace HultUI.Core.Elements;

public readonly struct OverlayHandle : IEquatable<OverlayHandle>
{
    internal int Id { get; init; }

    public bool IsValid => Id != 0;

    public bool Equals(OverlayHandle other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is OverlayHandle h && Equals(h);
    public override int GetHashCode() => Id;
    public static bool operator ==(OverlayHandle left, OverlayHandle right) => left.Equals(right);
    public static bool operator !=(OverlayHandle left, OverlayHandle right) => !left.Equals(right);
}
