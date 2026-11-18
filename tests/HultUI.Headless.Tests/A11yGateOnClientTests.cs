using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// The Windows bridge's gate (plan 17 round 16): a window builds its accessibility tree and raises
/// events only once a UI Automation client has asked for its root. This desk always has a client
/// listening that never asks (0 root asks in 16 launches), and for it every window built and diffed its
/// tree and raised every change: in Junction a ref move with History shown was 55 % bridge bytes. A
/// reader asks for the root of the window it reads, and the tree is built inside that ask, so its first
/// reads find it. These tests ask as a client does (<c>WM_GETOBJECT</c> with <c>UiaRootObjectId</c>,
/// <see cref="TestHwnd.AskForRoot"/>), through the window's real host (<see cref="UiaHostBridge"/>).
/// </summary>
[SupportedOSPlatform("windows")]
public class A11yGateOnClientTests
{
    private sealed class Gated : IDisposable
    {
        public readonly TestHwnd Hwnd = TestHwnd.Create();
        public readonly HeadlessWindow Window;
        public readonly WindowsAccessibilityBridge Bridge;
        public readonly List<UiaRaised> Raised = [];

        public Gated(HeadlessWindow window, Element root)
        {
            Window = window;
            window.SetRoot(root);
            var router = new AccessibilityActionRouter(window.Window);
            Bridge = new WindowsAccessibilityBridge(Hwnd.Handle, new UiaHostBridge(window.Window, router)) { Raised = Raised.Add };
            window.InstallAccessibilityBridge(Bridge);
        }

        public int Trees => Window.AccessibilityTreesBuilt;

        // The window closes, and disposes its bridge, which gives the window procedure back before the
        // test window goes.
        public void Dispose()
        {
            Window.Window.Dispose();
            Bridge.Dispose();
            Hwnd.Dispose();
        }
    }

    private static (Element Root, ButtonElement First, ButtonElement Second, TextElement Label) Scene()
    {
        var first = Button.New("First");
        var second = Button.New("Second");
        var label = TextB.New("before");
        return (Flex.Column(gap: 4).Padding(8).Children(first, second, label), first, second, label);
    }

    private static void ClientsListen() =>
        Assert.True(UiaInterop.UiaClientsAreListening(),
            "UI Automation says no client listens on this desktop: the gate cannot open (no tree is built for nobody)");

    [WindowsFact]
    public void NothingIsBuiltOrRaised_UntilAClientAsksForTheRoot_ThenTheAskBuildsTheTree()
    {
        ClientsListen();
        using var session = HeadlessSession.Create(400, 300);
        var (root, first, second, label) = Scene();
        using var gated = new Gated(session.Window, root);
        session.PumpFrames(2);

        // Focus moves and a rename with clients listening: no tree, no event, the bridge inactive.
        first.Focus();
        session.PumpFrame();
        label.Content = "after";
        second.Focus();
        session.PumpFrames(3);
        Assert.False(gated.Bridge.RootAsked);
        Assert.False(gated.Bridge.IsActive);
        Assert.Equal(0, gated.Trees);
        Assert.Null(gated.Bridge.RootNode);
        Assert.Empty(gated.Raised);

        // The ask builds the tree before it is answered, with no frame in between: the client's first
        // reads find it, and the focus it marks, taken as the client takes the tree (no event).
        gated.Hwnd.AskForRoot();
        Assert.True(gated.Bridge.RootAsked);
        Assert.True(gated.Bridge.IsActive);
        Assert.Equal(1, gated.Trees);
        Assert.NotNull(gated.Bridge.RootNode);
        Assert.Equal("after", Find(gated.Bridge.RootNode!, label.Id)?.Name);
        Assert.Equal(second.Id, gated.Bridge.FocusedNodeId);

        // From then on as without the gate: a focus move is told, and a rename.
        gated.Raised.Clear();
        first.Focus();
        label.Content = "again";
        session.PumpFrames(2);
        Assert.Contains(new UiaRaised(UiaRaisedKind.Event, UiaInterop.UIA_AutomationFocusChangedEventId, first.Id), gated.Raised);
        Assert.Contains(new UiaRaised(UiaRaisedKind.Property, UiaInterop.UIA_NamePropertyId, label.Id), gated.Raised);
    }

    [WindowsFact]
    public void ARootAskedInsideAFrame_IsBuiltByTheNextFrame()
    {
        ClientsListen();
        using var session = HeadlessSession.Create(400, 300);
        var (root, first, _, _) = Scene();
        using var gated = new Gated(session.Window, root);
        session.PumpFrames(2);
        first.Focus();
        session.PumpFrame();

        // A wait inside a frame can let a client's ask in (a sent message): no frame inside the frame.
        bool inFrame = false;
        gated.Window.Window.RequestPostFrameCallback(() =>
        {
            inFrame = gated.Window.Window.IsDrawingFrame;
            gated.Hwnd.AskForRoot();
        });
        session.PumpFrame();
        Assert.True(inFrame);
        Assert.True(gated.Bridge.RootAsked);
        Assert.Equal(0, gated.Trees);
        Assert.Null(gated.Bridge.RootNode);

        session.PumpFrame();
        Assert.Equal(1, gated.Trees);
        Assert.NotNull(gated.Bridge.RootNode);
        Assert.Equal(first.Id, gated.Bridge.FocusedNodeId);
    }

