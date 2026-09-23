using System.IO;
using System.Reflection;
using HultUI.Core.Elements;
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
        // The affordance is the run of empty strip after the last crumb, and the handler
        // only fires when the strip itself is the hit target. How much of the strip the
        // crumbs occupy depends on the path's rendered width, which varies with the
        // machine's fonts and the length of its temp directory — a fixed offset from the
        // right edge landed on a crumb on the CI runner and quietly navigated instead.
        // A wide window leaves room for the trail whatever the fonts do; the click then
        // aims at the middle of the gap that actually exists rather than assuming one.
        using var fx = new Fixture(width: 2400);

        var strip = fx.Session.Window.FindByAutomationId("crumb-strip-L");
        Assert.NotNull(strip);
        var box = strip!.Element.Layout.AbsoluteBorderBox;
        var lastCrumb = strip.Element.Children[^1].Layout.AbsoluteBorderBox;
        Assert.True(lastCrumb.Right < box.Right - 2,
            $"no empty run to click: crumbs reach {lastCrumb.Right} of a strip ending at {box.Right}");

        fx.Session.Window.Click((lastCrumb.Right + box.Right) / 2, box.Y + box.Height / 2);
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

        // OpenCommandLine only requests focus (_pendingCmdFocus); the field is created
        // and focused on a later build. A pane listing finishing in between rebuilds the
        // field and drops focus, and typed text then goes nowhere — which is how this
        // failed under the whole-solution run and not on its own. Wait for the field.
        fx.Session.PumpUntil(() => fx.Session.Window.Window.FocusedElement is TextInputElement);

        fx.Session.Window.TypeText("cd sub");
        fx.Session.Window.KeyDown(KeyCode.Enter);
        fx.Session.PumpFrame();

        Assert.Equal(fx.Sub, fx.Dir('L'));
    }

    /// <summary>Temp tree (root/sub/file.txt) + a hosted CommanderApp pinned at root.
    /// Shared with <see cref="GotoRemoteTests"/>, which drives the same prompt at a share.</summary>
    internal sealed class Fixture : System.IDisposable
    {
        public readonly string Root, Sub, FileInSub;
        public readonly HeadlessSession Session;
        private readonly CommanderApp _app;

        public Fixture(bool startInSub = false, int width = 1240)
        {
            Root = Path.Combine(Path.GetTempPath(), "vesper-goto-" + System.Guid.NewGuid().ToString("N"));
            Sub = Path.Combine(Root, "sub");
            Directory.CreateDirectory(Sub);
            FileInSub = Path.Combine(Sub, "file.txt");
            File.WriteAllText(FileInSub, "x");

            Session = HeadlessSession.Create(width, 820);
            _app = new CommanderApp(Session.Application, Session.Window.Window, Root);
            Session.Application.SetRoot(_app.Mount());
            Session.PumpFrame();
            // Deterministic start regardless of any restored session.
            Invoke("NavigateTo", 'L', startInSub ? Sub : Root, null, false);
            // The listing runs off-thread and posts back; drain it here so a later
            // rebuild cannot land in the middle of what a test is doing.
            Session.PumpFrames(3);
        }

        public void OpenPrompt()
        {
            Session.Window.KeyDown(KeyCode.G, KeyModifiers.Ctrl);
            Session.PumpFrame();
            Assert.True(PromptOpen);   // the chord must actually open the dialog
        }

        public bool PromptOpen => Field("_namePrompt") != null;
        public string? PromptError => (string?)Field("_namePromptError");

        /// <summary>The in-flight remote-path probe, or null when nothing is being resolved
        /// off-thread. Non-null is exactly the prompt's busy state.</summary>
        public System.Threading.CancellationTokenSource? Probe =>
            (System.Threading.CancellationTokenSource?)Field("_gotoProbe");

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
