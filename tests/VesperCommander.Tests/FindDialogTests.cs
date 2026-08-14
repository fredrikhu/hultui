using System.IO;
using System.Reflection;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The Alt+F7 dialog's scope row — folder excludes and the depth bound — driven headlessly.
/// The dialog is resizable and its size is session-remembered, so the layout assertions run at
/// its minimum width, where an exclude field shrunk to nothing or a stepper pushed onto a
/// second line would show up.
/// </summary>
public class FindDialogTests
{
    [Fact]
    public void DepthStepper_StepsAndStopsAtUnlimited()
    {
        var session = Open(out var app);
        try
        {
            Assert.Equal(0, Field<int>(app, "_findMaxDepth"));
            Assert.Contains("Unlimited", session.Window.DumpAccessibilityTree());

            Click(session, "find-depth:inc");
            Assert.Equal(1, Field<int>(app, "_findMaxDepth"));
            Assert.Contains("1 level", session.Window.DumpAccessibilityTree());

            Click(session, "find-depth:inc");
            Click(session, "find-depth:inc");
            Assert.Equal(3, Field<int>(app, "_findMaxDepth"));
            Assert.Contains("3 levels", session.Window.DumpAccessibilityTree());

            // Below 0 there is nothing to step down to — 0 *is* unbounded.
            for (int i = 0; i < 5; i++) Click(session, "find-depth:dec");
            Assert.Equal(0, Field<int>(app, "_findMaxDepth"));
            Assert.Contains("Unlimited", session.Window.DumpAccessibilityTree());
        }
        finally { session.Dispose(); Cleanup(app); }
    }

    [Fact]
    public void ScopeRow_StaysOnOneLineAtTheMinimumWidth()
    {
        var session = Open(out var app);
        try
        {
            SetField(app, "_findW", 460f);   // FindMinW — the narrowest the grip allows
            Mark(app); session.PumpFrame();

            var field = session.Window.FindByAutomationId("find-exclude");
            var inc = session.Window.FindByAutomationId("find-depth:inc");
            var dec = session.Window.FindByAutomationId("find-depth:dec");
            Assert.NotNull(field); Assert.NotNull(inc); Assert.NotNull(dec);

            var f = field!.Element.Layout.AbsoluteBorderBox;
            var i = inc!.Element.Layout.AbsoluteBorderBox;
            var d = dec!.Element.Layout.AbsoluteBorderBox;

            // The field still has room to type in. (The height check is a floor against an
            // outright collapse, not a fidelity check — a TextInput without the explicit
            // Ui.FieldHeight still lays out; it just stops painting.)
            Assert.True(f.Width > 80, $"exclude field squeezed to {f.Width}px");
            Assert.True(f.Height >= 24, $"exclude field collapsed to {f.Height}px tall");

            // One line: the stepper sits to the right of the field, centred on it.
            Assert.True(d.X > f.X + f.Width, "the stepper wrapped under the exclude field");
            Assert.True(i.X > d.X, "the stepper's + and − swapped places");
            Assert.True(System.Math.Abs((f.Y + f.Height / 2) - (i.Y + i.Height / 2)) < 4,
                "the stepper is not vertically centred on the exclude field");

            // And the whole row fits inside the card rather than being clipped away.
            Assert.True(i.X + i.Width <= f.X + f.Width + 200, "the stepper overflows the card");
        }
        finally { session.Dispose(); Cleanup(app); }
    }

    [Fact]
    public void ExcludeAndDepth_PersistAsDefaultsWhenTheDialogCloses()
    {
        var session = Open(out var app);
        try
        {
            SetField(app, "_findExclude", ".git;.vs");
            Click(session, "find-depth:inc");
            Invoke(app, "CloseFind");
            session.PumpFrame();

            var saved = SettingsStore.Load();
            Assert.Equal(1, saved.FindMaxDepth);
            Assert.Equal(".git;.vs", saved.FindExcludeFolders);

            // And a fresh dialog opens with them already in place.
            Invoke(app, "OpenFind", 'L');
            session.PumpFrame();
            Assert.Equal(1, Field<int>(app, "_findMaxDepth"));
            Assert.Equal(".git;.vs", Field<string>(app, "_findExclude"));
        }
        finally
        {
            // Leave the shared scratch settings as we found them — other tests read it.
            var s = SettingsStore.Load();
            s.FindMaxDepth = 0; s.FindExcludeFolders = "";
            SettingsStore.Save(s);
            session.Dispose(); Cleanup(app);
        }
    }

    // ── harness ──────────────────────────────────────────────────────────
    private static readonly List<string> _tmp = new();

    private static HeadlessSession Open(out CommanderApp app)
    {
        var root = Path.Combine(Path.GetTempPath(), "vfind-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); _tmp.Add(root);
        var session = HeadlessSession.Create(1240, 820);
        app = new CommanderApp(session.Application, session.Window.Window, root);
        session.Application.SetRoot(app.Mount());
        session.PumpFrame();
        Invoke(app, "OpenFind", 'L');
        session.PumpFrame();
        return session;
    }

    private static void Click(HeadlessSession session, string automationId)
    {
        var n = session.Window.FindByAutomationId(automationId);
        Assert.NotNull(n);
        n!.Click();
        session.PumpFrame();
    }

    private static void Cleanup(CommanderApp? app)
    {
        (app as System.IDisposable)?.Dispose();
        foreach (var d in _tmp) { try { Directory.Delete(d, true); } catch { } }
        _tmp.Clear();
    }

    static void SetField(CommanderApp app, string f, object v)
        => typeof(CommanderApp).GetField(f, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, v);
    static T Field<T>(CommanderApp app, string f)
        => (T)typeof(CommanderApp).GetField(f, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
    static void Mark(CommanderApp app)
        => Invoke(app, "MarkDirty");
    static void Invoke(CommanderApp app, string method, params object[] args)
        => typeof(CommanderApp).GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, args);
}