    [WindowsFact]
    public void ARootAskedWhileTheWindowHasAFrameToDraw_IsBuiltByThatFrame()
    {
        ClientsListen();
        using var session = HeadlessSession.Create(400, 300);
        var (root, _, _, label) = Scene();
        using var gated = new Gated(session.Window, root);
        session.PumpFrames(2);

        // A live resize's step (or any dirt): the frame that draws it builds the tree too; the ask
        // draws nothing from inside the window procedure.
        session.Window.SetClientSize(520, 360);
        label.Content = "resized";
        gated.Hwnd.AskForRoot();
        Assert.Equal(0, gated.Trees);

        session.PumpFrame();
        Assert.Equal(1, gated.Trees);
        Assert.Equal("resized", Find(gated.Bridge.RootNode!, label.Id)?.Name);
    }

    [WindowsFact]
    public void EachWindowHasItsOwnGate_AndOnceOpenItNeverCloses()
    {
        ClientsListen();
        using var session = HeadlessSession.Create(400, 300);
        var (rootA, firstA, _, labelA) = Scene();
        var (rootB, firstB, _, labelB) = Scene();
        using var a = new Gated(session.Window, rootA);
        using var b = new Gated(session.CreateWindow("second", 400, 300), rootB);
        session.PumpFrames(2);

        a.Hwnd.AskForRoot();
        firstA.Focus();
        firstB.Focus();
        labelA.Content = "a";
        labelB.Content = "b";
        session.PumpFrames(3);
        Assert.Equal(1, a.Trees);
        Assert.NotEmpty(a.Raised);
        Assert.False(b.Bridge.RootAsked);
        Assert.False(b.Bridge.IsActive);
        Assert.Equal(0, b.Trees);
        Assert.Empty(b.Raised);

        // Seconds on, more asks and changes: the tree built at the first ask is kept up to date, never
        // built again, and the window stays open to clients.
        for (int i = 0; i < 10; i++)
        {
            labelA.Content = $"a{i}";
            session.PumpFrame(TimeSpan.FromMilliseconds(500));
            a.Hwnd.AskForRoot();
        }
        Assert.True(a.Bridge.IsActive);
        Assert.Equal(1, a.Trees);
        Assert.Equal("a9", Find(a.Bridge.RootNode!, labelA.Id)?.Name);

        // The second window's gate opens on its own ask.
        b.Hwnd.AskForRoot();
        Assert.Equal(1, b.Trees);
        Assert.Equal("b", Find(b.Bridge.RootNode!, labelB.Id)?.Name);
    }

    /// <summary>
    /// What reads <see cref="AccessibilityClients"/> (a web page's tree, a terminal's settle timers and
    /// announcements, a text's versions) waits for the same ask (the user's question 66 (a)): for the
    /// process, from the first ask for any of its windows' roots, told as the ask builds the tree; and
    /// nobody once the last window asked for has closed.
    /// </summary>
    [WindowsFact]
    public void AClientIsSaidToListen_FromTheFirstRootAsk_UntilTheLastWindowAskedForCloses()
    {
        ClientsListen();
        var forced = AccessibilityClients.Force;
        var heard = new List<bool>();
        AccessibilityClients.Force = null;
        AccessibilityClients.Report(false);
        AccessibilityClients.ActiveChanged += heard.Add;
        try
        {
            using var session = HeadlessSession.Create(400, 300);
            var (rootA, _, _, _) = Scene();
            var (rootB, _, _, _) = Scene();
            var a = new Gated(session.Window, rootA);
            var b = new Gated(session.CreateWindow("second", 400, 300), rootB);
            session.PumpFrames(3);
            Assert.False(AccessibilityClients.Active);
            Assert.Empty(heard);

            b.Hwnd.AskForRoot();
            Assert.True(AccessibilityClients.Active);
            Assert.Equal([true], heard);
            session.PumpFrames(2);
            a.Hwnd.AskForRoot();
            session.PumpFrames(2);
            Assert.Equal([true], heard);

            // A window asked for closes; another is still open to clients.
            b.Dispose();
            session.PumpFrames(2);
            Assert.True(AccessibilityClients.Active);
            a.Dispose();
            Assert.False(AccessibilityClients.Active);
            Assert.Equal([true, false], heard);
        }
        finally
        {
            AccessibilityClients.ActiveChanged -= heard.Add;
            AccessibilityClients.Force = forced;
            AccessibilityClients.Report(false);
        }
    }

    private static AccessibilityNode? Find(AccessibilityNode node, ElementId id)
    {
        if (node.ElementId == id) return node;
        foreach (var child in node.Children)
            if (Find(child, id) is { } found) return found;
        return null;
    }
}
