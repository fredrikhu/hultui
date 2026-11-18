namespace HultUI.Core.Elements;

/// <summary>Where a scroll that reveals a band leaves it.</summary>
public enum ScrollAlignment
{
    /// <summary>The least scroll that shows the whole band; none when it is already shown.</summary>
    Nearest,
    /// <summary>The band's top at the viewport's top.</summary>
    Start,
    /// <summary>The band's middle at the viewport's middle.</summary>
    Center,
    /// <summary>The band's bottom at the viewport's bottom.</summary>
    End,
}
