using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Headless;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace HultUI.Platform.Accessibility.Android.Tests;

/// <summary>The window's router as the bridge sees it: every action it routes, written down, and
/// the display scale.</summary>
internal sealed class FakeHost : IAccessibilityActionHost, IAccessibilityGeometryHost, IAccessibilityEditActions
{
    public List<string> Calls { get; } = [];
    public float Scale { get; set; } = 1f;
    public int ActiveChanges { get; private set; }

    public void InvokeElement(ElementId id) => Calls.Add($"invoke {id.Value}");
    public void ToggleElement(ElementId id) => Calls.Add($"toggle {id.Value}");
    public void SelectElement(ElementId id) => Calls.Add($"select {id.Value}");
    public void FocusElement(ElementId id) => Calls.Add($"focus {id.Value}");
    public void SetRangeValue(ElementId id, double value) => Calls.Add($"range {id.Value} {value}");
    public void ExpandCollapseElement(ElementId id, bool expand) => Calls.Add($"{(expand ? "expand" : "collapse")} {id.Value}");
    public void ScrollElementIntoView(ElementId id) => Calls.Add($"reveal {id.Value}");
    public void ActiveChanged() => ActiveChanges++;
    public void SetValue(ElementId id, string value) => Calls.Add($"value {id.Value} {value}");
    public void ScrollBy(ElementId id, float dx, float dy) => Calls.Add($"scroll {id.Value} {dx} {dy}");
    public void SetSelection(ElementId id, int anchor, int caret) => Calls.Add($"selection {id.Value} {anchor} {caret}");
    public void LongPress(ElementId id) => Calls.Add($"long press {id.Value}");
    public void Dismiss(ElementId id) => Calls.Add($"dismiss {id.Value}");
}

/// <summary>The events the bridge sends, in order.</summary>
internal sealed class RecordingSink : IAndroidEventSink
{
    public List<AndroidEvent> Events { get; } = [];
    public void Send(IReadOnlyList<AndroidEvent> events) => Events.AddRange(events);
    public void Clear() => Events.Clear();
}

/// <summary>
/// A headless window laid out at 1:1, and a bridge that TalkBack listens to, fed the window's trees
/// as the window feeds its own bridge: a build, then the diff from the tree before.
/// </summary>
internal sealed class Harness : IDisposable
{
    public HeadlessSession Session { get; }
    public FakeHost Host { get; } = new();
    public RecordingSink Sink { get; } = new();
    public AndroidAccessibilityBridge Bridge { get; }
    private AccessibilityNode? _applied;

    public Harness(Element root, int width = 400, int height = 800, float scale = 1f, bool enabled = true)
    {
        // As the bridge sets it, before the first tree is built here.
        AccessibilityTreeBuilder.MarksLongPress = true;
        Session = HeadlessSession.Create(width, height);
        Session.Window.SetRoot(root);
        Session.PumpFrame();
        Host.Scale = scale;
        Bridge = new AndroidAccessibilityBridge(Host);
        Bridge.SetEventSink(Sink);
        if (enabled) Bridge.SetEnabled(true);
        Apply();
        Sink.Clear();
    }

    public AccessibilityNode Tree => _applied!;

    /// <summary>A frame, then the window's tree now, applied with its diff from the last.</summary>
    public AccessibilityNode Apply()
    {
        Session.PumpFrame();
        var tree = Session.Window.AccessibilityTree;
        Bridge.ApplyChanges(AccessibilityTreeDiffer.Diff(_applied, tree), tree);
        _applied = tree;
        return tree;
    }

    /// <summary>The virtual view of <paramref name="element"/>, as TalkBack would read it.</summary>
    public AndroidNode Node(Element element) =>
        Bridge.Node(Bridge.IdOf(element.Id)) ?? throw new InvalidOperationException($"no node for {element}");

    public int Id(Element element) => Bridge.IdOf(element.Id);

    /// <summary>Reads every node from the host down, as TalkBack does when it first looks, so each
    /// has the id TalkBack would hold.</summary>
    public List<AndroidNode> ReadAll()
    {
        var all = new List<AndroidNode>();
        var stack = new Stack<int>();
        stack.Push(AndroidIds.Host);
        while (stack.Count > 0)
        {
            var node = Bridge.Node(stack.Pop());
            if (node is null) continue;
            all.Add(node);
            for (int i = node.Children.Length - 1; i >= 0; i--) stack.Push(node.Children[i]);
        }
        return all;
    }

    public void Dispose()
    {
        // AccessibilityClients is the process's: what the bridge reported is taken back.
        Bridge.SetEnabled(false);
        Session.Dispose();
    }
}
