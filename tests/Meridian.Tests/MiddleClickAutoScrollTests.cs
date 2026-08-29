using System;
using System.IO;
using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Middle-click autoscroll in Meridian's lists.
///
/// <para>The gesture belongs to the framework — every scroll view opts in by default — so
/// what is worth pinning here is that this app's surfaces really are those views, and that
/// nothing in it has given the middle button another meaning that the press would now have to
/// share. Nothing has: a search for a middle-button handler across the app finds none, which
/// is a fact worth a test rather than a memory.</para>
///
/// <para>The scroll runs from the frame loop rather than from pointer moves, so this holds the
/// pointer still and pumps — an implementation that only moved on input would sit there.</para>
/// </summary>
public class MiddleClickAutoScrollTests : IDisposable
{
    private const float DeadZone = 14f;   // matches Window.AutoScrollDeadZone

    private readonly string _root;

    public MiddleClickAutoScrollTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "meridian-autoscroll-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        // Enough requests that the sidebar's tree has somewhere to scroll.
        for (int i = 0; i < 200; i++)
            File.WriteAllText(Path.Combine(_root, $"req-{i:000}.req"), "GET http://127.0.0.1/ok\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void HoldingTheMiddleButtonBelowTheAnchorScrollsAList()
    {
        Hosted((session, app) =>
        {
            var list = Scrollable(session);
            var box = list.Layout.AbsoluteContentBox;
            float x = box.X + box.Width / 2f;
            float anchor = box.Y + 40f;
            Assert.Equal(0f, list.ScrollOffsetY);

            session.Window.MouseDown(x, anchor, MouseButton.Middle);
            session.Window.MoveMouse(x, anchor + DeadZone + 40f);
            PumpFor(session, 0.5);

            Assert.True(Scrollable(session).ScrollOffsetY > 0,
                "holding the middle button below the anchor did not scroll the list");

            session.Window.MouseUp(x, anchor + DeadZone + 40f, MouseButton.Middle);
        });
    }

    /// <summary>Parks in the dead zone without ending the gesture, resumes on leaving it, and
    /// stops for good on release — the two ways a Windows autoscroll ends.</summary>
    [Fact]
    public void ItStopsInTheDeadZoneAndAgainOnRelease()
    {
        Hosted((session, app) =>
        {
            var box = Scrollable(session).Layout.AbsoluteContentBox;
            float x = box.X + box.Width / 2f;
            float anchor = box.Y + 40f;

            session.Window.MouseDown(x, anchor, MouseButton.Middle);
            session.Window.MoveMouse(x, anchor + DeadZone + 40f);
            PumpFor(session, 0.3);
            float ran = Scrollable(session).ScrollOffsetY;
            Assert.True(ran > 0, "the first leg did not scroll");

            session.Window.MoveMouse(x, anchor + 2f);
            PumpFor(session, 0.4);
            Assert.Equal(ran, Scrollable(session).ScrollOffsetY, 1);

            session.Window.MoveMouse(x, anchor + DeadZone + 40f);
            PumpFor(session, 0.3);
            float resumed = Scrollable(session).ScrollOffsetY;
            Assert.True(resumed > ran, "leaving the dead zone again did not resume");

            session.Window.MouseUp(x, anchor + DeadZone + 40f, MouseButton.Middle);
            PumpFor(session, 0.5);
            Assert.Equal(resumed, Scrollable(session).ScrollOffsetY, 1);
        });
    }


    /// <summary>
    /// The gesture survives the view rebuilding under it.
    /// <para>Meridian's rebuild <i>patches</i> its lists rather than replacing them, so the
    /// captured target stays the right object and the failure that bit Junction and Vesper —
    /// a replaced element dropped from the tree without being marked detached — cannot happen
    /// here. Asserted rather than assumed: if this view ever starts replacing instead, the
    /// gesture has to keep working, and this is what would notice.</para>
    /// </summary>
    [Fact]
    public void ItKeepsScrollingAcrossARebuild()
    {
        Hosted((session, app) =>
        {
            var box = Scrollable(session).Layout.AbsoluteContentBox;
            float x = box.X + box.Width / 2f, anchor = box.Y + 40f;

            session.Window.MouseDown(x, anchor, MouseButton.Middle);
            session.Window.MoveMouse(x, anchor + DeadZone + 40f);
            PumpFor(session, 0.2);
            float early = Scrollable(session).ScrollOffsetY;
            Assert.True(early > 0, "it never started scrolling");

            typeof(MeridianApp).GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(app, null);
            PumpFor(session, 0.4);

            Assert.True(Scrollable(session).ScrollOffsetY > early,
                $"the scroll stopped at {Scrollable(session).ScrollOffsetY:0} after the rebuild "
                + $"(it was {early:0} before) — the gesture lost its target");

            session.Window.MouseUp(x, anchor + DeadZone + 40f, MouseButton.Middle);
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    /// <summary>Pumps <paramref name="seconds"/> of virtual time. PumpFrame advances the clock
    /// itself, so nothing else here may advance it — that would run the scroll at double speed
    /// while the frame count stayed the same.</summary>
    private static void PumpFor(HeadlessSession session, double seconds)
    {
        int frames = (int)Math.Round(seconds * 60);
        for (int i = 0; i < frames; i++) session.PumpFrame();
    }

    /// <summary>The first view on screen with somewhere to scroll. Re-read each time: a
    /// rebuild replaces it, and a reference from before one describes a detached tree.</summary>
    private static ScrollViewElement Scrollable(HeadlessSession session)
    {
        return Find(Root(session)) ?? throw new Xunit.Sdk.XunitException("nothing on screen can scroll");

        static ScrollViewElement? Find(Element? e)
        {
            if (e is null) return null;
            if (e is ScrollViewElement { CanAutoScroll: true } sv) return sv;
            foreach (var c in e.Children)
                if (Find(c) is { } hit) return hit;
            return null;
        }
    }

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window);

    private void Hosted(Action<HeadlessSession, MeridianApp> body)
    {
        using var session = HeadlessSession.Create(1440, 900);
        MeridianApp? app = null;
        try
        {
            app = new MeridianApp(session.Application, session.Window.Window);
            session.Application.SetRoot(app.Mount());
            app.OpenWorkspace(_root);
            session.PumpFrames(4);
            body(session, app);
        }
        finally { app?.Dispose(); }
    }
}
