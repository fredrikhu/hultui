using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Platform.Accessibility.Android.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// The virtual tree (docs/design/TALKBACK.md §2): the root is the host view, ids are kept while an
/// element is in the tree and never given to another, bounds are the drawn box in device pixels clipped
/// to what shows, and a finger exploring the screen finds the node drawn under it.
/// </summary>
public class TreeAndBoundsTests
{
    [Fact]
    public void TheRoot_IsTheHostsOneChild_AndItsChildrenPointBackAtIt()
    {
        var save = Button.New("Save");
        var group = Box.New().Children(save);
        group.Role = Core.Accessibility.AccessibilityRole.Group;
        var page = Box.New().Width(400).Height(800).Children(group);
        using var h = new Harness(page);

        var host = h.Bridge.Node(AndroidIds.Host)!;
        Assert.Equal(AndroidIds.None, host.ParentId);
        Assert.Equal(h.Id(page), Assert.Single(host.Children));
        Assert.Equal(AndroidIds.Host, h.Node(page).ParentId);
        Assert.Equal([h.Id(group)], h.Node(page).Children);
        Assert.Equal(h.Id(page), h.Node(group).ParentId);
        Assert.Equal(h.Id(group), h.Node(save).ParentId);
    }

    [Fact]
    public void AnElementKeepsItsId_AndAGoneOnesIdIsNeverGivenAgain()
    {
        var a = Button.New("A");
        var page = Box.New().Width(400).Height(800).Children(a);
        using var h = new Harness(page);
        h.ReadAll();
        int idA = h.Id(a);

        h.Apply();
        Assert.Equal(idA, h.Id(a));

        var b = Button.New("B");
        page.Children(b);
        h.Apply();
        h.ReadAll();
        Assert.Null(h.Bridge.Node(idA));
        Assert.NotEqual(idA, h.Id(b));
        Assert.Equal("B", h.Node(b).Text);
    }

    // ── What is left out, as Android leaves out what is not important for accessibility ──

    // A named list item that draws an icon and a label, as a navigation row does.
    private static (BoxElement Item, ImageElement Icon, TextElement Label) Row(string name)
    {
        var icon = Image.New().Width(16).Height(16);
        var label = Text.New(name);
        var item = Box.New().Children(icon, label);
        item.Role = Core.Accessibility.AccessibilityRole.ListItem;
        item.AccessibleName = name;
        return (item, icon, label);
    }

    [Fact]
    public void ANamedControlsIconAndLabel_AreNoViewsOfTheirOwn_AndAFingerOnThemFindsTheControl()
    {
        // TalkBack read them after the name: "Home. Unlabelled. Image. Home".
        var (item, _, label) = Row("Home");
        using var h = new Harness(Box.New().Width(400).Height(800).Children(item));

        var node = h.Node(item);
        Assert.Equal("Home", node.Text);
        Assert.Empty(node.Children);
        Assert.DoesNotContain(h.ReadAll(), n => n.Text == "Home" && n.Id != node.Id);

        var box = label.Layout.AbsoluteBorderBox;
        Assert.Equal(node.Id, h.Bridge.HitTest(box.X + 2, box.Y + 2));
    }

    [Fact]
    public void AnImageWithNoName_IsLeftOut_AndANamedOneIsNot()
    {
        var decorative = Image.New().Width(16).Height(16);
        var logo = Image.New().AccessibleName("Logo").Width(16).Height(16);
        var page = Box.New().Width(400).Height(800).Children(decorative, logo);
        using var h = new Harness(page);

        var all = h.ReadAll();
        Assert.Single(all, n => n.ClassName == AndroidClasses.ImageView);
        Assert.Equal("Logo", h.Node(logo).ContentDescription);
    }

    [Fact]
    public void AControlInsideANamedItem_StaysAStop_InPlaceOfTheHiddenGroupAroundIt()
    {
        var check = Checkbox.New(false, "Starred");
        var group = Box.New().Children(Text.New("Inbox"), check);
        group.Role = Core.Accessibility.AccessibilityRole.Group;
        var item = Box.New().Children(group);
        item.Role = Core.Accessibility.AccessibilityRole.ListItem;
        item.AccessibleName = "Inbox";
        using var h = new Harness(Box.New().Width(400).Height(800).Children(item));

        Assert.Equal([h.Id(check)], h.Node(item).Children);
        Assert.Equal(h.Id(item), h.Node(check).ParentId);

        // A second control into the hidden group: the item's virtual children changed.
        h.ReadAll();
        var flag = Checkbox.New(false, "Flagged");
        group.Children(Text.New("Inbox"), check, flag);
        h.Apply();
        Assert.Contains(h.Sink.Events, e => e.Source == h.Id(item) && (e.Changes & AndroidContentChange.Subtree) != 0);
        Assert.Equal([h.Id(check), h.Id(flag)], h.Node(item).Children);
    }

