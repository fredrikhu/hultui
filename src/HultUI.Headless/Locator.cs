using System.Globalization;
using System.Text;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;

namespace HultUI.Headless;

// ──── Locators ────
// Playwright-style locators over the accessibility tree, on the session's virtual clock.
// Test infrastructure only: it reads the same snapshot the Query API reads and acts through
// the same input driver, and nothing in the frame loop knows it exists.

public sealed partial class HeadlessSession
{
    /// <summary>How long a locator's actions and <see cref="HeadlessExpect"/>'s assertions wait,
    /// in <b>virtual</b> time, before they fail. Default 5 s.</summary>
    public TimeSpan LocatorTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <inheritdoc cref="HeadlessWindow.GetByRole"/>
    public Locator GetByRole(AccessibilityRole role, string? name = null, bool exact = false) => Window.GetByRole(role, name, exact);

    /// <inheritdoc cref="HeadlessWindow.GetByText(string, bool)"/>
    public Locator GetByText(string text, bool exact = false) => Window.GetByText(text, exact);

    /// <inheritdoc cref="HeadlessWindow.GetByLabel"/>
    public Locator GetByLabel(string label, bool exact = false) => Window.GetByLabel(label, exact);

    /// <inheritdoc cref="HeadlessWindow.GetByTestId"/>
    public Locator GetByTestId(string automationId) => Window.GetByTestId(automationId);
}

public sealed partial class HeadlessWindow
{
    internal HeadlessSession Session => _session;

    /// <summary>A lazy locator for nodes of <paramref name="role"/>, optionally named
    /// <paramref name="name"/> (a case-insensitive substring unless <paramref name="exact"/>).</summary>
    public Locator GetByRole(AccessibilityRole role, string? name = null, bool exact = false)
        => Locator.Root(this).GetByRole(role, name, exact);

    /// <summary>A lazy locator for nodes whose name or value is <paramref name="text"/>
    /// (a case-insensitive substring unless <paramref name="exact"/>).</summary>
    public Locator GetByText(string text, bool exact) => Locator.Root(this).GetByText(text, exact);

    /// <summary>A lazy locator for the control labelled <paramref name="label"/>.</summary>
    public Locator GetByLabel(string label, bool exact = false) => Locator.Root(this).GetByLabel(label, exact);

    /// <summary>A lazy locator for the node whose AutomationId is <paramref name="automationId"/>.</summary>
    public Locator GetByTestId(string automationId) => Locator.Root(this).GetByTestId(automationId);
}

/// <summary>
/// A lazy query of one window's accessibility tree. Nothing is resolved until it is used, and
/// every use resolves it again against the current tree. Actions are strict: one matching
/// several elements fails, naming them. Actions wait (pumping the session's virtual time, up to
/// <see cref="HeadlessSession.LocatorTimeout"/>) until the element is attached, visible, enabled
/// and has not moved between two frames, then act through the window's input driver.
/// </summary>
public sealed class Locator
{
    private readonly Func<AccessibilityNode, IEnumerable<AccessibilityNode>>? _query;   // null: the window itself
    private readonly string _description;

    private Locator(HeadlessWindow window, Func<AccessibilityNode, IEnumerable<AccessibilityNode>>? query, string description)
    {
        Window = window;
        _query = query;
        _description = description;
    }

    internal static Locator Root(HeadlessWindow window) => new(window, null, "");

    /// <summary>The window this locator searches.</summary>
    public HeadlessWindow Window { get; }

    /// <summary>The session that owns <see cref="Window"/>; its clock is the one waits pump.</summary>
    public HeadlessSession Session => Window.Session;

    public override string ToString() => _description;

    // ──── Building ────

    private Locator Within(Func<AccessibilityNode, bool> match, string step)
        => new(Window, root => (_query is null ? Subtree(root) : _query(root).SelectMany(Descendants).Distinct()).Where(match), Join(step));

    private Locator Then(Func<IEnumerable<AccessibilityNode>, IEnumerable<AccessibilityNode>> narrow, string step)
        => new(Window, root => narrow(_query is null ? [root] : _query(root)), Join(step));

    private string Join(string step) => _description.Length == 0 ? step : $"{_description} >> {step}";

