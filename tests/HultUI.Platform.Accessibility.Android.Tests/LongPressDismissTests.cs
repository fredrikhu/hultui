using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Platform.Accessibility.Android.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// TalkBack's long press (a double tap held, the long-click action), offered where a finger's long
/// press means something: a handler or a context menu on the node's element or above it, a list's
/// for its items. And its dismiss, offered by a dialog and a menu. Both go to the router.
/// </summary>
public class LongPressDismissTests
{
    private static Harness Page(params Element[] children) =>
        new(Box.New().Width(400).Height(800).Children(children));

    private static IReadOnlyList<ContextMenuItem> Menu() => [new ContextMenuItem { Label = "Rename" }];

    [Fact]
    public void AButtonWithAContextMenu_OffersALongPress_AndAPlainOneDoesNot()
    {
        var options = Button.New("Options").ContextMenu(Menu());
        var plain = Button.New("Plain");
        using var h = Page(options, plain);
        Assert.True(h.Node(options).Can(AndroidActions.LongClick));
        Assert.False(h.Node(plain).Can(AndroidActions.LongClick));
    }

    [Fact]
    public void AButtonUnderAContextMenu_OrWithALongPressHandler_OffersOne()
    {
        var inside = Button.New("Inside");
        var card = Box.New().ContextMenu(Menu()).Children(inside);
        var held = Button.New("Held");
        held.OnLongPress(_ => { });
        using var h = Page(card, held);
        Assert.True(h.Node(inside).Can(AndroidActions.LongClick));
        Assert.True(h.Node(held).Can(AndroidActions.LongClick));
    }

    [Fact]
    public void ADisabledButton_OffersNoLongPress()
    {
        var options = Button.New("Options").ContextMenu(Menu());
        options.AccessibleEnabled = false;
        using var h = Page(options);
        Assert.False(h.Node(options).Can(AndroidActions.LongClick));
    }

    [Fact]
    public void ALongPress_GoesToTheRouter()
    {
        var options = Button.New("Options").ContextMenu(Menu());
        using var h = Page(options);
        Assert.True(h.Bridge.Perform(h.Id(options), AndroidActions.LongClick));
        Assert.Equal([$"long press {options.Id.Value}"], h.Host.Calls);
    }

    [Theory]
    [InlineData(30)]     // items as nodes
    [InlineData(3000)]   // served on demand
    public void AListsItems_OfferTheLongPressOfTheListsContextMenu(int count)
    {
        var list = VirtualListElement<string>.New().ItemHeight(20).Height(200).FlexShrink(0)
            .RenderWith(t => Flex.Row().Children(Text.New(t)))
            .Items(Enumerable.Range(0, count).Select(i => $"item {i}").ToList())
            .ContextMenu(Menu());
        using var h = Page(list);
        int item = h.Node(list).Children[2];
        Assert.True(h.Bridge.Node(item)!.Can(AndroidActions.LongClick));
        Assert.True(h.Bridge.Perform(item, AndroidActions.LongClick));
        Assert.Equal([$"long press {VirtualItemId.For(list.Id, 2).Value}"], h.Host.Calls);
    }

    [Fact]
    public void ADialog_OffersToBeDismissed_AndAButtonDoesNot()
    {
        var delete = Button.New("Delete");
        var dialog = Box.New().Children(delete);
        dialog.Role = AccessibilityRole.Dialog;
        dialog.AccessibleName = "Delete the file?";
        using var h = Page(dialog);
        Assert.True(h.Node(dialog).Can(AndroidActions.Dismiss));
        Assert.False(h.Node(delete).Can(AndroidActions.Dismiss));

        Assert.True(h.Bridge.Perform(h.Id(dialog), AndroidActions.Dismiss));
        Assert.Equal([$"dismiss {dialog.Id.Value}"], h.Host.Calls);
    }
}
