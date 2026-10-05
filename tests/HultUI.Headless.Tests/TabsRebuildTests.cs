using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="TabsElement"/> keeps its elements. It used to build its strip and panel anew on
/// every switch and every rebuild of the app around it: a switch replaced every header, so a
/// screen reader saw the tabs go and new ones come rather than the selection move, and a rebuild
/// threw the open panel away and built it again, taking the keyboard out of a text box in it on
/// every keystroke that changed the app's state.
/// </summary>
public class TabsRebuildTests
{
    private static Element Strip(TabsElement tabs) => tabs.Children.Single(c => c.Role == AccessibilityRole.TabList);
    private static Element PanelHost(TabsElement tabs) => tabs.Children.Single(c => c.Role == AccessibilityRole.TabPanel);
    private static Element Panel(TabsElement tabs) => Assert.Single(PanelHost(tabs).Children);
    private static Element? Focused(HeadlessSession session) => session.Window.Window.FocusedElement;

    [Fact]
    public void ASwitchKeepsTheStripAndItsTabs_AndTellsAReaderTheSelectionMoved()
    {
        using var session = HeadlessSession.Create(480, 240);
        var tabs = Tabs.New()
            .Tab("General", () => TextElement.New("general page"))
            .Tab("Inbox", () => TextElement.New("inbox page"))
            .Tab("Logs", () => TextElement.New("logs page"));
        session.Application.SetRoot(Box.New().Children(tabs));
        session.PumpFrames(2);

        var strip = Strip(tabs);
        var headers = strip.Children.ToArray();
        var host = PanelHost(tabs);
        var before = AccessibilityTreeBuilder.Build(tabs);

        session.Window.Click(headers[1]);
        session.PumpFrames(2);

        Assert.Same(strip, Strip(tabs));
        Assert.Equal(headers, Strip(tabs).Children);
        Assert.Same(host, PanelHost(tabs));

        var diff = AccessibilityTreeDiffer.Diff(before, AccessibilityTreeBuilder.Build(tabs));
        var headerIds = headers.Select(h => h.Id).ToHashSet();
        Assert.DoesNotContain(diff.Added, a => headerIds.Contains(a.Node.ElementId));
        Assert.DoesNotContain(diff.Removed, r => headerIds.Contains(r.Node.ElementId));
        var selection = diff.PropertyChanged.Where(c => c.PropertyName == AccessibilityProperties.IsSelected).ToList();
        Assert.Equal(2, selection.Count);
        Assert.Contains(selection, c => c.Node.ElementId == headers[0].Id && c.Node.IsSelected == false);
        Assert.Contains(selection, c => c.Node.ElementId == headers[1].Id && c.Node.IsSelected == true);
    }

    [Fact]
    public void ARebuildKeepsTheOpenPanel_AndTheFocusInIt()
    {
        using var session = HeadlessSession.Create(480, 240);
        var mail = new Mailbox();
        var root = mail.Mount();
        session.Application.SetRoot(root);
        session.PumpFrames(2);

        var tabs = Find<TabsElement>(root);
        var panel = Panel(tabs);
        var field = (TextInputElement)panel.Children[0];
        var count = (TextElement)panel.Children[1];
        session.Window.Click(field);
        Assert.Same(field, Focused(session));

        mail.Unread = 4;
        mail.Rebuild();
        session.PumpFrames(2);

        Assert.Same(tabs, Find<TabsElement>(root));
        Assert.Same(panel, Panel(tabs));
        Assert.Same(field, Focused(session));
        // Patched from the new build, not frozen at the first.
        Assert.Same(count, panel.Children[1]);
        Assert.Equal("4 unread", count.Content);
        Assert.Equal("Inbox 4", AccessibilityTreeBuilder.Build(Strip(tabs)).Children[1].Name);
    }

