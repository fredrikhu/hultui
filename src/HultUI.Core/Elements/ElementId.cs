namespace HultUI.Core.Elements;

public readonly record struct ElementId(ulong Value)
{
    private static ulong _next;

    public static ElementId New() => new(Interlocked.Increment(ref _next));
}
