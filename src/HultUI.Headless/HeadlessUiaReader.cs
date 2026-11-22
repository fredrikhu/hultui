using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Platform.Accessibility.Windows;

namespace HultUI.Headless;

/// <summary>What a UI Automation event the bridge raised was (<see cref="HeadlessUiaReader.Raised"/>).</summary>
public enum HeadlessUiaEventKind { Event, Property, Structure, Notification }

/// <summary>A UI Automation event the bridge raised: its kind, the event's or property's UIA id, and the
/// element it was raised on (an item a list serves on demand by its <see cref="VirtualItemId"/>).</summary>
public readonly record struct HeadlessUiaEvent(HeadlessUiaEventKind Kind, int Id, ElementId? Element);

/// <summary>
/// What a Windows screen reader is told by a headless window: the real UI Automation bridge, on a hidden
/// window of its own, driven by the window's per-frame path as a listening client would have it, with
/// every event it raises kept in order (<see cref="Raised"/>). For an app's tests of what Narrator or
/// NVDA hears: one focus event a move, on the node the move ends on, with its state as of then.
/// Windows only. Dispose it before the session.
/// </summary>
/// <example>
/// <code>
/// using var reader = HeadlessUiaReader.Install(session.Window);
/// session.Window.PressKey(KeyCode.Down);
/// session.PumpFrames(2);
/// Assert.Equal([VirtualItemId.For(list.Id, 3)], reader.FocusEvents());
/// </code>
/// </example>
[SupportedOSPlatform("windows")]
public sealed class HeadlessUiaReader : IDisposable
{
    /// <summary>UIA's focus-changed event id.</summary>
    public const int FocusChangedEvent = UiaInterop.UIA_AutomationFocusChangedEventId;
    /// <summary>UIA's Name property id.</summary>
    public const int NameProperty = UiaInterop.UIA_NamePropertyId;
    /// <summary>UIA's HasKeyboardFocus property id.</summary>
    public const int HasKeyboardFocusProperty = UiaInterop.UIA_HasKeyboardFocusPropertyId;
    /// <summary>UIA's SelectionItem.IsSelected property id.</summary>
    public const int IsSelectedProperty = UiaInterop.UIA_SelectionItemIsSelectedPropertyId;

    private readonly IntPtr _hwnd;
    private readonly WindowsAccessibilityBridge _bridge;
    private readonly List<HeadlessUiaEvent> _raised = [];
    private readonly List<ElementId> _disconnected = [];
    private readonly Listening _listening;

