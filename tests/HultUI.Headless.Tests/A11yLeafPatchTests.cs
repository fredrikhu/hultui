// The AT-SPI bridge is Linux's, but with its signals recorded instead of sent it runs anywhere:
// nothing here reaches a bus.
#pragma warning disable CA1416
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Linux;
using HultUI.Platform.Accessibility.Windows;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// A change of a leaf's own properties — a text input's value, a toggle, a range, a name, a
/// description, an expanded state — is patched into the applied accessibility tree: the leaf's node
/// is made again, compared with the applied one and put in its place, and the bridges are handed the
/// changes, with no tree built (plan 17, round 4). Each rebuilt and diffed the whole tree for one
/// node: a keystroke beside 200 rows 53 KB with UIA, a check box's click among 500 rows 87 KB, a
/// playing video's scrubber two or three trees a second. Each case runs twice: once as it is, and
/// once with a change in the same frame that cannot be patched (an automation id, which no bridge
/// tells), so the tree is rebuilt; what each bridge tells, and the trees, must be the same.
/// </summary>
public class A11yLeafPatchTests
{
    /// <summary>A real bridge, reported active whether or not a client listens; counts patches.</summary>
    private sealed class Counting(IAccessibilityBridge inner) : IAccessibilityBridge
    {
        public int Patches;
        public AccessibilityNode? Tree;
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) { Tree = root; inner.ApplyChanges(diff, root); }
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root)
        {
            Tree = root;
            Patches++;
            inner.ApplyPatchedProperties(patch, root);
        }
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
    }

    private sealed class Signals(Func<string, string> map) : IAtSpiSignals
    {
        public readonly List<string> Lines = [];
        public void EmitChildrenChanged(string parentPath, string childPath, int index, bool isAdd) =>
            Lines.Add($"children-changed:{(isAdd ? "add" : "remove")} {map(parentPath)} {map(childPath)} {index}");
        public void EmitPropertyChange(string path, string atSpiProperty, string newValue) => Lines.Add($"property-change:{atSpiProperty} {map(path)} {newValue}");
        public void EmitValueChanged(string path, double value) => Lines.Add($"property-change:accessible-value {map(path)} {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        public void EmitRoleChange(string path, uint role) => Lines.Add($"property-change:accessible-role {map(path)} {role}");
        public void EmitStateChanged(string path, string state, bool active) => Lines.Add($"state-changed:{state} {map(path)} {(active ? 1 : 0)}");
        public void EmitTextChanged(string path, bool insert, int offset, string text) => Lines.Add($"text-changed {map(path)} {insert} {offset} {text}");
        public void EmitTextCaretMoved(string path, int offset) => Lines.Add($"text-caret-moved {map(path)} {offset}");
        public void EmitTextSelectionChanged(string path) => Lines.Add($"text-selection-changed {map(path)}");
        public void EmitAnnouncement(string path, string text, int politeness) => Lines.Add($"announcement {map(path)}");
        public void EmitCacheAdd(IAtSpiTree tree, AtSpiAccessible node) => Lines.Add($"cache-add {map(node.Path)} {map(node.ParentPath)} {node.IndexInParent}");
        public void EmitCacheRemove(string path) => Lines.Add($"cache-remove {map(path)}");
    }

    /// <summary>A scene: what to mount, the steps, and a box whose automation id a step changes, in
    /// the frame of its own change, to have the tree rebuilt instead (<c>force</c>).</summary>
    private sealed class Scene
    {
        public required Element Root;
        public readonly BoxElement Forcer = Box.New().Width(Dimension.Px(10)).Height(Dimension.Px(10)).AccessibleName("forcer");
        // Its automation id changed in both runs: a rebuild just before a step.
        public readonly BoxElement Warm = Box.New().Width(Dimension.Px(10)).Height(Dimension.Px(10)).AccessibleName("warm");
        private int _warm;
        public readonly List<(string Name, Action<HeadlessSession>? Before, Action Change, Action<HeadlessSession, Action> Frames)> Steps = [];

        public void Step(string name, Action change) => Steps.Add((name, null, change, (s, force) => { force(); s.PumpFrame(); }));

        /// <summary>A change that marks layout (a label's), 16 ms after a rebuild, so it comes inside the
        /// coalesced layout refresh's wait (and not as its leading edge, which rebuilds); then its frame
        /// and two 120 ms apart, so the refresh has come and gone and both runs show the same tree.</summary>
        public void Settled(string name, Action change) => Steps.Add((name,
            s =>
            {
                Warm.AutomationId = $"warm-{_warm++}";
                s.PumpFrame(TimeSpan.FromMilliseconds(16));
            },
            change,
            (s, force) =>
            {
                force();
                s.PumpFrame(TimeSpan.FromMilliseconds(16));
                s.PumpFrame(TimeSpan.FromMilliseconds(120));
                s.PumpFrame(TimeSpan.FromMilliseconds(120));
            }));

        /// <summary>A range's change 300 ms after the last: its refresh is due at once.</summary>
        public void Range(string name, Action change) =>
            Steps.Add((name, null, change, (s, force) => { force(); s.PumpFrame(TimeSpan.FromMilliseconds(300)); }));
    }

    /// <summary>Cases whose change is patched, with no tree built for it.</summary>
    public static TheoryData<string> Patched => new()
    {
        "typing", "password", "checkbox click", "checkbox by code, offscreen", "switch", "app toggle", "slider",
        "progress, toggle while pending", "number field", "leaf name", "leaf description", "leaf expanded",
        "canvas name", "label", "label in an explicitly named button", "two leaves, reverse order",
        "selection and leaves", "in an open overlay",
    };

    /// <summary>Cases the tree is rebuilt for, as before.</summary>
    public static TheoryData<string> Rebuilt => new()
    {
        "label in a button named from its content", "box that gains a name", "box whose name goes",
        "text a button takes for its name", "radio", "box with a child, named", "masked",
    };

    private static BoxElement Rows(int n)
    {
        var rows = new Element[n];
        for (int i = 0; i < n; i++)
            rows[i] = Flex.Row(gap: 8).FlexShrink(0).Padding(new Thickness(2, 8)).Children(
                TextB.New($"{i}").FontSize(12f).Width(Dimension.Px(40)), TextB.New($"row {i} label").FontSize(12f));
        return Flex.Column().FlexShrink(0).Children(rows);
    }

    private static Scene Make(string which)
    {
        Scene With(Element content, Action<Scene> steps)
        {
            var scene = new Scene { Root = null! };
            scene.Root = Flex.Column(gap: 4).Padding(8).Children(TextB.New("Header"), content, scene.Forcer, scene.Warm, Rows(30));
            steps(scene);
            return scene;
        }

        switch (which)
        {
            case "typing":
            {
                var input = TextInput.New("").Width(Dimension.Px(300));
                return With(input, s =>
                {
                    s.Steps.Add(("focus, a", session => { input.Focus(); session.PumpFrame(); }, () => input.Text += "a",
                        (session, force) => { force(); session.PumpFrame(); }));
                    s.Step("b", () => input.Text += "b");
                    s.Step("c", () => input.Text += "c");
                });
            }
            case "password":
            {
                var input = TextInput.New("").Width(Dimension.Px(300));
                input.Masked = true;
                return With(input, s =>
                {
                    s.Step("s", () => input.Text += "s");
                    s.Step("e", () => input.Text += "e");
                    s.Step("set", () => input.Text = "secret");
                });
            }
            case "checkbox click" or "checkbox by code, offscreen":
            {
                var boxes = new CheckboxElement[60];
                var rows = new Element[boxes.Length];
                for (int i = 0; i < boxes.Length; i++)
                {
                    boxes[i] = Checkbox.New(i % 3 == 0, $"Option {i}");
                    rows[i] = Flex.Row(gap: 8).FlexShrink(0).Children(boxes[i], TextB.New($"Detail {i}"));
                }
                var view = ScrollView.New().Height(Dimension.Px(200)).Children(Flex.Column().FlexShrink(0).Children(rows));
                return With(view, s =>
                {
                    if (which == "checkbox click")
                    {
                        s.Step("toggle 2", () => boxes[2].IsChecked = !boxes[2].IsChecked);
                        s.Step("toggle 5", () => boxes[5].IsChecked = !boxes[5].IsChecked);
                        s.Step("focus and toggle", () => { boxes[5].Focus(); boxes[5].IsChecked = !boxes[5].IsChecked; });
                    }
                    else
                    {
                        s.Step("toggle 45", () => boxes[45].IsChecked = !boxes[45].IsChecked);
                        s.Step("toggle 59", () => boxes[59].IsChecked = !boxes[59].IsChecked);
                    }
                });
            }
            case "switch":
            {
                var toggle = ToggleSwitch.New(false, "Wi-Fi").Width(Dimension.Px(200));
                return With(toggle, s =>
                {
                    s.Step("on", () => toggle.IsOn = true);
                    s.Step("off", () => toggle.IsOn = false);
                });
            }
            case "app toggle":
            {
                var box = Box.New().Width(Dimension.Px(24)).Height(Dimension.Px(24)).AccessibleName("Bold");
                box.AccessibleToggled = false;
                return With(box, s =>
                {
                    s.Step("on", () => box.AccessibleToggled = true);
                    s.Step("none", () => box.AccessibleToggled = null);
                    s.Step("off", () => box.AccessibleToggled = false);
                });
            }
            case "slider":
            {
                var slider = Slider.New(30, 0, 100).Width(Dimension.Px(240));
                return With(slider, s =>
                {
                    s.Range("60", () => slider.Value = 60);
                    s.Range("max", () => slider.Maximum = 200);
                    s.Range("min and value", () => { slider.Minimum = 10; slider.Value = 150; });
                    s.Range("step", () => slider.Step = 5);
                });
            }
            case "progress, toggle while pending":
            {
                var bar = ProgressBar.New(0.1).Width(Dimension.Px(240));
                var check = Checkbox.New(false, "Notify me");
                return With(Flex.Column().Children(bar, check), s =>
                {
                    s.Range("tick", () => bar.Value = 0.2);
                    // The bar's two moves wait for the range's refresh; the toggle's patch tells them,
                    // as the rebuild it stands for would.
                    s.Steps.Add(("tick, tick and toggle", null, () => bar.Value = 0.3, (session, force) =>
                    {
                        session.PumpFrame(TimeSpan.FromMilliseconds(16));
                        bar.Value = 0.35;
                        session.PumpFrame(TimeSpan.FromMilliseconds(16));
                        check.IsChecked = true;
                        force();
                        session.PumpFrame(TimeSpan.FromMilliseconds(16));
                    }));
                    s.Range("done", () => bar.Value = 1);
                });
            }
            case "number field":
            {
                var number = NumericUpDown.New(5, 0, 10).Width(Dimension.Px(120));
                return With(number, s =>
                {
                    s.Range("6", () => number.Value = 6);
                    s.Range("7", () => number.Value = 7);
                });
            }
            case "leaf name" or "leaf description" or "leaf expanded":
            {
                var box = Box.New().Width(Dimension.Px(120)).Height(Dimension.Px(24)).AccessibleName("Inbox (0)").AccessibleDescription("first");
                box.AccessibleExpanded = false;
                return With(box, s =>
                {
                    if (which == "leaf name")
                    {
                        s.Step("1", () => box.AccessibleName = "Inbox (1)");
                        s.Step("2", () => box.AccessibleName = "Inbox (2)");
                    }
                    else if (which == "leaf description")
                        s.Step("second", () => box.AccessibleDescription = "second");
                    else
                    {
                        s.Step("open", () => box.AccessibleExpanded = true);
                        s.Step("closed", () => box.AccessibleExpanded = false);
                        s.Step("none", () => box.AccessibleExpanded = null);
                    }
                });
            }
            case "canvas name":
            {
                var canvas = Canvas.New().Width(Dimension.Px(24)).Height(Dimension.Px(24));
                canvas.AccessibleName = "Play";
                return With(canvas, s => s.Step("pause", () => canvas.AccessibleName = "Pause"));
            }
            case "label":
            {
                var status = TextB.New("Status: idle");
                var grow = TextB.New("A");
                return With(Flex.Column().Children(status, Flex.Row(gap: 6).Children(grow, TextB.New("after"))), s =>
                {
                    s.Settled("busy", () => status.Content = "Status: busy");
                    s.Settled("grow", () => grow.Content = "A much longer label");
                    s.Settled("empty", () => status.Content = "");
                });
            }
            case "label in an explicitly named button" or "label in a button named from its content":
            {
                var text = TextB.New("Save");
                var icon = Canvas.New().Width(Dimension.Px(16)).Height(Dimension.Px(16)).AccessibleName("disk");
                var button = Box.New().Padding(4).Children(icon, text);
                button.On<ClickEvent>(_ => { });
                if (which == "label in an explicitly named button") button.AccessibleName = "Save the file";
                return With(button, s => s.Settled("text", () => text.Content = "Save all"));
            }
            case "two leaves, reverse order":
            {
                var label = TextB.New("Top");
                var check = Checkbox.New(false, "Bottom");
                return With(Flex.Column().Children(label, Rows(5), check), s =>
                {
                    s.Settled("both", () => { check.IsChecked = true; label.Content = "Top 2"; });
                    s.Settled("both again", () => { check.IsChecked = false; label.Content = "Top 3"; });
                });
            }
            case "selection and leaves":
            {
                var label = TextB.New("Nothing selected");
                var items = Enumerable.Range(0, 20).Select(i => $"item {i}").ToList();
                var list = VirtualListElement<string>.New().ItemHeight(24)
                    .RenderWith(t => Flex.Row().Padding(new Thickness(2, 8)).Children(TextB.New(t).FontSize(12f)))
                    .Items(items).Height(Dimension.Px(200));
                var check = Checkbox.New(false, "Remember");
                return With(Flex.Column().Children(label, list, check), s =>
                {
                    s.Settled("select 3", () => { list.SelectedIndex = 3; check.IsChecked = true; label.Content = "item 3"; });
                    s.Settled("select 4", () => { label.Content = "item 4"; list.SelectedIndex = 4; check.IsChecked = false; });
                });
            }
            case "in an open overlay":
            {
                // A dialog's fields: the overlay's node is a child of the root node, and its leaves are
                // found down from it (plan 17, round 5).
                var check = Checkbox.New(false, "Remember me");
                var input = TextInput.New("").Width(Dimension.Px(200));
                var content = Flex.Column(gap: 4).Padding(8).Children(TextB.New("Sign in"), input, check);
                return With(Box.New().Width(Dimension.Px(10)).Height(Dimension.Px(10)), s =>
                {
                    s.Steps.Add(("open, toggle", session =>
                    {
                        session.Window.Window.ShowOverlay(content, 300, 40);
                        session.PumpFrame();
                        session.PumpFrame(TimeSpan.FromMilliseconds(300));
                    }, () => check.IsChecked = true, (session, force) => { force(); session.PumpFrame(); }));
                    s.Steps.Add(("focus, a", session => { input.Focus(); session.PumpFrame(); }, () => input.Text += "a",
                        (session, force) => { force(); session.PumpFrame(); }));
                    s.Step("b", () => input.Text += "b");
                    s.Step("toggle back", () => check.IsChecked = false);
                });
            }
            case "palette typing" or "palette list scrolled":
            {
                // An open overlay's part of the tree is built again on its own (plan 17, round 5): the
                // field's value and the new list for a keystroke, the list's scroll for a move.
                var words = Enumerable.Range(0, 40).Select(i => $"{(i % 3 == 0 ? "Save" : i % 3 == 1 ? "Open" : "Close")} item {i}").ToList();
                var palette = new CommandPalette(() => words.Select(w => new PaletteCommand(w, () => { })));
                HeadlessSession? live = null;
                return With(Box.New().Width(Dimension.Px(10)).Height(Dimension.Px(10)), s =>
                {
                    void Open(HeadlessSession session)
                    {
                        live = session;
                        palette.Open(session.Window.Window);
                        session.PumpFrame();
                        session.PumpFrame(TimeSpan.FromMilliseconds(300));
                    }
                    if (which == "palette typing")
                    {
                        s.Steps.Add(("open, s", Open, () => live!.Window.TypeText("s"), (session, force) => { force(); session.PumpFrame(); }));
                        s.Step("a", () => live!.Window.TypeText("a"));
                        s.Step("backspace", () => live!.Window.PressKey(KeyCode.Backspace));
                        s.Step("zz, no match", () => live!.Window.TypeText("zz"));
                        s.Step("backspace x3", () => { for (int i = 0; i < 3; i++) live!.Window.PressKey(KeyCode.Backspace); });
                    }
                    else
                    {
                        s.Steps.Add(("open", Open, () => { }, (session, force) => { force(); session.PumpFrame(); }));
                        for (int k = 1; k <= 3; k++)
                            s.Settled($"down x10 ({k})", () => { for (int i = 0; i < 10; i++) live!.Window.PressKey(KeyCode.Down); });
                        s.Settled("up x25", () => { for (int i = 0; i < 25; i++) live!.Window.PressKey(KeyCode.Up); });
                    }
                });
            }
            case "overlay children" or "move in an overlay":
            {
                var count = new HultUI.Core.Reactivity.Signal<int>(2);
                var choices = new Picker(count);
                var grow = Box.New().Width(Dimension.Px(20)).Height(Dimension.Px(10));
                var content = Flex.Column(gap: 4).Padding(8).Children(Flex.Row(gap: 4).Children(grow, Button.New("After")), choices.Mount());
                return With(Box.New().Width(Dimension.Px(10)).Height(Dimension.Px(10)), s =>
                {
                    void Open(HeadlessSession session)
                    {
                        session.Window.Window.ShowOverlay(content, 300, 40);
                        session.PumpFrame();
                        session.PumpFrame(TimeSpan.FromMilliseconds(300));
                    }
                    if (which == "overlay children")
                    {
                        s.Steps.Add(("open, 4", Open, () => count.Value = 4, (session, force) => { force(); session.PumpFrame(); }));
                        s.Step("1", () => count.Value = 1);
                        s.Step("3", () => count.Value = 3);
                    }
                    else
                    {
                        s.Steps.Add(("open", Open, () => { }, (session, force) => { force(); session.PumpFrame(); }));
                        s.Settled("grow", () => { grow.Box.Width = Dimension.Px(120); grow.MarkLayoutDirty(); });
                        s.Settled("shrink", () => { grow.Box.Width = Dimension.Px(60); grow.MarkLayoutDirty(); });
                    }
                });
            }
            case "box that gains a name" or "box whose name goes":
            {
                var box = Box.New().Width(Dimension.Px(24)).Height(Dimension.Px(24));
                if (which == "box whose name goes") box.AccessibleName = "Badge";
                return With(box, s => s.Step("name", () => box.AccessibleName = which == "box whose name goes" ? null : "Badge"));
            }
            case "text a button takes for its name":
            {
                var button = Button.New("Consumed");
                return With(button, s => s.Settled("text", () => ((TextElement)button.Children[0]).Content = "Consumed 2"));
            }
            case "radio":
            {
                var a = RadioButton.New("g", true, "A").Width(Dimension.Px(120));
                var b = RadioButton.New("g", false, "B").Width(Dimension.Px(120));
                return With(Flex.Column().Children(a, b), s => s.Step("b", () => { a.IsChecked = false; b.IsChecked = true; }));
            }
            case "box with a child, named":
            {
                var box = Box.New().Children(TextB.New("Folder")).AccessibleName("Folder");
                return With(box, s => s.Step("rename", () => box.AccessibleName = "Folder 2"));
            }
            case "masked":
            {
                var input = TextInput.New("abc").Width(Dimension.Px(300));
                return With(input, s =>
                {
                    s.Settled("mask", () => input.Masked = true);
                    s.Settled("reveal", () => input.Masked = false);
                });
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(which));
        }
    }

    /// <summary>Buttons as many as <paramref name="count"/> says.</summary>
    private sealed class Picker(HultUI.Core.Reactivity.Signal<int> count) : HultUI.Core.Components.Component
    {
        public override Element Build()
        {
            var column = Flex.Column(gap: 4);
            for (int i = 0; i < count.Value; i++) column.AddChild(Button.New($"Choice {i}"));
            return column;
        }
    }

    /// <summary>Cases a change inside an open overlay builds that overlay's part of the tree again for,
    /// and no tree (plan 17, round 5).</summary>
    public static TheoryData<string> OverlayParts => new()
    {
        "palette typing", "palette list scrolled", "overlay children", "move in an overlay",
    };

    [Theory]
    [MemberData(nameof(OverlayParts))]
    public void ATSPI_AnOverlaysPartBuiltAgain_TellsWhatARebuildTells_AndBuildsNoTree(string which)
    {
        var parts = Play(which, forceRebuild: false, AtSpi);
        var rebuilt = Play(which, forceRebuild: true, AtSpi);
        Assert.Equal(rebuilt.Told, parts.Told);
        Assert.Equal(rebuilt.Trees, parts.Trees);
        Assert.All(parts.TreesPerStep, built => Assert.Equal(0, built));
    }

    [WindowsTheory]
    [MemberData(nameof(OverlayParts))]
    [SupportedOSPlatform("windows")]
    public void UIA_AnOverlaysPartBuiltAgain_RaisesWhatARebuildRaises_AndBuildsNoTree(string which)
    {
        var parts = PlayUia(which, forceRebuild: false);
        var rebuilt = PlayUia(which, forceRebuild: true);
        Assert.Equal(rebuilt.Told, parts.Told);
        Assert.Equal(rebuilt.Trees, parts.Trees);
        Assert.All(parts.TreesPerStep, built => Assert.Equal(0, built));
    }

    /// <summary>What a run told and built: the bridge's lines, a dump of the tree after each step,
    /// the patches and trees per step.</summary>
    private sealed record Run(List<string> Told, List<string> Trees, int Patches, int[] TreesPerStep);

    /// <summary>Mounts the scene, lets the mount's refreshes go, then runs each step, with or without
    /// forcing a rebuild in its frame.</summary>
    private static Run Play(string which, bool forceRebuild, Func<Func<ulong, string>, (IAccessibilityBridge Bridge, Func<List<string>> Told)> bridge)
    {
        using var session = HeadlessSession.Create(800, 900);
        var scene = Make(which);
        ulong baseId = MinId(scene.Root);
        string Map(ulong raw) => raw == 0 ? "root" : VirtualItemId.TryDecode(new ElementId(raw), out var list, out int index)
            ? $"v{list.Value - baseId}#{index}" : $"e{raw - baseId}";
        var (inner, told) = bridge(Map);
        var counting = new Counting(inner);
        session.Window.InstallAccessibilityBridge(counting);
        session.Window.SetRoot(scene.Root);
        session.PumpFrames(2);
        for (int i = 0; i < 3; i++) session.PumpFrame(TimeSpan.FromMilliseconds(300));
        told().Clear();
        int patches = counting.Patches;
        var trees = new List<string>();
        var perStep = new int[scene.Steps.Count];
        int flip = 0;
        void Force()
        {
            if (forceRebuild) scene.Forcer.AutomationId = $"forced-{flip++}";
        }
        for (int i = 0; i < scene.Steps.Count; i++)
        {
            var (name, before, change, frames) = scene.Steps[i];
            before?.Invoke(session);
            int built = session.Window.AccessibilityTreesBuilt;
            told().Add($"== {name}");
            change();
            frames(session, Force);
            perStep[i] = session.Window.AccessibilityTreesBuilt - built;
            trees.Add(Dump(counting.Tree!, Map));
        }
        return new Run([.. told()], trees, counting.Patches - patches, perStep);
    }

    private static ulong MinId(Element e)
    {
        ulong min = e.Id.Value;
        foreach (var child in e.Children) min = Math.Min(min, MinId(child));
        return min;
    }

    /// <summary>Everything a node carries but its automation id, and that each child's parent is it.</summary>
    private static string Dump(AccessibilityNode root, Func<ulong, string> map)
    {
        var sb = new StringBuilder();
        Walk(root, 0);
        return sb.ToString();

        void Walk(AccessibilityNode n, int depth)
        {
            sb.Append(' ', depth).Append(n.Parent is null ? "root" : map(n.ElementId.Value)).Append(' ').Append(n.Role)
                .Append(" n=").Append(n.Name).Append(" d=").Append(n.Description).Append(" r=").Append(n.BoundingRect)
                .Append(" s=").Append(n.States).Append(" off=").Append(n.IsOffscreen).Append(" v=").Append(n.Value)
                .Append(" range=").Append(n.Range).Append(" kids=").Append(n.Children.Count).Append('\n');
            foreach (var c in n.Children)
            {
                Assert.Same(n, c.Parent);
                Walk(c, depth + 1);
            }
        }
    }

    private static (IAccessibilityBridge, Func<List<string>>) AtSpi(Func<ulong, string> map)
    {
        const string prefix = "/org/a11y/atspi/accessible/";
        var signals = new Signals(path => path.StartsWith(prefix) && ulong.TryParse(path.AsSpan(prefix.Length), out var raw) ? map(raw) : path);
        return (new LinuxAccessibilityBridge(signals), () => signals.Lines);
    }

    [Theory]
    [MemberData(nameof(Patched))]
    public void ATSPI_APatchedChange_TellsWhatARebuildTells_AndBuildsNoTree(string which)
    {
        var patched = Play(which, forceRebuild: false, AtSpi);
        var rebuilt = Play(which, forceRebuild: true, AtSpi);
        Assert.Equal(rebuilt.Told, patched.Told);
        Assert.Equal(rebuilt.Trees, patched.Trees);
        Assert.True(patched.Patches >= patched.TreesPerStep.Length, $"{patched.Patches} patches for {patched.TreesPerStep.Length} steps");
        AssertNoTreeForTheChange(which, patched);
    }

    [Theory]
    [MemberData(nameof(Rebuilt))]
    public void ATSPI_AChangeAPatchCannotTell_RebuildsTheTree(string which)
    {
        var run = Play(which, forceRebuild: false, AtSpi);
        Assert.Equal(0, run.Patches);
        Assert.All(run.TreesPerStep, built => Assert.True(built >= 1));
        Assert.Equal(Play(which, forceRebuild: true, AtSpi).Told, run.Told);
    }

    /// <summary>No change builds a tree: one that marks no layout is patched, and a label's (which
    /// marks layout) is told by the coalesced layout refresh it brings, which finds the same nodes and
    /// brings the applied tree up to date in place.</summary>
    private static void AssertNoTreeForTheChange(string which, Run run) =>
        Assert.All(run.TreesPerStep, built => Assert.Equal(0, built));

    [WindowsTheory]
    [MemberData(nameof(Patched))]
    [SupportedOSPlatform("windows")]
    public void UIA_APatchedChange_RaisesWhatARebuildRaises_AndBuildsNoTree(string which)
    {
        var patched = PlayUia(which, forceRebuild: false);
        var rebuilt = PlayUia(which, forceRebuild: true);
        Assert.Equal(rebuilt.Told, patched.Told);
        Assert.Equal(rebuilt.Trees, patched.Trees);
        Assert.True(patched.Patches >= patched.TreesPerStep.Length);
        AssertNoTreeForTheChange(which, patched);
    }

    [WindowsTheory]
    [MemberData(nameof(Rebuilt))]
    [SupportedOSPlatform("windows")]
    public void UIA_AChangeAPatchCannotTell_RebuildsTheTree(string which)
    {
        var run = PlayUia(which, forceRebuild: false);
        Assert.Equal(0, run.Patches);
        Assert.Equal(PlayUia(which, forceRebuild: true).Told, run.Told);
    }

    /// <summary>A run with the UIA bridge on a hidden window: the events it raises, with what each
    /// node's wrapper answers for the property raised.</summary>
    [SupportedOSPlatform("windows")]
    private static Run PlayUia(string which, bool forceRebuild)
    {
        using var hwnd = TestHwnd.Create();
        WindowsAccessibilityBridge? uia = null;
        var lines = new List<string>();
        try
        {
            return Play(which, forceRebuild, map =>
            {
                uia = new WindowsAccessibilityBridge(hwnd.Handle, new NoHost());
                uia.Raised = r =>
                {
                    string value = "";
                    if (r.Kind == UiaRaisedKind.Property && r.Element is { } id && uia.NativeFragment(id) is var native && native != IntPtr.Zero)
                    {
                        try
                        {
                            var wrapper = (UiaFragment)NativeProvider.LogicOf(native)!;
                            wrapper.GetPropertyValue(r.Id, out var v);
                            value = v.Vt switch
                            {
                                UiaVariant.VT_BSTR => "s:" + v.AsString(),
                                UiaVariant.VT_BOOL => "b:" + v.ShortValue,
                                UiaVariant.VT_I4 => "i:" + v.IntValue,
                                UiaVariant.VT_R8 => "d:" + v.DoubleValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                _ => "vt" + v.Vt,
                            };
                            v.Free();
                        }
                        finally { Marshal.Release(native); }
                    }
                    lines.Add($"{r.Kind} {r.Id} {(r.Element is { } e ? map(e.Value) : "root")} {value}");
                };
                return (uia, () => lines);
            });
        }
        finally { uia?.Dispose(); }
    }

    [Fact]
    public void APasswordFieldsPatch_CarriesItsBullets_NeverItsText()
    {
        using var session = HeadlessSession.Create(800, 600);
        var signals = new Signals(path => path[(path.LastIndexOf('/') + 1)..]);
        using var linux = new LinuxAccessibilityBridge(signals);
        var counting = new Counting(linux);
        session.Window.InstallAccessibilityBridge(counting);
        var input = TextInput.New("").Width(Dimension.Px(300));
        input.Masked = true;
        session.Window.SetRoot(Flex.Column().Children(TextB.New("Password"), input));
        session.PumpFrames(3);
        signals.Lines.Clear();

        input.Text = "hunter2";
        session.PumpFrame();

        Assert.Equal(1, counting.Patches);
        var line = Assert.Single(signals.Lines);
        Assert.Equal($"property-change:accessible-value {input.Id.Value} {new string(TextInputElement.MaskGlyph, 7)}", line);
        Assert.DoesNotContain("hunter2", line);
        var acc = ((IAtSpiTree)linux).Find(AtSpiAccessible.PathFor(input.Id))!;
        Assert.Equal(new string(TextInputElement.MaskGlyph, 7), acc.Value);
        Assert.Equal(new string(TextInputElement.MaskGlyph, 7), Find(counting.Tree!, input)!.Value);
    }

    [Fact]
    public void ATSPI_APatchSetsTheObjectsFields_ThatTheBusThreadReads()
    {
        using var session = HeadlessSession.Create(800, 600);
        using var linux = new LinuxAccessibilityBridge(new Signals(p => p));
        session.Window.InstallAccessibilityBridge(new Counting(linux));
        var check = Checkbox.New(false, "Opt");
        var slider = Slider.New(30, 0, 100).Width(Dimension.Px(200));
        var box = Box.New().Width(Dimension.Px(40)).Height(Dimension.Px(20)).AccessibleName("Box").AccessibleDescription("first");
        box.AccessibleExpanded = false;
        var input = TextInput.New("x").Width(Dimension.Px(200));
        session.Window.SetRoot(Flex.Column().Children(TextB.New("Fields"), check, slider, box, input));
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(300));
        int trees = session.Window.AccessibilityTreesBuilt;
        AtSpiAccessible Acc(Element e) => ((IAtSpiTree)linux).Find(AtSpiAccessible.PathFor(e.Id))!;
        var before = (Acc(check), Acc(slider), Acc(box), Acc(input));

        check.IsChecked = true;
        slider.Value = 70;
        box.AccessibleName = "Box 2";
        box.AccessibleDescription = "second";
        box.AccessibleExpanded = true;
        input.Text = "xy";
        session.PumpFrame(TimeSpan.FromMilliseconds(300));

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        // The same objects, with the new values: no snapshot was built.
        Assert.Same(before.Item1, Acc(check));
        Assert.Same(before.Item3, Acc(box));
        Assert.True(Acc(check).Toggled);
        Assert.Equal(70, Acc(slider).Range!.Value.Value);
        Assert.Equal(("Box 2", "second", (bool?)true), (Acc(box).Name, Acc(box).Description, Acc(box).Expanded));
        Assert.Equal("xy", Acc(input).Value);
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void UIA_APatchedLeaf_LeavesNoWrapperOnItsOldNode()
    {
        using var hwnd = TestHwnd.Create();
        using var session = HeadlessSession.Create(800, 600);
        using var uia = new WindowsAccessibilityBridge(hwnd.Handle, new NoHost());
        session.Window.InstallAccessibilityBridge(new Counting(uia));
        var check = Checkbox.New(false, "Opt");
        session.Window.SetRoot(Flex.Column().Children(TextB.New("Fields"), check, Rows(20)));
        session.PumpFrames(3);
        var old = WeakNode(uia, check);

        check.IsChecked = true;
        session.PumpFrame();
        check.IsChecked = false;
        session.PumpFrame();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(old.IsAlive, "the replaced node is still held");
        var wrapper = uia.Wrappers.Single(w => w.ElementId == check.Id);
        Assert.False(wrapper.Toggled);
        // The wrapper's node is the one in the tree, in its parent's children.
        Assert.Contains(wrapper.Node, wrapper.Node.Parent!.Children);
        Assert.Same(uia.RootNode, RootOf(wrapper.Node));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    [SupportedOSPlatform("windows")]
    private static WeakReference WeakNode(WindowsAccessibilityBridge uia, Element e) =>
        new(uia.Wrappers.Single(w => w.ElementId == e.Id).Node);

    private static AccessibilityNode RootOf(AccessibilityNode node)
    {
        while (node.Parent is { } parent) node = parent;
        return node;
    }

    [Fact]
    public void ManyLeavesInOneFrame_RebuildTheTree()
    {
        using var session = HeadlessSession.Create(800, 900);
        var bridge = new Counting(new HeadlessAccessibilityBridge());
        session.Window.InstallAccessibilityBridge(bridge);
        var boxes = Enumerable.Range(0, 40).Select(i => Checkbox.New(false, $"Box {i}")).ToArray();
        session.Window.SetRoot(Flex.Column().Children(boxes));
        session.PumpFrames(3);
        int trees = session.Window.AccessibilityTreesBuilt;

        foreach (var box in boxes.Take(32)) box.IsChecked = true;
        session.PumpFrame();
        Assert.Equal((trees, 1), (session.Window.AccessibilityTreesBuilt, bridge.Patches));

        // 33 changes: one more than a frame patches.
        foreach (var box in boxes.Take(33)) box.IsChecked = !box.IsChecked;
        session.PumpFrame();
        Assert.Equal((trees + 1, 1), (session.Window.AccessibilityTreesBuilt, bridge.Patches));
        Assert.All(boxes.Take(33), b => Assert.Equal(b.IsChecked, Find(bridge.Tree!, b)!.IsToggled));
    }

    [Fact]
    public void AProgressBarTickingEveryFrame_BuildsNoTree_AndIsToldOnItsSchedule()
    {
        using var session = HeadlessSession.Create(800, 900);
        var recording = new HeadlessAccessibilityBridge();
        var bridge = new Counting(recording);
        session.Window.InstallAccessibilityBridge(bridge);
        var bar = ProgressBar.New().Width(Dimension.Px(300));
        session.Window.SetRoot(Flex.Column().Children(TextB.New("Downloading"), bar, Rows(200)));
        session.PumpFrames(2);
        session.PumpFrame(TimeSpan.FromMilliseconds(300));
        int trees = session.Window.AccessibilityTreesBuilt;
        recording.ClearDiffs();

        // Two seconds at 60 frames a second.
        for (int i = 1; i <= 120; i++)
        {
            bar.Value = i / 120.0;
            session.PumpFrame(TimeSpan.FromMilliseconds(1000.0 / 60));
        }
        session.PumpFrame(TimeSpan.FromMilliseconds(300));

        Assert.Equal(trees, session.Window.AccessibilityTreesBuilt);
        Assert.InRange(recording.Diffs.Count, 8, 10);
        Assert.Equal(1.0, Find(bridge.Tree!, bar)!.Range!.Value.Value);
    }

    private static AccessibilityNode? Find(AccessibilityNode node, Element element)
    {
        if (node.ElementId == element.Id) return node;
        foreach (var child in node.Children)
            if (Find(child, element) is { } found) return found;
        return null;
    }

    /// <summary>A host with nothing behind it: the bridge's events, not actions a client asks for.</summary>
    private sealed class NoHost : IUiaHostBridge
    {
        public void InvokeElement(ElementId id) { }
        public void ToggleElement(ElementId id) { }
        public void SelectElement(ElementId id) { }
        public void SetValue(ElementId id, string value) { }
        public void RequestFocus(ElementId id) { }
        public float ContentScale => 1f;
        public string? GetValue(ElementId id) => null;
        public bool? GetToggleState(ElementId id) => null;
        public int GetItemCount(ElementId listId) => 0;
        public string? GetItemLabel(ElementId listId, int index) => null;
        public bool GetItemSelected(ElementId listId, int index) => false;
        public int GetSelectedIndex(ElementId listId) => -1;
        public bool GetCanSelectMultiple(ElementId listId) => false;
        public IReadOnlyList<int> GetSelectedIndices(ElementId listId) => [];
        public void SelectItemOnly(ElementId listId, int index) { }
        public void SetItemSelected(ElementId listId, int index, bool selected) { }
        public int FindItemIndex(ElementId listId, int startAfter, AccessibleItemQuery query) => -1;
        public bool GetItemOffscreen(ElementId listId, int index) => false;
        public bool TryGetItemLayout(ElementId listId, int index, out Rect layoutRect) { layoutRect = default; return false; }
        public bool TryGetScrollInfo(ElementId id, out ScrollInfo info) { info = default; return false; }
        public void SetScrollPercent(ElementId id, double horizontalPercent, double verticalPercent) { }
        public void ScrollByAmount(ElementId id, int horizontalAmount, int verticalAmount) { }
        public bool TryGetRangeValue(ElementId id, out RangeValueInfo info) { info = default; return false; }
        public void SetRangeValue(ElementId id, double value) { }
    }
}
