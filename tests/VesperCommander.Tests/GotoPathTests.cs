using System.IO;
using System.Reflection;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// End-to-end coverage for Go to path (Ctrl+G) and the command line's "cd":
/// drives the real <see cref="CommanderApp"/> headlessly — open the prompt with the
/// real chord, type into the real input, commit with Enter — and asserts the pane
/// lands where the resolution rules say (absolute, relative against the pane dir,
/// "..", a file target focusing its parent, and a bad path keeping the dialog open
/// with an error).
/// </summary>
public class GotoPathTests
{
    [Fact]
    public void CtrlG_AbsolutePath_NavigatesPane()
    {
        using var fx = new Fixture();
        fx.OpenPrompt();
        fx.Session.Window.TypeText(fx.Sub);
        fx.Session.Window.KeyDown(KeyCode.Enter);
        fx.Session.PumpFrame();

        Assert.Equal(fx.Sub, fx.Dir('L'));
        Assert.False(fx.PromptOpen);
    }

    [Fact]
    public void CtrlG_RelativePath_ResolvesAgainstPaneDir()
    {
        using var fx = new Fixture();
        fx.OpenPrompt();
        fx.Session.Window.TypeText("sub");
        fx.Session.Window.KeyDown(KeyCode.Enter);
        fx.Session.PumpFrame();

        Assert.Equal(fx.Sub, fx.Dir('L'));
    }

    [Fact]
    public void CtrlG_DotDot_GoesToParent()
    {
        using var fx = new Fixture(startInSub: true);
        fx.OpenPrompt();
        fx.Session.Window.TypeText("..");
        fx.Session.Window.KeyDown(KeyCode.Enter);
        fx.Session.PumpFrame();

        Assert.Equal(fx.Root, fx.Dir('L'));
    }

    [Fact]
    public void CtrlG_FilePath_LandsInParentFolder()
    {
        using var fx = new Fixture();
        fx.OpenPrompt();
        fx.Session.Window.TypeText(fx.FileInSub);
        fx.Session.Window.KeyDown(KeyCode.Enter);
        fx.Session.PumpFrame();

        Assert.Equal(fx.Sub, fx.Dir('L'));
    }

    [Fact]
    public void CtrlG_MissingPath_KeepsPromptOpenWithError()
    {
        using var fx = new Fixture();
        fx.OpenPrompt();
        fx.Session.Window.TypeText(Path.Combine(fx.Root, "no-such-dir"));
        fx.Session.Window.KeyDown(KeyCode.Enter);
        fx.Session.PumpFrame();

        Assert.Equal(fx.Root, fx.Dir('L'));       // didn't move
        Assert.True(fx.PromptOpen);                // dialog stayed up
        Assert.NotNull(fx.PromptError);            // with a message
    }

    [Fact]
    public void ClickingEmptyBreadcrumbStrip_OpensGotoPrompt()
    {
        using var fx = new Fixture();
        // The crumb row grows to fill the strip, so a point near its right edge lands past
        // the last crumb — the address-bar affordance, not a crumb navigation.
        var strip = fx.Session.Window.FindByAutomationId("crumb-strip-L");
        Assert.NotNull(strip);
        var box = strip!.Element.Layout.AbsoluteBorderBox;
        fx.Session.Window.Click(box.X + box.Width - 4, box.Y + box.Height / 2);
        fx.Session.PumpFrame();

        Assert.True(fx.PromptOpen);
        Assert.Equal(fx.Root, fx.Dir('L'));   // the click itself didn't navigate
    }

    [Fact]
    public void ClickingACrumb_NavigatesWithoutOpeningThePrompt()
    {
        using var fx = new Fixture(startInSub: true);
        // The drive crumb sits at the strip's left edge; clicking it must navigate to the
        // volume root's picker path, not fall through to the strip's go-to handler.
        var strip = fx.Session.Window.FindByAutomationId("crumb-strip-L");
        var box = strip!.Element.Layout.AbsoluteBorderBox;
        fx.Session.Window.Click(box.X + 6, box.Y + box.Height / 2);
        fx.Session.PumpFrame();

        Assert.False(fx.PromptOpen);          // the guard kept the strip handler out of it
    }

    [Fact]
    public void CommandLine_Cd_NavigatesPane()
    {
        using var fx = new Fixture();
        fx.Invoke("OpenCommandLine", 'L');
        fx.Session.PumpFrame();
        fx.Session.Window.TypeText("cd sub");
        fx.Session.Window.KeyDown(KeyCode.Enter);
        fx.Session.PumpFrame();

        Assert.Equal(fx.Sub, fx.Dir('L'));
    }

    /// <summary>Temp tree (root/sub/file.txt) + a hosted CommanderApp pinned at root.</summary>
    private sealed class Fixture : System.IDisposable
    {
        public readonly string Root, Sub, FileInSub;
        public readonly HeadlessSession Session;
        private readonly CommanderApp _app;

        public Fixture(bool startInSub = false)
        {
            Root = Path.Combine(Path.GetTempPath(), "vesper-goto-" + System.Guid.NewGuid().ToString("N"));
            Sub = Path.Combine(Root, "sub");
            Directory.CreateDirectory(Sub);
            FileInSub = Path.Combine(Sub, "file.txt");
            File.WriteAllText(FileInSub, "x");

            Session = HeadlessSession.Create(1240, 820);
            _app = new CommanderApp(Session.Application, Session.Window.Window, Root);
            Session.Application.SetRoot(_app.Mount());
            Session.PumpFrame();
            // Deterministic start regardless of any restored session.
            Invoke("NavigateTo", 'L', startInSub ? Sub : Root, null, false);
            Session.PumpFrame();
        }

        public void OpenPrompt()
        {
            Session.Window.KeyDown(KeyCode.G, KeyModifiers.Ctrl);
            Session.PumpFrame();
            Assert.True(PromptOpen);   // the chord must actually open the dialog
        }

        public bool PromptOpen => Field("_namePrompt") != null;
        public string? PromptError => (string?)Field("_namePromptError");

        public string Dir(char side)
            => (string)typeof(CommanderApp).GetMethod("Dir", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(_app, new object[] { side })!;

        public void Invoke(string method, params object?[] args)
            => typeof(CommanderApp).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(_app, args);

        private object? Field(string name)
            => typeof(CommanderApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(_app);

        public void Dispose()
        {
            (_app as System.IDisposable)?.Dispose();
            Session.Dispose();
            try { Directory.Delete(Root, true); } catch { /* best-effort temp cleanup */ }
        }
    }
}
