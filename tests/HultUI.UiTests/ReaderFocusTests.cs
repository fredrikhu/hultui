using System.Collections.Concurrent;
using System.Diagnostics;
using FlaUI.UIA3;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Platform.SDL3;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.UiTests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// What a real UI Automation client's focus handler hears as the selection of a focused list moves.
/// Narrator and NVDA said nothing as the arrows walked a list or a menu (2026-11-15): they speak focus
/// moves, and the bridge raised only SelectionItem.IsSelected changes. The headless tests check the
/// bridge's own events and answers; this one checks that UI Automation delivers the focus event to a
/// client, in this process, through <c>IUIAutomation.AddFocusChangedEventHandler</c>, and that the
/// element it hands over is the item, by name, with HasKeyboardFocus. The window is never the
/// foreground (<see cref="ForegroundGuard"/>); its keys are not needed, the selection is moved from
/// code.
/// </summary>
[Collection("ui")] // one Application per process, and SDL is process-global
public sealed class ReaderFocusTests(ITestOutputHelper output)
{
    [UiFact]
    public void AFocusedListsSelectionMove_ReachesAClientsFocusHandler_AsItsItem()
    {
        using var guard = ForegroundGuard.Begin();
        using var app = new Application(new SdlWindowingPlatform(), "reader focus", 360, 260);
        var items = Enumerable.Range(0, 20).Select(i => $"file {i}").ToList();
        var list = VirtualListElement<string>.New().ItemHeight(20)
            .RenderWith(t => Flex.Row().Children(Text.New(t).FontSize(12f)))
            .Items(items)
            .SelectedIndex(0)
            .FlexGrow(1);
        list.IsFocusable = true;
        list.AccessibleName = "Files";
        app.SetRoot(Flex.Column().FlexGrow(1).Children(list));
        app.RunFrame(TimeSpan.FromMilliseconds(16));

        using var automation = new UIA3Automation();
        var heard = new ConcurrentQueue<(string Name, bool HasFocus)>();
        automation.RegisterFocusChangedEvent(element =>
        {
            try { heard.Enqueue((element.Name, element.Properties.HasKeyboardFocus.ValueOrDefault)); }
            catch (Exception ex) { heard.Enqueue(($"<{ex.GetType().Name}>", false)); }
        });
        try
        {
            // The client is listening now: the window builds its tree for it.
            Pump(app, TimeSpan.FromMilliseconds(300));
            list.Focus();
            Pump(app, TimeSpan.FromMilliseconds(500));
            list.SelectedIndex = 3;
            bool got = Pump(app, TimeSpan.FromSeconds(3), () => heard.Any(h => h.Name == "file 3"));
            // Only this window's: the handler hears the desktop's focus moves too (another app's).
            foreach (var (name, focus) in heard.Where(h => h.Name.StartsWith("file", StringComparison.OrdinalIgnoreCase)))
                output.WriteLine($"focus event: {name}, HasKeyboardFocus {focus}");
            if (!got && heard.IsEmpty)
                Assert.Fail("no focus event reached the client's handler from a window that is not the foreground: "
                    + "UI Automation may not deliver one without it");
            Assert.Contains(heard, h => h.Name == "file 0" && h.HasFocus);
            Assert.Contains(heard, h => h.Name == "file 3" && h.HasFocus);
        }
        finally { automation.UnregisterAllEvents(); }
    }

    /// <summary>Runs the window's frames, and its messages with them, for <paramref name="time"/> or
    /// until <paramref name="until"/> holds.</summary>
    private static bool Pump(Application app, TimeSpan time, Func<bool>? until = null)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < time)
        {
            app.RunFrame(TimeSpan.FromMilliseconds(16));
            if (until?.Invoke() == true) return true;
            Thread.Sleep(10);
        }
        return until?.Invoke() ?? true;
    }
}
