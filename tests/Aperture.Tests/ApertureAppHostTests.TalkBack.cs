using HultUI.Core.Events;
using HultUI.Headless;
using HultUI.Platform.Accessibility.Android;
using Xunit;

namespace Aperture.Tests;

/// <summary>
/// Aperture's main views as TalkBack reads them (plan 20 phase 6's exit criterion): the TalkBack
/// bridge installed as the window's own, fed by the window's frames as on a phone, and every virtual
/// view read from the surface down, as TalkBack reads them when it first looks. Each view's every
/// stop says something: no control TalkBack would call "Unlabelled", no image that says nothing. On a
/// device this waits for Aperture's Android build (phase 5).
/// </summary>
public partial class ApertureAppHostTests
{
    /// <summary>The bridge as the window's own, listening; the body runs with it, and it stops
    /// listening after (the clients it reported are the process's).</summary>
    private static void WithTalkBack(HeadlessSession session, Action<AndroidAccessibilityBridge> body)
    {
        var router = session.Window.InstallActionRouter();
        var bridge = session.Window.InstallAccessibilityBridge(new AndroidAccessibilityBridge(router));
        bridge.SetEnabled(true);
        try
        {
            for (int i = 0; i < 4; i++) session.PumpFrame();
            body(bridge);
        }
        finally { bridge.SetEnabled(false); }
    }

    /// <summary>Every virtual view from the surface down, in reading order.</summary>
    private static List<AndroidNode> ReadAll(AndroidAccessibilityBridge bridge)
    {
        var all = new List<AndroidNode>();
        var stack = new Stack<int>();
        stack.Push(AndroidIds.Host);
        while (stack.Count > 0)
        {
            if (bridge.Node(stack.Pop()) is not { } node) continue;
            all.Add(node);
            for (int i = node.Children.Length - 1; i >= 0; i--) stack.Push(node.Children[i]);
        }
        return all;
    }

    private static string Says(AndroidNode n) =>
        string.Join(", ", new[] { n.Text, n.ContentDescription, n.HintText, n.ContainerTitle, n.PaneTitle }
            .Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>
    /// What TalkBack would say wrongly in the view: a control it stops on (one a double tap runs, or
    /// that takes the focus or a long press) with no words, which it reads as "Unlabelled"; an image
    /// shown with none. The reading itself comes with the failure, to see what is said.
    /// </summary>
    private static void AssertEveryStopSaysSomething(AndroidAccessibilityBridge bridge, string view)
    {
        var nodes = ReadAll(bridge).Where(n => n.Id != AndroidIds.Host).ToList();
        var silent = nodes.Where(n =>
            (n.Has(AndroidNodeFlags.Clickable) || n.Has(AndroidNodeFlags.Focusable) || n.Can(AndroidActions.LongClick)
                || n.ClassName == AndroidClasses.ImageView)
            && n.Has(AndroidNodeFlags.VisibleToUser) && Says(n).Length == 0).ToList();
        string reading = string.Join("\n", nodes.Where(n => n.Has(AndroidNodeFlags.VisibleToUser) && Says(n).Length > 0)
            .Select(n => $"  {n.ClassName.Split('.')[^1]}{(n.RoleDescription is { } r ? " (" + r + ")" : "")}: {Says(n)}"));
        Assert.True(silent.Count == 0,
            $"{view}: {silent.Count} stop(s) say nothing: " +
            string.Join("; ", silent.Select(n => $"{n.ClassName} at {n.BoundsInScreen}")) + $"\nWhat is said:\n{reading}");
        Assert.True(nodes.Count > 5, $"{view}: hardly anything to read\n{reading}");
    }

    [Fact]
    public void TalkBack_ReadsHome_EveryStopSaysSomething()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Continue watching");
            WithTalkBack(session, bridge =>
            {
                AssertEveryStopSaysSomething(bridge, "Home");
                var said = ReadAll(bridge).Select(Says).ToList();
                // A card is one stop that says its title.
                Assert.Contains(said, s => s.Contains("Solaris", StringComparison.Ordinal));
            });
        });
    }

    [Fact]
    public void TalkBack_ReadsALibrarysGrid_EveryStopSaysSomething()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");
            WithTalkBack(session, bridge => AssertEveryStopSaysSomething(bridge, "Films"));
        });
    }

    [Fact]
    public void TalkBack_ReadsADetailPage_EveryStopSaysSomething()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.FindByAutomationId("rail-films")!.Click();
            PumpUntilShown(session, "Stalker");
            session.Window.FindByAutomationId("card-m1")!.Click();
            for (int i = 0; i < 6; i++) session.PumpFrame();
            PumpUntilShown(session, "Stalker is a fixture.");
            WithTalkBack(session, bridge => AssertEveryStopSaysSomething(bridge, "Stalker's page"));
        });
    }

    [Fact]
    public void TalkBack_ReadsSearch_EveryStopSaysSomething()
    {
        Host(Library(), (session, app) =>
        {
            PumpUntilShown(session, "Films");
            session.Window.PressKey(KeyCode.Slash);
            for (int i = 0; i < 6; i++) session.PumpFrame();
            session.Window.TypeText("sta");
            for (int i = 0; i < 8; i++) session.PumpFrame();
            PumpUntilShown(session, "Stalker");
            WithTalkBack(session, bridge => AssertEveryStopSaysSomething(bridge, "Search"));
        });
    }
}
