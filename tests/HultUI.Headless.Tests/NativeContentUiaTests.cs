using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Windows;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// How the UI Automation bridge joins a foreign tree — a web page's — to the app's, tested at
/// the provider interfaces UI Automation calls. What UI Automation then makes of the answers
/// is proved against a real browser in <c>HultUI.UiTests</c>; these pin the answers, because
/// each one was a way to get it wrong that a client only showed as a hang or an empty element.
/// </summary>
[SupportedOSPlatform("windows")]
public class NativeContentUiaTests
{
    private const int ServerSide = 0x2, Override = 0x8;
    private const int IsControlElement = 30016, ControlType = 30003;

    private sealed class Rig : IDisposable
    {
        private readonly TestHwnd _window = TestHwnd.Create();
        private readonly TestHwnd _content = TestHwnd.Create();
        private readonly TestHwnd _hitTest = TestHwnd.Create();
        private readonly HandleWindowHost _host;

        public WindowsAccessibilityBridge Bridge { get; }
        public UiaFragmentRoot Root { get; }
        public AccessibilityNode Plain { get; }
        public AccessibilityNode Hosting { get; }
        public nint ContentWindow => _content.Handle;
        public nint HitTestWindow => _hitTest.Handle;

        /// <summary>An app root holding a button and, below it, a group that hosts native content.</summary>
        public Rig(bool withHitTestWindow = true)
        {
            _host = HandleWindowHost.Create(_window.Handle);
            Bridge = Assert.IsType<WindowsAccessibilityBridge>(AccessibilityBridgeFactory.Create(_host.Application.PrimaryWindow));
            Root = new UiaFragmentRoot(Bridge);

            var app = new AccessibilityNode(ElementId.New(), AccessibilityRole.Application, null, null, new Rect(0, 0, 200, 100));
            Plain = new AccessibilityNode(ElementId.New(), AccessibilityRole.Button, "Back", null, new Rect(0, 0, 200, 20));
            Hosting = new AccessibilityNode(ElementId.New(), AccessibilityRole.Group, "Page", null, new Rect(0, 20, 200, 80))
            {
                NativeContent = new NativeAccessibleContent(_content.Handle, withHitTestWindow ? _hitTest.Handle : 0),
            };
            app.AddChild(Plain);
            app.AddChild(Hosting);
            Bridge.ApplyChanges(AccessibilityTreeDiffer.Diff(null, app), app);
        }

        public UiaFragment Fragment(AccessibilityNode node)
        {
            var native = Bridge.NativeFragment(node.ElementId);
            try { return Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native)); }
            finally { Marshal.Release(native); }
        }

        /// <summary>The provider at the middle of a node, as UI Automation would ask: in screen coordinates.</summary>
        public object? At(AccessibilityNode node)
        {
            var onScreen = Bridge.ToScreen(node.BoundingRect);
            Assert.Equal(0, Root.ElementProviderFromPoint(onScreen.X + onScreen.Width / 2, onScreen.Y + onScreen.Height / 2, out var hit));
            try { return NativeProvider.LogicOf(hit); }
            finally { if (hit != 0) Marshal.Release(hit); }
        }

        public void Dispose()
        {
            Bridge.Dispose();
            _host.Dispose();
            _hitTest.Dispose();
            _content.Dispose();
            _window.Dispose();
        }
    }

    private static int Options(INativeSimple provider)
    {
        Assert.Equal(0, provider.GetProviderOptions(out int options));
        return options;
    }

    private static bool HasHost(INativeSimple provider)
    {
        Assert.Equal(0, provider.GetHostRawElementProvider(out var host));
        if (host == 0) return false;
        Marshal.Release(host);
        return true;
    }

    [WindowsFact]
    public void TheContentWindow_IsStoodForByTheNodeThatHostsIt_AndNoOtherWindowIs()
    {
        using var rig = new Rig();

        Assert.Equal(0, rig.Root.GetOverrideProviderForHwnd(rig.ContentWindow, out var provider));
        try
        {
            var fragment = Assert.IsType<UiaFragment>(NativeProvider.LogicOf(provider));
            Assert.Equal(rig.Hosting.ElementId, fragment.ElementId);
        }
        finally { Marshal.Release(provider); }

        // The hit-test window is not a child of ours to stand for, and zero is every
        // ordinary node's "window": neither may be answered with a node.
        Assert.Equal(0, rig.Root.GetOverrideProviderForHwnd(rig.HitTestWindow, out var other));
        Assert.Equal(0, other);
        Assert.Equal(0, rig.Root.GetOverrideProviderForHwnd(0, out var none));
        Assert.Equal(0, none);
    }

    [WindowsFact]
    public void TheHostingNode_IsAnOverrideForItsWindow_AndAControl()
    {
        using var rig = new Rig();
        var hosting = rig.Fragment(rig.Hosting);

        // Without the override flag UI Automation takes the node for the window's main
        // provider, asks for the override again, and walks that circle for ever.
        Assert.Equal(ServerSide | Override, Options(hosting));
        Assert.True(HasHost(hosting));

        // The window's own providers answer with the node, and a browser's helper window says
        // it is not a control; the node has to say it is, or the control view drops it.
        Assert.Equal(0, hosting.GetPropertyValue(IsControlElement, out var isControl));
        Assert.Equal(UiaVariant.VT_BOOL, isControl.Vt);
        Assert.NotEqual(0, isControl.ShortValue);
    }

    [WindowsFact]
    public void AnOrdinaryNode_IsAPlainServerSideProvider_WithNoWindow()
    {
        using var rig = new Rig();
        var plain = rig.Fragment(rig.Plain);

        // 0x2. This read 0x1 for as long as nothing looked: that is ClientSideProvider.
        Assert.Equal(ServerSide, Options(plain));
        Assert.False(HasHost(plain));
        Assert.Equal(0, plain.GetPropertyValue(IsControlElement, out var isControl));
        Assert.Equal(UiaVariant.VT_EMPTY, isControl.Vt); // left to UI Automation's default
    }

    [WindowsFact]
    public void APointOverTheContent_IsAnsweredWithTheHitTestWindow()
    {
        using var rig = new Rig();

        // Not the node: its box is all a client would get. A provider that only stands for
        // the window that can resolve the point, which UI Automation then asks.
        var standIn = Assert.IsType<UiaWindowStandIn>(rig.At(rig.Hosting));
        Assert.Equal(ServerSide | Override, Options(standIn));
        Assert.True(HasHost(standIn));
        Assert.Equal(0, standIn.GetPropertyValue(ControlType, out var type));
        Assert.Equal(UiaVariant.VT_EMPTY, type.Vt); // it says nothing itself

        var elsewhere = Assert.IsType<UiaFragment>(rig.At(rig.Plain));
        Assert.Equal(rig.Plain.ElementId, elsewhere.ElementId);
    }

    [WindowsFact]
    public void WithNoHitTestWindow_APointOverTheContent_IsTheNodeItself()
    {
        using var rig = new Rig(withHitTestWindow: false);

        var hit = Assert.IsType<UiaFragment>(rig.At(rig.Hosting));
        Assert.Equal(rig.Hosting.ElementId, hit.ElementId);
    }
}