    [Fact]
    public void ARebuildKeepsTheFocusOnATab()
    {
        using var session = HeadlessSession.Create(480, 240);
        var mail = new Mailbox();
        var root = mail.Mount();
        session.Application.SetRoot(root);
        session.PumpFrames(2);

        var tabs = Find<TabsElement>(root);
        var inbox = Strip(tabs).Children[1];
        session.Window.Click(inbox);
        session.PumpFrames(2);

        mail.Rebuild();
        session.PumpFrames(2);

        Assert.Same(inbox, Strip(tabs).Children[1]);
        Assert.Same(inbox, Focused(session));
        Assert.Equal(1, tabs.SelectedIndex);
    }

    /// <summary>Closing the tab that has the keyboard hands it to the tab now open, rather than
    /// dropping it on the floor.</summary>
    [Fact]
    public void ClosingTheFocusedTab_PutsTheKeyboardOnTheOneNowOpen()
    {
        using var session = HeadlessSession.Create(480, 240);
        var mail = new Mailbox();
        var root = mail.Mount();
        session.Application.SetRoot(root);
        session.PumpFrames(2);

        var tabs = Find<TabsElement>(root);
        var inbox = Strip(tabs).Children[1];
        session.Window.Click(Strip(tabs).Children[2]);
        session.PumpFrames(2);
        Assert.Equal(2, tabs.SelectedIndex);

        session.Window.PressKey(KeyCode.Delete);   // Logs' onClose drops it and rebuilds
        session.PumpFrames(2);

        Assert.Equal(2, Strip(tabs).Children.Count);
        Assert.Equal(1, tabs.SelectedIndex);
        Assert.Same(inbox, Focused(session));
        Assert.Equal("inbox page", ((TextElement)Panel(tabs)).Content);
    }

    /// <summary>
    /// A panel belongs to its tab. When a rebuild puts another tab where the open one was — the
    /// open tab closed — the panel is that tab's own, never the closed one's patched over: two
    /// text boxes of the same kind would otherwise share one element, undo history and all.
    /// </summary>
    [Fact]
    public void ATabThatTakesTheOpenOnesPlaceGetsItsOwnPanel()
    {
        using var session = HeadlessSession.Create(480, 240);
        var docs = new Documents();
        var root = docs.Mount();
        session.Application.SetRoot(root);
        session.PumpFrames(2);

        var tabs = Find<TabsElement>(root);
        session.Window.Click(Strip(tabs).Children[1]);
        session.PumpFrames(2);
        var draftB = Panel(tabs);
        Assert.Equal("b", ((TextInputElement)draftB).Text);

        docs.Names.Remove("B");
        docs.Rebuild();
        session.PumpFrames(2);

        Assert.Equal(1, tabs.SelectedIndex);
        Assert.NotSame(draftB, Panel(tabs));
        Assert.Equal("c", ((TextInputElement)Panel(tabs)).Text);
    }

    /// <summary>Closing a tab before the open one leaves the same tab open, with its panel and
    /// its header; it used to open whichever tab had slid into the open one's place.</summary>
    [Fact]
    public void ClosingATabBeforeTheOpenOne_KeepsItOpen()
    {
        using var session = HeadlessSession.Create(480, 240);
        var docs = new Documents { Names = ["A", "B", "C", "D"] };
        var root = docs.Mount();
        session.Application.SetRoot(root);
        session.PumpFrames(2);

        var tabs = Find<TabsElement>(root);
        var headers = Strip(tabs).Children.ToArray();
        session.Window.Click(headers[2]);
        session.PumpFrames(2);
        var draftC = Panel(tabs);

        docs.Names.Remove("A");
        docs.Rebuild();
        session.PumpFrames(2);

        Assert.Equal(1, tabs.SelectedIndex);
        Assert.Same(draftC, Panel(tabs));
        Assert.Equal("c", ((TextInputElement)draftC).Text);
        // A's header is the one that went; the others moved up with their tabs.
        Assert.Equal(headers[1..], Strip(tabs).Children);
        Assert.Same(headers[2], Focused(session));
    }

