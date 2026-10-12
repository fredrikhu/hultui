using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.Core.Tests.Accessibility;

/// <summary>
/// The differ pairs a node's children by position while their ids agree and indexes only what
/// follows the first difference (plan 17, a11y-build-diff-garbage). Bridges turn its output into
/// a screen reader's events in order, so it must say exactly what the differ before it said:
/// these tests diff thousands of random tree pairs with both and compare every entry.
/// </summary>
/// <remarks>
/// Trees are made as the builder makes them: every id unique within a tree, and every node of
/// the new tree a new object. The edits are those a rebuild sees — properties changed, children
/// added, removed, reordered, moved to another parent, the root replaced — mixed at random.
/// </remarks>
public class AccessibilityTreeDifferModelTests(ITestOutputHelper output)
{
    [Fact]
    public void RandomTreePairs_DiffExactlyAsTheIndexEveryChildDifferDid()
    {
        var seen = new Coverage();
        for (int seed = 0; seed < 3000; seed++)
        {
            var (previous, current) = new TreePair(seed).Make();
            var expected = Model.Diff(previous, current);
            var actual = AccessibilityTreeDiffer.Diff(previous, current);
            AssertSame(seed, expected, actual);
            seen.Add(expected);
        }

        // The pairs reach every kind of entry, many times over.
        output.WriteLine(seen.ToString());
        Assert.True(seen.Empty > 100 && seen.Added > 500 && seen.Removed > 500 && seen.Moved > 100
            && seen.Reordered > 200 && seen.Changed > 500, seen.ToString());
    }

    [Fact]
    public void WideChildLists_DiffExactlyAsTheIndexEveryChildDifferDid()
    {
        // A list of a few hundred rows under one parent, edited the ways a list is: one row
        // renamed, rows inserted or deleted anywhere, the order reversed or shuffled.
        for (int seed = 0; seed < 300; seed++)
        {
            var (previous, current) = new TreePair(seed, wide: true).Make();
            AssertSame(seed, Model.Diff(previous, current), AccessibilityTreeDiffer.Diff(previous, current));
        }
    }

    private static void AssertSame(int seed, TreeDiff expected, TreeDiff actual)
    {
        AssertSequence(seed, "Added", expected.Added, actual.Added);
        AssertSequence(seed, "Removed", expected.Removed, actual.Removed);
        AssertSequence(seed, "Reordered", expected.Reordered, actual.Reordered);
        AssertSequence(seed, "PropertyChanged", expected.PropertyChanged, actual.PropertyChanged);
    }

    // Entries hold nodes, which compare by reference: the same node objects, in the same order.
    private static void AssertSequence<T>(int seed, string what, List<T> expected, List<T> actual)
    {
        int n = Math.Min(expected.Count, actual.Count);
        for (int i = 0; i < n; i++)
            Assert.True(EqualityComparer<T>.Default.Equals(expected[i], actual[i]),
                $"seed {seed}: {what}[{i}] is {actual[i]}, the old differ said {expected[i]}");
        Assert.True(expected.Count == actual.Count,
            $"seed {seed}: {what} has {actual.Count} entries, the old differ said {expected.Count}");
    }

    private sealed class Coverage
    {
        public int Empty, Added, Removed, Moved, Reordered, Changed;

        public void Add(TreeDiff diff)
        {
            if (diff.IsEmpty) Empty++;
            if (diff.Added.Count > 0) Added++;
            if (diff.Removed.Count > 0) Removed++;
            if (diff.Reordered.Count > 0) Reordered++;
            if (diff.PropertyChanged.Count > 0) Changed++;
            var added = new HashSet<ElementId>();
            foreach (var a in diff.Added) added.Add(a.Node.ElementId);
            foreach (var r in diff.Removed)
                if (added.Contains(r.Node.ElementId)) { Moved++; break; }
        }

        public override string ToString() =>
            $"pairs: {Empty} unchanged, {Added} with additions, {Removed} with removals, {Moved} with a node "
            + $"moved to another parent, {Reordered} with reordered children, {Changed} with property changes";
    }

    /// <summary>A node before it is built: what it says, and its children.</summary>
    private sealed class Spec
    {
        public ulong Id;
        public AccessibilityRole Role;
        public string? Name, Description, Value, AutomationId;
        public Rect Bounds;
        public bool IsFocusable, IsFocused, IsEnabled, IsPassword, IsOffscreen;
        public bool? IsToggled, Expanded, IsSelected;
        public AccessibleText? Text;
        public NativeAccessibleContent Native;
        public List<Spec> Children = [];

