using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Platform.Accessibility.Android.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// What TalkBack is told (docs/design/TALKBACK.md §3, events): content changes on the nodes it has
/// been shown, coalesced per node and per call; a dialog's title as it opens; the input focus; a
/// scroll; a field's edit; an announcement as a live region. Nothing while no service listens.
/// </summary>
public class EventTests
{
    private static Harness Page(params Element[] children) =>
        new(Box.New().Width(400).Height(800).Children(children));

    private static AccessibilityNode NodeOf(Harness h, Element element) =>
        Find(h.Tree, element.Id) ?? throw new InvalidOperationException("not in the tree");

    private static AccessibilityNode? Find(AccessibilityNode node, ElementId id)
    {
        if (node.ElementId == id) return node;
        foreach (var child in node.Children)
            if (Find(child, id) is { } found) return found;
        return null;
    }

    private static (AndroidEventType, int, AndroidContentChange) Content(int id, AndroidContentChange change) =>
        (AndroidEventType.WindowContentChanged, id, change);

    private static IEnumerable<(AndroidEventType, int, AndroidContentChange)> Told(Harness h) =>
        h.Sink.Events.Select(e => (e.Type, e.Source, e.Changes));

    [Fact]
    public void TheFirstTreeAfterAServiceStartsListening_IsToldWhole()
    {
        using var h = Page(Button.New("Save"));
        h.Bridge.SetEnabled(false);
        h.Bridge.SetEnabled(true);
        h.Apply();

        Assert.Equal([Content(AndroidIds.Host, AndroidContentChange.Subtree)], Told(h));
        Assert.Equal(3, h.Host.ActiveChanges);   // on (the harness), off, on
    }

    [Fact]
    public void WhileNoServiceListens_TheBridgeIsInactive_AndTellsNothing()
    {
        var save = Button.New("Save");
        using var h = new Harness(Box.New().Width(400).Height(800).Children(save), enabled: false);
        Assert.False(h.Bridge.IsActive);

        save.Label = "Saved";
        h.Apply();
        h.Bridge.Announce(null, "Saved", AnnouncementPoliteness.Polite);
        Assert.Empty(h.Sink.Events);
    }

    [Fact]
    public void ARename_IsToldOnANodeTalkBackHasSeen_AndNotOnOneItHasNot()
    {
        var seen = Button.New("Save");
        var unseen = Button.New("Cancel");
        using var h = Page(seen, unseen);
        int id = h.Id(seen);   // shown to TalkBack; Cancel never is

        seen.Label = "Saved";
        unseen.Label = "Close";
        h.Apply();

        Assert.Equal([Content(id, AndroidContentChange.Text)], Told(h));
        Assert.Equal("Saved", h.Bridge.Node(id)!.Text);
    }

    [Fact]
    public void AChildAdded_IsToldAsTheParentsSubtreeChange()
    {
        var list = Box.New().Children(Text.New("one"));
        list.Role = AccessibilityRole.Group;   // a box with no role is no node: its children are its parent's
        using var h = Page(list);
        h.ReadAll();

        list.Children(Text.New("one"), Text.New("two"));
        h.Apply();
        Assert.Contains(Content(h.Id(list), AndroidContentChange.Subtree), Told(h));
    }

    [Fact]
    public void ADialogOpening_IsToldAsAPaneThatAppeared()
    {
        var page = Box.New().Width(400).Height(800);
        using var h = new Harness(page);
        h.ReadAll();

        var dialog = Box.New().Children(Button.New("Delete"));
        dialog.Role = AccessibilityRole.Dialog;
        dialog.AccessibleName = "Delete the file?";
        page.Children(dialog);
        h.Apply();

        var appeared = Assert.Single(h.Sink.Events, e => e.Type == AndroidEventType.WindowStateChanged);
        Assert.Equal(AndroidContentChange.PaneAppeared, appeared.Changes);
        Assert.Equal(h.Id(dialog), appeared.Source);
        Assert.Equal("Delete the file?", h.Bridge.Node(appeared.Source)!.PaneTitle);
        // And as its container's title, which TalkBack says as its focus goes in: the window puts
        // the focus on the dialog's button at once, and TalkBack said only the button.
        Assert.Equal("Delete the file?", h.Bridge.Node(appeared.Source)!.ContainerTitle);
    }

