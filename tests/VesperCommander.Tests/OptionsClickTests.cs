using System.IO;
using System.Reflection;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Drives the real Options dialog headlessly to prove its controls are clickable.
/// Regression guard for the toggle switches doing nothing: the switch had its own
/// OnClick while its row also had one, so a click on the switch fired the toggle twice
/// (switch + bubbled row) and netted no change.
/// </summary>
public class OptionsClickTests
{
    [Fact]
    public void ToggleSwitch_Click_TogglesOnce()
    {
        var session = Open(out var app);
        try
        {
            SetField(app, "_optSearch", "Directories first");   // surface the toggle on any page
            Mark(app); session.PumpFrame();

            var t = session.Window.FindByAutomationId("opt-toggle:Directories first");
            Assert.NotNull(t);
            var b = t!.Element.Layout.AbsoluteBorderBox;

            bool before = Draft<bool>(app, "DirsFirst");
            // Click squarely on the switch (left edge of the row), the spot that looked dead.
            session.Window.Click(b.X + 15, b.Y + b.Height / 2);
            session.PumpFrame();

            Assert.NotEqual(before, Draft<bool>(app, "DirsFirst"));
        }
        finally { session.Dispose(); Cleanup(app); }
    }

    [Fact]
    public void AccentSwatch_Click_Selects()
    {
        var session = Open(out var app);
        try
        {
            SetField(app, "_optionsPage", 4);   // Colors
            Mark(app); session.PumpFrame();

            int cur = Draft<int>(app, "Accent");
            int target = (cur + 1) % 4;
            var s = session.Window.FindByAutomationId("opt-accent:" + target);
            Assert.NotNull(s);

            s!.Click();
            session.PumpFrame();

            Assert.Equal(target, Draft<int>(app, "Accent"));
        }
        finally { session.Dispose(); Cleanup(app); }
    }

    // ── harness ──────────────────────────────────────────────────────────
    private static readonly List<string> _tmp = new();

    private static HeadlessSession Open(out CommanderApp app)
    {
        var root = Path.Combine(Path.GetTempPath(), "voc-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); _tmp.Add(root);
        var session = HeadlessSession.Create(1240, 820);
        app = new CommanderApp(session.Application, session.Window.Window, root);
        session.Application.SetRoot(app.Mount());
        session.PumpFrame();
        SetField(app, "_optionsOpen", true);
        Mark(app); session.PumpFrame();
        return session;
    }

    private static void Cleanup(CommanderApp? app)
    {
        (app as System.IDisposable)?.Dispose();
        foreach (var d in _tmp) { try { Directory.Delete(d, true); } catch { } }
        _tmp.Clear();
    }

    static void SetField(CommanderApp app, string f, object v)
        => typeof(CommanderApp).GetField(f, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(app, v);
    static void Mark(CommanderApp app)
        => typeof(CommanderApp).GetMethod("MarkDirty", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, null);
    static T Draft<T>(CommanderApp app, string field)
    {
        var draft = typeof(CommanderApp).GetField("_draft", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
        return (T)draft.GetType().GetField(field)!.GetValue(draft)!;
    }
}
