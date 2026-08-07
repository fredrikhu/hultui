using System.IO;
using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Regression guard for hover surviving reconciliation. <c>Ui.HoverBg</c> declares the
/// tint through the Interaction system: the override is copied to the retained element
/// on patch and re-applied by <c>RefreshInteractionAfterPatch</c>, so the highlight both
/// works after rebuilds AND survives a rebuild that lands mid-hover (a manual
/// enter/leave handler would have its tint wiped by the patch until re-entry — the
/// CI flake fixed in 4c0a4ee). Drives the real app end to end.
/// </summary>
public class HoverAfterRebuildTests
{
    [Fact]
    public void HistoryBackButton_HoverAfterRebuilds_TintsLiveElement()
    {
        var root = Path.Combine(Path.GetTempPath(), "vesper-hover-" + System.Guid.NewGuid().ToString("N"));
        var sub = Path.Combine(root, "sub");
        Directory.CreateDirectory(sub);

        using var session = HeadlessSession.Create(1240, 820);
        CommanderApp? commander = null;
        try
        {
            commander = new CommanderApp(session.Application, session.Window.Window, root);
            session.Application.SetRoot(commander.Mount());
            session.PumpFrame();

            // Enable the back button (attaches its HoverBg) …
            Invoke(commander, "NavigateTo", 'L', root, null, false);
            session.PumpFrame();
            Invoke(commander, "NavigateTo", 'L', sub, null, true);
            session.PumpFrame();

            // … then force one more rebuild so the retained button holds a COPIED
            // handler whose closure captured a now-discarded build's element.
            Invoke(commander, "MarkDirty");
            session.PumpFrame();

            var back = session.Window.FindByAutomationId("hist-back-L");
            Assert.NotNull(back);
            var box = (BoxElement)back!.Element;
            var b = box.Layout.AbsoluteBorderBox;
            var before = box.BackgroundColor;

            // Input injection is synchronous: hover state is set and the interaction
            // override applied when MoveMouse returns.
            session.Window.MoveMouse(b.X + b.Width / 2, b.Y + b.Height / 2);
            Assert.NotEqual(before, box.BackgroundColor);

            // The tint must survive a rebuild landing mid-hover: the patch resets the
            // background prop, then RefreshInteractionAfterPatch re-applies the hover
            // override. (This is why async stragglers no longer need draining here.)
            Invoke(commander, "MarkDirty");
            session.PumpFrame();
            Assert.NotEqual(before, box.BackgroundColor);

            // And un-hover restores the base look.
            session.Window.MoveMouse(b.X + b.Width / 2, b.Y + b.Height + 80);
            Assert.Equal(before, box.BackgroundColor);
        }
        finally
        {
            (commander as System.IDisposable)?.Dispose();
            try { Directory.Delete(root, true); } catch { /* best-effort temp cleanup */ }
        }
    }

    private static void Invoke(CommanderApp app, string method, params object?[] args)
    {
        var m = typeof(CommanderApp).GetMethod(method,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
        m.Invoke(app, m.GetParameters().Length == 0 ? null : args);
    }
}
