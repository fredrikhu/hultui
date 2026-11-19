namespace HultUI.Core.Elements;

/// <summary>What a row shows besides its item: whether it is selected, and whether it is the
/// list's current item.</summary>
public readonly record struct ListItemState(bool IsSelected, bool IsCurrent);
