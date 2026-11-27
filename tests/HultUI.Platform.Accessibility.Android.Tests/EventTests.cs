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

    /// <summary>An empty field's events carry its name, as a native field's carry its hint. Its
    /// focus event carried "", and TalkBack said "Unlabeled" for it (Aperture's Search, a Galaxy S25).
    /// One with text carries its text.</summary>
    [Fact]
    public void AnEmptyFieldsEvents_CarryItsName_AndAFilledOnesItsText()
    {
        var empty = TextInput.New("").AccessibleName("Search").Width(200);
        var filled = TextInput.New("drama").AccessibleName("Genre").Width(200);
        using var h = Page(empty, filled);
        Assert.Equal("Search", AndroidNodeMapper.EventWords(h.Node(empty), AndroidEventType.ViewAccessibilityFocused));
        Assert.Equal("drama", AndroidNodeMapper.EventWords(h.Node(filled), AndroidEventType.ViewAccessibilityFocused));
    }

    /// <summary>A content change carries no words, as a native view's carries none: TalkBack then says
    /// what changed, a toggle's new state. With the name in it, a double tap on Aperture's Favourites
    /// chip said "Favourites", not "On" (TalkBack 15 on the emulator). A live region's carries its
    /// words, which are what changed: an alert's, the announcer's. A click carries the name.</summary>
    [Fact]
    public void AContentChange_CarriesNoWords_UnlessItsNodeIsALiveRegion()
    {
        var favourites = Button.New("Favourites").Width(120);
        favourites.AccessibleToggled = true;
        var alert = Text.New("Saved").AccessibleRole(AccessibilityRole.Alert);
        using var h = Page(favourites, alert);
        Assert.Null(AndroidNodeMapper.EventWords(h.Node(favourites), AndroidEventType.WindowContentChanged));
        Assert.Equal("Favourites", AndroidNodeMapper.EventWords(h.Node(favourites), AndroidEventType.ViewClicked));
        Assert.Equal("Saved", AndroidNodeMapper.EventWords(h.Node(alert), AndroidEventType.WindowContentChanged));
        h.Bridge.Announce(null, "File saved", AnnouncementPoliteness.Polite);
        Assert.Equal("File saved",
            AndroidNodeMapper.EventWords(h.Bridge.Node(AndroidIds.Announcer)!, AndroidEventType.WindowContentChanged));
    }

    /// <summary>A toggle TalkBack is on that changes its state is told as clicked, after the change,
    /// as a native switch sends its click: TalkBack says the new state from it. From the content
    /// change alone it said only the name (Aperture's Favourites chip, a Galaxy S25). One it is not
    /// on is not.</summary>
    [Fact]
    public void AToggleTalkBackIsOn_ChangingItsState_IsToldAsClicked()
    {
        var favourites = Button.New("Favourites").Width(120);
        favourites.AccessibleToggled = false;
        var unwatched = Button.New("Unwatched").Width(120);
        unwatched.AccessibleToggled = false;
        using var h = Page(favourites, unwatched);
        h.ReadAll();
        int id = h.Id(favourites), other = h.Id(unwatched);
        Assert.True(h.Bridge.Perform(id, AndroidActions.AccessibilityFocus));
        h.Sink.Clear();

        favourites.AccessibleToggled = true;
        unwatched.AccessibleToggled = true;
        h.Apply();
        var clicked = Assert.Single(h.Sink.Events, e => e.Type == AndroidEventType.ViewClicked);
        Assert.Equal(id, clicked.Source);
        Assert.True(h.Bridge.Node(id)!.Has(AndroidNodeFlags.Checked));
        // A switch to TalkBack, its state in words as Android's own gives it: TalkBack says that, and
        // read the chip "Favourites. Switch", neither on nor off (TalkBack 15 on the emulator).
        Assert.Equal(AndroidClasses.Switch, h.Bridge.Node(id)!.ClassName);
        Assert.Equal("On", h.Bridge.Node(id)!.StateDescription);
        favourites.AccessibleToggled = false;
        h.Apply();
        Assert.Equal("Off", h.Bridge.Node(id)!.StateDescription);
        Assert.Contains(h.Sink.Events, e => e.Type == AndroidEventType.WindowContentChanged && e.Source == other);
    }

    /// <summary>A scroll moves TalkBack's focused node on screen, though its box in the tree (which is
    /// unscrolled) stays the same. So the node is told as changed, Android reads its bounds again,
    /// and the focus box moves with it. It stayed where the node had been (a Galaxy S25).</summary>
    [Fact]
    public void AScroll_TellsTheFocusedNodeChanged_SoTheFocusBoxFollowsIt()
    {
        var row = Button.New("Row").Height(40).FlexShrink(0);
        var page = ScrollView.Vertical().Width(300).Height(200)
            .Children(Flex.Column().FlexShrink(0).Children(row, Box.New().Height(900).FlexShrink(0)));
        using var h = new Harness(page, width: 300, height: 200, scale: 2f);
        h.ReadAll();   // as TalkBack reads the window when it first looks
        int rowId = h.Id(row);
        Assert.True(h.Bridge.Perform(rowId, AndroidActions.AccessibilityFocus));
        h.Sink.Clear();

        page.ScrollOffsetY = 20;
        h.Apply();
        Assert.True(h.Sink.Events.Any(e => e.Type == AndroidEventType.WindowContentChanged && e.Source == rowId),
            $"row {rowId}: " + string.Join(", ", h.Sink.Events.Select(e => $"{e.Type} {e.Source} {e.Changes}")));
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
