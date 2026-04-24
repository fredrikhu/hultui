using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;

namespace HultUI.Platform.Accessibility.Linux;

/// <summary>
/// Represents a single AT-SPI accessible object exported on the a11y bus. One
/// is created per <see cref="AccessibilityNode"/>; the instance stays alive
/// (and retains the same D-Bus path) as long as the node's
/// <see cref="ElementId"/> is present across tree rebuilds.
/// </summary>
/// <remarks>
/// The D-Bus method implementations (GetName, GetRole, GetChildAtIndex, etc.)
/// are stubbed out until on-desktop verification. This class currently just
/// holds the snapshotted state and computes the object path.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class AtSpiAccessible : IDisposable
{
    private readonly AtSpiBus _bus;

    public ElementId ElementId { get; }
    public string ObjectPath { get; }

    private string? _name;
    private string? _description;
    private string? _value;
    private AccessibilityRole _role;
    private bool _isFocusable;
    private bool _isFocused;
    private bool _isEnabled;
    private bool? _isToggled;

    public AtSpiAccessible(AtSpiBus bus, AccessibilityNode node)
    {
        _bus = bus;
        ElementId = node.ElementId;
        ObjectPath = AtSpiConstants.AccessiblePathPrefix + node.ElementId.Value;
        UpdateFrom(node);
    }

    public void UpdateFrom(AccessibilityNode node)
    {
        _name = node.Name;
        _description = node.Description;
        _value = node.Value;
        _role = node.Role;
        _isFocusable = node.IsFocusable;
        _isFocused = node.IsFocused;
        _isEnabled = node.IsEnabled;
        _isToggled = node.IsToggled;
    }

    public string Name => _name ?? "";
    public string Description => _description ?? "";
    public string Value => _value ?? "";
    public uint AtSpiRole => AtSpiConstants.MapRole(_role);
    public bool IsFocused => _isFocused;
    public bool IsEnabled => _isEnabled;
    public bool? IsToggled => _isToggled;

    public void Dispose()
    {
        // TODO: unregister from bus when per-object export is wired.
    }
}
