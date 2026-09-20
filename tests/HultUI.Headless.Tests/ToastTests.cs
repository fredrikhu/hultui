using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

public class ToastTests
{
    private sealed class Page : Component
    {
        public readonly ToastHost Toasts;
        public int Saves;
        public Page() => Toasts = new ToastHost(MarkDirty);

        public override Element Build()
        {
            // A button pinned to the bottom edge, under where the strip will lie.
            var save = Box.New().Padding(new Thickness(8, 14)).OnClick(_ => Saves++)
                .Children(TextElement.New("Save"));
            save.AccessibleName = "Save";
            var root = Flex.Column().Children(
                Box.New().FlexGrow(1),
                Flex.Row().JustifyContent(JustifyContent.FlexEnd).Children(save));
            if (Toasts.Build() is { } strip) root.AddChild(strip);
            return root;
        }
    }

    private static (HeadlessSession session, Page page) Mount()
    {
        var session = HeadlessSession.Create(width: 600, height: 400);
        var page = new Page();
        session.Window.SetRoot(page.Mount());
        session.PumpFrame();
        return (session, page);
    }

    [Fact]
    public void Flash_ShowsAnAlert_ThatFadesIn_AndGoesAfterItsTime()
    {
        var (session, page) = Mount();
        using var _ = session;

        page.Toasts.Flash("Saved");
        session.PumpFrame();
        var toast = session.Window.FindByRole(AccessibilityRole.Alert, "Saved");
        Assert.NotNull(toast);
        Assert.InRange(page.Toasts.Opacity, 0.01f, 0.99f);

        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        Assert.Equal(1f, page.Toasts.Opacity);
        // Bottom-centre.
        var box = toast!.Element.Layout.AbsoluteBorderBox;
        Assert.InRange(box.X + box.Width / 2, 290, 310);
        Assert.Equal(400 - 22, box.Bottom, 1);

        session.PumpFrame(TimeSpan.FromMilliseconds(1400));   // t≈1.6 s: inside the 1.7 s hold
        Assert.True(page.Toasts.IsShowing);
        Assert.Equal(1f, page.Toasts.Opacity);
        session.PumpFrame(TimeSpan.FromMilliseconds(200));    // t≈1.8 s: the hold ran out, the fade began
        session.PumpFrame(TimeSpan.FromMilliseconds(100));    // mid-fade
        Assert.True(page.Toasts.IsShowing);
        Assert.InRange(page.Toasts.Opacity, 0.01f, 0.99f);
        session.PumpFrame(TimeSpan.FromMilliseconds(300));    // gone
        session.PumpFrame();
        Assert.False(page.Toasts.IsShowing);
        Assert.Null(session.Window.FindByRole(AccessibilityRole.Alert, "Saved"));
    }

    [Fact]
    public void ASecondFlash_Replaces_AndRestartsTheClock_WithoutBlinking()
    {
        var (session, page) = Mount();
        using var _ = session;
        page.Toasts.Flash("First");
        session.PumpFrame(TimeSpan.FromMilliseconds(1500));
        Assert.Equal(1f, page.Toasts.Opacity);

        page.Toasts.Flash("Second");
        session.PumpFrame();
        Assert.Null(session.Window.FindByRole(AccessibilityRole.Alert, "First"));
        Assert.NotNull(session.Window.FindByRole(AccessibilityRole.Alert, "Second"));
        Assert.Equal(1f, page.Toasts.Opacity);

        session.PumpFrame(TimeSpan.FromMilliseconds(1500));   // the first clock would have run out
        Assert.True(page.Toasts.IsShowing);
        session.PumpFrame(TimeSpan.FromMilliseconds(600));    // the second runs out; fading
        session.PumpFrame(TimeSpan.FromMilliseconds(300));    // faded
        session.PumpFrame();
        Assert.False(page.Toasts.IsShowing);
    }

    [Fact]
    public void WithAnAction_ItIsAButton_ThatRunsAndDismisses()
    {
        var (session, page) = Mount();
        using var _ = session;
        int ran = 0;
        page.Toasts.Flash("Pushed main", action: ("Create merge request", () => ran++));
        session.PumpFrame(TimeSpan.FromMilliseconds(200));

        var toast = session.Window.FindByRole(AccessibilityRole.Button, "Create merge request");
        Assert.NotNull(toast);
        session.PumpFrame(TimeSpan.FromMilliseconds(2000));   // past a plain toast's time, inside an action's
        Assert.True(page.Toasts.IsShowing);

        toast!.Click();
        session.PumpFrame();
        Assert.Equal(1, ran);
        Assert.False(page.Toasts.IsShowing);
    }

    [Fact]
    public void Dismiss_TakesItDownNow()
    {
        var (session, page) = Mount();
        using var _ = session;
        page.Toasts.Flash("Copied");
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        page.Toasts.Dismiss();
        session.PumpFrame();
        Assert.False(page.Toasts.IsShowing);
        Assert.Null(session.Window.FindByRole(AccessibilityRole.Alert, "Copied"));
    }

    [Fact]
    public void TheStrip_LetsClicksThrough_ToWhatIsUnderIt()
    {
        var (session, page) = Mount();
        using var _ = session;
        page.Toasts.Flash("Saved");
        session.PumpFrame(TimeSpan.FromMilliseconds(200));

        var save = session.Window.FindByRole(AccessibilityRole.Button, "Save")!;
        var strip = session.Window.FindByRole(AccessibilityRole.Alert, "Saved")!.Element.Parent!;
        var saveBox = save.Element.Layout.AbsoluteBorderBox;
        Assert.True(strip.Layout.AbsoluteBorderBox.Contains(new Point(saveBox.X + 5, saveBox.Y + 5)),
            "the strip should lie over the button for this to prove anything");

        save.Click();
        Assert.Equal(1, page.Saves);
    }
}
