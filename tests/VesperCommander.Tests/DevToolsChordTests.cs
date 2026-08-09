using System;
using System.IO;
using System.Reflection;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Vesper's Ctrl+Shift+C ("Copy as path") against the framework's DevTools chords,
/// which are intercepted before dispatch and would otherwise swallow it. The app's
/// binding predates the inspector and is the one that has to win.
/// </summary>
public class DevToolsChordTests
{
    [Fact]
    public void CtrlShiftC_StillCopiesTheCurrentPath()
    {
        using var fx = new Fixture();

        fx.Session.Window.KeyDown(KeyCode.C, KeyModifiers.Ctrl | KeyModifiers.Shift);
        fx.Session.PumpFrame();

        Assert.Equal(fx.Root, fx.Session.Window.Clipboard.GetText());
    }

    [Fact]
    public void CtrlShiftC_DoesNotOpenTheInspector()
    {
        using var fx = new Fixture();

        fx.Session.Window.KeyDown(KeyCode.C, KeyModifiers.Ctrl | KeyModifiers.Shift);
        fx.Session.PumpFrame();

        // A file manager's users have no use for an element inspector appearing
        // when they copy a path.
        Assert.False(fx.Session.Application.IsInspectorOpen);
    }

    [Fact]
    public void CtrlShiftI_OpensTheInspector()
    {
        using var fx = new Fixture();

        fx.Session.Window.KeyDown(KeyCode.I, KeyModifiers.Ctrl | KeyModifiers.Shift);
        fx.Session.PumpFrame();

        // Vesper doesn't claim this chord, so the framework's binding stands. If that
        // ever stops being wanted in a shipped build, the opt-out is one line:
        // app.DevToolsHotkeysEnabled = false.
        Assert.True(fx.Session.Application.IsInspectorOpen);
    }

    private sealed class Fixture : IDisposable
    {
        public readonly string Root;
        public readonly HeadlessSession Session;
        private readonly CommanderApp _app;

        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "vesper-chord-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);

            Session = HeadlessSession.Create(1240, 820);
            _app = new CommanderApp(Session.Application, Session.Window.Window, Root);
            Session.Application.SetRoot(_app.Mount());
            Session.PumpFrame();

            // Pin the pane explicitly. CommanderApp reads the developer's real
            // settings.json, so with RestoreTabs on it reopens a saved session and the
            // active folder is whatever that machine last had — which made the
            // copy-as-path assertion pass or fail depending on the box it ran on.
            typeof(CommanderApp)
                .GetMethod("NavigateTo", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(_app, ['L', Root, null, false]);
            Session.PumpFrame();
        }

        public void Dispose()
        {
            _app.Dispose();
            Session.Dispose();
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
