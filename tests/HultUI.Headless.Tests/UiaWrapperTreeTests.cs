using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// Which tree the UIA bridge's wrappers point into. The window builds a new accessibility tree
/// on every rebuild, and a node reaches its whole tree through Parent and Children. The bridge
/// used to move a wrapper to the new tree only when the diff named its node (added, changed, or
/// a parent whose children changed), so every other wrapper kept the tree it was last moved to:
/// 500 checkbox rows held 201 trees (34 MB) after 200 toggles, and UIA sibling navigation
/// answered from an old tree. These drive the real bridge on a hidden window through the
/// headless window's own build, diff and apply, with no focus taken.
/// </summary>
[SupportedOSPlatform("windows")]
public class UiaWrapperTreeTests
{
    /// <summary>The real bridge, reported active whether or not a UIA client listens.</summary>
    private sealed class ActiveBridge(WindowsAccessibilityBridge inner) : IAccessibilityBridge
    {
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
    }

    private sealed class Rig : IDisposable
    {
        private readonly TestHwnd _hwnd = TestHwnd.Create();
        public HeadlessSession Session { get; } = HeadlessSession.Create(800, 600);
        public WindowsAccessibilityBridge Bridge { get; }

        public Rig()
        {
            var window = Session.Window.Window;
            var router = new AccessibilityActionRouter(window);
            var host = new UiaHostBridge(window, router);
            window.SetUiaHostBridge(host);
            window.SetActionRouter(router);
            Bridge = new WindowsAccessibilityBridge(_hwnd.Handle, host);
            Session.Window.InstallAccessibilityBridge(new ActiveBridge(Bridge));
        }

        public void Mount(Element root)
        {
            Session.Window.SetRoot(root);
            Session.PumpFrames(2);
        }

        /// <summary>The element's wrapper, or null when the bridge keeps none for it.</summary>
        public UiaFragment? Wrapper(Element element)
        {
            var native = Bridge.NativeFragment(element.Id);
            if (native == IntPtr.Zero) return null;
            try { return Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native)); }
            finally { Marshal.Release(native); }
        }

        /// <summary>The element <paramref name="direction"/> leads to from <paramref name="from"/>'s wrapper, or null.</summary>
        public ElementId? Navigate(Element from, int direction)
        {
            var wrapper = Wrapper(from) ?? throw new InvalidOperationException("no wrapper");
            Assert.Equal(0, wrapper.Navigate(direction, out var target));
            if (target == IntPtr.Zero) return null;
            try { return Assert.IsType<UiaFragment>(NativeProvider.LogicOf(target)).ElementId; }
            finally { Marshal.Release(target); }
        }

        public void Dispose()
        {
            Bridge.Dispose();
            Session.Dispose();
            _hwnd.Dispose();
        }
    }

    private static AccessibilityNode RootOf(AccessibilityNode node)
    {
        while (node.Parent is not null) node = node.Parent;
        return node;
    }

    private static (CheckboxElement[] Boxes, Element Root) CheckboxRows(int count)
    {
        var boxes = new CheckboxElement[count];
        var rows = new Element[count];
        for (int i = 0; i < count; i++)
        {
            boxes[i] = Checkbox.New(false, $"Option {i}");
            rows[i] = Flex.Row(gap: 8).FlexShrink(0).Children(boxes[i], TextB.New($"Row {i}"));
        }
        return (boxes, ScrollView.New().FlexGrow(1).Children(Flex.Column().FlexShrink(0).Children(rows)));
    }

    [WindowsFact]
    public void AfterManyDiffs_EveryWrapperPointsIntoTheCurrentTree()
    {
        using var rig = new Rig();
        var (boxes, root) = CheckboxRows(40);
        rig.Mount(root);

        // Each toggle is a diff that names one node; 30 of them used to leave 31 trees behind.
        for (int i = 0; i < 30; i++)
        {
            boxes[i].IsChecked = !boxes[i].IsChecked;
            rig.Session.PumpFrame();
        }

        var current = rig.Bridge.RootNode!;
        var trees = rig.Bridge.Wrappers.Select(w => RootOf(w.Node)).Distinct(ReferenceEqualityComparer.Instance).ToList();
        Assert.Same(current, Assert.Single(trees));

        // And each wrapper sits on its own node of that tree.
        var byId = new Dictionary<ElementId, AccessibilityNode>();
        Index(current);
        Assert.Equal(byId.Count, rig.Bridge.Wrappers.Count());
        Assert.All(rig.Bridge.Wrappers, w => Assert.Same(byId[w.ElementId], w.Node));

        void Index(AccessibilityNode node)
        {
            byId.Add(node.ElementId, node);
            foreach (var child in node.Children) Index(child);
        }
    }

    [WindowsFact]
    public void AnOldTree_IsCollected_OnceTheDiffsHaveMovedOn()
    {
        using var rig = new Rig();
        var (boxes, root) = CheckboxRows(40);
        rig.Mount(root);
        var first = WeakTree(rig);

        for (int i = 0; i < 10; i++)
        {
            boxes[i].IsChecked = !boxes[i].IsChecked;
            rig.Session.PumpFrame();
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(first.IsAlive, "a wrapper still holds the tree the window was mounted with");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference WeakTree(Rig rig) => new(rig.Bridge.RootNode!);

    [WindowsFact]
    public void SiblingNavigation_FollowsAnInsertAndARemoval()
    {
        using var rig = new Rig();
        var a = TextB.New("A");
        var b = TextB.New("B");
        var c = TextB.New("C");
        var x = TextB.New("X");
        var column = Flex.Column().FlexShrink(0).Children(a, b, c);
        rig.Mount(column);
        Assert.Equal(b.Id, rig.Navigate(a, UiaInterop.NavigateDirection_NextSibling));

        // A's own node does not change, so the diff never names it: its wrapper must still
        // learn that X is its next sibling now.
        column.ReplaceChildren(a, x, b, c);
        rig.Session.PumpFrame();
        Assert.Equal(x.Id, rig.Navigate(a, UiaInterop.NavigateDirection_NextSibling));
        Assert.Equal(a.Id, rig.Navigate(x, UiaInterop.NavigateDirection_PreviousSibling));
        Assert.Equal(x.Id, rig.Navigate(b, UiaInterop.NavigateDirection_PreviousSibling));

        column.ReplaceChildren(a, c);
        rig.Session.PumpFrame();
        Assert.Equal(c.Id, rig.Navigate(a, UiaInterop.NavigateDirection_NextSibling));
        Assert.Equal(a.Id, rig.Navigate(c, UiaInterop.NavigateDirection_PreviousSibling));
        Assert.Null(rig.Navigate(c, UiaInterop.NavigateDirection_NextSibling));
        Assert.Null(rig.Wrapper(x));
    }

    [WindowsFact]
    public void ANewRoot_DropsTheWrappersOfTheOldOne()
    {
        // A new root diffs from no tree: everything is added and nothing removed, so the
        // removal step never sees the old root's elements.
        using var rig = new Rig();
        var (oldBoxes, oldRoot) = CheckboxRows(10);
        rig.Mount(oldRoot);
        Assert.NotNull(rig.Wrapper(oldBoxes[3]));

        var (newBoxes, newRoot) = CheckboxRows(10);
        rig.Mount(newRoot);

        Assert.NotNull(rig.Wrapper(newBoxes[3]));
        Assert.Null(rig.Wrapper(oldBoxes[3]));
        var current = rig.Bridge.RootNode!;
        Assert.All(rig.Bridge.Wrappers, w => Assert.Same(current, RootOf(w.Node)));
    }
}
