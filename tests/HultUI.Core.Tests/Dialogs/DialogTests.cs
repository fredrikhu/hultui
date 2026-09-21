using System.Threading.Tasks;
using HultUI.Core.Builders;
using HultUI.Core.Dialogs;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Dialogs;

public class DialogTests
{
    [Fact]
    public async Task Show_ShowsOverlay_AndResolvesResultOnClose()
    {
        var host = new FakeOverlayHost();
        IDialogController<int>? controller = null;

        var task = Dialog.Show<int>(host, ctrl =>
        {
            controller = ctrl;
            return Box.New();
        });

        Assert.NotNull(host.Shown);        // overlay was presented
        Assert.False(task.IsCompleted);     // stays open until closed
        Assert.NotNull(controller);

        controller!.Close(42);

        Assert.True(host.Closed);           // overlay torn down
        Assert.Equal(42, await task);       // awaited result flows through
    }

    [Fact]
    public async Task Close_IsIdempotent()
    {
        var host = new FakeOverlayHost();
        IDialogController<int>? controller = null;
        var task = Dialog.Show<int>(host, ctrl => { controller = ctrl; return Box.New(); });

        controller!.Close(1);
        controller!.Close(2); // second close is ignored

        Assert.Equal(1, await task);
    }

    [Fact]
    public void Show_WindowPresentation_ThrowsNotSupported()
    {
        var host = new FakeOverlayHost();
        Assert.Throws<System.NotSupportedException>(() =>
        {
            _ = Dialog.Show<int>(host, _ => Box.New(), new DialogOptions { Presentation = DialogPresentation.Window });
        });
    }

    [Fact]
    public void Confirm_UsesTheFrameworkButtons_SecondaryToCancelAndPrimaryToConfirm()
    {
        var host = new FakeOverlayHost();
        _ = Dialog.Confirm(host, "Delete?", "This cannot be undone.");

        var buttons = Descendants(host.Shown!).OfType<ButtonElement>().ToList();
        Assert.Equal(2, buttons.Count);
        Assert.Equal(ButtonVariant.Secondary, buttons[0].Variant);
        Assert.Equal("Cancel", buttons[0].Label);
        Assert.Equal(ButtonVariant.Primary, buttons[1].Variant);
        Assert.Equal("OK", buttons[1].Label);
    }

    [Fact]
    public async Task Confirm_Destructive_MakesTheConfirmingButtonDanger_AndItCloses()
    {
        var host = new FakeOverlayHost();
        var task = Dialog.Confirm(host, "Delete?", "Gone for good.", destructive: true);

        var confirm = Descendants(host.Shown!).OfType<ButtonElement>().Last();
        Assert.Equal(ButtonVariant.Danger, confirm.Variant);

        HultUI.Core.Events.EventDispatcher.Dispatch(new HultUI.Core.Events.ClickEvent(), confirm);
        Assert.True(host.Closed);
        Assert.True(await task);
    }

    private static IEnumerable<Element> Descendants(Element e)
    {
        foreach (var c in e.Children)
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }

    private sealed class FakeOverlayHost : IOverlayHost
    {
        public Element? Shown { get; private set; }
        public bool Closed { get; private set; }

        public OverlayHandle ShowOverlay(Element content, float x, float y, bool dismissOnOutsideClick = true, bool hitTestable = true, Action? onDismissed = null)
        {
            Shown = content;
            return new OverlayHandle { Id = 1 };
        }

        public void CloseOverlay(OverlayHandle handle) => Closed = true;
        public void UpdateOverlayPosition(OverlayHandle handle, float x, float y) { }
        public Theme? ActiveTheme => Theme.Light;
    }
}
