using System.Diagnostics;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Linux;
using Xunit;

namespace HultUI.Platform.Accessibility.Linux.Tests;

/// <summary>
/// Seam W11 in the AT-SPI bridge: a described node is an object like any other, with a path
/// of its own and the role, name, states and action an assistive technology reads; acting on
/// it reaches the action host by the node's id. And whether assistive technology listens
/// follows <c>org.a11y.Status</c>.
/// </summary>
[Collection(AccessibilityClientsCollection.Name)]
public class AccessibleContentBridgeTests
{
    private sealed class Described : BoxElement, IAccessibleContentSource
    {
        public IReadOnlyList<AccessibleContentNode> AccessibleContent { get; init; } = [];
        public float AccessibleContentScale => 1f;
        public void PerformAccessibleAction(int key, AccessibleContentAction action, string? value) { }
    }

    private sealed class Host : IAccessibilityActionHost
    {
        public readonly List<(string What, ElementId Id)> Asked = [];
        public void InvokeElement(ElementId id) => Asked.Add(("invoke", id));
        public void ToggleElement(ElementId id) => Asked.Add(("toggle", id));
        public void SelectElement(ElementId id) => Asked.Add(("select", id));
        public void FocusElement(ElementId id) => Asked.Add(("focus", id));
    }

    private static (Described Element, LinuxAccessibilityBridge Bridge, IAtSpiTree Tree, Host Host) Bridge()
    {
        var described = new Described
        {
            AccessibleName = "Web view",
            AccessibleContent =
            [
                new(1, AccessibilityRole.Document, "A page", new Rect(0, 0, 100, 100))
                {
                    Children =
                    [
                        new(30, AccessibilityRole.Button, "Press me", new Rect(10, 10, 50, 20)) { IsFocusable = true },
                        new(18, AccessibilityRole.Checkbox, "Remember me", new Rect(10, 40, 20, 20)) { IsToggled = true, IsFocusable = true },
                        new(7, AccessibilityRole.Label, "Settings", new Rect(10, 70, 80, 20)),
                    ],
                },
            ],
        };
        var root = BoxElement.New();
        root.AddChild(described);
        var host = new Host();
        var bridge = new LinuxAccessibilityBridge(host, connect: false);
        bridge.Publish(AccessibilityTreeBuilder.Build(root));
        return (described, bridge, bridge, host);
    }

    private static string PathOf(ElementId id) => AtSpiConstants.AccessiblePathPrefix + id.Value;

    [Fact]
    public void ADescribedNode_IsAnObject_WithItsPathRoleAndName()
    {
        var (described, _, tree, _) = Bridge();
        var page = tree.Find(PathOf(ContentNodeId.For(described.Id, 1)));
        Assert.NotNull(page);
        Assert.Equal(AtSpiConstants.RoleDocumentWeb, page.Role);
        Assert.Equal("A page", page.Name);
        Assert.Equal(PathOf(described.Id), page.ParentPath);
        Assert.Equal(3, page.ChildCount);

        var button = tree.Find(PathOf(ContentNodeId.For(described.Id, 30)))!;
        Assert.Equal(AtSpiConstants.RolePushButton, button.Role);
        Assert.Equal("click", button.ActionName);
        Assert.Contains(AtSpiConstants.ActionInterface, button.Interfaces);
        var check = tree.Find(PathOf(ContentNodeId.For(described.Id, 18)))!;
        Assert.True(check.Toggled);
        Assert.Equal("toggle", check.ActionName);
    }

    [Fact]
    public void ActingOnADescribedNode_AsksTheHost_ByItsId()
    {
        var (described, _, tree, host) = Bridge();
        var button = tree.Find(PathOf(ContentNodeId.For(described.Id, 30)))!;
        var check = tree.Find(PathOf(ContentNodeId.For(described.Id, 18)))!;
        var label = tree.Find(PathOf(ContentNodeId.For(described.Id, 7)))!;
        Assert.True(tree.DoAction(button));
        Assert.True(tree.DoAction(check));
        Assert.False(tree.DoAction(label));   // a label has no action
        Assert.True(tree.GrabFocus(button));
        Assert.False(tree.GrabFocus(label));  // nor takes the keyboard
        Assert.Equal(
            [("invoke", ContentNodeId.For(described.Id, 30)), ("toggle", ContentNodeId.For(described.Id, 18)),
             ("focus", ContentNodeId.For(described.Id, 30))],
            host.Asked);
    }

    [Fact]
    public void TheApplication_IsNamedAfterItsEntryAssembly()
    {
        // A screen reader announces the application by this name: it was empty.
        var (_, _, tree, _) = Bridge();
        var expected = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
        Assert.False(string.IsNullOrEmpty(expected));
        Assert.Equal(expected, tree.Root!.Name);
        Assert.Equal(AtSpiConstants.RoleApplication, tree.Root.Role);
    }

    /// <summary>Linux with an accessibility bus: set <c>org.a11y.Status.IsEnabled</c> as an
    /// assistive technology does, and see the bridge report it and turn on, and off again; then
    /// put it back.</summary>
    [Fact]
    public void WhetherAnyoneListens_FollowsTheStatusOnTheBus()
    {
        if (!OperatingSystem.IsLinux() || Busctl("get-property org.a11y.Bus /org/a11y/bus org.a11y.Status IsEnabled") is not { } before)
            return;   // no session bus with org.a11y.Bus here: nothing to follow
        var forced = AccessibilityClients.Force;
        AccessibilityClients.Force = null;
        using var bridge = new LinuxAccessibilityBridge();
        try
        {
            Busctl("set-property org.a11y.Bus /org/a11y/bus org.a11y.Status IsEnabled b true");
            Assert.True(SpinUntil(() => AccessibilityClients.Active), "IsEnabled true was not seen");
            Assert.True(bridge.IsActive);
            Busctl("set-property org.a11y.Bus /org/a11y/bus org.a11y.Status IsEnabled b false");
            if (Busctl("get-property org.a11y.Bus /org/a11y/bus org.a11y.Status ScreenReaderEnabled")?.Trim() == "b false")
            {
                Assert.True(SpinUntil(() => !AccessibilityClients.Active), "IsEnabled false was not seen");
                Assert.False(bridge.IsActive);
            }
        }
        finally
        {
            Busctl($"set-property org.a11y.Bus /org/a11y/bus org.a11y.Status IsEnabled {before.Trim()}");
            AccessibilityClients.Report(false);
            AccessibilityClients.Force = forced;
        }
    }

    private static bool SpinUntil(Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.Elapsed < TimeSpan.FromSeconds(5)) Thread.Sleep(20);
        return done();
    }

    private static string? Busctl(string arguments)
    {
        try
        {
            var start = new ProcessStartInfo("busctl", "--user " + arguments) { RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(start)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return p.ExitCode == 0 ? output : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }
}
