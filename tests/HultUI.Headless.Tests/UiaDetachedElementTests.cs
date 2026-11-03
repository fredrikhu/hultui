using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.RichText;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using Xunit.Abstractions;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// An element that left the tree answers UI Automation with UIA_E_ELEMENTNOTAVAILABLE, not with
/// what it showed. A client that held a text, a field, its value pattern and a range of its text
/// and a report before the window's content was replaced (a screen locked over a person's name) read the old
/// name, value and text from all of them, and navigated the old tree from the text.
/// The calls go through the providers' COM vtables, as UI Automation makes them.
/// </summary>
[SupportedOSPlatform("windows")]
public class UiaDetachedElementTests(ITestOutputHelper output)
{
    private const int NotAvailable = unchecked((int)0x80040201);

    private sealed class Active(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) => inner.ApplyPatchedProperties(patch, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
    }

    // The vtable slots UI Automation calls (after QueryInterface, AddRef, Release).
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetPropertyValueFn(IntPtr self, int id, out UiaVariant value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int OutPtrIntFn(IntPtr self, int arg, out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int OutPtrFn(IntPtr self, out IntPtr result);

    private static T Slot<T>(IntPtr com, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(com), slot * IntPtr.Size));

    private static (int Hr, string? Text) Name(IntPtr simple) => Property(simple, UiaInterop.UIA_NamePropertyId);

    private static (int Hr, string? Text) Property(IntPtr simple, int id)
    {
        int hr = Slot<GetPropertyValueFn>(simple, 5)(simple, id, out var v);
        string? text = v.Vt == UiaVariant.VT_BSTR ? Marshal.PtrToStringBSTR(v.PointerValue) : null;
        if (v.Vt == UiaVariant.VT_BSTR) Marshal.FreeBSTR(v.PointerValue);
        return (hr, text);
    }

    private static (int Hr, IntPtr Result) Navigate(IntPtr fragment, int direction)
    {
        int hr = Slot<OutPtrIntFn>(fragment, 3)(fragment, direction, out var p);
        return (hr, p);
    }

    private static IntPtr Pattern(IntPtr simple, int patternId)
    {
        Assert.Equal(0, Slot<OutPtrIntFn>(simple, 4)(simple, patternId, out var p));
        Assert.NotEqual(IntPtr.Zero, p);
        return p;
    }

    private static (int Hr, string? Text) Bstr(IntPtr com, int slot)
    {
        int hr = Slot<OutPtrFn>(com, slot)(com, out var b);
        string? text = b == IntPtr.Zero ? null : Marshal.PtrToStringBSTR(b);
        if (b != IntPtr.Zero) Marshal.FreeBSTR(b);
        return (hr, text);
    }

    private static (int Hr, string? Text) RangeText(IntPtr range)
    {
        int hr = Slot<OutPtrIntFn>(range, 12)(range, -1, out var b);   // ITextRangeProvider::GetText
        string? text = b == IntPtr.Zero ? null : Marshal.PtrToStringBSTR(b);
        if (b != IntPtr.Zero) Marshal.FreeBSTR(b);
        return (hr, text);
    }

    /// <summary>The name of a provider a navigation returned, which this releases.</summary>
    private static string Described((int Hr, IntPtr Result) nav)
    {
        if (nav.Result == IntPtr.Zero) return $"hr 0x{nav.Hr:X8}, null";
        try
        {
            var simple = NativeProvider.Tearoff(nav.Result, NativeProvider.As.Simple);
            try { return $"hr 0x{nav.Hr:X8}, an element named {Name(simple).Text ?? "(none)"}"; }
            finally { Marshal.Release(simple); }
        }
        finally { Marshal.Release(nav.Result); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference OldTree(IntPtr simple)
    {
        var node = Assert.IsType<UiaFragment>(NativeProvider.LogicOf(simple)).Node;
        while (node.Parent is not null) node = node.Parent;
        return new(node);
    }

    [WindowsFact]
    public void AnElementThatLeftTheTree_AnswersElementNotAvailable_AndHoldsNoOldTree()
    {
        using var hwnd = TestHwnd.Create();
        using var session = HeadlessSession.Create(600, 400);
        var window = session.Window.Window;
        var router = new AccessibilityActionRouter(window);
        var host = new UiaHostBridge(window, router);
        window.SetUiaHostBridge(host);
        window.SetActionRouter(router);
        using var bridge = new WindowsAccessibilityBridge(hwnd.Handle, host);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        void Settle() { for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(150)); }

        var label = TextB.New("Anna Andersson");
        var field = TextInput.New("Anna Andersson");
        var report = RichTextEditor.New(new RichDocument([RichBlock.Paragraph("Anna Andersson")])).Width(300f).Height(100f);
        session.Window.SetRoot(Flex.Column().Children(label, Flex.Row().Children(field), report));
        Settle();

        // What a client holds: the text and the field as elements, the field's value pattern
        // and a range over all of a report's text.
        var labelSimple = bridge.NativeFragmentSimple(label.Id);
        var labelFragment = bridge.NativeFragment(label.Id);
        var fieldSimple = bridge.NativeFragmentSimple(field.Id);
        Assert.NotEqual(IntPtr.Zero, labelSimple);
        Assert.NotEqual(IntPtr.Zero, fieldSimple);
        var value = Pattern(fieldSimple, UiaInterop.UIA_ValuePatternId);
        var reportSimple = bridge.NativeFragmentSimple(report.Id);
        var text = Pattern(reportSimple, UiaInterop.UIA_TextPatternId);
        Assert.Equal(0, Slot<OutPtrFn>(text, 7)(text, out var range));   // ITextProvider::GetDocumentRange
        Assert.Equal((0, "Anna Andersson"), Name(labelSimple));
        Assert.Equal((0, "Anna Andersson"), RangeText(range));
        Assert.Equal((0, "Anna Andersson"), Property(fieldSimple, UiaInterop.UIA_ValueValuePropertyId));
        var oldTree = OldTree(labelSimple);   // the tree the bridge was told, its root

        var disconnected = new List<ElementId>();
        bridge.ProviderDisconnected = disconnected.Add;
        var lockScreen = TextB.New("Locked");
        session.Window.SetRoot(lockScreen);
        Settle();

        var name = Name(labelSimple);
        var fieldValue = Property(fieldSimple, UiaInterop.UIA_ValueValuePropertyId);
        var patternValue = Bstr(value, 4);   // IValueProvider::get_Value
        var rangeText = RangeText(range);
        var parent = Navigate(labelFragment, UiaInterop.NavigateDirection_Parent);
        string parentSays = Described(parent);
        string siblingSays = Described(Navigate(labelFragment, UiaInterop.NavigateDirection_NextSibling));
        output.WriteLine($"Name: hr 0x{name.Hr:X8}, {name.Text ?? "(empty)"}");
        output.WriteLine($"Field ValueValue: hr 0x{fieldValue.Hr:X8}, {fieldValue.Text ?? "(empty)"}");
        output.WriteLine($"Value pattern get_Value: hr 0x{patternValue.Hr:X8}, {patternValue.Text ?? "(null)"}");
        output.WriteLine($"Text range GetText: hr 0x{rangeText.Hr:X8}, {rangeText.Text ?? "(null)"}");
        output.WriteLine($"Navigate parent: {parentSays}");
        output.WriteLine($"Navigate next sibling: {siblingSays}");
        int latePattern = Slot<OutPtrIntFn>(fieldSimple, 4)(fieldSimple, UiaInterop.UIA_ValuePatternId, out var late);
        output.WriteLine($"GetPatternProvider(Value): hr 0x{latePattern:X8}, {(late == IntPtr.Zero ? "null" : "a provider")}");
        if (late != IntPtr.Zero && latePattern == 0) Marshal.Release(late);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        bool treeAlive = oldTree.IsAlive;
        output.WriteLine($"Old tree alive: {treeAlive}");

        Assert.Equal(NotAvailable, name.Hr);
        Assert.Null(name.Text);
        Assert.Equal(NotAvailable, fieldValue.Hr);
        Assert.Null(fieldValue.Text);
        Assert.Equal(NotAvailable, patternValue.Hr);
        Assert.Equal(NotAvailable, rangeText.Hr);
        Assert.Equal(NotAvailable, parent.Hr);
        Assert.Equal(IntPtr.Zero, parent.Result);
        Assert.Equal(NotAvailable, latePattern);
        if (latePattern != 0) Assert.Equal(IntPtr.Zero, late);
        // UI Automation was told of each provider a client had (and of the ones events made).
        Assert.Subset(disconnected.ToHashSet(), new HashSet<ElementId> { label.Id, field.Id, report.Id });
        // Nothing the client holds keeps the old tree alive.
        Assert.False(treeAlive);

        // The bridge's own elements still answer.
        var now = bridge.NativeFragmentSimple(lockScreen.Id);
        try { Assert.Equal((0, "Locked"), Name(now)); }
        finally { Marshal.Release(now); }

        foreach (var p in new[] { labelSimple, labelFragment, fieldSimple, reportSimple, value, text, range })
            Marshal.Release(p);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (IntPtr Root, WeakReference Bridge) HeldRootOfDisposedBridge(TestHwnd hwnd, HeadlessSession session)
    {
        var window = session.Window.Window;
        var router = new AccessibilityActionRouter(window);
        var host = new UiaHostBridge(window, router);
        window.SetUiaHostBridge(host);
        window.SetActionRouter(router);
        var bridge = new WindowsAccessibilityBridge(hwnd.Handle, host);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        session.Window.SetRoot(TextB.New("Anna Andersson"));
        for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(150));

        var root = bridge.NativeRootSimple();   // what WM_GETOBJECT hands a client
        Assert.Equal(0, Name(root).Hr);
        bridge.Dispose();
        session.Window.InstallAccessibilityBridge();   // the window lets go of it, as a closed window would
        return (root, new WeakReference(bridge));
    }

    [WindowsFact]
    public void TheRootAClientHolds_AnswersElementNotAvailable_AfterTheBridgeIsDisposed()
    {
        using var hwnd = TestHwnd.Create();
        using var session = HeadlessSession.Create(600, 400);
        var (root, bridge) = HeldRootOfDisposedBridge(hwnd, session);
        try
        {
            var name = Name(root);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.Equal(NotAvailable, name.Hr);
            Assert.Null(name.Text);
            Assert.False(bridge.IsAlive);   // the root no longer holds the bridge and its last tree
        }
        finally { Marshal.Release(root); }
    }

    /// <summary>
    /// A UIA thread looking a provider up while the UI thread drops its element. The lookup and
    /// its AddRef were outside the lock Drop takes, so the drop could free the object between them
    /// and the AddRef write freed memory. A race: without the lock this corrupts the heap only
    /// sometimes, so it runs many rounds.
    /// </summary>
    [WindowsFact]
    public void LookingUpAProviderWhileItsElementIsDropped_GetsALiveObjectOrNone()
    {
        using var hwnd = TestHwnd.Create();
        using var session = HeadlessSession.Create(600, 400);
        var window = session.Window.Window;
        var router = new AccessibilityActionRouter(window);
        var host = new UiaHostBridge(window, router);
        window.SetUiaHostBridge(host);
        window.SetActionRouter(router);
        using var bridge = new WindowsAccessibilityBridge(hwnd.Handle, host);
        session.Window.InstallAccessibilityBridge(new Active(bridge));

        var label = TextB.New("Anna Andersson");
        var other = TextB.New("Locked");
        using var stop = new CancellationTokenSource();
        int got = 0, bad = 0;
        var client = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                var p = bridge.NativeFragmentSimple(label.Id);
                if (p == IntPtr.Zero) continue;
                int hr = Name(p).Hr;
                if (hr != 0 && hr != NotAvailable) Interlocked.Increment(ref bad);
                Marshal.Release(p);
                Interlocked.Increment(ref got);
            }
        });
        for (int i = 0; i < 300; i++)
        {
            session.Window.SetRoot(label);
            session.PumpFrame();
            session.Window.SetRoot(other);
            session.PumpFrame();
        }
        stop.Cancel();
        client.Wait();
        output.WriteLine($"Providers the client got: {got}");
        Assert.Equal(0, bad);
    }
}
