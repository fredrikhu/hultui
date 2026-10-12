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
        /// <summary>Builds of the page, and calls of the toast's <c>onChanged</c>.</summary>
        public int Builds, Changes;
        /// <summary>The opacity the toast was drawn at, frame by frame, while one was mounted.</summary>
        public readonly List<float> Drawn = [];

        public Page() => Toasts = new ToastHost(() => { Changes++; MarkDirty(); });

        /// <summary>A rebuild for something else than the toast.</summary>
        public void Rebuild() => MarkDirty();

        public override Element Build()
        {
            Builds++;
            // A button pinned to the bottom edge, under where the strip will lie.
            var save = Box.New().Padding(new Thickness(8, 14)).OnClick(_ => Saves++)
                .Children(TextElement.New("Save"));
            save.AccessibleName = "Save";
            // Out of the flow, so the page lays out as it would without it.
            var frames = Canvas.New(_ => { if (Shown() is { } toast) Drawn.Add(toast.Opacity); })
                .Width(Dimension.Px(1)).Height(Dimension.Px(1));
            frames.Position.Type = PositionType.Absolute;
            var root = Flex.Column().Children(
                Box.New().FlexGrow(1),
                Flex.Row().JustifyContent(JustifyContent.FlexEnd).Children(save),
                frames);
            if (Toasts.Build() is { } strip) root.AddChild(strip);
            return root;
        }

        /// <summary>The toast mounted on the page, if any.</summary>
        public ToastElement? Shown()
        {
            var children = Mount().Children[0].Children;
            for (int i = 0; i < children.Count; i++)
                if (children[i].Key == "toast" && children[i].Children[0] is ToastElement toast) return toast;
            return null;
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

    // ── The fades are drawn on the toast, not built (plan 17, round 3, step 12) ──

    /// <summary>The fades called <c>onChanged</c> on every step, and every app hands it the
    /// <c>MarkDirty</c> of its whole window: Junction rebuilt itself 23 times for one toast. Now
    /// the owner hears of a toast twice, when it appears and when it goes, in the real loop.</summary>
    [Fact]
    public void AToast_RebuildsItsOwnerWhenItAppearsAndWhenItGoes_NotForItsFades()
    {
        var (session, page) = Mount();
        using var _ = session;
        int builds = page.Builds;

        page.Toasts.Flash("Saved");
        session.RunLoop(() => !page.Toasts.IsShowing, limit: TimeSpan.FromSeconds(5));

        Assert.Equal(2, page.Changes);
        Assert.Equal(2, page.Builds - builds);
        // And the fades were drawn: a frame for each step of 0.12 s in and 0.18 s out, at 60 Hz.
        Assert.InRange(page.Drawn.Count, 17, 22);
    }

    /// <summary>Each fade step writes the toast on screen: drawn rising to whole, held, then
    /// falling to nothing, frame by frame, with no rebuild between.</summary>
    [Fact]
    public void TheToastOnScreen_FadesIn_AndOut_FrameByFrame()
    {
        var (session, page) = Mount();
        using var _ = session;

        page.Toasts.Flash("Saved");
        session.RunLoop(() => !page.Toasts.IsShowing, limit: TimeSpan.FromSeconds(5));

        var drawn = page.Drawn;
        int top = drawn.IndexOf(1f);
        Assert.True(top > 2, $"drawn {string.Join(" ", drawn)}");
        for (int i = 1; i <= top; i++)
            Assert.True(drawn[i] > drawn[i - 1], $"fading in, frame {i}: {string.Join(" ", drawn)}");
        int falling = drawn.FindIndex(top, o => o < 1f);
        Assert.True(drawn.Count - falling >= 8, $"fading out: {string.Join(" ", drawn)}");
        for (int i = falling + 1; i < drawn.Count; i++)
            Assert.True(drawn[i] < drawn[i - 1], $"fading out, frame {i}: {string.Join(" ", drawn)}");
        Assert.True(drawn[^1] < 0.01f, $"the last frame with the toast drew it at {drawn[^1]}");
    }

    /// <summary>A rebuild in the middle of a fade (the owner's, for anything else) builds the toast
    /// at the fade's level, and the steps after it still reach the toast on screen.</summary>
    [Fact]
    public void ARebuildMidFade_KeepsTheLevel_AndTheFadeGoesOnOnScreen()
    {
        var (session, page) = Mount();
        using var _ = session;
        page.Toasts.Flash("Saved");
        session.PumpFrame();
        session.PumpFrame();
        float level = page.Toasts.Opacity;
        Assert.InRange(level, 0.05f, 0.95f);

        int builds = page.Builds;
        page.Rebuild();
        Assert.Equal(builds + 1, page.Builds);
        Assert.Equal(level, page.Shown()!.Opacity);

        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        Assert.Equal(1f, page.Shown()!.Opacity);
    }

    /// <summary>What a screen reader is told is what it was told when every fade frame rebuilt:
    /// the alert and its text added when the toast appears, its message announced once, both
    /// removed when it goes, and nothing in between.</summary>
    [Fact]
    public void AScreenReader_HearsTheToastAppearOnceAndGoOnce()
    {
        var (session, page) = Mount();
        using var _ = session;
        var bridge = session.Window.InstallAccessibilityBridge();
        session.PumpFrame();
        bridge.ClearDiffs();
        bridge.ClearAnnouncements();

        page.Toasts.Flash("Saved");
        session.RunLoop(() => !page.Toasts.IsShowing, limit: TimeSpan.FromSeconds(5));
        for (int i = 0; i < 6; i++) session.PumpFrame(TimeSpan.FromMilliseconds(50));   // trailing refreshes

        var told = bridge.Diffs.Where(d => !d.IsEmpty).Select(Told).ToList();
        Assert.Equal(["+Alert:Saved +Label:Saved", "-Label:Saved -Alert:Saved"], told);
        var announced = Assert.Single(bridge.Announcements);
        Assert.Equal("Saved", announced.Text);
        Assert.Equal(AnnouncementPoliteness.Polite, announced.Politeness);
        Assert.Equal(AccessibilityRole.Alert, announced.Source?.Role);
    }

    private static string Told(TreeDiff diff) => string.Join(" ",
        diff.Added.Select(a => $"+{a.Node.Role}:{a.Node.Name}")
            .Concat(diff.Removed.Select(r => $"-{r.Node.Role}:{r.Node.Name}"))
            .Concat(diff.PropertyChanged.Select(p => $"~{p.Node.Role}:{p.Node.Name}.{p.PropertyName}"))
            .Concat(diff.Reordered.Select(n => $"^{n.Role}:{n.Name}")));
}
