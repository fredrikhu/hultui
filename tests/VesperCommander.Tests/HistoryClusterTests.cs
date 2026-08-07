using System.IO;
using System.Reflection;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// End-to-end guard for "the history buttons do nothing": drives the real
/// <see cref="CommanderApp"/> in a headless window through a genuine disabled→enabled
/// transition (buttons build disabled with no history, then a navigation enables them),
/// and asserts the back chevron is reachable by assistive tech (AutomationId + Button
/// role) and that clicking it actually navigates to the previous folder.
///
/// <para>The original bug was the framework not copying event handlers when the differ
/// patches a retained element in place, so the enabled build's click handler never
/// landed. The fix keys the buttons by enabled state to force a fresh element.</para>
/// </summary>
public class HistoryClusterTests
{
    [Fact]
    public void BackButton_Click_NavigatesToPreviousFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "vesper-hist-" + System.Guid.NewGuid().ToString("N"));
        var sub = Path.Combine(root, "sub");
        Directory.CreateDirectory(sub);

        using var session = HeadlessSession.Create(1240, 820);
        CommanderApp? commander = null;
        try
        {
            commander = new CommanderApp(session.Application, session.Window.Window, root);
            session.Application.SetRoot(commander.Mount());
            session.PumpFrame();

            // The pane's Back stack starts empty (the session store persists folders, not
            // history), so the back button's first build is DISABLED — no click handler.
            // Whatever folder the pane opened on is where a Back should return to; capture
            // it (relative assert keeps the test independent of any restored session).
            var prev = Dir(commander, 'L');
            Assert.NotEqual(sub, prev);

            // Navigate -> sub: records prev in Back, flipping the button disabled -> enabled.
            // Its handler only lands if the differ replaces the retained (handler-less) box,
            // which the state-dependent key forces.
            Invoke(commander, "NavigateTo", 'L', sub, null, true);
            session.PumpFrame();
            Assert.Equal(sub, Dir(commander, 'L'));

            var back = session.Window.FindByAutomationId("hist-back-L");
            Assert.NotNull(back);

            back!.Click();
            session.PumpFrame();

            Assert.Equal(prev, Dir(commander, 'L'));
        }
        finally
        {
            (commander as System.IDisposable)?.Dispose();
            try { Directory.Delete(root, true); } catch { /* best-effort temp cleanup */ }
        }
    }

    private static void Invoke(CommanderApp app, string method, params object?[] args)
        => typeof(CommanderApp).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, args);

    private static string Dir(CommanderApp app, char side)
        => (string)typeof(CommanderApp).GetMethod("Dir", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, new object[] { side })!;
}
