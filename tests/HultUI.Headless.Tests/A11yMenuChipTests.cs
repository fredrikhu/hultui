using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Headless.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// A chip that says it is a combo box and opens a menu on its click (Aperture's genre, view and
/// quality chips). TalkBack's double tap on it arrives as an expand, which did nothing: the router
/// knew only the dropdown, the date picker and what says whether it is open. And the reader's
/// focus, given a menu with nothing current, went to the menu itself (a Galaxy S25, plan 20).
/// </summary>
public class A11yMenuChipTests
{
    [Fact]
    public void AReadersExpand_OpensTheChipsMenuBesideIt_OnItsCheckedItem()
    {
        bool? was = AccessibilityClients.Force;
        AccessibilityClients.Force = true;
        try
        {
            using var session = HeadlessSession.Create(400, 600);
            string? chosen = null;
            IReadOnlyList<ContextMenuItem> Menu() =>
            [
                new() { Label = "Any genre", IsChecked = false, Action = () => chosen = "any" },
                new() { Label = "Drama", IsChecked = true, Action = () => chosen = "drama" },
                new() { Label = "Comedy", IsChecked = false, Action = () => chosen = "comedy" },
            ];
            var chip = Box.New().Padding(8).AccessibleRole(AccessibilityRole.ComboBox).AccessibleName("Genre: Drama")
                .ContextMenu(Menu).Children(Text.New("Drama"));
            chip.IsFocusable = true;
            chip.OnClick(e => session.Window.Window.OpenContextMenu(e.CurrentTarget, e.Position));
            session.Window.SetRoot(Flex.Column().Width(400).Height(600)
                .Children(Box.New().Height(200).FlexShrink(0), Flex.Row().Children(chip)));
            var bridge = session.Window.InstallAccessibilityBridge();
            session.PumpFrame();
            var router = new AccessibilityActionRouter(session.Window.Window);

            router.ExpandCollapseElement(chip.Id, expand: true);
            router.DrainPendingActions();
            session.PumpFrame();
            session.PumpFrame();
            var menu = FindRole(session.Window.AccessibilityTree, AccessibilityRole.Menu);
            Assert.NotNull(menu);
            Assert.Equal(1, menu!.CurrentItem);   // the checked item: where the reader's focus goes
            Assert.Equal(menu.ElementId, bridge.FocusedNode?.ElementId);
            // Beside the chip, where the click a reader's activation sends lands; not at the
            // window's corner, where a click at (0, 0) put it.
            var box = chip.WindowBorderBox;
            Assert.True(menu.BoundingRect.Y >= box.Y && menu.BoundingRect.X >= box.X,
                $"the menu at {menu.BoundingRect}, the chip at {box}");
            Assert.Null(chosen);

            // A choice (a double tap on an item) closes the menu, and the reader's focus comes back to
            // the chip: TalkBack went to the window's first element (a Galaxy S25).
            router.InvokeElement(VirtualItemId.For(menu.ElementId, 2));
            router.DrainPendingActions();
            session.PumpFrame();
            session.PumpFrame();
            Assert.Equal("comedy", chosen);
            Assert.Null(FindRole(session.Window.AccessibilityTree, AccessibilityRole.Menu));
            Assert.Equal(chip.Id, bridge.FocusedNode?.ElementId);
        }
        finally { AccessibilityClients.Force = was; }
    }

    private static AccessibilityNode? FindRole(AccessibilityNode node, AccessibilityRole role) =>
        node.Role == role ? node : node.Children.Select(c => FindRole(c, role)).FirstOrDefault(n => n is not null);
}
