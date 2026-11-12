using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// The UI Automation bridge makes a list's item node's wrapper when something first asks for it — a
/// client's navigation, a hit test, an event the item raises — not when a diff adds the node (plan 17
/// round 12 step 3). Each key of Junction's commit file walk replaced a file's 633 diff rows with the
/// next file's, and the bridge made 633 wrappers and let 633 go, 85.5 KB a key, for a list no client
/// walked. A wrapper made late answers what one made with the tree answers, and is moved with the
/// tree and let go with its item as any other.
/// </summary>
[SupportedOSPlatform("windows")]
public class UiaLazyItemWrapperTests
{
    private sealed class Pane(int count) : Component
    {
        public string Prefix = "file a";
        public int Count = count;
        public int Selected = -1;
        public readonly Ref<VirtualListElement<string>> List = new();

        public override Element Build()
        {
            var items = Enumerable.Range(0, Count).Select(i => $"{Prefix} line {i}").ToList();
            var list = VirtualListElement<string>.New()
                .ItemHeight(20f)
                .Items(items)
                .AccessibleItemLabel(Label)
                .SelectedIndex(Selected)
                .RenderWith((s, selected) => Flex.Row().Padding(new Thickness(2, 8)).Children(TextB.New(s).FontSize(12f)))
                .FlexGrow(1);
            // Another file is another list, as Junction's diff is (its key is the file).
            list.Key = Prefix;
            return Flex.Column().FlexGrow(1).Children(list.WithRef(List));
        }

        private static readonly Func<string, string?> Label = s => s;

        public void Show(string prefix) { Prefix = prefix; MarkDirty(); }
        public void Select(int index) { Selected = index; MarkDirty(); }
    }

    private sealed class Rig : IDisposable
    {
        public readonly HeadlessSession Session = HeadlessSession.Create(400, 300);
        private readonly TestHwnd _hwnd = TestHwnd.Create();
        public readonly WindowsAccessibilityBridge Uia;
        public readonly List<UiaRaised> Raised = [];
        public readonly List<ElementId> Disconnected = [];
        public readonly Pane Pane = new(600);

        public Rig()
        {
            Uia = new WindowsAccessibilityBridge(_hwnd.Handle, new NoHost());
            Uia.Raised = Raised.Add;
            Uia.ProviderDisconnected = Disconnected.Add;
            Session.Window.InstallAccessibilityBridge(new Listening(Uia));
            Session.Application.SetRoot(Pane.Mount());
            Session.PumpFrames(2);
            Session.PumpFrame(TimeSpan.FromMilliseconds(300));
        }

        public int ItemWrappers => Uia.Wrappers.Count(w => VirtualItemId.IsItem(w.ElementId));

        public AccessibilityNode ListNode()
        {
            var id = Pane.List.Current!.Id;
            return Find(Uia.RootNode!, id)!;

            static AccessibilityNode? Find(AccessibilityNode n, ElementId id)
            {
                if (n.ElementId == id) return n;
                foreach (var c in n.Children) if (Find(c, id) is { } f) return f;
                return null;
            }
        }

        /// <summary>What a client navigating to the item reads: its wrapper, through the provider UIA
        /// would get, and its name.</summary>
        public (UiaFragment Wrapper, string? Name) Ask(ElementId item)
        {
            IntPtr native = Uia.NativeFragment(item);
            Assert.NotEqual(IntPtr.Zero, native);
            try
            {
                var wrapper = (UiaFragment)NativeProvider.LogicOf(native)!;
                wrapper.GetPropertyValue(UiaInterop.UIA_NamePropertyId, out var v);
                string? name = v.AsString();
                v.Free();
                return (wrapper, name);
            }
            finally { Marshal.Release(native); }
        }

        public void Dispose()
        {
            Session.Dispose();
            Uia.Dispose();
            _hwnd.Dispose();
        }
    }

    /// <summary>A listener whatever the machine: UI Automation reports one only while a client listens.</summary>
    private sealed class Listening(IAccessibilityBridge inner) : IAccessibilityBridge, ITreeDiffBorrower
    {
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) => inner.ApplyPatchedProperties(patch, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
    }

    [WindowsFact]
    public void AListOfItemNodes_MakesNoItemWrapper_UntilAClientAsksForOne()
    {
        using var rig = new Rig();
        var list = rig.ListNode();
        Assert.Equal(600, list.Children.Count);
        Assert.Equal(0, rig.ItemWrappers);
        Assert.Contains(rig.Uia.Wrappers, w => w.ElementId == list.ElementId);

        // A client navigates to item 250: its wrapper is made then, from its node, and answers as
        // one made with the tree would.
        var item = list.Children[250];
        var (wrapper, name) = rig.Ask(item.ElementId);
        Assert.Equal("file a line 250", name);
        Assert.Same(item, wrapper.Node);
        Assert.Equal(item.BoundingRect, wrapper.Bounds);
        Assert.Equal(1, rig.ItemWrappers);
        // Asked again: the same wrapper.
        Assert.Same(wrapper, rig.Ask(item.ElementId).Wrapper);
        Assert.Equal(1, rig.ItemWrappers);
    }

