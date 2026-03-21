using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Components;

public class TreeDifferTests
{
    [Fact]
    public void SameType_PatchesProperties()
    {
        var parent = Box.New();
        var existing = Box.New().Width(100).Background(Color.Red);
        parent.AddChild(existing);

        var incoming = Box.New().Width(200).Background(Color.Blue);

        TreeDiffer.Reconcile(parent, existing, incoming);

        Assert.Same(existing, parent.Children[0]);
        Assert.Equal(Dimension.Px(200), existing.Box.Width);
        Assert.Equal(Color.Blue, existing.BackgroundColor);
    }

    [Fact]
    public void DifferentType_Replaces()
    {
        var parent = Box.New();
        var existing = Box.New();
        parent.AddChild(existing);

        var incoming = Text.New("Replaced");

        TreeDiffer.Reconcile(parent, existing, incoming);

        Assert.IsType<TextElement>(parent.Children[0]);
        Assert.Equal("Replaced", ((TextElement)parent.Children[0]).Content);
    }

    [Fact]
    public void Children_SameCount_PatchesAll()
    {
        var parent = Box.New();
        var existing = Box.New().Children(
            Text.New("A"),
            Text.New("B")
        );
        parent.AddChild(existing);

        var incoming = Box.New().Children(
            Text.New("X"),
            Text.New("Y")
        );

        TreeDiffer.Reconcile(parent, existing, incoming);

        Assert.Equal(2, existing.Children.Count);
        Assert.Equal("X", ((TextElement)existing.Children[0]).Content);
        Assert.Equal("Y", ((TextElement)existing.Children[1]).Content);
    }

    [Fact]
    public void Children_MoreNew_AddsExtras()
    {
        var parent = Box.New();
        var existing = Box.New().Children(Text.New("A"));
        parent.AddChild(existing);

        var incoming = Box.New().Children(
            Text.New("A"),
            Text.New("B"),
            Text.New("C")
        );

        TreeDiffer.Reconcile(parent, existing, incoming);

        Assert.Equal(3, existing.Children.Count);
        Assert.Equal("C", ((TextElement)existing.Children[2]).Content);
    }

    [Fact]
    public void Children_FewerNew_RemovesExtras()
    {
        var parent = Box.New();
        var existing = Box.New().Children(
            Text.New("A"),
            Text.New("B"),
            Text.New("C")
        );
        parent.AddChild(existing);

        var incoming = Box.New().Children(Text.New("Only"));

        TreeDiffer.Reconcile(parent, existing, incoming);

        Assert.Single(existing.Children);
        Assert.Equal("Only", ((TextElement)existing.Children[0]).Content);
    }

    [Fact]
    public void Children_NewEmpty_RemovesAll()
    {
        var parent = Box.New();
        var existing = Box.New().Children(Text.New("A"), Text.New("B"));
        parent.AddChild(existing);

        var incoming = Box.New(); // no children

        TreeDiffer.Reconcile(parent, existing, incoming);

        Assert.Empty(existing.Children);
    }

    [Fact]
    public void Children_OldEmpty_AddsAll()
    {
        var parent = Box.New();
        var existing = Box.New();
        parent.AddChild(existing);

        var incoming = Box.New().Children(Text.New("New1"), Text.New("New2"));

        TreeDiffer.Reconcile(parent, existing, incoming);

        Assert.Equal(2, existing.Children.Count);
    }

    [Fact]
    public void Keyed_MatchesByKey()
    {
        var parent = Box.New();
        var a = Box.New().Background(Color.Red);
        a.Key = "a";
        var b = Box.New().Background(Color.Blue);
        b.Key = "b";
        var existing = Box.New().Children(a, b);
        parent.AddChild(existing);

        // Incoming: reversed order
        var newA = Box.New().Background(Color.Green);
        newA.Key = "a";
        var newB = Box.New().Background(Color.White);
        newB.Key = "b";
        var incoming = Box.New().Children(newB, newA);

        TreeDiffer.Reconcile(parent, existing, incoming);

        // After reconciliation, order should be [b, a] but elements are reused
        Assert.Equal(2, existing.Children.Count);
        // Element b should now be first with White background
        Assert.Same(b, existing.Children[0]);
        Assert.Equal(Color.White, ((BoxElement)existing.Children[0]).BackgroundColor);
        // Element a should be second with Green background
        Assert.Same(a, existing.Children[1]);
        Assert.Equal(Color.Green, ((BoxElement)existing.Children[1]).BackgroundColor);
    }

    [Fact]
    public void Keyed_RemovesUnmatched()
    {
        var parent = Box.New();
        var a = Box.New(); a.Key = "a";
        var b = Box.New(); b.Key = "b";
        var c = Box.New(); c.Key = "c";
        var existing = Box.New().Children(a, b, c);
        parent.AddChild(existing);

        // Only keep "b"
        var newB = Box.New().Background(Color.Red); newB.Key = "b";
        var incoming = Box.New().Children(newB);

        TreeDiffer.Reconcile(parent, existing, incoming);

        Assert.Single(existing.Children);
        Assert.Same(b, existing.Children[0]);
    }

    [Fact]
    public void Keyed_AddsNew()
    {
        var parent = Box.New();
        var a = Box.New(); a.Key = "a";
        var existing = Box.New().Children(a);
        parent.AddChild(existing);

        var newA = Box.New(); newA.Key = "a";
        var newB = Box.New(); newB.Key = "b";
        var incoming = Box.New().Children(newA, newB);

        TreeDiffer.Reconcile(parent, existing, incoming);

        Assert.Equal(2, existing.Children.Count);
        Assert.Same(a, existing.Children[0]);  // "a" reused
        Assert.Equal("b", existing.Children[1].Key); // "b" is new
    }

    [Fact]
    public void Nested_RecursivePatching()
    {
        var parent = Box.New();
        var existing = Box.New().Children(
            Box.New().Children(
                Text.New("Deep")
            )
        );
        parent.AddChild(existing);

        var innerBox = existing.Children[0];
        var deepText = innerBox.Children[0];

        var incoming = Box.New().Children(
            Box.New().Background(Color.Red).Children(
                Text.New("Updated")
            )
        );

        TreeDiffer.Reconcile(parent, existing, incoming);

        // Inner box should be patched (same type)
        Assert.Same(innerBox, existing.Children[0]);
        Assert.Equal(Color.Red, ((BoxElement)innerBox).BackgroundColor);
        // Deep text should be patched
        Assert.Same(deepText, innerBox.Children[0]);
        Assert.Equal("Updated", ((TextElement)deepText).Content);
    }

    [Fact]
    public void TypeMismatchAtChildLevel_ReplacesChild()
    {
        var parent = Box.New();
        var existing = Box.New().Children(Box.New(), Text.New("Keep"));
        parent.AddChild(existing);

        var keptText = existing.Children[1];

        var incoming = Box.New().Children(Text.New("Replaced"), Text.New("Keep"));

        TreeDiffer.Reconcile(parent, existing, incoming);

        // First child replaced (Box → Text)
        Assert.IsType<TextElement>(existing.Children[0]);
        Assert.Equal("Replaced", ((TextElement)existing.Children[0]).Content);
        // Second child patched (same type)
        Assert.Same(keptText, existing.Children[1]);
    }
}
