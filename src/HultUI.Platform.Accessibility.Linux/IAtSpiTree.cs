using System.Runtime.Versioning;

namespace HultUI.Platform.Accessibility.Linux;

/// <summary>
/// Read-only view of the exported accessible tree, consumed by the D-Bus method
/// handlers on the bus receive thread. The bridge publishes an immutable
/// snapshot on each structural rebuild, so lookups here are lock-free.
/// </summary>
[SupportedOSPlatform("linux")]
internal interface IAtSpiTree
{
    /// <summary>Our unique bus name on the a11y bus.</summary>
    string BusName { get; }

    /// <summary>The desktop reference (our root's parent) returned by Embed.</summary>
    (string BusName, string Path) Desktop { get; }

    /// <summary>The application root snapshot, or null before the first build.</summary>
    AtSpiAccessible? Root { get; }

    /// <summary>Looks up a node by its D-Bus object path, or null if unknown.</summary>
    AtSpiAccessible? Find(string path);

    /// <summary>All exported nodes, for the Cache bulk-fetch.</summary>
    IReadOnlyCollection<AtSpiAccessible> All { get; }

    /// <summary>The application id the registry assigns via Properties.Set.</summary>
    int ApplicationId { get; set; }

    /// <summary>
    /// Performs the node's default action (click / toggle / select) by routing to
    /// the action host on the UI thread. Returns false when no action host is
    /// wired or the node exposes no action.
    /// </summary>
    bool DoAction(AtSpiAccessible node);

    /// <summary>Gives the node the keyboard (Component.GrabFocus), through the action host.
    /// False when it cannot take focus or nothing routes the request.</summary>
    bool GrabFocus(AtSpiAccessible node);

    /// <summary>Sets a range's value (Value.CurrentValue), through the action host. False when the
    /// node has no range, it is read-only, or nothing routes the request.</summary>
    bool SetCurrentValue(AtSpiAccessible node, double value);
}
