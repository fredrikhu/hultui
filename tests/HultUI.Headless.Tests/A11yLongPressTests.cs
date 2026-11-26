using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Dialogs;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Headless.Tests;

using TextB = HultUI.Core.Builders.Text;

/// <summary>
/// A reader's long press and dismiss, on the window (TalkBack's long-click and dismiss actions,
/// through the router): a long press does what a finger's does, but the parts for the eyes, and
/// opens a context menu from the keyboard's place; a dismiss closes a menu or a dialog. And the
/// tree says where a long press means something only while a bridge asks it to.
/// </summary>
public class A11yLongPressTests
{
    private static IReadOnlyList<ContextMenuItem> Menu(List<string> chosen) =>
    [
        new ContextMenuItem { Label = "Rename", Action = () => chosen.Add("rename") },
        new ContextMenuItem { Label = "Delete", Action = () => chosen.Add("delete") },
    ];

    private static (HeadlessSession Session, AccessibilityActionRouter Router) Mount(Element content)
    {
        var session = HeadlessSession.Create(400, 600);
        session.Window.SetRoot(Flex.Column().Width(400).Height(600).Children(content));
        session.PumpFrame();
        return (session, new AccessibilityActionRouter(session.Window.Window));
    }

    private static AccessibilityNode? FindRole(AccessibilityNode node, AccessibilityRole role) =>
        node.Role == role ? node : node.Children.Select(c => FindRole(c, role)).FirstOrDefault(n => n is not null);

    [Fact]
    public void ALongPress_RunsTheHandlerOnTheWayUp()
    {
        int held = 0;
        var button = Button.New("Hold me");
        var card = Box.New().Children(button);
        card.OnLongPress(_ => held++);
        var (session, router) = Mount(card);
        using var _ = session;

        router.LongPress(button.Id);
        router.DrainPendingActions();
        Assert.Equal(1, held);
    }

    [Fact]
    public void ALongPress_OpensTheContextMenu_WithItsFirstItemCurrent()
    {
        var chosen = new List<string>();
        var button = Button.New("Options").ContextMenu(Menu(chosen));
        var (session, router) = Mount(button);
        using var _ = session;

        router.LongPress(button.Id);
        router.DrainPendingActions();
        session.PumpFrame();
        var menu = FindRole(session.Window.AccessibilityTree, AccessibilityRole.Menu);
        Assert.NotNull(menu);
        Assert.Equal(0, menu!.CurrentItem);   // the reader's focus goes into the menu

        router.Dismiss(menu.ElementId);
        router.DrainPendingActions();
        session.PumpFrame();
        Assert.Null(FindRole(session.Window.AccessibilityTree, AccessibilityRole.Menu));
        Assert.Empty(chosen);
    }

    [Fact]
    public void ALongPressOnAListsItem_OpensTheListsContextMenu()
    {
        var chosen = new List<string>();
        var list = VirtualListElement<string>.New().ItemHeight(30).Height(300).FlexShrink(0)
            .RenderWith(t => Flex.Row().Children(TextB.New(t)))
            .Items(Enumerable.Range(0, 50).Select(i => $"file {i}").ToList())
            .ContextMenu(Menu(chosen));
        var (session, router) = Mount(list);
        using var _ = session;

        router.LongPress(VirtualItemId.For(list.Id, 3));
        router.DrainPendingActions();
        session.PumpFrame();
        Assert.NotNull(FindRole(session.Window.AccessibilityTree, AccessibilityRole.Menu));
    }

    [Fact]
    public void ALongPressWhereNothingListens_DoesNothing()
    {
        var button = Button.New("Plain");
        var (session, router) = Mount(button);
        using var _ = session;
        router.LongPress(button.Id);
        router.DrainPendingActions();
        session.PumpFrame();
        Assert.Null(FindRole(session.Window.AccessibilityTree, AccessibilityRole.Menu));
    }

    [Fact]
    public void ADismiss_ClosesADialog_AsEscapeWould()
    {
        var (session, router) = Mount(Button.New("Behind"));
        using var _ = session;
        var shown = Dialog.Confirm(session.Window.Window, "Delete the file?", "It cannot be undone.");
        session.PumpFrame();
        var dialog = FindRole(session.Window.AccessibilityTree, AccessibilityRole.Dialog);
        Assert.NotNull(dialog);

        router.Dismiss(dialog!.ElementId);
        router.DrainPendingActions();
        session.PumpFrame();
        Assert.True(shown.IsCompleted);
        Assert.False(shown.Result);
        Assert.Null(FindRole(session.Window.AccessibilityTree, AccessibilityRole.Dialog));
    }

    [Fact]
    public void TheTreeSaysWhereALongPressMeansSomething_OnlyWhileABridgeAsks()
    {
        var button = Button.New("Options").ContextMenu(Menu([]));
        var (session, _) = Mount(button);
        using var __ = session;
        bool was = AccessibilityTreeBuilder.MarksLongPress;
        try
        {
            AccessibilityTreeBuilder.MarksLongPress = false;
            var off = AccessibilityTreeBuilder.Build(session.Window.Window.Root!, null, 0, 0, default);
            Assert.False(Find(off, button.Id)!.OffersLongPress);

            AccessibilityTreeBuilder.MarksLongPress = true;
            var on = AccessibilityTreeBuilder.Build(session.Window.Window.Root!, null, 0, 0, default);
            Assert.True(Find(on, button.Id)!.OffersLongPress);
        }
        finally { AccessibilityTreeBuilder.MarksLongPress = was; }
    }

    private static AccessibilityNode? Find(AccessibilityNode node, ElementId id) =>
        node.ElementId == id ? node : node.Children.Select(c => Find(c, id)).FirstOrDefault(n => n is not null);
}