    [Fact]
    public void Bounds_AreTheBoxTimesTheScale_PlusTheSurfacesPlaceOnScreen()
    {
        var save = Button.New("Save").Width(100).Height(40);
        var page = Box.New().Width(400).Height(800).Padding(10).Children(save);
        using var h = new Harness(page, scale: 2.5f);

        var box = save.Layout.AbsoluteBorderBox;
        var node = h.Bridge.Node(h.Id(save), originX: 0, originY: 100)!;
        Assert.Equal(new PixelBox(
            (int)MathF.Floor(box.X * 2.5f), 100 + (int)MathF.Floor(box.Y * 2.5f),
            (int)MathF.Ceiling(box.Right * 2.5f), 100 + (int)MathF.Ceiling(box.Bottom * 2.5f)), node.BoundsInScreen);
        Assert.Equal(250, node.BoundsInScreen.Right - node.BoundsInScreen.Left);
    }

    // A page of 200 that scrolls: a spacer, then a row of buttons below the fold.
    private static (ScrollViewElement Page, ButtonElement Below) Scroller()
    {
        var below = Button.New("Below").Height(40).FlexShrink(0);
        var page = ScrollView.Vertical().Width(300).Height(200).Children(
            Box.New().Height(300).FlexShrink(0),
            below,
            Box.New().Height(300).FlexShrink(0));
        return (page, below);
    }

    [Fact]
    public void ANodeBelowTheFold_IsNotVisible_UntilThePageScrollsToIt_AndThenIsWhereItIsDrawn()
    {
        var (page, below) = Scroller();
        using var h = new Harness(page, width: 300, height: 200);

        var hidden = h.Node(below);
        Assert.False(hidden.Has(AndroidNodeFlags.VisibleToUser));
        Assert.True(hidden.BoundsInScreen.IsEmpty);

        page.ScrollOffsetY = 160;   // the button now at 140..180 of the 200
        h.Apply();
        var shown = h.Node(below);
        Assert.True(shown.Has(AndroidNodeFlags.VisibleToUser));
        var drawn = below.ScreenBorderBox;
        Assert.Equal((int)MathF.Floor(drawn.Y), shown.BoundsInScreen.Top);
        Assert.Equal(140, shown.BoundsInScreen.Top);
    }

    [Fact]
    public void ANodeHalfScrolledOut_IsClippedToTheViewport()
    {
        var (page, below) = Scroller();
        using var h = new Harness(page, width: 300, height: 200);

        page.ScrollOffsetY = 120;   // the button at 180..220: half below the 200
        h.Apply();
        var node = h.Node(below);
        Assert.True(node.Has(AndroidNodeFlags.VisibleToUser));
        Assert.Equal(180, node.BoundsInScreen.Top);
        Assert.Equal(200, node.BoundsInScreen.Bottom);
    }

    [Fact]
    public void ANodeInAViewSmallerThanTheWindow_IsClippedToTheView_NotTheWindow()
    {
        // The view's 200 at the top of a window of 800: the button below its fold is still in the window.
        var (view, below) = Scroller();
        var page = Box.New().Width(400).Height(800).Children(view, Box.New().Height(600).FlexShrink(0));
        using var h = new Harness(page);

        view.ScrollOffsetY = 120;   // the button at 180..220 of the view's 200
        h.Apply();
        var node = h.Node(below);
        Assert.Equal(180, node.BoundsInScreen.Top);
        Assert.Equal(200, node.BoundsInScreen.Bottom);

        view.ScrollOffsetY = 0;     // the button at 300..340: in the window, out of the view
        h.Apply();
        Assert.True(h.Node(below).BoundsInScreen.IsEmpty);
        Assert.False(h.Node(below).Has(AndroidNodeFlags.VisibleToUser));
        Assert.False(TestHit(h, 20, 320, below), "a finger below the view found what the view hides");
    }

    private static bool TestHit(Harness h, float x, float y, Element element) => IsWithin(h, h.Bridge.HitTest(x, y), element);

