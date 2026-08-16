using HultUI.Core.Builders;
using Xunit;

namespace HultUI.Headless.Tests;

// Inside a HultUI.* namespace bare `Text` binds to the HultUI.Text namespace.
using Text = HultUI.Core.Builders.Text;

/// <summary>
/// Window.IsInTree exists because a Ref keeps pointing at an element after a rebuild
/// unmounts it — a non-null ref is not proof the element is on screen. Junction's find
/// bar spent a held reveal on exactly such a ghost; this is the framework-side check
/// it (and the maximize-button cache before it) guards with.
/// </summary>
public class WindowIsInTreeTests
{
    [Fact]
    public void AMountedElement_IsInTree_AndStopsBeingWhenRemoved()
    {
        using var session = HeadlessSession.Create();
        var child = Text.New("hello");
        var root = Box.New().Children(child);
        session.Window.SetRoot(root);
        session.PumpFrame();

        Assert.True(session.Window.Window.IsInTree(child));

        root.RemoveChild(child);
        Assert.False(session.Window.Window.IsInTree(child));
    }

    [Fact]
    public void ALeafDetachedWithItsSubtree_IsNotInTree_ThoughItsParentIsStillSet()
    {
        // Removal nulls Parent only on the removed subtree's top element, so a bare
        // Parent check on the leaf still answers "attached" — the walk has to reach
        // the window's root to tell the truth.
        using var session = HeadlessSession.Create();
        var leaf = Text.New("leaf");
        var branch = Box.New().Children(leaf);
        var root = Box.New().Children(branch);
        session.Window.SetRoot(root);
        session.PumpFrame();

        root.RemoveChild(branch);

        Assert.NotNull(leaf.Parent);
        Assert.False(session.Window.Window.IsInTree(leaf));
    }
}
