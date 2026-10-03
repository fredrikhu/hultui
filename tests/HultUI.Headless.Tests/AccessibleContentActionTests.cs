using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Headless;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>Seam W11: what assistive technology asks of a described node goes to the element
/// that described it, by the node's key, on the UI thread.</summary>
public class AccessibleContentActionTests
{
    private sealed class Described : BoxElement, IAccessibleContentSource
    {
        public readonly List<(int Key, AccessibleContentAction Action, string? Value, int Thread)> Actions = [];
        public IReadOnlyList<AccessibleContentNode> AccessibleContent { get; } =
        [
            new(1, AccessibilityRole.Document, "Page", new Rect(0, 0, 100, 100))
            {
                Children = [new(42, AccessibilityRole.Button, "Press me", new Rect(10, 10, 50, 20)) { IsFocusable = true }],
            },
        ];
        public float AccessibleContentScale => 1f;
        public void PerformAccessibleAction(int key, AccessibleContentAction action, string? value) =>
            Actions.Add((key, action, value, Environment.CurrentManagedThreadId));
    }

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }

    [Fact]
    public void AnActionOnADescribedNode_ReachesItsSource_OnTheUiThread()
    {
        using var session = HeadlessSession.Create(400, 300);
        var described = new Described { AccessibleName = "Content" };
        described.Box.Width = 200;
        described.Box.Height = 150;
        session.Window.SetRoot(Box.New().FlexGrow(1).Children(described));
        session.PumpFrame();

        var button = Walk(session.Window.AccessibilityTree).First(n => n.Name == "Press me");
        Assert.True(ContentNodeId.TryDecode(button.ElementId, out _, out var key));
        Assert.Equal(42, key);

        var router = new AccessibilityActionRouter(session.Window.Window);
        session.Window.Window.SetActionRouter(router);
        int uiThread = Environment.CurrentManagedThreadId;
        var asked = new Thread(() =>
        {
            // As an assistive technology's thread would.
            router.InvokeElement(button.ElementId);
            router.ToggleElement(button.ElementId);
            router.SelectElement(button.ElementId);
            router.FocusElement(button.ElementId);
            router.SetValue(button.ElementId, "text");
        });
        asked.Start();
        asked.Join();
        Assert.Empty(described.Actions);   // queued, not run on the asking thread
        session.PumpFrame();

        Assert.Equal(
            [(42, AccessibleContentAction.Invoke, (string?)null), (42, AccessibleContentAction.Toggle, null),
             (42, AccessibleContentAction.Select, null), (42, AccessibleContentAction.Focus, null),
             (42, AccessibleContentAction.SetValue, "text")],
            described.Actions.Select(a => (a.Key, a.Action, a.Value)));
        Assert.All(described.Actions, a => Assert.Equal(uiThread, a.Thread));
    }
}
