using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.RichText;
using Xunit;
using TextB = HultUI.Core.Builders.Text;

namespace HultUI.Headless.Tests;

/// <summary>
/// What typing in a rich text editor needs of what sits around it: a formatting bar says when it
/// no longer shows the editor (<see cref="RichTextToolbar.IsStale"/>), so an app rebuilds for the
/// keys that move a button and no others; and an editor in a row of a list whose items stand for
/// its rows builds no tree for its keys, whether a reader can reach it or not.
/// </summary>
public sealed class RichTextInListRowTests : IDisposable
{
    // The editor tells a reader its caret only while one listens.
    public RichTextInListRowTests() => AccessibilityClients.Force = true;

    public void Dispose() => AccessibilityClients.Force = null;

    private static (HeadlessSession Session, RichTextToolbar Bar, RichTextEditorElement Editor) Mounted(
        IReadOnlyList<RichTextCommand?>? commands = null)
    {
        var editorRef = new Ref<RichTextEditorElement>();
        var bar = new RichTextToolbar(editorRef, () => { });
        if (commands is not null) bar.Commands = commands;
        var session = HeadlessSession.Create(width: 700, height: 300);
        var built = bar.Build();   // before the editor is on screen, as a row is made
        var editor = RichTextEditor.New(RichDocument.Empty).Width(600).Height(200);
        editor.WithRef(editorRef);
        session.Window.SetRoot(Box.New().Width(700).Height(300).Children(built, editor));
        session.PumpFrame();
        session.Window.Click(editor);
        session.PumpFrames(2);
        return (session, bar, editor);
    }

    [Fact]
    public void ABarBuiltBeforeItsEditorIsStale_UntilItIsBuiltWithIt()
    {
        var (session, bar, _) = Mounted();
        using (session)
        {
            Assert.True(bar.IsStale);
            bar.Build();
            Assert.False(bar.IsStale);
        }
    }

    /// <summary>A character and a caret move leave the buttons as they are; a format taken up,
    /// the caret moving into or out of one, and Undo coming to life do not.</summary>
    [Fact]
    public void ABarIsStale_OnlyWhenABuildWouldMakeOtherButtons()
    {
        var (session, bar, editor) = Mounted([RichTextCommand.Bold, RichTextCommand.Italic, RichTextCommand.Heading1]);
        using (session)
        {
            bar.Build();
            var window = session.Window;
            void Key(KeyCode key, KeyModifiers mods = KeyModifiers.None) { window.PressKey(key, mods); session.PumpFrame(); }

            window.TypeText("plain ");
            session.PumpFrame();
            Assert.False(bar.IsStale);
            Key(KeyCode.Left);
            Assert.False(bar.IsStale);
            Key(KeyCode.Right);

            Key(KeyCode.B, KeyModifiers.Ctrl);
            Assert.True(bar.IsStale);          // the next character will be bold
            bar.Build();
            window.TypeText("bold");
            session.PumpFrame();
            Assert.False(bar.IsStale);

            editor.State.Select(RichSelection.Caret(0, 2));   // out of the bold word
            session.PumpFrame();
            Assert.True(bar.IsStale);
            bar.Build();
            Assert.False(bar.IsStale);
            editor.State.Select(RichSelection.Caret(0, 8));   // and back into it
            session.PumpFrame();
            Assert.True(bar.IsStale);
        }
    }

    [Fact]
    public void UndoComingToLife_MakesTheBarStale_AndAReadOnlyEditorDisablesItsButtons()
    {
        var (session, bar, editor) = Mounted([RichTextCommand.Undo, RichTextCommand.Bold]);
        using (session)
        {
            bar.Build();
            session.Window.TypeText("a");
            session.PumpFrame();
            Assert.True(bar.IsStale);
            bar.Build();
            session.Window.TypeText("b");
            session.PumpFrame();
            Assert.False(bar.IsStale);

            editor.ReadOnly = true;
            Assert.True(bar.IsStale);
        }
    }

    /// <summary>A key in an editor in a row builds no tree, where it built one a key. A list whose
    /// rows answer for themselves tells what is in them from the applied tree.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AKeyInAnEditorInAListRow_BuildsNoTree(bool composite)
    {
        using var session = HeadlessSession.Create(width: 700, height: 400);
        var bridge = session.Window.InstallAccessibilityBridge();
        var editorRef = new Ref<RichTextEditorElement>();
        var rows = Enumerable.Range(0, 20).ToList();
        var list = VirtualListElement<int>.New()
            .ItemHeight(24f)
            .Items(rows)
            .RowHeights((_, i) => i == 2 ? 120f : 24f)
            .RenderWith((i, _) => i == 2
                ? RichTextEditor.New(RichDocument.Empty).AccessibleName("Comment").Height(Dimension.Px(100)).WithRef(editorRef)
                : TextB.New($"Row {i}").FontSize(12f))
            .AccessibleItemLabel(i => $"Item {i}")
            .FlexGrow(1);
        list.RowsAreComposite = composite;
        session.Window.SetRoot(Flex.Column().Width(700).Height(400).Children(list));
        session.PumpFrames(3);
        session.Window.Click(editorRef.Current!);
        session.PumpFrames(3);
        var tree = bridge.CurrentTree;
        Assert.Equal(composite, HasNode(tree, "Comment"));

        int trees = session.Window.AccessibilityTreesBuilt;
        for (int i = 0; i < 5; i++)
        {
            session.Window.TypeText("x");
            session.PumpFrames(2);
        }
        session.Window.PressKey(KeyCode.Left);
        session.PumpFrames(2);

        Assert.Equal("xxxxx", editorRef.Current!.Document.ToPlainText());
        Assert.Equal(0, session.Window.AccessibilityTreesBuilt - trees);
        Assert.Equal(!composite, HasNode(bridge.CurrentTree, "Item 2"));
        var diff = AccessibilityTreeDiffer.Diff(bridge.CurrentTree, session.Window.Window.BuildAccessibilityTree());
        Assert.True(diff.IsEmpty, "the keys changed a tree nothing told");
    }

    private static bool HasNode(AccessibilityNode? node, string name) =>
        node is not null && (node.Name == name || node.Children.Any(c => HasNode(c, name)));
}
