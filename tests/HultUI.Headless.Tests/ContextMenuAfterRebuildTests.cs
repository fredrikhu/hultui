using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// The right-click menu of a text field and of a selectable label, after the component they are
/// in has rebuilt once (plan 17 round 18). The rebuild patched each with the menu its build's fresh
/// copy had made for itself, so the menu read and acted on that copy, out of the window: the field's
/// Cut, Copy and Paste were off with its text selected, the label's Copy too, and Select All
/// selected the copy. Meridian's URL field, Junction's commit message and diff text, Vesper's rename
/// and Aperture's search all rebuild while they are on screen.
/// </summary>
public class ContextMenuAfterRebuildTests
{
    private sealed class Form : Component
    {
        public int Count;

        public override Element Build() => Flex.Column().Children(
            TextInput.New("hello world").AutomationId("field").Width(Dimension.Px(200)),
            TextB.New("selectable words").Selectable().AutomationId("label"),
            TextArea.New("area text").AutomationId("area").Width(200f).Height(60f),
            TextB.New($"rebuilt {Count}"));

        public void Rebuild()
        {
            Count++;
            MarkDirty();
        }
    }

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode? node)
    {
        if (node is null) yield break;
        yield return node;
        foreach (var child in node.Children)
            foreach (var d in Walk(child))
                yield return d;
    }

    private static (HeadlessSession Session, Form Form) Rebuilt()
    {
        var session = HeadlessSession.Create(400, 300);
        var form = new Form();
        session.Application.SetRoot(form.Mount());
        session.PumpFrames(2);
        var field = session.Window.FindByAutomationId("field")!.Element;
        var label = session.Window.FindByAutomationId("label")!.Element;
        var area = session.Window.FindByAutomationId("area")!.Element;

        form.Rebuild();
        session.PumpFrames(2);

        // Patched, not replaced: the menu is asked of the elements that were there before.
        Assert.Same(field, session.Window.FindByAutomationId("field")!.Element);
        Assert.Same(label, session.Window.FindByAutomationId("label")!.Element);
        Assert.Same(area, session.Window.FindByAutomationId("area")!.Element);
        Assert.Equal("rebuilt 1", session.Window.FindByText("rebuilt 1")?.Name);
        return (session, form);
    }

    /// <summary>Right-clicks the element, then clicks the menu's item; false when the item is off.</summary>
    private static bool Choose(HeadlessSession session, string automationId, string item)
    {
        session.Window.FindByAutomationId(automationId)!.RightClick();
        session.PumpFrames(2);
        var tree = Walk(session.Window.AccessibilityTree).ToList();
        // Named with its shortcut: "Copy, Ctrl+C".
        var node = tree.FirstOrDefault(n => n.Role == AccessibilityRole.MenuItem && n.Name?.Split(", ")[0] == item);
        Assert.True(node is not null, $"no menu item {item}: {string.Join(", ", tree.Select(n => $"{n.Role} {n.Name}"))}");
        bool enabled = node.IsEnabled;
        var r = node.BoundingRect;
        session.Window.Click(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        session.PumpFrames(2);
        if (session.Window.OverlayContents.Count > 0)
        {
            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(2);
        }
        return enabled;
    }

    /// <summary>Opens the focused element's menu from the keyboard (ContextMenuKeyboardTests), types the
    /// start of an item's label and presses Enter; false when type-ahead found no enabled item by that name.</summary>
    private static bool ChooseFromTheKeyboard(HeadlessSession session, KeyCode key, KeyModifiers modifiers,
        string typed, string item)
    {
        session.Window.PressKey(key, modifiers);
        session.PumpFrames(1);
        var menu = session.Window.OverlayContents.OfType<ContextMenuElement>().FirstOrDefault();
        Assert.True(menu is not null, $"{key} opened no menu");
        foreach (char c in typed)
            session.Window.PressKey((KeyCode)((int)KeyCode.A + (c - 'a')), layoutKey: c, text: c.ToString());
        bool found = menu.ItemAt(menu.HoveredIndex) is { IsDisabled: false } hovered && hovered.Label == item;
        session.Window.PressKey(KeyCode.Enter);
        session.PumpFrames(2);
        if (session.Window.OverlayContents.Count > 0)
        {
            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(2);
        }
        return found;
    }

    /// <summary>The same menus opened from the keyboard (Shift+F10 and the Menu key) and chosen by
    /// type-ahead, after the rebuild: they act on the live field, text area and label as well.</summary>
    [Fact]
    public void AfterARebuild_TheMenusOpenedFromTheKeyboard_ActOnTheLiveElements()
    {
        var (session, _) = Rebuilt();
        using var _s = session;
        var field = (TextInputElement)session.Window.FindByAutomationId("field")!.Element;
        field.Focus();
        session.PumpFrames(1);
        session.Window.PressKey(KeyCode.A, KeyModifiers.Ctrl);
        session.PumpFrames(1);

        Assert.True(ChooseFromTheKeyboard(session, KeyCode.F10, KeyModifiers.Shift, "cop", "Copy"));
        Assert.Equal("hello world", session.Window.Clipboard.Text);
        Assert.True(ChooseFromTheKeyboard(session, KeyCode.Menu, KeyModifiers.None, "cu", "Cut"));
        Assert.Equal("", field.Text);
        Assert.True(ChooseFromTheKeyboard(session, KeyCode.Menu, KeyModifiers.None, "p", "Paste"));
        Assert.Equal("hello world", field.Text);

        // Tab on to the label and the text area, as a keyboard user goes.
        var label = (TextElement)session.Window.FindByAutomationId("label")!.Element;
        session.Window.PressKey(KeyCode.Tab);
        session.PumpFrames(1);
        Assert.Same(label, session.Window.Window.FocusedElement);
        Assert.True(ChooseFromTheKeyboard(session, KeyCode.F10, KeyModifiers.Shift, "se", "Select All"));
        Assert.Equal("selectable words", label.SelectedText);
        Assert.True(ChooseFromTheKeyboard(session, KeyCode.Menu, KeyModifiers.None, "cop", "Copy"));
        Assert.Equal("selectable words", session.Window.Clipboard.Text);

        var area = (TextAreaElement)session.Window.FindByAutomationId("area")!.Element;
        session.Window.PressKey(KeyCode.Tab);
        session.PumpFrames(1);
        Assert.Same(area, session.Window.Window.FocusedElement);
        Assert.True(ChooseFromTheKeyboard(session, KeyCode.F10, KeyModifiers.Shift, "se", "Select All"));
        Assert.True(ChooseFromTheKeyboard(session, KeyCode.Menu, KeyModifiers.None, "cop", "Copy"));
        Assert.Equal("area text", session.Window.Clipboard.Text);
    }

    [Fact]
    public void AfterARebuild_AFieldsMenu_CopiesAndCutsItsSelection()
    {
        var (session, _) = Rebuilt();
        using var _s = session;
        var field = (TextInputElement)session.Window.FindByAutomationId("field")!.Element;
        session.Window.FindByAutomationId("field")!.Click();
        session.PumpFrames(1);
        session.Window.PressKey(KeyCode.A, KeyModifiers.Ctrl);
        session.PumpFrames(1);

        Assert.True(Choose(session, "field", "Copy"));
        Assert.Equal("hello world", session.Window.Clipboard.Text);

        Assert.True(Choose(session, "field", "Cut"));
        Assert.Equal("", field.Text);

        Assert.True(Choose(session, "field", "Paste"));
        Assert.Equal("hello world", field.Text);
    }

    [Fact]
    public void AfterARebuild_ALabelsMenu_SelectsAllOfIt_AndCopiesIt()
    {
        var (session, _) = Rebuilt();
        using var _s = session;
        var label = (TextElement)session.Window.FindByAutomationId("label")!.Element;
        session.Window.FindByAutomationId("label")!.Click();
        session.PumpFrames(1);

        Assert.True(Choose(session, "label", "Select All"));
        Assert.Equal("selectable words", label.SelectedText);

        Assert.True(Choose(session, "label", "Copy"));
        Assert.Equal("selectable words", session.Window.Clipboard.Text);
    }
}