        public Spec Clone()
        {
            var copy = (Spec)MemberwiseClone();
            copy.Children = Children.ConvertAll(c => c.Clone());
            return copy;
        }

        public AccessibilityNode Build()
        {
            var node = new AccessibilityNode(new ElementId(Id), Role, Name, Description, Bounds,
                isFocusable: IsFocusable, isFocused: IsFocused, isEnabled: IsEnabled, isToggled: IsToggled,
                value: Value, automationId: AutomationId, expanded: Expanded, isPassword: IsPassword,
                isSelected: IsSelected)
            {
                IsOffscreen = IsOffscreen,
                Text = Text,
                NativeContent = Native,
            };
            foreach (var child in Children)
                node.AddChild(child.Build());
            return node;
        }

        public void Walk(List<Spec> into)
        {
            into.Add(this);
            foreach (var c in Children) c.Walk(into);
        }

        public bool Contains(Spec other) => ReferenceEquals(this, other) || Children.Exists(c => c.Contains(other));
    }

    /// <summary>One random pair of trees: an old one, and the old one edited.</summary>
    private sealed class TreePair(int seed, bool wide = false)
    {
        private static readonly string?[] Names = [null, "", "Open", "Save", "Save as", "Close", "row"];
        private static readonly string?[] Values = [null, "", "a", "ab", "abc"];
        private static readonly AccessibilityRole[] Roles =
            [AccessibilityRole.Group, AccessibilityRole.Button, AccessibilityRole.Label, AccessibilityRole.ListItem,
             AccessibilityRole.Checkbox, AccessibilityRole.TextInput, AccessibilityRole.List];
        private static readonly AccessibleText?[] Texts =
            [null, new("hello"), new("hello", caret: 2), new("hello", caret: 2, selectionStart: 1, selectionEnd: 3),
             new("hello world")];
        private static readonly Rect[] Rects = [new(0, 0, 10, 10), new(0, 10, 10, 10), new(5, 0, 10, 20)];

        private readonly Random _rng = new(seed);
        private ulong _nextId = 1000;

        public (AccessibilityNode? Previous, AccessibilityNode? Current) Make()
        {
            // Now and then no tree on one side: an assistive technology attaching or leaving.
            int ends = _rng.Next(40);
            var before = wide ? Wide() : Random(depth: 0);
            if (ends == 0) return (null, before.Build());
            if (ends == 1) return (before.Build(), null);
            if (ends == 2) return (null, null);

            var after = before.Clone();
            if (_rng.Next(6) > 0) Edit(after);
            if (_rng.Next(60) == 0) after.Id = _nextId++;   // the root replaced outright
            return (before.Build(), after.Build());
        }

        private Spec Node()
        {
            var spec = new Spec { Id = _nextId++ };
            Randomize(spec);
            return spec;
        }

        private void Randomize(Spec spec)
        {
            spec.Role = Pick(Roles);
            spec.Name = Pick(Names);
            spec.Description = _rng.Next(5) == 0 ? "described" : null;
            spec.Value = Pick(Values);
            spec.AutomationId = _rng.Next(4) == 0 ? "auto" : null;
            spec.Bounds = Pick(Rects);
            spec.IsFocusable = _rng.Next(2) == 0;
            spec.IsFocused = _rng.Next(8) == 0;
            spec.IsEnabled = _rng.Next(6) > 0;
            spec.IsPassword = _rng.Next(10) == 0;
            spec.IsOffscreen = _rng.Next(3) == 0;
            spec.IsToggled = Tri();
            spec.Expanded = Tri();
            spec.IsSelected = Tri();
            spec.Text = Pick(Texts);
            spec.Native = _rng.Next(10) == 0 ? new NativeAccessibleContent(1) : default;
        }

        private bool? Tri() => _rng.Next(3) switch { 0 => null, 1 => true, _ => false };

        private T Pick<T>(T[] from) => from[_rng.Next(from.Length)];

        private Spec Random(int depth)
        {
            var spec = Node();
            int children = depth >= 4 ? 0 : _rng.Next(depth == 0 ? 2 : 0, 7 - depth);
            for (int i = 0; i < children; i++) spec.Children.Add(Random(depth + 1));
            return spec;
        }

