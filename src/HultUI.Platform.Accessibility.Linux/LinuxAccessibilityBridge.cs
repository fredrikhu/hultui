using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;

namespace HultUI.Platform.Accessibility.Linux;

/// <summary>
/// AT-SPI2 bridge. Connects to the a11y bus discovered via
/// <c>org.a11y.Bus.GetAddress</c> on the session bus, registers the
/// application root with the AT-SPI registry via <c>Socket.Embed</c>, and
/// exports per-node objects under <c>/org/a11y/atspi/accessible/&lt;id&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// This scaffold contains the connection + registration flow and the diff →
/// signal mapping, but the per-object D-Bus interface export is deferred to a
/// follow-up — see <see cref="AtSpiAccessible"/> and its friends. Until those
/// land the bridge always reports <see cref="IsActive"/>=false so the Window
/// short-circuits any tree-build cost.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class LinuxAccessibilityBridge : IAccessibilityBridge, IDisposable
{
    private readonly Dictionary<ElementId, AtSpiAccessible> _exports = [];
    private AtSpiBus? _bus;
    private AccessibilityNode? _rootNode;
    private bool _registered;
    private bool _disposed;

    public LinuxAccessibilityBridge()
    {
        // Try connecting to the a11y bus eagerly. If no AT is present the
        // registry won't respond and we stay inactive — that matches the "is
        // anyone listening" contract UIA expresses via UiaClientsAreListening.
        TryConnect();
    }

    public bool IsActive => _registered;

    private void TryConnect()
    {
        try
        {
            _bus = AtSpiBus.Open();
            if (_bus is null) return;
            _registered = _bus.RegisterApplication();
        }
        catch
        {
            // Silent: no AT-SPI available means we just stay inactive.
            _registered = false;
        }
    }

    public void ApplyChanges(TreeDiff diff, AccessibilityNode root)
    {
        if (!_registered || _bus is null) return;

        foreach (var added in diff.Added)
        {
            var export = new AtSpiAccessible(_bus, added.Node);
            _exports[added.Node.ElementId] = export;
            if (added.Parent is null) _rootNode = added.Node;
            _bus.EmitChildrenChanged(added.Parent?.ElementId, added.Node.ElementId, isAdd: true);
        }

        foreach (var removed in diff.Removed)
        {
            if (_exports.Remove(removed.Node.ElementId, out var export))
                export.Dispose();
            _bus.EmitChildrenChanged(removed.Parent?.ElementId, removed.Node.ElementId, isAdd: false);
        }

        foreach (var change in diff.PropertyChanged)
        {
            if (!_exports.TryGetValue(change.Node.ElementId, out var export)) continue;
            export.UpdateFrom(change.Node);
            var atSpiProp = AtSpiConstants.MapProperty(change.PropertyName);
            if (!string.IsNullOrEmpty(atSpiProp))
                _bus.EmitPropertyChange(change.Node.ElementId, atSpiProp);
        }
    }

    public void NotifyFocusChanged(AccessibilityNode? focused)
    {
        if (!_registered || _bus is null || focused is null) return;
        _bus.EmitStateChanged(focused.ElementId, "focused", active: true);
    }

    public void NotifyPropertyChanged(AccessibilityNode node, string propertyName)
    {
        if (!_registered || _bus is null) return;
        if (!_exports.TryGetValue(node.ElementId, out var export)) return;
        export.UpdateFrom(node);
        var atSpiProp = AtSpiConstants.MapProperty(propertyName);
        if (!string.IsNullOrEmpty(atSpiProp))
            _bus.EmitPropertyChange(node.ElementId, atSpiProp);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var export in _exports.Values)
            export.Dispose();
        _exports.Clear();

        _bus?.Dispose();
        _bus = null;
    }
}