    [Fact]
    public void AToggle_IsToldAsAStateChange_OfItsCheckedState()
    {
        var wifi = Checkbox.New(false, "Wi-Fi");
        using var h = Page(wifi);
        int id = h.Id(wifi);

        wifi.IsChecked = true;
        h.Apply();
        Assert.Equal([Content(id, AndroidContentChange.StateDescription | AndroidContentChange.Checked)], Told(h));
        Assert.True(h.Bridge.Node(id)!.Has(AndroidNodeFlags.Checked));
    }

    [Fact]
    public void TheInputFocus_IsToldOnTheFocusedNode()
    {
        var name = TextInput.New("").AccessibleName("Name").Width(200);
        using var h = Page(name);

        h.Bridge.NotifyFocusChanged(NodeOf(h, name));
        var focused = Assert.Single(h.Sink.Events);
        Assert.Equal(AndroidEventType.ViewFocused, focused.Type);
        Assert.Equal(h.Id(name), focused.Source);
        Assert.Equal(focused.Source, h.Bridge.InputFocusedId);

        h.Bridge.NotifyFocusChanged(NodeOf(h, name));   // told already
        Assert.Single(h.Sink.Events);
    }

    [Fact]
    public void AScroll_IsToldWithWhereItIsAndHowFarItMoved_InDevicePixels()
    {
        var page = ScrollView.Vertical().Width(300).Height(200).Children(Box.New().Height(900).FlexShrink(0));
        using var h = new Harness(page, width: 300, height: 200, scale: 2f);
        int id = h.Id(page);

        page.ScrollOffsetY = 50;
        h.Apply();
        var scrolled = Assert.Single(h.Sink.Events, e => e.Type == AndroidEventType.ViewScrolled);
        Assert.Equal(id, scrolled.Source);
        Assert.Equal(100, scrolled.ScrollY);
        Assert.Equal(100, scrolled.ScrollDeltaY);
        Assert.Equal(1400, scrolled.MaxScrollY);
    }

    [Fact]
    public void AnEdit_IsToldAsTheTextThatWentAndCame()
    {
        var name = TextInput.New("Ada").AccessibleName("Name").Width(200);
        using var h = Page(name);
        int id = h.Id(name);

        name.Text = "Adam";
        h.Apply();
        var edit = Assert.Single(h.Sink.Events, e => e.Type == AndroidEventType.ViewTextChanged);
        Assert.Equal(id, edit.Source);
        Assert.Equal("Ada", edit.BeforeText);
        Assert.Equal((3, 0, 1), (edit.FromIndex, edit.RemovedCount, edit.AddedCount));
    }

    [Fact]
    public void ManyChangesInOneCall_AreToldAsOneChangeOfTheWholeTree()
    {
        var buttons = Enumerable.Range(0, AndroidAccessibilityBridge.MaxContentChanges + 5)
            .Select(i => Button.New($"b{i}").Height(10).FlexShrink(0)).ToArray();
        using var h = Page(buttons);
        h.ReadAll();

        foreach (var b in buttons) b.Label += "!";
        h.Apply();
        Assert.Equal([Content(AndroidIds.Host, AndroidContentChange.Subtree)], Told(h));
    }

    [Fact]
    public void AnAnnouncement_IsALiveRegionsChange_ThatSaysTheText()
    {
        var save = Button.New("Save");
        using var h = Page(save);

        h.Bridge.Announce(NodeOf(h, save), "File saved", AnnouncementPoliteness.Assertive);
        Assert.Equal([Content(AndroidIds.Announcer, AndroidContentChange.Text)], Told(h));
        var announcer = h.Bridge.Node(AndroidIds.Announcer)!;
        Assert.Equal("File saved", announcer.Text);
        Assert.Equal(AndroidLiveRegion.Assertive, announcer.LiveRegion);
        Assert.Equal(h.Node(save).BoundsInScreen, announcer.BoundsInScreen);
        // Never among the host's children: a swipe does not land on it.
        Assert.DoesNotContain(AndroidIds.Announcer, h.Bridge.Node(AndroidIds.Host)!.Children);
    }
}
