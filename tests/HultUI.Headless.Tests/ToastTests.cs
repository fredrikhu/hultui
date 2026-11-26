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

    private static (HeadlessSession session, Page page) Mount(int width = 600, int height = 400)
    {
        var session = HeadlessSession.Create(width: width, height: height);
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

    // ── At a phone's width (plan 20) ──

    private const string Long = "The transcode would not play on this device, so the original file is playing instead, over your limit";

    /// <summary>The message is the first text in the pill; the verb, if any, the second.</summary>
    private static TextElement[] Texts(ToastElement toast) => toast.Children.OfType<TextElement>().ToArray();

    /// <summary>A toast was one line: at a phone's 360 DIP a long message ran to the window's
    /// edges and was cut off there. It wraps inside the window, with a margin at either side, and
    /// all of it shows: more than one line, fewer than the four after which it would end in "…".</summary>
    [Fact]
    public void ALongToast_InANarrowWindow_WrapsInsideIt_AndShowsAllOfIt()
    {
        var (session, page) = Mount(width: 360, height: 640);
        using var _ = session;
        page.Toasts.Flash("Saved");
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        float line = Texts(page.Shown()!)[0].Layout.AbsoluteBorderBox.Height;
        var shortPill = page.Shown()!.Layout.AbsoluteBorderBox;
        Assert.True(shortPill.Width < 200, $"a short toast is {shortPill.Width} wide: it should not fill the strip");

        page.Toasts.Flash(Long);
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        var toast = page.Shown()!;
        var pill = toast.Layout.AbsoluteBorderBox;
        Assert.True(pill.X >= 16 - 0.5f && pill.Right <= 360 - 16 + 0.5f, $"the pill spans {pill.X}..{pill.Right} in a window 360 wide");
        Assert.Equal(640 - 22, pill.Bottom, 1);
        // The room the pill wraps in, which the pill itself need not fill: its lines end at a word.
        var room = toast.Parent!.Layout.AbsoluteContentBox;
        Assert.True(room.X >= 16 - 0.5f && room.Right <= 360 - 16 + 0.5f, $"the pill's room spans {room.X}..{room.Right} in a window 360 wide");

        var text = Texts(toast)[0].Layout.AbsoluteBorderBox;
        Assert.True(text.Right <= pill.Right, $"the message ends at {text.Right}, past the pill's {pill.Right}");
        Assert.True(text.Height >= 2 * line - 1 && text.Height <= 3 * line + 1,
            $"the message is {text.Height} tall, a line {line}: it should wrap to two or three lines");
        // Still named by all of it.
        Assert.NotNull(session.Window.FindByRole(AccessibilityRole.Alert, Long));
    }

    /// <summary>With a verb, the message wraps and the verb stays whole, on one line, inside the pill.</summary>
    [Fact]
    public void ALongToast_WithAnAction_KeepsTheVerbWhole()
    {
        var (session, page) = Mount(width: 360, height: 640);
        using var _ = session;
        page.Toasts.Flash("Saved", action: ("Play from start", () => { }));
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        var verbAlone = Texts(page.Shown()!)[1].Layout.AbsoluteBorderBox;

        page.Toasts.Flash(Long, action: ("Play from start", () => { }));
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        var toast = page.Shown()!;
        var pill = toast.Layout.AbsoluteBorderBox;
        var verb = Texts(toast)[1].Layout.AbsoluteBorderBox;
        Assert.True(pill.X >= 16 - 0.5f && pill.Right <= 360 - 16 + 0.5f, $"the pill spans {pill.X}..{pill.Right} in a window 360 wide");
        Assert.Equal(verbAlone.Width, verb.Width, 1);
        Assert.Equal(verbAlone.Height, verb.Height, 1);
        Assert.True(verb.Right <= pill.Right, $"the verb ends at {verb.Right}, past the pill's {pill.Right}");
        Assert.True(Texts(toast)[0].Layout.AbsoluteBorderBox.Height > verb.Height + 1, "the message should wrap beside the verb");
    }

    // ── Swiped away (plan 20) ──

    private const string Middling = "The transcode won't play here, so this is the original";

    /// <summary>A finger on the toast's centre drags it <paramref name="by"/> sideways in steps of
    /// 10, a frame of 50 ms between (200 DIP a second: no flick), and stays down.</summary>
    private static void Pull(HeadlessSession session, ToastElement toast, float by)
    {
        var box = toast.Layout.AbsoluteBorderBox;
        float x = box.X + box.Width / 2, y = box.Y + box.Height / 2;
        session.Window.TouchDown(0, x, y);
        int steps = (int)MathF.Ceiling(Math.Abs(by) / 10f);
        for (int i = 1; i <= steps; i++)
        {
            session.PumpFrame(TimeSpan.FromMilliseconds(50));
            session.Window.TouchMove(0, x + by * i / steps, y);
        }
        session.PumpFrame();
    }

    private static float PulledBy(ToastElement toast) => toast.Transform?.TranslateX ?? 0f;

    /// <summary>A plain toast had no way to go before its time, and on a phone it lies over what
    /// is at the window's bottom (the player's buttons) for that long; and it still takes the taps
    /// that land on it. A finger pulls it sideways, it fades as it goes, and let go past
    /// <c>SwipeFraction</c> of its width it slides the rest of the way and is gone.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void ASwipe_PullsTheToastSideways_AndFarEnough_SendsItAway(int direction)
    {
        var (session, page) = Mount(width: 360, height: 640);
        using var _ = session;
        page.Toasts.Flash(Middling);
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        var toast = page.Shown()!;
        float width = toast.Layout.AbsoluteBorderBox.Width;

        Pull(session, toast, direction * width * 0.25f);
        Assert.Equal(direction * width * 0.25f, PulledBy(page.Shown()!), 1);
        Assert.InRange(page.Shown()!.Opacity, 0.7f, 0.8f);

        session.Window.TouchMove(0, toast.Layout.AbsoluteBorderBox.X + width / 2 + direction * width * 0.5f, toast.Layout.AbsoluteBorderBox.Y + 5);
        session.PumpFrame(TimeSpan.FromMilliseconds(50));
        session.Window.TouchUp(0);
        session.PumpFrame(TimeSpan.FromMilliseconds(100));
        Assert.True(Math.Abs(PulledBy(page.Shown()!)) > width * 0.5f, $"sliding away: at {PulledBy(page.Shown()!)}");
        session.PumpFrame(TimeSpan.FromMilliseconds(100));
        session.PumpFrame();
        Assert.False(page.Toasts.IsShowing);
        Assert.Null(session.Window.FindByRole(AccessibilityRole.Alert, Middling));
    }

    /// <summary>Let go short of that, it slides back; and it does not time out while the finger
    /// holds it, then has its whole time again from the release.</summary>
    [Fact]
    public void AShortSwipe_SlidesBack_AndTheToastHasItsWholeTimeAgain()
    {
        var (session, page) = Mount(width: 360, height: 640);
        using var _ = session;
        page.Toasts.Flash(Middling);
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        var toast = page.Shown()!;
        float width = toast.Layout.AbsoluteBorderBox.Width;

        Pull(session, toast, width * 0.2f);
        session.PumpFrame(TimeSpan.FromSeconds(2));     // past a plain toast's 1.7 s, held
        Assert.True(page.Toasts.IsShowing);
        Assert.Equal(1f, page.Toasts.Opacity);

        session.Window.TouchUp(0);
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        Assert.Equal(0f, PulledBy(page.Shown()!));
        Assert.Equal(1f, page.Shown()!.Opacity);
        session.PumpFrame(TimeSpan.FromMilliseconds(1400));   // ≈1.6 s from the release
        Assert.True(page.Toasts.IsShowing);
        session.PumpFrame(TimeSpan.FromMilliseconds(200));    // its time ran out, the fade began
        session.PumpFrame(TimeSpan.FromMilliseconds(300));
        session.PumpFrame();
        Assert.False(page.Toasts.IsShowing);
    }

    /// <summary>Caught as it fades out, it comes back whole while the finger holds it.</summary>
    [Fact]
    public void AToastCaughtAsItFadesOut_ComesBackWhole()
    {
        var (session, page) = Mount(width: 360, height: 640);
        using var _ = session;
        page.Toasts.Flash(Middling);
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        session.PumpFrame(TimeSpan.FromMilliseconds(1400));
        session.PumpFrame(TimeSpan.FromMilliseconds(200));    // the hold ran out, the fade began
        session.PumpFrame(TimeSpan.FromMilliseconds(90));     // mid-fade
        Assert.InRange(page.Toasts.Opacity, 0.05f, 0.95f);

        // Down and past the slop at once, before the fade can finish.
        var box = page.Shown()!.Layout.AbsoluteBorderBox;
        session.Window.TouchDown(0, box.X + box.Width / 2, box.Y + box.Height / 2);
        session.Window.TouchMove(0, box.X + box.Width / 2 + 20, box.Y + box.Height / 2);
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        session.PumpFrame(TimeSpan.FromSeconds(2));
        Assert.True(page.Toasts.IsShowing);
        Assert.Equal(1f, page.Toasts.Opacity);
        session.Window.TouchUp(0);
    }

    /// <summary>A flick sends it away from well short of the distance a slow pull needs.</summary>
    [Fact]
    public void AFlick_SendsItAway()
    {
        var (session, page) = Mount(width: 360, height: 640);
        using var _ = session;
        page.Toasts.Flash(Middling);
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        var box = page.Shown()!.Layout.AbsoluteBorderBox;
        var from = new Point(box.X + box.Width / 2, box.Y + box.Height / 2);

        session.Window.Swipe(from, new Point(from.X + box.Width * 0.25f, from.Y), TimeSpan.FromMilliseconds(60), steps: 4);
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        session.PumpFrame();
        Assert.False(page.Toasts.IsShowing);
    }

    /// <summary>With a verb, swiping it away is not choosing the verb.</summary>
    [Fact]
    public void ASwipedToastWithAnAction_DoesNotRunIt()
    {
        var (session, page) = Mount(width: 360, height: 640);
        using var _ = session;
        int ran = 0;
        page.Toasts.Flash("Pushed main", action: ("Create merge request", () => ran++));
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        var toast = page.Shown()!;

        Pull(session, toast, toast.Layout.AbsoluteBorderBox.Width * 0.6f);
        session.Window.TouchUp(0);
        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        session.PumpFrame();
        Assert.False(page.Toasts.IsShowing);
        Assert.Equal(0, ran);
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

    // ── A second Flash in the middle of a fade (plan 17, round 3, step 17) ──

    /// <summary>A second Flash cancelled the fade it met and kept its level, so a replacement
    /// that came while the first was fading in or out stayed half drawn for its whole hold, then
    /// faded out from there. It goes on from that level to whole (no blink), holds there for its
    /// own time, and fades out like any toast — on the toast on screen, not only the level.</summary>
    [Theory]
    [InlineData(false)]   // while the first fades in
    [InlineData(true)]    // while the first fades out
    public void ASecondFlash_MidFade_IsWholeForItsHold_ThenFadesOut(bool whileFadingOut)
    {
        var (session, page) = Mount();
        using var _ = session;
        page.Toasts.Flash("First");
        session.PumpFrame();
        if (whileFadingOut)
        {
            session.PumpFrame(TimeSpan.FromMilliseconds(200));
            Assert.Equal(1f, page.Toasts.Opacity);
            session.PumpFrame(TimeSpan.FromMilliseconds(1400));
            session.PumpFrame(TimeSpan.FromMilliseconds(200));    // the hold ran out, the fade began
            session.PumpFrame(TimeSpan.FromMilliseconds(100));    // mid-fade
        }
        else session.PumpFrame();                                 // mid-fade in
        float level = page.Toasts.Opacity;
        Assert.InRange(level, 0.05f, 0.95f);

        page.Toasts.Flash("Second");
        session.PumpFrame();
        Assert.Null(session.Window.FindByRole(AccessibilityRole.Alert, "First"));
        Assert.NotNull(session.Window.FindByRole(AccessibilityRole.Alert, "Second"));
        Assert.True(page.Shown()!.Opacity >= level, $"dipped from {level} to {page.Shown()!.Opacity}");

        session.PumpFrame(TimeSpan.FromMilliseconds(200));
        Assert.Equal(1f, page.Toasts.Opacity);
        Assert.Equal(1f, page.Shown()!.Opacity);
        session.PumpFrame(TimeSpan.FromMilliseconds(1300));   // ≈1.5 s into its 1.7 s hold
        Assert.True(page.Toasts.IsShowing);
        Assert.Equal(1f, page.Shown()!.Opacity);

        session.PumpFrame(TimeSpan.FromMilliseconds(300));    // its hold ran out, the fade began
        session.PumpFrame(TimeSpan.FromMilliseconds(90));     // mid-fade
        Assert.True(page.Toasts.IsShowing);
        Assert.InRange(page.Shown()!.Opacity, 0.01f, 0.99f);
        session.PumpFrame(TimeSpan.FromMilliseconds(300));    // gone
        session.PumpFrame();
        Assert.False(page.Toasts.IsShowing);
        Assert.Null(session.Window.FindByRole(AccessibilityRole.Alert, "Second"));
    }

    /// <summary>The same in the real loop, frame by frame: from the level the second Flash met
    /// in the first one's fade out, the toast on screen rises to whole without a dip, is drawn
    /// whole until its hold ends, then falls to nothing; the owner rebuilds only when it changes
    /// and when it goes.</summary>
    [Fact]
    public void ASecondFlash_WhileTheFirstFadesOut_RisesToWhole_ThenFadesOut_FrameByFrame()
    {
        var (session, page) = Mount();
        using var _ = session;
        page.Toasts.Flash("First");
        session.RunLoop(() => page.Toasts.Opacity == 1f, limit: TimeSpan.FromSeconds(1));
        session.RunLoop(() => page.Toasts.Opacity < 0.7f, limit: TimeSpan.FromSeconds(3));
        float level = page.Toasts.Opacity;
        Assert.InRange(level, 0.05f, 0.7f);
        int from = page.Drawn.Count, changes = page.Changes;

        page.Toasts.Flash("Second");
        session.RunLoop(() => !page.Toasts.IsShowing, limit: TimeSpan.FromSeconds(5));

        var drawn = page.Drawn.Skip(from).ToList();
        string all = $"from {level}: {string.Join(" ", drawn)}";
        int top = drawn.IndexOf(1f);
        Assert.True(top >= 1, all);
        Assert.True(drawn[0] >= level, all);
        for (int i = 1; i <= top; i++)
            Assert.True(drawn[i] > drawn[i - 1], $"rising, frame {i}: {all}");
        int falling = drawn.FindIndex(top, o => o < 1f);
        Assert.True(drawn.Count - falling >= 8, $"fading out: {all}");
        for (int i = falling + 1; i < drawn.Count; i++)
            Assert.True(drawn[i] < drawn[i - 1], $"fading out, frame {i}: {all}");
        Assert.True(drawn[^1] < 0.01f, all);
        Assert.Equal(changes + 2, page.Changes);
    }

    private static string Told(TreeDiff diff) => string.Join(" ",
        diff.Added.Select(a => $"+{a.Node.Role}:{a.Node.Name}")
            .Concat(diff.Removed.Select(r => $"-{r.Node.Role}:{r.Node.Name}"))
            .Concat(diff.PropertyChanged.Select(p => $"~{p.Node.Role}:{p.Node.Name}.{p.PropertyName}"))
            .Concat(diff.Reordered.Select(n => $"^{n.Role}:{n.Name}")));
}