    private HeadlessUiaReader(HeadlessWindow window)
    {
        // A "STATIC" window: a system class, so no class to register and no window procedure to keep
        // alive; the bridge subclasses it. A popup that is never shown.
        IntPtr cls = Marshal.StringToHGlobalUni("STATIC");
        try
        {
            _hwnd = CreateWindowExW(0, cls, IntPtr.Zero, unchecked((int)0x80000000) /* WS_POPUP */,
                0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }
        finally { Marshal.FreeHGlobal(cls); }
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("Could not create the reader's hidden window.");
        var w = window.Window;
        var router = new AccessibilityActionRouter(w);
        var host = new UiaHostBridge(w, router);
        w.SetUiaHostBridge(host);
        w.SetActionRouter(router);
        _bridge = new WindowsAccessibilityBridge(_hwnd, host)
        {
            Raised = e => _raised.Add(new HeadlessUiaEvent((HeadlessUiaEventKind)e.Kind, e.Id, e.Element)),
            // On the bridge's disconnect thread.
            ProviderDisconnected = id => { lock (_disconnected) _disconnected.Add(id); },
        };
        _listening = new Listening(_bridge);
        window.InstallAccessibilityBridge(_listening);
    }

    /// <summary>Installs the UI Automation bridge on <paramref name="window"/>, reported listening
    /// whether or not a client is.</summary>
    public static HeadlessUiaReader Install(HeadlessWindow window) => new(window);

    /// <summary>Every event the bridge raised since it was installed or <see cref="Clear"/>ed, in order.</summary>
    public IReadOnlyList<HeadlessUiaEvent> Raised => _raised;

    /// <summary>The elements the focus events went to, in order.</summary>
    public List<ElementId?> FocusEvents() =>
        _raised.Where(e => e.Kind == HeadlessUiaEventKind.Event && e.Id == FocusChangedEvent).Select(e => e.Element).ToList();

    /// <summary>The elements whose providers the bridge disconnected (they left the tree) since it was
    /// installed or <see cref="Clear"/>ed: a client holding one is told it is gone. The bridge tells UI
    /// Automation on a thread of its own after the frame; this waits for what it has not told yet.</summary>
    public IReadOnlyList<ElementId> Disconnected
    {
        get
        {
            Flush();
            lock (_disconnected) return _disconnected.ToArray();
        }
    }

    /// <summary>Forgets the events raised and the providers disconnected so far.</summary>
    public void Clear()
    {
        Flush();
        _raised.Clear();
        lock (_disconnected) _disconnected.Clear();
    }

    private void Flush()
    {
        if (!_bridge.FlushDisconnects(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("The UI Automation bridge did not tell its disconnects within 10 s.");
    }

    /// <summary>The tree the bridge has applied.</summary>
    public AccessibilityNode? Tree => _bridge.RootNode;

    /// <summary>The node of <paramref name="id"/> in the applied tree, or null (an item served on demand has none).</summary>
    public AccessibilityNode? Node(ElementId id) => _bridge.RootNode is { } root ? Find(root, id) : null;

    /// <summary>What UIA's GetFocus on the window's root answers: a node's element, an item a list serves on
    /// demand (its <see cref="VirtualItemId"/>), or null for none.</summary>
    public ElementId? GetFocus()
    {
        var root = _bridge.NativeRootFragmentRoot();
        try
        {
            ((UiaFragmentRoot)NativeProvider.LogicOf(root)!).GetFocus(out var focus);
            if (focus == IntPtr.Zero) return null;
            try { return IdOf(focus); }
            finally { Marshal.Release(focus); }
        }
        finally { Marshal.Release(root); }
    }

    /// <summary>The Name the provider of <paramref name="id"/> answers.</summary>
    public string? Name(ElementId id) => Ask(id, NameProperty, v => v.AsString());

    /// <summary>The SelectionItem.IsSelected the provider of <paramref name="id"/> answers; null when it has none.</summary>
    public bool? IsSelected(ElementId id) => Ask(id, IsSelectedProperty, v => v.Vt == UiaVariant.VT_BOOL ? v.ShortValue != 0 : (bool?)null);

    /// <summary>Whether the provider of <paramref name="id"/> answers that it has the keyboard focus.</summary>
    public bool HasKeyboardFocus(ElementId id) => Ask(id, HasKeyboardFocusProperty, v => v.Vt == UiaVariant.VT_BOOL && v.ShortValue != 0);

    /// <summary>The place in its set and the set's size the provider of <paramref name="id"/> answers; (0, 0) for none.</summary>
    public (int At, int Of) Place(ElementId id) =>
        (Ask(id, UiaInterop.UIA_PositionInSetPropertyId, v => v.Vt == 0 ? 0 : v.IntValue),
         Ask(id, UiaInterop.UIA_SizeOfSetPropertyId, v => v.Vt == 0 ? 0 : v.IntValue));

    private T Ask<T>(ElementId id, int property, Func<UiaVariant, T> read)
    {
        IntPtr native = VirtualItemId.TryDecode(id, out var listId, out int index) && Node(listId) is { IsVirtualItemContainer: true }
            ? _bridge.NativeVirtualItem(listId, index)
            : _bridge.NativeFragment(id);
        if (native == IntPtr.Zero) throw new InvalidOperationException($"No provider for {id}.");
        try
        {
            var simple = (INativeSimple)NativeProvider.LogicOf(native)!;
            simple.GetPropertyValue(property, out var value);
            try { return read(value); }
            finally { value.Free(); }
        }
        finally { Marshal.Release(native); }
    }

    private static ElementId? IdOf(IntPtr native) => NativeProvider.LogicOf(native) switch
    {
        UiaFragment f => f.ElementId,
        UiaVirtualItem v => VirtualItemId.For(v.ListId, v.Index),
        _ => null,
    };

    private static AccessibilityNode? Find(AccessibilityNode node, ElementId id)
    {
        if (node.ElementId == id) return node;
        var children = node.Children;
        for (int i = 0; i < children.Count; i++)
            if (Find(children[i], id) is { } found) return found;
        return null;
    }

    public void Dispose()
    {
        // The window may run a frame after this: it finds no client listening, and drops its tree.
        _listening.Disposed = true;
        _bridge.Dispose();
        DestroyWindow(_hwnd);
    }

    /// <summary>The bridge, reported listening whether or not a client is.</summary>
    private sealed class Listening(IAccessibilityBridge inner) : IAccessibilityBridge, IEagerItemsLimit
    {
        public bool Disposed;
        public bool IsActive => !Disposed;
        // A list over 200 items is served on demand, as in a real window.
        public int MaxEagerVirtualItems => AccessibilityTreeBuilder.MaxEagerVirtualItemsOf(inner);
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) => inner.ApplyPatchedProperties(patch, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
        public void NotifyChangesApplied(AccessibilityNode root) => inner.NotifyChangesApplied(root);
        public void NotifyWindowActivated() => inner.NotifyWindowActivated();
        public void Announce(AccessibilityNode? source, string text, AnnouncementPoliteness politeness) => inner.Announce(source, text, politeness);
    }

    // Blittable signatures: no marshalling to make at run time, under AOT or not.
    [DllImport("user32.dll")]
    private static extern IntPtr CreateWindowExW(int exStyle, IntPtr cls, IntPtr name, int style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("user32.dll")]
    private static extern int DestroyWindow(IntPtr hWnd);
}
