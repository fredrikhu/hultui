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

    // A keyed row the way builds produce it: two keyed children around an unkeyed label.
    private static BoxElement KeyedRow(string label, string first = "a", string last = "b")
    {
        var a = Box.New().Width(20);
        a.Key = first;
        var b = Box.New().Width(30);
        b.Key = last;
        return Box.New().Children(a, Text.New(label), b);
    }

    private static void ClearFlags(Element element)
    {
        element.DirtyFlags = DirtyFlags.None;
        foreach (var child in element.Children) ClearFlags(child);
    }

    /// <summary>
    /// A keyed list that comes back as it was — every child matched, in the same order — is
    /// not a structural change. Replacing the children anyway told every ancestor its children
    /// had changed and its layout was stale, and a structural change rebuilds the whole
    /// accessibility tree that frame: on every rebuild of anything with a keyed list in it.
    /// </summary>
    [Fact]
    public void Keyed_AListThatComesBackAsItWas_ChangesNoStructureAndNoLayout()
    {
        var parent = Box.New();
        var existing = KeyedRow("label");
        parent.AddChild(existing);
        var children = existing.Children.ToArray();
        ClearFlags(parent);   // as a frame leaves them

        TreeDiffer.Reconcile(parent, existing, KeyedRow("label"));

        Assert.Equal(children, existing.Children);
        Assert.False(parent.DirtyFlags.HasFlag(DirtyFlags.Children));
        Assert.False(existing.DirtyFlags.HasFlag(DirtyFlags.Children));
        Assert.False(existing.DirtyFlags.HasFlag(DirtyFlags.Layout));
    }

    /// <summary>A child whose content changed marks its own way up; its siblings' order did
    /// not change, so it is still not a structural change.</summary>
    [Fact]
    public void Keyed_AChangedChildInAnUnchangedList_IsNotAStructuralChange()
    {
        var parent = Box.New();
        var existing = KeyedRow("before");
        parent.AddChild(existing);
        ClearFlags(parent);

        TreeDiffer.Reconcile(parent, existing, KeyedRow("a longer label after"));

        Assert.Equal("a longer label after", ((TextElement)existing.Children[1]).Content);
        Assert.True(existing.DirtyFlags.HasFlag(DirtyFlags.Layout));   // the label's own change
        Assert.False(parent.DirtyFlags.HasFlag(DirtyFlags.Children));
    }

    [Fact]
    public void Keyed_AReorderIsStillAStructuralChange()
    {
        var parent = Box.New();
        var existing = KeyedRow("label");
        parent.AddChild(existing);
        var (a, b) = (existing.Children[0], existing.Children[2]);
        ClearFlags(parent);

        TreeDiffer.Reconcile(parent, existing, KeyedRow("label", first: "b", last: "a"));

        Assert.Same(b, existing.Children[0]);
        Assert.Same(a, existing.Children[2]);
        Assert.True(existing.DirtyFlags.HasFlag(DirtyFlags.Children));
        Assert.True(parent.DirtyFlags.HasFlag(DirtyFlags.Children));
        Assert.True(existing.DirtyFlags.HasFlag(DirtyFlags.Layout));
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

    /// <summary>A row where one child carries a key and the rest do not — a toolbar with a
    /// keyed toggle button in it. The unkeyed children are matched by position, so they are
    /// patched rather than rebuilt: rebuilding them threw away live element state, which is
    /// how a search field beside a keyed button lost the keyboard on every keystroke.
    /// </summary>
    [Fact]
    public void Keyed_UnkeyedSiblingsAreMatchedByPosition()
    {
        var parent = Box.New();
        var first = Box.New();
        var toggle = Box.New(); toggle.Key = "toggle";
        var field = Box.New();
        var existing = Box.New().Children(first, toggle, field);
        parent.AddChild(existing);

        var newToggle = Box.New().Background(Color.Red); newToggle.Key = "toggle";
        var incoming = Box.New().Children(Box.New(), newToggle, Box.New().Width(50));

        TreeDiffer.Reconcile(parent, existing, incoming);

        Assert.Equal(3, existing.Children.Count);
        Assert.Same(first, existing.Children[0]);
        Assert.Same(toggle, existing.Children[1]);
        Assert.Same(field, existing.Children[2]);
        Assert.Equal(Dimension.Px(50), field.Box.Width);   // patched, not replaced
    }

    /// <summary>Changing a key still replaces that child — the apps use it to force a
    /// rebuild when an element's two states cannot be patched into one another — and the
    /// unkeyed siblings around it are untouched by that.</summary>
    [Fact]
    public void Keyed_ChangedKeyReplacesOnlyThatChild()
    {
        var parent = Box.New();
        var before = Box.New();
        var toggle = Box.New(); toggle.Key = "toggle#off";
        var after = Box.New();
        var existing = Box.New().Children(before, toggle, after);
        parent.AddChild(existing);

        var newToggle = Box.New(); newToggle.Key = "toggle#on";
        var incoming = Box.New().Children(Box.New(), newToggle, Box.New());

        TreeDiffer.Reconcile(parent, existing, incoming);

        Assert.Same(before, existing.Children[0]);
        Assert.NotSame(toggle, existing.Children[1]);
        Assert.Equal("toggle#on", existing.Children[1].Key);
        Assert.Same(after, existing.Children[2]);
    }

    /// <summary>An unkeyed child appearing mid-row (the ✕ that shows up once a query is
    /// typed) consumes a new position rather than shifting the ones before it.</summary>
    [Fact]
    public void Keyed_UnkeyedInsertKeepsEarlierSiblings()
    {
        var parent = Box.New();
        var icon = Box.New();
        var field = Box.New();
        var toggle = Box.New(); toggle.Key = "toggle";
        var existing = Box.New().Children(icon, field, toggle);
        parent.AddChild(existing);

        var newToggle = Box.New(); newToggle.Key = "toggle";
        var incoming = Box.New().Children(Box.New(), Box.New(), Box.New(), newToggle);

        TreeDiffer.Reconcile(parent, existing, incoming);

        Assert.Equal(4, existing.Children.Count);
        Assert.Same(icon, existing.Children[0]);
        Assert.Same(field, existing.Children[1]);
        Assert.NotSame(icon, existing.Children[2]);
        Assert.Same(toggle, existing.Children[3]);
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