    /// <summary>Another file's list, a key of the commit file walk: no wrapper made or let go for its
    /// 600 items, and the structure events the same: the parent's ChildrenBulkRemoved and
    /// ChildrenBulkAdded.</summary>
    [WindowsFact]
    public void AnotherList_MakesAndDropsNoItemWrapper_AndRaisesTheSameStructureEvents()
    {
        using var rig = new Rig();
        int wrappers = rig.Uia.Wrappers.Count();
        rig.Raised.Clear();

        rig.Pane.Show("file b");
        rig.Session.PumpFrame(TimeSpan.FromMilliseconds(16));

        var list = rig.ListNode();
        Assert.Equal("file b line 0", list.Children[0].Name);
        Assert.Equal(0, rig.ItemWrappers);
        Assert.Equal(wrappers, rig.Uia.Wrappers.Count());   // the list's wrapper for the old one's
        Assert.Empty(rig.Disconnected);
        Assert.Equal(
            [$"{UiaRaisedKind.Structure} {UiaInterop.StructureChangeType_ChildrenBulkAdded}",
             $"{UiaRaisedKind.Structure} {UiaInterop.StructureChangeType_ChildrenBulkRemoved}"],
            rig.Raised.Where(r => r.Kind == UiaRaisedKind.Structure).Select(r => $"{r.Kind} {r.Id}"));
    }

    /// <summary>A selection move raises SelectionItem.IsSelected on the item that had it and the one
    /// that has it, each on its own wrapper, made for the event from its node with its new state.</summary>
    [WindowsFact]
    public void ASelectionMove_RaisesOnTheTwoItems_FromWrappersMadeForTheEvent()
    {
        using var rig = new Rig();
        rig.Pane.Select(3);
        rig.Session.PumpFrame(TimeSpan.FromMilliseconds(16));
        rig.Raised.Clear();

        rig.Pane.Select(4);
        rig.Session.PumpFrame(TimeSpan.FromMilliseconds(16));

        var list = rig.ListNode();
        var told = rig.Raised.Where(r => r.Id == UiaInterop.UIA_SelectionItemIsSelectedPropertyId).Select(r => r.Element).ToList();
        Assert.Equal([list.Children[3].ElementId, list.Children[4].ElementId], told);
        Assert.Equal(false, rig.Uia.Wrappers.Single(w => w.ElementId == list.Children[3].ElementId).Selected);
        Assert.Equal(true, rig.Uia.Wrappers.Single(w => w.ElementId == list.Children[4].ElementId).Selected);
    }

    /// <summary>A wrapper made late is moved with the tree, and let go with its item: a client that
    /// holds it is told its provider is gone.</summary>
    [WindowsFact]
    public void AWrapperMadeLate_IsLetGoWithItsItem()
    {
        using var rig = new Rig();
        var item = rig.ListNode().Children[400].ElementId;
        rig.Ask(item);
        Assert.Equal(1, rig.ItemWrappers);

        rig.Pane.Count = 300;
        rig.Pane.Show("file a");   // the same list, fewer items
        rig.Session.PumpFrame(TimeSpan.FromMilliseconds(16));
        rig.Session.PumpFrame(TimeSpan.FromMilliseconds(300));

        Assert.Equal(300, rig.ListNode().Children.Count);
        Assert.Equal(0, rig.ItemWrappers);
        Assert.Equal([item], rig.Disconnected);
    }

    private sealed class NoHost : IUiaHostBridge
    {
        public void InvokeElement(ElementId id) { }
        public void ToggleElement(ElementId id) { }
        public void SelectElement(ElementId id) { }
        public void SetValue(ElementId id, string value) { }
        public void RequestFocus(ElementId id) { }
        public float ContentScale => 1f;
        public string? GetValue(ElementId id) => null;
        public bool? GetToggleState(ElementId id) => null;
        public int GetItemCount(ElementId listId) => 0;
        public string? GetItemLabel(ElementId listId, int index) => null;
        public bool GetItemSelected(ElementId listId, int index) => false;
        public int GetSelectedIndex(ElementId listId) => -1;
        public bool GetCanSelectMultiple(ElementId listId) => false;
        public IReadOnlyList<int> GetSelectedIndices(ElementId listId) => [];
        public void SelectItemOnly(ElementId listId, int index) { }
        public void SetItemSelected(ElementId listId, int index, bool selected) { }
        public int FindItemIndex(ElementId listId, int startAfter, AccessibleItemQuery query) => -1;
        public bool GetItemOffscreen(ElementId listId, int index) => false;
        public bool TryGetItemLayout(ElementId listId, int index, out Rect layoutRect) { layoutRect = default; return false; }
        public bool TryGetScrollInfo(ElementId id, out ScrollInfo info) { info = default; return false; }
        public void SetScrollPercent(ElementId id, double horizontalPercent, double verticalPercent) { }
        public void ScrollByAmount(ElementId id, int horizontalAmount, int verticalAmount) { }
        public bool TryGetRangeValue(ElementId id, out RangeValueInfo info) { info = default; return false; }
        public void SetRangeValue(ElementId id, double value) { }
    }
}
