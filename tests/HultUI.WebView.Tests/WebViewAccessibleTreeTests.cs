using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Headless;
using HultUI.WebView.Testing;
using Xunit;

namespace HultUI.WebView.Tests;

/// <summary>
/// Step 5.6 and seam W11: an engine that hands its page's accessibility tree over as data
/// (CEF) has it served as the web view's children, in layout units; assistive technology's
/// actions reach the engine by node; and the tree is on only while someone listens.
/// </summary>
public class WebViewAccessibleTreeTests
{
    private static WebViewElement? _last;

    private static (HeadlessSession Session, WebViewController Web, FakeWebEngineView View) Show(float scale)
    {
        var session = HeadlessSession.Create(400, 300, scale);
        var engine = new FakeWebEngine();
        var web = new WebViewController(engine);
        var element = WebViewElement.New(web);
        _last = element;
        session.Application.SetRoot(Flex.Column().Children(Box.New().Height(40f).FlexShrink(0), element.FlexGrow(1)));
        session.PumpFrames(2);
        return (session, web, engine.Views[0]);
    }

    private static readonly AccessibleContentNode Form = new(20, AccessibilityRole.Document, "A11y form", new Rect(0, 0, 800, 520))
    {
        Children =
        [
            new(26, AccessibilityRole.Heading, "Settings", new Rect(24, 64, 400, 130)),
            new(30, AccessibilityRole.Button, "Press me", new Rect(1220, 260, 220, 72)) { IsFocusable = true },
        ],
    };

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }

    [Fact]
    public void ThePagesTree_IsTheWebViewsChildren_InLayoutUnits()
    {
        var (session, web, view) = Show(scale: 2f);
        using var _ = session;
        using var __ = web;
        var bridge = session.Window.InstallAccessibilityBridge();
        session.PumpFrame();
        bridge.ClearDiffs();

        view.AccessibleTree = [Form];
        session.PumpFrame();

        var document = Walk(bridge.CurrentTree!).Single(n => n.Role == AccessibilityRole.Document);
        Assert.Equal("A11y form", document.Name);
        Assert.Equal(AccessibilityRole.Group, document.Parent!.Role);   // the web view's own node
        // Device pixels at 2 per layout unit, from the page's top-left at (0, 40).
        Assert.Equal(new Rect(0, 40, 400, 260), document.BoundingRect);
        var heading = document.Children[0];
        Assert.Equal(new Rect(12, 72, 200, 65), heading.BoundingRect);
        Assert.True(document.Children[1].IsOffscreen);   // 610 layout units across a 400-wide window
        Assert.Equal(3, Assert.Single(bridge.Diffs).Added.Count);
    }

    [Fact]
    public void AnActionOnAPageNode_ReachesTheEngine_ByItsKey()
    {
        var (session, web, view) = Show(scale: 1f);
        using var _ = session;
        using var __ = web;
        session.Window.InstallAccessibilityBridge();
        view.AccessibleTree = [Form];
        session.PumpFrame();
        var button = Walk(session.Window.AccessibilityTree).Single(n => n.Name == "Press me");

        // What the window's action router does with a described node's id (tested with it in
        // HultUI.Headless.Tests): the element that described it is asked, by the node's key.
        Assert.True(ContentNodeId.TryDecode(button.ElementId, out var owner, out var key));
        var element = _last!;
        Assert.Equal(element.Id, owner);
        var source = (IAccessibleContentSource)element;
        source.PerformAccessibleAction(key, AccessibleContentAction.Invoke);
        source.PerformAccessibleAction(key, AccessibleContentAction.Focus);
        source.PerformAccessibleAction(key, AccessibleContentAction.SetValue, "x");

        Assert.Equal(
            [(30, AccessibleContentAction.Invoke, (string?)null), (30, AccessibleContentAction.Focus, null), (30, AccessibleContentAction.SetValue, "x")],
            view.AccessibleActions);
    }

    [Fact]
    public void ThePagesTree_IsOn_OnlyWhileSomeoneListens()
    {
        var forced = AccessibilityClients.Force;
        try
        {
            AccessibilityClients.Force = false;
            var (session, web, view) = Show(scale: 1f);
            using var _ = session;
            using var __ = web;
            Assert.False(view.AccessibilityEnabled);

            AccessibilityClients.Force = true;
            session.PumpFrame();
            Assert.True(view.AccessibilityEnabled);

            AccessibilityClients.Force = false;
            session.PumpFrame();
            Assert.False(view.AccessibilityEnabled);

            // A page made while someone listens starts with it on.
            AccessibilityClients.Force = true;
            using var later = new WebViewController(new FakeWebEngine());
            Assert.True(((FakeWebEngineView)later.View).AccessibilityEnabled);
        }
        finally { AccessibilityClients.Force = forced; }
    }
}
