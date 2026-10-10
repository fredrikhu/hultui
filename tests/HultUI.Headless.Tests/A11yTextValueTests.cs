// The AT-SPI bridge is Linux's, but with its signals recorded instead of sent it runs anywhere:
// nothing here reaches a bus.
#pragma warning disable CA1416
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.RichText;
using HultUI.Platform.Accessibility.Linux;
using HultUI.Platform.Accessibility.Windows;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// An element that offers text a reader moves through (UIA's text pattern, AT-SPI's Text) tells a
/// change of it as a text change, not as its value. A keystroke in a rich text editor raised a UIA
/// ValueValue property change carrying the whole document and an AT-SPI accessible-value signal
/// with it, besides the text change. As Chromium: a contenteditable's ValueValue change carries no
/// text (Blink sends no value for a non-atomic text field), and AT-SPI's accessible-value is a
/// range value's. The value is still there to read. A text input, which offers no text pattern,
/// still tells its value, as an &lt;input&gt;'s change does.
/// </summary>
public class A11yTextValueTests
{
    private sealed class Active(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
    }

    private static (RichTextEditorElement Editor, TextInputElement Input, Element Root) Fixture()
    {
        var editor = RichTextEditor.New(new RichDocument([RichBlock.Paragraph("hello world"), RichBlock.Paragraph("second")]))
            .Width(300f).Height(150f);
        var input = TextInput.New("").Width(Dimension.Px(200));
        return (editor, input, Flex.Column(gap: 8).Padding(8).Children(editor, input));
    }

    private static void Mount(HeadlessSession session, Element root, Element focus)
    {
        session.Window.SetRoot(root);
        session.PumpFrames(2);
        session.Window.Click(focus);
        session.PumpFrame();
        if (focus is RichTextEditorElement editor)
        {
            editor.State.Select(RichSelection.Caret(0, 0));
            session.PumpFrame();
        }
    }

    // ── UIA ──

    [SupportedOSPlatform("windows")]
    private sealed class UiaRig : IDisposable
    {
        private readonly TestHwnd _hwnd = TestHwnd.Create();
        public HeadlessSession Session { get; } = HeadlessSession.Create(600, 400);
        public WindowsAccessibilityBridge Bridge { get; }
        public readonly List<UiaRaised> Raised = [];

        public UiaRig()
        {
            var window = Session.Window.Window;
            var router = new AccessibilityActionRouter(window);
            var host = new UiaHostBridge(window, router);
            window.SetUiaHostBridge(host);
            window.SetActionRouter(router);
            Bridge = new WindowsAccessibilityBridge(_hwnd.Handle, host);
            Bridge.Raised = Raised.Add;
            Session.Window.InstallAccessibilityBridge(new Active(Bridge));
        }

