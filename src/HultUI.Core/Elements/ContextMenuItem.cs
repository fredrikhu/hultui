namespace HultUI.Core.Elements;

public sealed class ContextMenuItem
{
    public string Label { get; init; } = "";
    public Action? Action { get; init; }
    public bool IsSeparator { get; init; }
    public bool IsDisabled { get; init; }
    public string? ShortcutHint { get; init; }

    public static ContextMenuItem Separator() => new() { IsSeparator = true };
}