    /// <summary>A tab renamed — a "•" for unsaved changes — is the same tab, and its panel is
    /// kept and renamed with it.</summary>
    [Fact]
    public void RenamingTheOpenTab_KeepsItsPanel()
    {
        using var session = HeadlessSession.Create(480, 240);
        var docs = new Documents { Names = ["A", "B"] };
        var root = docs.Mount();
        session.Application.SetRoot(root);
        session.PumpFrames(2);

        var tabs = Find<TabsElement>(root);
        session.Window.Click(Strip(tabs).Children[1]);
        session.PumpFrames(2);
        var draftB = Panel(tabs);

        docs.Names[1] = "B •";
        docs.Rebuild();
        session.PumpFrames(2);

        Assert.Equal(1, tabs.SelectedIndex);
        Assert.Same(draftB, Panel(tabs));
        Assert.Equal("B •", AccessibilityTreeBuilder.Build(PanelHost(tabs)).Name);
    }

    /// <summary>A tab opened under the pointer keeps the open look when the pointer leaves. A
    /// hover puts back the look it found when it began, and that was the closed tab's.</summary>
    [Fact]
    public void ATabOpenedUnderThePointerKeepsItsOpenLook()
    {
        using var session = HeadlessSession.Create(480, 240);
        var tabs = Tabs.New().Style(TabStyle.Pill)
            .Tab("General", () => TextElement.New("general page"))
            .Tab("Inbox", () => TextElement.New("inbox page"));
        session.Application.SetRoot(Box.New().Children(tabs));
        session.PumpFrames(2);
        var theme = session.Application.Theme;
        var inbox = (BoxElement)Strip(tabs).Children[1];
        var box = inbox.WindowBorderBox;

        session.Window.MoveMouse(box.X + box.Width / 2, box.Y + box.Height / 2);
        session.PumpFrame(TimeSpan.FromMilliseconds(300));
        Assert.Equal(theme.SurfaceVariant, inbox.BackgroundColor);

        session.Window.Click(inbox);
        session.PumpFrame(TimeSpan.FromMilliseconds(300));
        session.Window.MoveMouse(470, 230);
        session.PumpFrame(TimeSpan.FromMilliseconds(300));

        Assert.Equal(1, tabs.SelectedIndex);
        Assert.Equal(theme.Surface, inbox.BackgroundColor);
    }

    private sealed class Mailbox : Component
    {
        public int Unread = 3;
        public bool WithLogs = true;

        public override Element Build()
        {
            var tabs = Tabs.New()
                .Tab("General", () => Box.New().Children(TextInput.New(""), TextElement.New($"{Unread} unread")))
                .Tab("Inbox", () => TextElement.New("inbox page"), badge: Unread.ToString());
            if (WithLogs)
                tabs.Tab("Logs", () => TextElement.New("logs page"), onClose: () => { WithLogs = false; MarkDirty(); });
            return Box.New().Children(tabs);
        }

        public void Rebuild() => MarkDirty();
    }

    private sealed class Documents : Component
    {
        public List<string> Names = ["A", "B", "C"];

        public override Element Build()
        {
            var tabs = Tabs.New();
            foreach (var name in Names)
                tabs.Tab(name, () => TextInput.New(name.ToLowerInvariant()));
            return Box.New().Children(tabs);
        }

        public void Rebuild() => MarkDirty();
    }

    private static T Find<T>(Element root) where T : Element
    {
        if (root is T hit) return hit;
        foreach (var child in root.Children)
            if (TryFind<T>(child) is { } found) return found;
        throw new InvalidOperationException($"No {typeof(T).Name} under {root}.");
    }

    private static T? TryFind<T>(Element root) where T : Element
    {
        if (root is T hit) return hit;
        foreach (var child in root.Children)
            if (TryFind<T>(child) is { } found) return found;
        return null;
    }
}
