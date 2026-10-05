using System.IO;
using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The recent-folders popover under the back · caret · forward cluster, now the framework's:
/// it takes the keyboard (↑ ↓ walk the rows, Enter jumps, Esc closes), Alt+← still goes back,
/// and a press on the cluster while it is open only closes it.
/// </summary>
public class HistoryPopoverTests
{
    [Fact]
    public void TheKeyboardWalksTheRows_AndEnterJumps()
    {
        using var fx = new Fixture();
        fx.OpenPopover();
        // Most recent first: the folder just left has the keyboard.
        Assert.True(fx.Row(fx.Alpha).IsFocused);

        fx.Session.Window.PressKey(KeyCode.Down);
        Assert.True(fx.Row(fx.Root).IsFocused);
        fx.Session.Window.PressKey(KeyCode.Enter);
        fx.Session.PumpFrames(3);

        Assert.Null(fx.Popover);
        Assert.Equal(fx.Root, fx.Dir());
    }

    [Fact]
    public void EscCloses_AndAPressOnTheClusterOnlyCloses()
    {
        using var fx = new Fixture();
        fx.OpenPopover();
        fx.Session.Window.PressKey(KeyCode.Escape);
        fx.Session.PumpFrames(2);
        Assert.Null(fx.Popover);

        fx.OpenPopover();
        fx.Session.Window.FindByAutomationId("hist-caret-L")!.Click();
        fx.Session.PumpFrames(2);
        Assert.Null(fx.Popover);
        Assert.Equal(fx.Beta, fx.Dir());
    }

    [Fact]
    public void AltLeftInsideGoesBack_AndClosesIt()
    {
        using var fx = new Fixture();
        fx.OpenPopover();
        fx.Session.Window.PressKey(KeyCode.Left, KeyModifiers.Alt);
        fx.Session.PumpFrames(3);
        Assert.Null(fx.Popover);
        Assert.Equal(fx.Alpha, fx.Dir());
    }

    /// <summary>A pane that has been to root → alpha → beta, so Back holds two folders.</summary>
    private sealed class Fixture : System.IDisposable
    {
        public readonly string Root, Alpha, Beta;
        public readonly HeadlessSession Session;
        private readonly CommanderApp _app;

        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "vesper-history-" + System.Guid.NewGuid().ToString("N"));
            Alpha = Path.Combine(Root, "alpha");
            Beta = Path.Combine(Alpha, "beta");
            Directory.CreateDirectory(Beta);

            Session = HeadlessSession.Create(1240, 820);
            _app = new CommanderApp(Session.Application, Session.Window.Window, Root);
            Session.Application.SetRoot(_app.Mount());
            Session.PumpFrame();
            // The first without history, so whatever the app opened on is not in it.
            Invoke("NavigateTo", 'L', Root, null, false);
            Session.PumpFrames(3);
            foreach (var dir in new[] { Alpha, Beta })
            {
                Invoke("NavigateTo", 'L', dir, null, true);
                Session.PumpFrames(3);
            }
        }

        public void OpenPopover()
        {
            Session.Window.FindByAutomationId("hist-caret-L")!.Click();
            Session.PumpFrames(2);
            Assert.NotNull(Popover);
        }

        public HeadlessElement? Popover => Session.Window.FindByRole(AccessibilityRole.Dialog, "Recent folders");

        public HeadlessElement Row(string path) => Session.Window.FindByRole(AccessibilityRole.Button, path)!;

        public string Dir() =>
            (string)typeof(CommanderApp).GetMethod("Dir", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(_app, new object[] { 'L' })!;

        public void Invoke(string method, params object?[] args)
            => typeof(CommanderApp).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(_app, args);

        public void Dispose()
        {
            (_app as System.IDisposable)?.Dispose();
            Session.Dispose();
            try { Directory.Delete(Root, true); } catch { /* best-effort temp cleanup */ }
        }
    }
}