        private Spec Wide()
        {
            var root = Node();
            var list = Node();
            root.Children.Add(Node());
            root.Children.Add(list);
            int rows = _rng.Next(50, 300);
            for (int i = 0; i < rows; i++)
            {
                var row = Node();
                if (_rng.Next(4) == 0) row.Children.Add(Node());
                list.Children.Add(row);
            }
            return root;
        }

        /// <summary>A handful of edits anywhere in the tree.</summary>
        private void Edit(Spec root)
        {
            int edits = _rng.Next(1, 6);
            for (int e = 0; e < edits; e++)
            {
                var all = new List<Spec>();
                root.Walk(all);
                // A wide tree's edits go to its long list half the time, or they would almost
                // never land there.
                var lists = wide ? all.FindAll(s => s.Children.Count > 20) : [];
                var at = lists.Count > 0 && _rng.Next(2) == 0 ? lists[0] : all[_rng.Next(all.Count)];
                switch (_rng.Next(9))
                {
                    case 0:   // what a node says
                    case 1:
                        ChangeSomething(at);
                        break;
                    case 2:   // a new child, anywhere among the others
                        at.Children.Insert(_rng.Next(at.Children.Count + 1), Random(depth: 2));
                        break;
                    case 3:   // a child gone
                        if (at.Children.Count > 0) at.Children.RemoveAt(_rng.Next(at.Children.Count));
                        break;
                    case 4:   // the last children gone, or new ones at the end
                        if (at.Children.Count > 0 && _rng.Next(2) == 0)
                        {
                            int gone = _rng.Next(1, at.Children.Count + 1);
                            at.Children.RemoveRange(at.Children.Count - gone, gone);
                        }
                        else
                            for (int i = _rng.Next(1, 4); i > 0; i--) at.Children.Add(Random(depth: 3));
                        break;
                    case 5:   // one child moved among its siblings
                        if (at.Children.Count > 1)
                        {
                            int from = _rng.Next(at.Children.Count);
                            var child = at.Children[from];
                            at.Children.RemoveAt(from);
                            at.Children.Insert(_rng.Next(at.Children.Count + 1), child);
                        }
                        break;
                    case 6:   // reversed or shuffled
                        if (_rng.Next(2) == 0) at.Children.Reverse();
                        else Shuffle(at.Children);
                        break;
                    case 7:   // a subtree moved under another parent
                        Move(root, all);
                        break;
                    case 8:   // a child replaced by a new one in its place
                        if (at.Children.Count > 0) at.Children[_rng.Next(at.Children.Count)] = Random(depth: 3);
                        break;
                }
            }
        }

        private void ChangeSomething(Spec spec)
        {
            switch (_rng.Next(6))
            {
                case 0: spec.Name = Pick(Names); break;
                case 1: spec.Value = Pick(Values); break;
                case 2: spec.Text = Pick(Texts); break;
                case 3: spec.Bounds = Pick(Rects); spec.IsOffscreen = _rng.Next(3) == 0; break;
                case 4: spec.IsToggled = Tri(); spec.IsFocused = !spec.IsFocused; break;
                default: Randomize(spec); break;
            }
        }

        private void Move(Spec root, List<Spec> all)
        {
            var node = all[_rng.Next(all.Count)];
            if (ReferenceEquals(node, root)) return;
            var target = all[_rng.Next(all.Count)];
            if (node.Contains(target)) return;
            var parent = all.Find(s => s.Children.Exists(c => ReferenceEquals(c, node)))!;
            parent.Children.Remove(node);
            target.Children.Insert(_rng.Next(target.Children.Count + 1), node);
        }

        private void Shuffle(List<Spec> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }

    /// <summary>
    /// The differ as it was before plan 17 (a11y-build-diff-garbage): every parent indexed all
    /// of its old children by id. Kept unchanged as the model the differ must agree with.
    /// </summary>
    private static class Model
    {
        public static TreeDiff Diff(AccessibilityNode? previous, AccessibilityNode? current)
        {
            var diff = new TreeDiff();

            if (previous is null && current is null)
                return diff;

            if (previous is null)
            {
                AddRecursive(diff, current!, parent: null);
                return diff;
            }

            if (current is null)
            {
                RemoveRecursive(diff, previous, parent: null);
                return diff;
            }

            if (previous.ElementId != current.ElementId)
            {
                RemoveRecursive(diff, previous, parent: null);
                AddRecursive(diff, current, parent: null);
                return diff;
            }

            DiffSubtree(diff, previous, current);
            return diff;
        }

