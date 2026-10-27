using System.IO;
using System.Reflection;
using HultUI.Core.Events;
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

            Step(session, +1);
            Assert.Equal(1, Field<int>(app, "_findMaxDepth"));
            Assert.Contains("1 level", session.Window.DumpAccessibilityTree());

            Step(session, +1);
            Step(session, +1);
            Assert.Equal(3, Field<int>(app, "_findMaxDepth"));
            Assert.Contains("3 levels", session.Window.DumpAccessibilityTree());

            // Below 0 there is nothing to step down to — 0 *is* unbounded.
            for (int i = 0; i < 5; i++) Step(session, -1);
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
            var depth = session.Window.FindByAutomationId("find-depth");
            Assert.NotNull(field); Assert.NotNull(depth);

            var f = field!.Element.Layout.AbsoluteBorderBox;
            var i = depth!.Element.Layout.AbsoluteBorderBox;
            var d = i;

            // The field still has room to type in. (The height check is a floor against an
            // outright collapse, not a fidelity check — a TextInput without the explicit
            // Ui.FieldHeight still lays out; it just stops painting.)
            Assert.True(f.Width > 80, $"exclude field squeezed to {f.Width}px");
            Assert.True(f.Height >= 24, $"exclude field collapsed to {f.Height}px tall");

            // One line: the stepper sits to the right of the field, centred on it.
            Assert.True(d.X > f.X + f.Width, "the depth field wrapped under the exclude field");
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
            Step(session, +1);
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

    /// <summary>Past 60 hits the results are a virtual list of item rows: a key makes the two rows
    /// the selection moves between and keeps the rest, which show what a remake of all of them
    /// shows; a click on a kept row picks its own hit. Each row used to be made again on every key
    /// and look its own place up in the results (two IndexOf walks a row).</summary>
    [Fact]
    public void AKeyInALongResultList_MakesTheTwoRowsTheSelectionMovesBetween()
    {
        using var t = new DiffViewTests.Fixture();
        for (int i = 0; i < 200; i++) t.WriteLeft($"f{i:000}.txt", "x");
        t.Open();
        t.Press(KeyCode.F7, KeyModifiers.Alt);
        t.SetField("_findPattern", ".txt");
        t.Invoke("RunFind");
        t.Session.PumpUntil(() => !t.Field<bool>("_findSearching"), System.TimeSpan.FromSeconds(30));
        t.Session.PumpFrames(3);
        var listRef = t.Field<object>("_findVList");
        HultUI.Core.Elements.Element List() =>
            (HultUI.Core.Elements.Element)listRef.GetType().GetProperty("Current")!.GetValue(listRef)!;
        string Rows() => string.Join("\n", List().Children.Skip(1).OrderBy(r => r.ScreenBorderBox.Y).Select(r =>
            $"{((HultUI.Core.Elements.BoxElement)r).BorderColor} {HultUI.Headless.LayoutProbe.Descendants<HultUI.Core.Elements.TextElement>(r).First().Content}"));
        Assert.Equal(200, t.Field<System.Collections.IList>("_findResults").Count);

        foreach (var key in new[] { KeyCode.Down, KeyCode.Down, KeyCode.Down, KeyCode.Up, KeyCode.PageDown })
        {
            int made = t.App.FindRowsMade;
            t.Press(key);
            t.Session.PumpFrames(2);
            if (key != KeyCode.PageDown) Assert.Equal(2, t.App.FindRowsMade - made);
            string kept = Rows();
            List().GetType().GetMethod("InvalidateRows")!.Invoke(List(), null);
            t.MarkDirty();
            t.Session.PumpFrames(2);
            Assert.Equal(Rows(), kept);
        }

        // A kept row's click picks its own hit.
        var top = List().ScreenBorderBox.Y;
        var row = List().Children.Skip(1).OrderBy(r => r.ScreenBorderBox.Y).First(r => r.ScreenBorderBox.Y > top + 40);
        string name = HultUI.Headless.LayoutProbe.Descendants<HultUI.Core.Elements.TextElement>(row).First().Content;
        t.Session.Window.Click(row);
        t.Session.PumpFrames(3);
        Assert.False(t.Field<bool>("_findOpen"));
        Assert.Equal(name, t.Field<List<FindEngine.Hit>>("_findResults")[t.Field<int>("_findSel")].Name);
    }

    /// <summary>Past 60 hits the results are a virtual list, which names each item to assistive
    /// technology itself: as its row shows it — the name, a folder said to be one, the folder it
    /// is in relative to the one searched, and a file's size. They were read out as the Hit
    /// record's ToString(), every field and the full path.</summary>
    [Fact]
    public void ALongResultList_NamesEachResultAsItsRowShowsIt()
    {
        using var t = new DiffViewTests.Fixture();
        for (int i = 0; i < 70; i++) t.WriteLeft($"f{i:000}.txt", "x");
        Directory.CreateDirectory(Path.Combine(t.Left, "sub"));
        File.WriteAllText(Path.Combine(t.Left, "sub", "deep.txt"), "xy");
        Directory.CreateDirectory(Path.Combine(t.Left, "notes.txt"));   // a folder the pattern matches
        t.Open();
        var bridge = t.Session.Window.InstallAccessibilityBridge();
        t.Press(KeyCode.F7, KeyModifiers.Alt);
        t.SetField("_findPattern", ".txt");
        t.SetField("_findRecursive", true);
        t.Invoke("RunFind");
        t.Session.PumpUntil(() => !t.Field<bool>("_findSearching"), System.TimeSpan.FromSeconds(30));
        t.Session.PumpFrames(3);
        Assert.Equal(72, t.Field<System.Collections.IList>("_findResults").Count);

        var names = new List<string>();
        void Walk(HultUI.Core.Accessibility.AccessibilityNode node)
        {
            if (node.Role == HultUI.Core.Accessibility.AccessibilityRole.ListItem) names.Add(node.Name ?? "");
            foreach (var child in node.Children) Walk(child);
        }
        Walk(bridge.CurrentTree!);

        Assert.Contains($"f000.txt, {FileSystem.ByteCount(1)}", names);
        Assert.Contains($"deep.txt, in sub, {FileSystem.ByteCount(2)}", names);
        Assert.Contains("notes.txt, folder", names);
        Assert.DoesNotContain(names, n => n.StartsWith("Hit {", System.StringComparison.Ordinal));
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

    /// <summary>One press of Up or Down on the depth field: click it to focus, then the key.</summary>
    private static void Step(HeadlessSession session, int direction)
    {
        var n = session.Window.FindByAutomationId("find-depth");
        Assert.NotNull(n);
        n!.Click();
        session.Window.PressKey(direction > 0 ? KeyCode.Up : KeyCode.Down);
        session.PumpFrame();
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