    [Fact]
    public void AScrollView_IsScrollable_AndOffersTheWaysItCanStillMove()
    {
        var (page, _) = Scroller();
        using var h = new Harness(page, width: 300, height: 200);

        var top = h.Node(page);
        Assert.Equal(AndroidClasses.ScrollView, top.ClassName);
        Assert.True(top.Has(AndroidNodeFlags.Scrollable));
        Assert.True(top.Can(AndroidActions.ScrollForward | AndroidActions.ScrollDown));
        Assert.False(top.Can(AndroidActions.ScrollBackward));

        page.ScrollOffsetY = 10_000;
        h.Apply();
        var end = h.Node(page);
        Assert.True(end.Can(AndroidActions.ScrollBackward | AndroidActions.ScrollUp));
        Assert.False(end.Can(AndroidActions.ScrollForward));
    }

    // ── Explore by touch ──

    private static bool IsWithin(Harness h, int id, Element element)
    {
        int target = h.Id(element);
        for (var node = h.Bridge.Node(id); node is not null; node = h.Bridge.Node(node.ParentId))
        {
            if (node.Id == target) return true;
            if (node.ParentId == AndroidIds.None) break;
        }
        return false;
    }

    [Fact]
    public void AFingerOverAButton_FindsTheButton_InDevicePixels()
    {
        var save = Button.New("Save").Width(100).Height(40);
        var cancel = Button.New("Cancel").Width(100).Height(40);
        var page = Box.New().Width(400).Height(800).Children(save, cancel);
        using var h = new Harness(page, scale: 3f);

        var box = cancel.Layout.AbsoluteBorderBox;
        int hit = h.Bridge.HitTest((box.X + 10) * 3f, (box.Y + 10) * 3f);
        Assert.True(IsWithin(h, hit, cancel), $"hit {hit} is not the Cancel button");
        Assert.False(IsWithin(h, hit, save));
    }

    [Fact]
    public void AFingerOnAScrolledPage_FindsWhatIsDrawnUnderIt()
    {
        var (page, below) = Scroller();
        using var h = new Harness(page, width: 300, height: 200);
        page.ScrollOffsetY = 160;
        h.Apply();

        int hit = h.Bridge.HitTest(20, 150);
        Assert.True(IsWithin(h, hit, below), $"hit {hit} is not the button drawn at 140..180");
    }

    [Fact]
    public void AFingerMovingAcross_EntersEachNodeAndLeavesTheOneBefore()
    {
        var save = Button.New("Save").Width(100).Height(40);
        var cancel = Button.New("Cancel").Width(100).Height(40);
        var page = Box.New().Width(400).Height(800).Children(save, cancel);
        using var h = new Harness(page);
        var s = save.Layout.AbsoluteBorderBox;
        var c = cancel.Layout.AbsoluteBorderBox;

        Assert.True(h.Bridge.HoverTo(s.X + 5, s.Y + 5));
        int onSave = Assert.Single(h.Sink.Events).Source;
        Assert.Equal(AndroidEventType.ViewHoverEnter, h.Sink.Events[0].Type);
        h.Sink.Clear();

        h.Bridge.HoverTo(s.X + 6, s.Y + 6);   // the same node: nothing
        Assert.Empty(h.Sink.Events);

        h.Bridge.HoverTo(c.X + 5, c.Y + 5);
        Assert.Equal(2, h.Sink.Events.Count);
        Assert.Equal(AndroidEventType.ViewHoverEnter, h.Sink.Events[0].Type);
        Assert.Equal((AndroidEventType.ViewHoverExit, onSave), (h.Sink.Events[1].Type, h.Sink.Events[1].Source));
        int onCancel = h.Sink.Events[0].Source;
        h.Sink.Clear();

        Assert.True(h.Bridge.HoverTo(null, null), "the finger left a node: the hover was a virtual view's");
        Assert.False(h.Bridge.HoverTo(null, null));
        Assert.Equal((AndroidEventType.ViewHoverExit, onCancel), (Assert.Single(h.Sink.Events).Type, h.Sink.Events[0].Source));
    }

    [Fact]
    public void TheTopmostOfOverlappingSiblings_TakesTheFinger()
    {
        // A dialog drawn over the page, as an overlay is: later siblings are drawn on top.
        var under = Button.New("Under").Width(200).Height(100);
        var over = Text.New("Over").Width(200).Height(100);
        var layer = Box.New().Width(400).Height(800).Children(under);
        var top = Box.New().Width(400).Height(800).Children(over);
        top.Role = Core.Accessibility.AccessibilityRole.Dialog;
        var page = Grid.Of("1fr", "1fr").Width(400).Height(800).Children(layer.GridCell(0, 0), top.GridCell(0, 0));
        using var h = new Harness(page);

        var box = under.Layout.AbsoluteBorderBox;
        int hit = h.Bridge.HitTest(box.X + 5, box.Y + 5);
        Assert.False(IsWithin(h, hit, under), "the page under the dialog took the finger");
        Assert.True(IsWithin(h, hit, top));
    }
}