        private static void DiffSubtree(TreeDiff diff, AccessibilityNode prev, AccessibilityNode curr)
        {
            CompareProperties(diff, prev, curr);

            if (prev.Children.Count == 0 && curr.Children.Count == 0)
                return;

            Dictionary<ElementId, (AccessibilityNode Node, int Index)>? prevById = null;
            if (prev.Children.Count > 0)
            {
                prevById = new Dictionary<ElementId, (AccessibilityNode, int)>(prev.Children.Count);
                for (int i = 0; i < prev.Children.Count; i++)
                    prevById[prev.Children[i].ElementId] = (prev.Children[i], i);
            }

            bool orderChanged = false;
            int lastMatchedPrevIndex = -1;

            for (int i = 0; i < curr.Children.Count; i++)
            {
                var currChild = curr.Children[i];
                if (prevById is not null && prevById.TryGetValue(currChild.ElementId, out var entry))
                {
                    DiffSubtree(diff, entry.Node, currChild);
                    if (entry.Index < lastMatchedPrevIndex)
                        orderChanged = true;
                    lastMatchedPrevIndex = entry.Index;
                    prevById.Remove(currChild.ElementId);
                }
                else
                {
                    AddRecursive(diff, currChild, curr);
                }
            }

            if (prevById is not null)
            {
                foreach (var (_, entry) in prevById)
                    RemoveRecursive(diff, entry.Node, prev);
            }

            if (orderChanged)
                diff.Reordered.Add(curr);
        }

        private static void CompareProperties(TreeDiff diff, AccessibilityNode prev, AccessibilityNode curr)
        {
            // Since plan 17 round 3 step 15: a role change is told, with the previous node.
            if (prev.Role != curr.Role)
                diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.Role, prev));
            if (prev.Name != curr.Name)
                diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.Name));
            if (prev.Description != curr.Description)
                diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.Description));
            // Since plan 17 round 3 step 15: an id change alone has the tree applied.
            if (prev.AutomationId != curr.AutomationId)
                diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.AutomationId));
            // A node with text on both sides tells a change of it as its text, not its value.
            if (!(prev.Text is not null && curr.Text is not null) && prev.Value != curr.Value)
                diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.Value));
            if (prev.IsPassword != curr.IsPassword)
                diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.IsPassword));
            if (prev.IsToggled != curr.IsToggled)
                diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.IsToggled));
            if (prev.IsSelected != curr.IsSelected)
                diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.IsSelected));
            if (prev.IsEnabled != curr.IsEnabled)
                diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.IsEnabled));
            if (prev.Expanded != curr.Expanded)
                diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.IsExpanded));
            if (prev.IsFocused != curr.IsFocused)
                diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.IsFocused));
            if (prev.NativeContent != curr.NativeContent)
                diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.NativeContent));
            if (!ReferenceEquals(prev.Text, curr.Text) && (prev.Text is not null || curr.Text is not null))
            {
                var (a, b) = (prev.Text, curr.Text);
                if (a?.Text != b?.Text)
                    diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.Text, prev));
                if (a?.Caret != b?.Caret || a?.SelectionStart != b?.SelectionStart || a?.SelectionEnd != b?.SelectionEnd)
                    diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.TextSelection, prev));
            }
            if (prev.BoundingRect != curr.BoundingRect && !(prev.IsOffscreen && curr.IsOffscreen))
                diff.PropertyChanged.Add(new PropertyChange(curr, AccessibilityProperties.BoundingRect));
        }

        private static void AddRecursive(TreeDiff diff, AccessibilityNode node, AccessibilityNode? parent)
        {
            diff.Added.Add(new AddedNode(node, parent));
            foreach (var child in node.Children)
                AddRecursive(diff, child, node);
        }

        private static void RemoveRecursive(TreeDiff diff, AccessibilityNode node, AccessibilityNode? parent)
        {
            foreach (var child in node.Children)
                RemoveRecursive(diff, child, node);
            diff.Removed.Add(new RemovedNode(node, parent));
        }
    }
}