    /// <summary>Nodes of <paramref name="role"/> within this locator's matches, optionally named.</summary>
    public Locator GetByRole(AccessibilityRole role, string? name = null, bool exact = false)
        => Within(n => n.Role == role && (name is null || Matches(n.Name, name, exact)),
            name is null ? $"role={role}" : $"role={role}[name=\"{name}\"{(exact ? " exact" : "")}]");

    /// <summary>Nodes whose name or value matches <paramref name="text"/>.</summary>
    public Locator GetByText(string text, bool exact = false)
        => Within(n => Matches(n.Name, text, exact) || Matches(n.Value, text, exact),
            $"text=\"{text}\"{(exact ? " exact" : "")}");

    /// <summary>
    /// The control labelled <paramref name="label"/>: a control whose own accessible name matches
    /// (what <c>AccessibleName</c> gives it), or the first control after a label node that matches,
    /// among that label's siblings and their descendants — the usual "label, then field" row.
    /// </summary>
    public Locator GetByLabel(string label, bool exact = false)
        => Within(n => IsControl(n) && (Matches(n.Name, label, exact) || LabelledBySibling(n, label, exact)),
            $"label=\"{label}\"{(exact ? " exact" : "")}");

    /// <summary>The node whose AutomationId is <paramref name="automationId"/>.</summary>
    public Locator GetByTestId(string automationId)
        => Within(n => n.AutomationId == automationId, $"testid={automationId}");

    /// <summary>The <paramref name="index"/>th match (zero-based), in tree order.</summary>
    public Locator Nth(int index)
        => Then(nodes => nodes.Skip(index).Take(1), $"nth={index}");

    /// <summary>The first match, in tree order.</summary>
    public Locator First => Nth(0);

    /// <summary>The matches whose subtree (the node or a descendant) has a name or value
    /// containing <paramref name="hasText"/>, case-insensitively.</summary>
    public Locator Filter(string hasText)
        => Then(nodes => nodes.Where(n => Subtree(n).Any(d => Matches(d.Name, hasText, false) || Matches(d.Value, hasText, false))),
            $"has-text=\"{hasText}\"");

    // ──── Resolving ────

    /// <summary>The matching elements now, in tree order, without waiting.</summary>
    public IReadOnlyList<HeadlessElement> All()
    {
        var root = Window.Window.BuildAccessibilityTree();
        if (root is null) return [];
        var found = new List<HeadlessElement>();
        foreach (var node in _query(root))
            if (Window.Window.FindElement(node.ElementId) is { } element)
                found.Add(new HeadlessElement(Window, node, element));
        return found;
    }

    /// <summary>How many elements match now.</summary>
    public int Count => All().Count;

    /// <summary>The one matching element now, without waiting; throws when none or several match.</summary>
    public HeadlessElement Resolve()
    {
        var all = All();
        if (all.Count == 1) return all[0];
        if (all.Count == 0) throw new InvalidOperationException($"Locator {this} matched no element.");
        throw StrictViolation(all);
    }

    internal InvalidOperationException StrictViolation(IReadOnlyList<HeadlessElement> all)
        => new($"Strict mode violation: locator {this} matched {all.Count} elements:\n{Dump(all)}" +
               "Narrow it (a name, .Filter, a parent locator) or pick one with .Nth/.First.");

    // ──── Waiting ────

    internal TimeSpan TimeoutOr(TimeSpan? timeout) => timeout ?? Session.LocatorTimeout;

    /// <summary>Pumps frames on the session's virtual clock until <paramref name="done"/> holds,
    /// asking it before each frame; false when <paramref name="timeout"/> passed first.</summary>
    internal bool PumpUntil(Func<bool> done, TimeSpan timeout)
    {
        var time = Session.Time;
        long end = time.GetTimestamp() + timeout.Ticks;
        while (true)
        {
            if (done()) return true;
            if (time.GetTimestamp() >= end) return false;
            Session.PumpFrame();
        }
    }

