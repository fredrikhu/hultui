using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// Visual properties are self-marking: assigning a changed value marks the
/// element (and its ancestors) paint-dirty, no-op writes don't, and list-typed
/// properties mark on in-place mutation via <see cref="DirtyList{T}"/>.
/// </summary>
public class SelfMarkingVisualTests
{
    private static (BoxElement parent, BoxElement child) CleanTree()
    {
        var child = Box.New();
        var parent = Box.New().Children(child);
        parent.DirtyFlags = DirtyFlags.None;
        child.DirtyFlags = DirtyFlags.None;
        return (parent, child);
    }

    [Fact]
    public void BackgroundColor_Change_MarksPaintDirtyUpToRoot()
    {
        var (parent, child) = CleanTree();

        child.BackgroundColor = Color.Red;

        Assert.True(child.DirtyFlags.HasFlag(DirtyFlags.Paint));
        Assert.True(parent.DirtyFlags.HasFlag(DirtyFlags.Paint));
    }

    [Fact]
    public void BackgroundColor_SameValue_DoesNotMark()
    {
        var (parent, child) = CleanTree();
        child.BackgroundColor = Color.Red;
        child.DirtyFlags = DirtyFlags.None;
        parent.DirtyFlags = DirtyFlags.None;

        child.BackgroundColor = Color.Red;

        Assert.Equal(DirtyFlags.None, child.DirtyFlags);
        Assert.Equal(DirtyFlags.None, parent.DirtyFlags);
    }

    [Fact]
    public void Transform_Change_MarksPaintDirty()
    {
        var (_, child) = CleanTree();

        child.Transform = Transform2D.Identity with { RotationDegrees = 45f };

        Assert.True(child.DirtyFlags.HasFlag(DirtyFlags.Paint));
    }

    [Fact]
    public void BoxShadows_InPlaceAdd_MarksPaintDirty()
    {
        var (parent, child) = CleanTree();
        child.BoxShadows = new List<BoxShadow>();
        parent.DirtyFlags = DirtyFlags.None;
        child.DirtyFlags = DirtyFlags.None;

        child.BoxShadows!.Add(new BoxShadow(0, 4, 12, new Color(0, 0, 0, 80)));

        Assert.True(child.DirtyFlags.HasFlag(DirtyFlags.Paint));
        Assert.True(parent.DirtyFlags.HasFlag(DirtyFlags.Paint));
    }

    [Fact]
    public void BoxShadows_AssignmentWrapsInDirtyList()
    {
        var box = Box.New();
        box.BoxShadows = new List<BoxShadow> { new(0, 1, 2, Color.Black) };
        Assert.IsType<DirtyList<BoxShadow>>(box.BoxShadows);
    }

    [Fact]
    public void ScrollOffset_Change_MarksPaintDirty()
    {
        var sv = ScrollViewElement.New();
        sv.Box.Width = 200;
        sv.Box.Height = 100;
        sv.AddChild(Box.New().Width(Dimension.Px(200)).Height(Dimension.Px(2000)));

        new HultUI.Core.Layout.LayoutEngine(new StubMeasureContext()).PerformLayout(sv, 200, 100);
        sv.UpdateContentSize();
        sv.DirtyFlags = DirtyFlags.None;

        sv.ScrollOffsetY = 50;

        Assert.True(sv.DirtyFlags.HasFlag(DirtyFlags.Paint));
    }
}
