using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using TodoListSample;
using Xunit;

namespace TodoListSample.Tests;

// ReactiveRuntime has static state (pending-effect queue, flush guard),
// so reactive UI tests must not run in parallel with each other.
[Collection("Reactive")]
public class TodoAppTests
{
    // ──── Tree walkers ────
    //
    // Build() produces a well-known structure, so we can reach every interactive
    // element by index. Walking the tree this way keeps tests self-documenting:
    // each helper names a conceptual UI part (header counter, input field, row
    // checkbox, etc.) rather than sprinkling index literals through assertions.

    private static BoxElement Mount(TodoApp app) => (BoxElement)app.Mount().Children[0];

    private static TextElement CounterText(BoxElement root)
    {
        var header = (BoxElement)root.Children[0];
        var innerColumn = (BoxElement)header.Children[0];
        return (TextElement)innerColumn.Children[1];
    }

    private static TextInputElement InputField(BoxElement root)
        => (TextInputElement)((BoxElement)root.Children[1]).Children[0];

    private static ScrollViewElement ItemList(BoxElement root)
        => (ScrollViewElement)root.Children[2];

    private static BoxElement ItemRow(BoxElement root, int index)
        => (BoxElement)ItemList(root).Children[index];

    private static CheckboxElement RowCheckbox(BoxElement row) => (CheckboxElement)row.Children[0];
    private static TextElement RowLabel(BoxElement row) => (TextElement)row.Children[1];
    private static ButtonElement RowDeleteButton(BoxElement row) => (ButtonElement)row.Children[2];

    private static ButtonElement ClearButton(BoxElement root)
        => (ButtonElement)((BoxElement)root.Children[3]).Children[0];

    // ──── Tests ────

    [Fact]
    public void Mount_RendersSeedItems()
    {
        var root = Mount(new TodoApp());

        Assert.Equal(3, ItemList(root).Children.Count);
        Assert.Equal("Learn HultUI basics", RowLabel(ItemRow(root, 0)).Content);
        Assert.Equal("Build a todo app", RowLabel(ItemRow(root, 1)).Content);
        Assert.Equal("Add more features", RowLabel(ItemRow(root, 2)).Content);
    }

    [Fact]
    public void Mount_ShowsInitialCompletionCounter()
    {
        var root = Mount(new TodoApp());

        Assert.Equal("1/3 completed", CounterText(root).Content);
        Assert.True(RowCheckbox(ItemRow(root, 0)).IsChecked);
        Assert.False(RowCheckbox(ItemRow(root, 1)).IsChecked);
    }

    [Fact]
    public void CheckboxClick_UpdatesCompletionCounter()
    {
        var root = Mount(new TodoApp());
        var checkbox = RowCheckbox(ItemRow(root, 1));

        EventDispatcher.Dispatch(new ClickEvent(), checkbox);

        Assert.True(checkbox.IsChecked);
        // Counter is a TextElement rebuilt every time the reactive list mutates.
        Assert.Equal("2/3 completed", CounterText(root).Content);
    }

    [Fact]
    public void EnterInTextInput_AppendsNewItem()
    {
        var root = Mount(new TodoApp());
        var input = InputField(root);

        // The runtime normally pipes text edits to the element's TextChanged
        // callback. Invoking it directly mirrors that path without needing a
        // focus or a real keyboard event.
        input.TextChanged!.Invoke("Buy milk");
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Enter }, input);

        Assert.Equal(4, ItemList(root).Children.Count);
        Assert.Equal("Buy milk", RowLabel(ItemRow(root, 3)).Content);
        Assert.Equal("1/4 completed", CounterText(root).Content);
    }

    [Fact]
    public void EnterWithBlankInput_IsIgnored()
    {
        var root = Mount(new TodoApp());
        var input = InputField(root);

        input.TextChanged!.Invoke("   ");
        EventDispatcher.Dispatch(new KeyDownEvent { Key = KeyCode.Enter }, input);

        Assert.Equal(3, ItemList(root).Children.Count);
    }

    [Fact]
    public void DeleteButton_RemovesSingleRow()
    {
        var root = Mount(new TodoApp());

        EventDispatcher.Dispatch(new ClickEvent(), RowDeleteButton(ItemRow(root, 1)));

        Assert.Equal(2, ItemList(root).Children.Count);
        Assert.Equal("Learn HultUI basics", RowLabel(ItemRow(root, 0)).Content);
        Assert.Equal("Add more features", RowLabel(ItemRow(root, 1)).Content);
    }

    [Fact]
    public void ClearCompleted_DropsOnlyDoneItems()
    {
        var root = Mount(new TodoApp());

        EventDispatcher.Dispatch(new ClickEvent(), ClearButton(root));

        Assert.Equal(2, ItemList(root).Children.Count);
        Assert.Equal("Build a todo app", RowLabel(ItemRow(root, 0)).Content);
        Assert.Equal("Add more features", RowLabel(ItemRow(root, 1)).Content);
        Assert.Equal("0/2 completed", CounterText(root).Content);
    }
}

[CollectionDefinition("Reactive")]
public class ReactiveTestCollection;