        public UiaFragment Wrapper(Element element)
        {
            var native = Bridge.NativeFragment(element.Id);
            Assert.NotEqual(IntPtr.Zero, native);
            try { return Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native)); }
            finally { Marshal.Release(native); }
        }

        /// <summary>What a client reads as the element's value: the property, and the pattern.</summary>
        public (string? Property, string? Pattern) Value(Element element)
        {
            var wrapper = Wrapper(element);
            wrapper.GetPropertyValue(UiaInterop.UIA_ValueValuePropertyId, out var property);
            string? fromProperty = property.Vt == UiaVariant.VT_BSTR ? property.AsString() : null;
            property.Free();
            wrapper.GetPatternProvider(UiaInterop.UIA_ValuePatternId, out var pattern);
            Assert.NotEqual(IntPtr.Zero, pattern);
            try
            {
                var provider = Assert.IsType<UiaValueProvider>(NativeProvider.LogicOf(pattern));
                provider.GetValue(out var bstr);
                try { return (fromProperty, Marshal.PtrToStringBSTR(bstr)); }
                finally { Marshal.FreeBSTR(bstr); }
            }
            finally { Marshal.Release(pattern); }
        }

        public void Dispose()
        {
            Bridge.Dispose();
            Session.Dispose();
            _hwnd.Dispose();
        }
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void UIA_AKeystrokeInRichText_IsATextChange_NotAValueChange_AndTheValueIsStillThere()
    {
        AccessibilityClients.Force = true;
        try
        {
            using var rig = new UiaRig();
            var (editor, input, root) = Fixture();
            Mount(rig.Session, root, editor);
            rig.Raised.Clear();

            rig.Session.Window.TypeText("x");
            rig.Session.PumpFrame();

            Assert.Contains(new UiaRaised(UiaRaisedKind.Event, UiaInterop.UIA_Text_TextChangedEventId, editor.Id), rig.Raised);
            Assert.DoesNotContain(rig.Raised, r => r.Kind == UiaRaisedKind.Property && r.Id == UiaInterop.UIA_ValueValuePropertyId);
            string document = editor.Document.ToPlainText();
            Assert.StartsWith("x", document);
            Assert.Equal((document, document), rig.Value(editor));

            // A text input has no text pattern: its value change is how a reader hears it.
            rig.Session.Window.Click(input);
            rig.Session.PumpFrame();
            rig.Raised.Clear();
            rig.Session.Window.TypeText("y");
            rig.Session.PumpFrame();
            Assert.Contains(new UiaRaised(UiaRaisedKind.Property, UiaInterop.UIA_ValueValuePropertyId, input.Id), rig.Raised);
            Assert.Equal(("y", "y"), rig.Value(input));
        }
        finally { AccessibilityClients.Force = null; }
    }

    // ── AT-SPI ──

    private sealed class Signals : IAtSpiSignals
    {
        public readonly List<string> Lines = [];
        private static string Short(string path) =>
            path.StartsWith(AtSpiConstants.AccessiblePathPrefix, StringComparison.Ordinal)
                ? path[AtSpiConstants.AccessiblePathPrefix.Length..] : "root";
        public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) => Lines.Add($"children-changed {Short(parentPath)}");
        public void EmitPropertyChange(string path, string atSpiProperty, string newValue) => Lines.Add($"property-change:{atSpiProperty} {Short(path)} {newValue}");
        public void EmitRoleChange(string path, uint role) => Lines.Add($"property-change:accessible-role {Short(path)}");
        public void EmitStateChanged(string path, string state, bool active) => Lines.Add($"state-changed:{state} {Short(path)} {(active ? 1 : 0)}");
        public void EmitTextChanged(string path, bool insert, int offset, string text) => Lines.Add($"text-changed:{(insert ? "insert" : "delete")} {Short(path)} {offset} {text}");
        public void EmitTextCaretMoved(string path, int offset) => Lines.Add($"text-caret-moved {Short(path)} {offset}");
        public void EmitTextSelectionChanged(string path) => Lines.Add($"text-selection-changed {Short(path)}");
        public void EmitAnnouncement(string path, string text, int politeness) => Lines.Add($"announcement {Short(path)}");
        public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) => Lines.Add($"cache-add {Short(node.Path)}");
        public void EmitCacheRemove(string path) => Lines.Add($"cache-remove {Short(path)}");
    }

    [Fact]
    public void ATSPI_AKeystrokeInRichText_IsTextChanged_NotAccessibleValue()
    {
        AccessibilityClients.Force = true;
        try
        {
            using var session = HeadlessSession.Create(600, 400);
            var signals = new Signals();
            using var bridge = new LinuxAccessibilityBridge(signals);
            session.Window.InstallAccessibilityBridge(new Active(bridge));
            var (editor, input, root) = Fixture();
            Mount(session, root, editor);
            signals.Lines.Clear();

            session.Window.TypeText("x");
            session.PumpFrame();
            Assert.Equal(
            [
                $"text-changed:insert {editor.Id.Value} 0 x",
                $"text-caret-moved {editor.Id.Value} 1",
            ], signals.Lines);

            // A text input has no Text interface here: its value change is how a reader hears it.
            session.Window.Click(input);
            session.PumpFrame();
            signals.Lines.Clear();
            session.Window.TypeText("y");
            session.PumpFrame();
            Assert.Contains($"property-change:accessible-value {input.Id.Value} y", signals.Lines);
        }
        finally { AccessibilityClients.Force = null; }
    }
}