    /// <summary>Waits until exactly one element matches and it is visible, enabled and where it
    /// was a frame ago, then returns it.</summary>
    private HeadlessElement Actionable(string action, TimeSpan? timeout)
    {
        HeadlessElement? target = null;
        Rect? previous = null;
        string state = "no element matched";
        var limit = TimeoutOr(timeout);
        bool ok = PumpUntil(() =>
        {
            var all = All();
            if (all.Count > 1) throw StrictViolation(all);
            if (all.Count == 0) { state = "no element matched"; previous = null; return false; }
            var e = all[0];
            if (!IsVisible(e)) { state = $"element is not visible: {Describe(e)}"; previous = null; return false; }
            if (!e.IsEnabled) { state = $"element is disabled: {Describe(e)}"; previous = null; return false; }
            bool stable = previous == e.Bounds;
            previous = e.Bounds;
            if (!stable) { state = $"element is moving: {Describe(e)}"; return false; }
            target = e;
            return true;
        }, limit);
        if (!ok)
            throw new TimeoutException(
                $"{action} on locator {this} timed out after {limit.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s of virtual time: {state}.");
        return target!;
    }

    // ──── Actions ────

    /// <summary>Clicks the element's centre once it is actionable.</summary>
    public void Click(TimeSpan? timeout = null) => Actionable("Click", timeout).Click();

    /// <summary>Focuses the field by clicking it, selects all its text and types <paramref name="value"/> over it.</summary>
    public void Fill(string value, TimeSpan? timeout = null)
    {
        var e = Actionable("Fill", timeout);
        e.Click();
        Window.PressKey(KeyCode.A, KeyModifiers.Ctrl);
        Window.PressKey(KeyCode.Backspace);
        Window.TypeText(value);
    }

    /// <summary>Focuses the element and presses <paramref name="key"/>.</summary>
    public void Press(KeyCode key, KeyModifiers modifiers = KeyModifiers.None, TimeSpan? timeout = null)
    {
        Actionable("Press", timeout).Focus();
        Window.PressKey(key, modifiers);
    }

    /// <summary>Moves the mouse to the element's centre.</summary>
    public void Hover(TimeSpan? timeout = null)
    {
        var box = Actionable("Hover", timeout).Element.WindowBorderBox;
        Window.MoveMouse(box.X + box.Width / 2f, box.Y + box.Height / 2f);
    }

    /// <summary>Clicks the checkbox (switch, radio button) unless it is already checked, then
    /// waits until it reports checked.</summary>
    public void Check(TimeSpan? timeout = null) => SetChecked(true, timeout);

    /// <summary>Clicks the checkbox unless it is already unchecked, then waits until it reports unchecked.</summary>
    public void Uncheck(TimeSpan? timeout = null) => SetChecked(false, timeout);

    private void SetChecked(bool on, TimeSpan? timeout)
    {
        var e = Actionable(on ? "Check" : "Uncheck", timeout);
        if (IsChecked(e) == on) return;
        e.Click();
        if (!PumpUntil(() => All() is [var now] && IsChecked(now) == on, TimeoutOr(timeout)))
            throw new TimeoutException($"Clicking locator {this} did not make it {(on ? "checked" : "unchecked")}: {DumpNow()}");
    }

