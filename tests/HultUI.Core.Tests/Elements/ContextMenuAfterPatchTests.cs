using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Platform;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// A text field's or a selectable label's own right-click menu stays the live element's when a
/// rebuild patches it (plan 17 round 18). The patch used to copy the build's menu, and the one a
/// fresh copy made for itself acted on that copy, out of the window: after one rebuild Cut, Copy
/// and Paste were off and Select All selected the copy. A menu the app sets still follows the build.
/// </summary>
public class ContextMenuAfterPatchTests
{
    public static TheoryData<string> Kinds => ["TextInput", "TextArea", "RichTextEditor", "SelectableLabel"];

    private static Element Make(string kind) => kind switch
    {
        "TextInput" => TextInputElement.New("Hello World"),
        "TextArea" => TextAreaElement.New("Hello World"),
        "RichTextEditor" => RichTextEditorElement.New(),
        _ => new TextElement { Content = "Hello World", Selectable = true },
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void AfterAPatch_TheOwnMenuActsOnTheLiveElement(string kind)
    {
        var live = Make(kind);
        var fresh = Make(kind);

        live.PatchFrom(fresh);
        live.PatchFrom(Make(kind));

        var menu = live.ContextMenuItems;
        Assert.NotNull(menu);
        Assert.Same(live, menu.Target);
    }

    [Fact]
    public void AfterAPatch_ALabelsSelectAllAndCopy_ActOnTheLiveLabel()
    {
        var live = new TextElement { Content = "Hello World", Selectable = true };
        var fresh = new TextElement { Content = "Hello World", Selectable = true };
        live.PatchFrom(fresh);
        var clipboard = new StubClipboard();
        live.Clipboard = clipboard;

        live.ContextMenuItems!().Single(i => i.Label == "Select All").Action!();
        Assert.Equal("Hello World", live.SelectedText);
        Assert.Equal("", fresh.SelectedText);

        var copy = live.ContextMenuItems!().Single(i => i.Label == "Copy");
        Assert.False(copy.IsDisabled);
        copy.Action!();
        Assert.Equal("Hello World", clipboard.Text);
    }

    [Fact]
    public void AfterAPatch_AFieldsCopyAndCut_ActOnTheLiveField()
    {
        var live = TextInputElement.New("Hello World");
        live.PatchFrom(TextInputElement.New("Hello World"));
        var clipboard = new StubClipboard();
        live.Clipboard = clipboard;

        live.ContextMenuItems!().Single(i => i.Label == "Select All").Action!();
        Assert.Equal("Hello World", live.SelectedText);
        var cut = live.ContextMenuItems!().Single(i => i.Label == "Cut");
        Assert.False(cut.IsDisabled);
        cut.Action!();
        Assert.Equal("Hello World", clipboard.Text);
        Assert.Equal("", live.Text);
    }

    [Fact]
    public void AMenuTheAppSets_FollowsTheBuild_AndTheOwnOneComesBackWhenItGoes()
    {
        IReadOnlyList<ContextMenuItem> appItems = [new ContextMenuItem { Label = "Mine" }];
        var live = TextInputElement.New("abc");

        live.PatchFrom(TextInputElement.New("abc").ContextMenu(appItems));
        Assert.Same(appItems, live.ContextMenuItems!());

        live.PatchFrom(TextInputElement.New("abc"));
        Assert.Same(live, live.ContextMenuItems!.Target);
        Assert.Contains(live.ContextMenuItems!(), i => i.Label == "Paste");
    }

    [Fact]
    public void AnAppsNull_TakesTheOwnMenuAway_AndFollowsTheBuild()
    {
        var off = TextInputElement.New("abc");
        off.ContextMenuItems = null;
        Assert.Null(off.ContextMenuItems);

        var live = TextInputElement.New("abc");
        live.PatchFrom(off);
        Assert.Null(live.ContextMenuItems);
    }

    [Fact]
    public void ALabelMadeSelectable_AfterTheAppGaveItAMenu_ServesItsOwn_AsBefore()
    {
        var label = new TextElement { Content = "words" };
        label.ContextMenu([new ContextMenuItem { Label = "Mine" }]);
        Assert.Equal("Mine", label.ContextMenuItems!().Single().Label);

        label.Selectable = true;
        Assert.Same(label, label.ContextMenuItems!.Target);
    }

    [Fact]
    public void ABoxHasNoMenu_UntilTheBuildGivesItOne_AndLosesItWithTheBuild()
    {
        var live = BoxElement.New();
        Assert.Null(live.ContextMenuItems);
        Assert.Null(new TextElement { Content = "plain" }.ContextMenuItems);

        IReadOnlyList<ContextMenuItem> items = [new ContextMenuItem { Label = "Open" }];
        live.PatchFrom(BoxElement.New().ContextMenu(items));
        Assert.Same(items, live.ContextMenuItems!());

        live.PatchFrom(BoxElement.New());
        Assert.Null(live.ContextMenuItems);
    }

    private sealed class StubClipboard : IClipboard
    {
        public string? Text { get; private set; }
        public string? GetText() => Text;
        public void SetText(string text) => Text = text;
        public bool HasFormat(string mimeType) => false;
        public byte[]? GetData(string mimeType) => null;
        public void SetData(IReadOnlyDictionary<string, byte[]> data) => throw new NotSupportedException();
        public void Clear() => Text = null;
    }
}
