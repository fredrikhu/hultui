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

    private sealed class Active(IAccessibilityBridge inner) : IAccessibilityBridge, IEagerItemsLimit
    {
        // The real bridge's number of items past which a list's are served on demand (UIA 200, AT-SPI 2,000).
        public int MaxEagerVirtualItems => AccessibilityTreeBuilder.MaxEagerVirtualItemsOf(inner);
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) => inner.ApplyPatchedProperties(patch, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
        public void NotifyChangesApplied(AccessibilityNode root) => inner.NotifyChangesApplied(root);
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
        // UI Automation is told on the bridge's disconnect thread, after the frame.
        Assert.True(bridge.FlushDisconnects(TimeSpan.FromSeconds(10)));

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

    // ── The disconnect thread (plan 17 round 18): a UiaDisconnectProvider took 0.56–0.73 ms on the UI
    //    thread with a client asking, so the providers an apply lets go are told gone on a thread of their own.

    private static WindowsAccessibilityBridge NewBridge(TestHwnd hwnd, HeadlessSession session)
    {
        var window = session.Window.Window;
        var router = new AccessibilityActionRouter(window);
        var host = new UiaHostBridge(window, router);
        window.SetUiaHostBridge(host);
        window.SetActionRouter(router);
        var bridge = new WindowsAccessibilityBridge(hwnd.Handle, host);
        session.Window.InstallAccessibilityBridge(new Active(bridge));
        return bridge;
    }

    /// <summary>What the bridge told, in order, and on which thread.</summary>
    private sealed class Told
    {
        private readonly List<(string What, int Thread)> _log = [];
        public void Add(string what) { lock (_log) _log.Add((what, Environment.CurrentManagedThreadId)); }
        public List<(string What, int Thread)> Log { get { lock (_log) return [.. _log]; } }
    }

    /// <summary>The providers a frame lets go are told gone after it, on a thread that is not the UI
    /// thread, every one of them.</summary>
    [WindowsFact]
    public void TheProvidersAFrameLetsGo_AreToldGoneOffTheUiThread()
    {
        using var hwnd = TestHwnd.Create();
        using var session = HeadlessSession.Create(600, 400);
        using var bridge = NewBridge(hwnd, session);
        var labels = Enumerable.Range(0, 12).Select(i => TextB.New($"Row {i}")).ToArray();
        session.Window.SetRoot(Flex.Column().Children(labels));
        for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(150));
        var held = labels.Select(l => bridge.NativeFragmentSimple(l.Id)).ToArray();   // what a client holds
        Assert.DoesNotContain(IntPtr.Zero, held);
        var told = new Told();
        bridge.ProviderDisconnected = id => told.Add($"{id.Value}");

        session.Window.SetRoot(TextB.New("Locked"));
        session.PumpFrame(TimeSpan.FromMilliseconds(16));
        Assert.True(bridge.FlushDisconnects(TimeSpan.FromSeconds(10)));

        var log = told.Log;
        output.WriteLine($"told {log.Count}, on threads {string.Join(", ", log.Select(t => t.Thread).Distinct())}; UI thread {Environment.CurrentManagedThreadId}");
        Assert.Equal(labels.Select(l => $"{l.Id.Value}").Order(), log.Select(t => t.What).Order());
        Assert.All(log, t => Assert.NotEqual(Environment.CurrentManagedThreadId, t.Thread));
        foreach (var p in held)
        {
            Assert.Equal(NotAvailable, Name(p).Hr);
            Marshal.Release(p);
        }
    }

    /// <summary>A window closed while the disconnect thread has providers left to tell: every one is told
    /// before UI Automation is told the window's providers are gone (UiaReturnRawElementProvider with
    /// none), those it had not taken on the closing thread, and none is lost.</summary>
    [WindowsFact]
    public void AWindowClosedWithProvidersLeftToTell_TellsEveryOne_BeforeTheWindowsProvidersGo()
    {
        using var hwnd = TestHwnd.Create();
        using var session = HeadlessSession.Create(600, 400);
        var bridge = NewBridge(hwnd, session);
        var labels = Enumerable.Range(0, 8).Select(i => TextB.New($"Row {i}")).ToArray();
        session.Window.SetRoot(Flex.Column().Children(labels));
        for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(150));
        var held = labels.Select(l => bridge.NativeFragmentSimple(l.Id)).ToArray();
        int ui = Environment.CurrentManagedThreadId;
        var told = new Told();
        using var entered = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        // The disconnect thread stops in the first one it tells until the gate opens: the rest wait.
        bridge.ProviderDisconnected = id =>
        {
            told.Add($"{id.Value}");
            if (Environment.CurrentManagedThreadId != ui && !entered.IsSet) { entered.Set(); gate.Wait(TimeSpan.FromSeconds(10)); }
        };
        bridge.ProvidersReleased = _ => told.Add("released");

        session.Window.SetRoot(TextB.New("Locked"));
        session.PumpFrame(TimeSpan.FromMilliseconds(16));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        using var opens = new Timer(_ => gate.Set(), null, 300, Timeout.Infinite);
        bridge.Dispose();
        session.Window.InstallAccessibilityBridge();   // the window lets go of it, as a closed window would
        gate.Set();
        Assert.True(bridge.FlushDisconnects(TimeSpan.FromSeconds(10)));

        var log = told.Log;
        output.WriteLine(string.Join(", ", log.Select(t => t.What == "released" ? "released" : t.Thread == ui ? "UI" : "thread")));
        Assert.Equal(labels.Length + 1, log.Count);   // none lost
        Assert.Equal("released", log[^1].What);        // every one before the window's providers go
        Assert.Equal(labels.Select(l => $"{l.Id.Value}").Order(), log.SkipLast(1).Select(t => t.What).Order());
        Assert.NotEqual(ui, log[0].Thread);            // the one the disconnect thread was telling, waited for
        Assert.Contains(log.SkipLast(1), t => t.Thread == ui);   // the ones it had not taken, told on closing
        foreach (var p in held) Marshal.Release(p);
    }

    /// <summary>An element back in the tree before the disconnect thread told its old provider gone: the
    /// old one is told before a new one is made for it, so UI Automation never hears of the old one after
    /// it has the new one (they share a runtime id), and the new one answers.</summary>
    [WindowsFact]
    public void AnElementBackInTheTree_HasItsOldProviderToldGone_BeforeANewOneIsMade()
    {
        using var hwnd = TestHwnd.Create();
        using var session = HeadlessSession.Create(600, 400);
        using var bridge = NewBridge(hwnd, session);
        var a = TextB.New("Anna");
        var b = TextB.New("Bertil");
        var c = TextB.New("Cecilia");
        session.Window.SetRoot(Flex.Column().Children(a, b, c));
        for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(150));
        var held = new[] { a, b, c }.Select(l => bridge.NativeFragmentSimple(l.Id)).ToArray();
        int ui = Environment.CurrentManagedThreadId;
        var told = new Told();
        using var entered = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        ElementId first = default;
        bridge.ProviderDisconnected = id =>
        {
            told.Add($"{id.Value}");
            if (Environment.CurrentManagedThreadId != ui && !entered.IsSet) { first = id; entered.Set(); gate.Wait(TimeSpan.FromSeconds(10)); }
        };

        session.Window.SetRoot(TextB.New("Locked"));
        session.PumpFrame(TimeSpan.FromMilliseconds(16));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        // The same elements back, the disconnect thread still in its first: the others wait to be told.
        session.Window.SetRoot(Flex.Column().Children(a, b, c));
        session.PumpFrame(TimeSpan.FromMilliseconds(16));
        var back = new[] { a, b, c }.First(l => l.Id != first);
        IntPtr now = bridge.NativeFragmentSimple(back.Id);
        bool toldBefore = told.Log.Any(t => t.What == $"{back.Id.Value}");
        gate.Set();
        try
        {
            Assert.True(bridge.FlushDisconnects(TimeSpan.FromSeconds(10)));
            Assert.True(toldBefore, "the old provider was told gone after the new one was made");
            Assert.Equal((0, back == a ? "Anna" : "Bertil"), Name(now));   // the new one answers
            Assert.Equal(NotAvailable, Name(held[Array.IndexOf(new[] { a, b, c }, back)]).Hr);
        }
        finally
        {
            Marshal.Release(now);
            foreach (var p in held) Marshal.Release(p);
        }
    }
}