    /// <summary>Picks <paramref name="option"/> (an item, exactly) in a dropdown: opens it with a
    /// click and selects the row with the keyboard, as a person would.</summary>
    public void SelectOption(string option, TimeSpan? timeout = null)
    {
        var e = Actionable("SelectOption", timeout);
        if (e.Element is not DropdownElement dropdown)
            throw new InvalidOperationException($"SelectOption: locator {this} is not a dropdown: {Describe(e)}");
        int index = dropdown.Items.ToList().IndexOf(option);
        if (index < 0)
            throw new InvalidOperationException(
                $"SelectOption: dropdown {this} has no option \"{option}\"; it has: {string.Join(", ", dropdown.Items.Select(i => $"\"{i}\""))}.");
        if (dropdown.SelectedIndex == index) return;
        if (!dropdown.IsOpen) e.Click();
        Window.PressKey(KeyCode.Home);
        for (int i = 0; i < index; i++) Window.PressKey(KeyCode.Down);
        Window.PressKey(KeyCode.Enter);
        if (dropdown.SelectedIndex != index)
            throw new InvalidOperationException(
                $"SelectOption: dropdown {this} shows \"{dropdown.SelectedItem}\" after picking \"{option}\".");
    }

    public Task ClickAsync(TimeSpan? timeout = null) => Run(() => Click(timeout));
    public Task FillAsync(string value, TimeSpan? timeout = null) => Run(() => Fill(value, timeout));
    public Task PressAsync(KeyCode key, KeyModifiers modifiers = KeyModifiers.None, TimeSpan? timeout = null) => Run(() => Press(key, modifiers, timeout));
    public Task HoverAsync(TimeSpan? timeout = null) => Run(() => Hover(timeout));
    public Task CheckAsync(TimeSpan? timeout = null) => Run(() => Check(timeout));
    public Task UncheckAsync(TimeSpan? timeout = null) => Run(() => Uncheck(timeout));
    public Task SelectOptionAsync(string option, TimeSpan? timeout = null) => Run(() => SelectOption(option, timeout));

    // The pump is synchronous and belongs on the test thread: the async forms run in place and
    // hand back a finished (or faulted) task.
    internal static Task Run(Action action)
    {
        try { action(); return Task.CompletedTask; }
        catch (Exception ex) { return Task.FromException(ex); }
    }

    // ──── Helpers ────

    internal static bool IsVisible(HeadlessElement e)
        => !e.Node.IsOffscreen && e.Bounds.Width > 0 && e.Bounds.Height > 0;

    internal static bool IsChecked(HeadlessElement e) => (e.IsToggled ?? e.Node.IsSelected) == true;

    internal static string Describe(HeadlessElement e)
    {
        var sb = new StringBuilder().Append(e.Role);
        if (e.Name is not null) sb.Append(" \"").Append(e.Name).Append('"');
        if (e.Value is not null) sb.Append(" value=\"").Append(e.Value).Append('"');
        if (e.AutomationId is not null) sb.Append(" id=").Append(e.AutomationId);
        var b = e.Bounds;
        sb.Append(CultureInfo.InvariantCulture, $" bounds=({b.X:0.#},{b.Y:0.#} {b.Width:0.#}x{b.Height:0.#})");
        if (!e.IsEnabled) sb.Append(" [disabled]");
        if (e.Node.IsOffscreen) sb.Append(" [offscreen]");
        if (e.IsToggled is { } t) sb.Append(t ? " [checked]" : " [unchecked]");
        return sb.ToString();
    }

    internal static string Dump(IReadOnlyList<HeadlessElement> all)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < all.Count; i++)
            sb.Append("  ").Append(i).Append(": ").Append(Describe(all[i])).Append('\n');
        return sb.ToString();
    }

    internal string DumpNow() => All() is { Count: > 0 } all ? "\n" + Dump(all) : "no element matches.";

    private static bool Matches(string? actual, string wanted, bool exact)
        => actual is not null && (exact
            ? actual == wanted
            : Normalize(actual).Contains(Normalize(wanted), StringComparison.OrdinalIgnoreCase));

    internal static string Normalize(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool IsControl(AccessibilityNode n) => n.Role is AccessibilityRole.TextInput or AccessibilityRole.Checkbox
        or AccessibilityRole.RadioButton or AccessibilityRole.Switch or AccessibilityRole.ComboBox or AccessibilityRole.Slider
        or AccessibilityRole.SpinButton or AccessibilityRole.Button;

    private static bool LabelledBySibling(AccessibilityNode control, string label, bool exact)
    {
        // Climb until an ancestor's child list holds a matching label before the branch the
        // control is in; the control must be the first control after that label.
        for (var branch = control; branch.Parent is { } parent; branch = parent)
        {
            var siblings = parent.Children;
            int at = IndexOf(siblings, branch);
            for (int i = at - 1; i >= 0; i--)
            {
                var s = siblings[i];
                if (s.Role == AccessibilityRole.Label && Matches(s.Name, label, exact))
                    return FirstControlAfter(siblings, i) == control;
                if (Subtree(s).Any(IsControl)) return false;   // another control sits between
            }
        }
        return false;
    }

    private static AccessibilityNode? FirstControlAfter(IReadOnlyList<AccessibilityNode> siblings, int index)
    {
        for (int i = index + 1; i < siblings.Count; i++)
            if (Subtree(siblings[i]).FirstOrDefault(IsControl) is { } c) return c;
        return null;
    }

    private static int IndexOf(IReadOnlyList<AccessibilityNode> list, AccessibilityNode node)
    {
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], node)) return i;
        return -1;
    }

    private static IEnumerable<AccessibilityNode> Subtree(AccessibilityNode node)
    {
        yield return node;
        foreach (var d in Descendants(node)) yield return d;
    }

    private static IEnumerable<AccessibilityNode> Descendants(AccessibilityNode node)
    {
        foreach (var child in node.Children)
            foreach (var d in Subtree(child))
                yield return d;
    }
}
