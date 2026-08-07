using System.IO;
using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Regression guard for hover surviving reconciliation. Handlers are copied from the
/// incoming build onto the retained element, so a hover handler that captured its own
/// element local (the old <c>Ui.HoverBg</c>) would tint the DISCARDED incoming instance —
/// hover highlight silently dead on every element that survives a rebuild. The fixed
/// helper acts on <c>e.CurrentTarget</c>; this drives the real app through rebuilds and
/// asserts a hover actually repaints the live element.
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

            session.Window.MoveMouse(b.X + b.Width / 2, b.Y + b.Height / 2);
            session.PumpFrame();

            Assert.NotEqual(before, box.BackgroundColor);
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
