using System.IO;
using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Middle-click autoscroll in the file panes, and the one place it could have collided with
/// something this app already meant by the middle button.
///
/// <para>The gesture itself belongs to the framework — every scroll view opts in by default —
/// so what is worth pinning here is that Vesper's lists really are those views and that the
/// tab strip's middle-click still closes a tab. The strip is a plain row with no scrollable
/// ancestor, so nothing captures the press; that is a fact about this layout, not about the
/// framework, and it is the kind of thing a later redesign could quietly change.</para>
///
/// <para>The scroll runs from the frame loop rather than from pointer moves, so these hold the
/// pointer still and pump — an implementation that only moved on input would sit there.</para>
/// </summary>
public class MiddleClickAutoScrollTests
{
    private const float DeadZone = 14f;   // matches Window.AutoScrollDeadZone

    [Fact]
    public void HoldingTheMiddleButtonBelowTheAnchorScrollsTheFileList()
    {
        Hosted(fileCount: 400, (session, app, root) =>
        {
            var list = FileList(session);
            Assert.Equal(0f, list.ScrollOffsetY);

            var box = list.Layout.AbsoluteContentBox;
            float x = box.X + box.Width / 2f;
            float anchor = box.Y + 40f;

            session.Window.MouseDown(x, anchor, MouseButton.Middle);
            session.Window.MoveMouse(x, anchor + DeadZone + 40f);
            PumpFor(session, 0.5);

            Assert.True(FileList(session).ScrollOffsetY > 0,
                "holding the middle button below the anchor did not scroll the pane");

            session.Window.MouseUp(x, anchor + DeadZone + 40f, MouseButton.Middle);
        });
    }

    /// <summary>Back inside the dead zone it parks without ending the gesture, and the release
    /// stops it for good — the two ways a Windows autoscroll ends.</summary>
    [Fact]
    public void ItStopsInTheDeadZoneAndAgainOnRelease()
    {
        Hosted(fileCount: 400, (session, app, root) =>
        {
            var box = FileList(session).Layout.AbsoluteContentBox;
            float x = box.X + box.Width / 2f;
            float anchor = box.Y + 40f;

            session.Window.MouseDown(x, anchor, MouseButton.Middle);
            session.Window.MoveMouse(x, anchor + DeadZone + 40f);
            PumpFor(session, 0.3);
            float ran = FileList(session).ScrollOffsetY;
            Assert.True(ran > 0, "the first leg did not scroll");

            session.Window.MoveMouse(x, anchor + 2f);   // back inside the dead zone
            PumpFor(session, 0.4);
            Assert.Equal(ran, FileList(session).ScrollOffsetY, 1);

            session.Window.MoveMouse(x, anchor + DeadZone + 40f);   // out again: resumes
            PumpFor(session, 0.3);
            float resumed = FileList(session).ScrollOffsetY;
            Assert.True(resumed > ran, "leaving the dead zone again did not resume");

            session.Window.MouseUp(x, anchor + DeadZone + 40f, MouseButton.Middle);
            PumpFor(session, 0.5);
            Assert.Equal(resumed, FileList(session).ScrollOffsetY, 1);
        });
    }

    /// <summary>The middle button already closes a tab here, and it does so on the press. The
    /// strip is a plain row with nothing scrollable above it, so the press is not claimed by
    /// an autoscroll and the tab still closes.</summary>
    [Fact]
    public void MiddleClickingATabStillClosesIt()
    {
        Hosted(fileCount: 20, (session, app, root) =>
        {
            Invoke(app, "NewTab", 'L', null);
            session.PumpFrames(2);
            Assert.Equal(2, TabCount(app, 'L'));

            var chip = session.Window.FindByText(Path.GetFileName(root))
                ?? throw new Xunit.Sdk.XunitException("no tab chip on screen");
            var box = chip.Element.Layout.AbsoluteBorderBox;
            session.Window.Click(box.X + box.Width / 2f, box.Y + box.Height / 2f, MouseButton.Middle);
            session.PumpFrames(2);

            Assert.Equal(1, TabCount(app, 'L'));
        });
    }


    /// <summary>
    /// The gesture survives the pane rebuilding under it, keeping its place.
    /// <para>A rebuild here <i>replaces</i> the list rather than patching it, and a replaced
    /// element is dropped from the tree without being marked detached — so it goes on looking
    /// alive. The window used to keep scrolling that corpse while the pane sat still. The
    /// earlier tests in this file only passed because no rebuild happened to land inside their
    /// short gestures; this one forces it.</para>
    /// </summary>
    [Fact]
    public void ItKeepsScrollingAcrossARebuildThatReplacesTheList()
    {
        Hosted(fileCount: 400, (session, app, root) =>
        {
            var box = FileList(session).Layout.AbsoluteContentBox;
            float x = box.X + box.Width / 2f, anchor = box.Y + 40f;

            session.Window.MouseDown(x, anchor, MouseButton.Middle);
            session.Window.MoveMouse(x, anchor + DeadZone + 40f);
            PumpFor(session, 0.2);
            float early = FileList(session).ScrollOffsetY;
            Assert.True(early > 0, "it never started scrolling");

            var before = FileList(session);
            Invoke(app, "MarkDirty");
            PumpFor(session, 0.4);
            Assert.False(ReferenceEquals(before, FileList(session)),
                "the rebuild patched the list — this no longer covers the replacement path");

            Assert.True(FileList(session).ScrollOffsetY > early,
                $"the scroll stopped at {FileList(session).ScrollOffsetY:0} after the rebuild "
                + $"(it was {early:0} before) — the gesture lost its target");

            session.Window.MouseUp(x, anchor + DeadZone + 40f, MouseButton.Middle);
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    /// <summary>Pumps <paramref name="seconds"/> of virtual time. PumpFrame advances the clock
    /// itself, so nothing else here may advance it — that would run the scroll at double
    /// speed while the frame count stayed the same.</summary>
    private static void PumpFor(HeadlessSession session, double seconds)
    {
        int frames = (int)System.Math.Round(seconds * 60);
        for (int i = 0; i < frames; i++) session.PumpFrame();
    }

    /// <summary>The left pane's list. Re-read each time: a rebuild replaces it, and a
    /// reference from before one describes a detached tree.</summary>
    private static ScrollViewElement FileList(HeadlessSession session)
    {
        var found = Find(Root(session));
        return found ?? throw new Xunit.Sdk.XunitException("no file list on screen");

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

    private static int TabCount(CommanderApp app, char side) =>
        ((System.Collections.ICollection)typeof(CommanderApp)
            .GetMethod("Tabs", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, [side])!).Count;

    private static void Invoke(CommanderApp app, string method, params object?[] args) =>
        typeof(CommanderApp).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, args);

    private static void Hosted(int fileCount, System.Action<HeadlessSession, CommanderApp, string> body)
    {
        var root = Path.Combine(Path.GetTempPath(), "vesper-autoscroll-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        for (int i = 0; i < fileCount; i++)
            File.WriteAllText(Path.Combine(root, $"file-{i:0000}.txt"), "x");

        using var session = HeadlessSession.Create(1240, 820);
        CommanderApp? app = null;
        try
        {
            app = new CommanderApp(session.Application, session.Window.Window, root, root);
            session.Application.SetRoot(app.Mount());
            Invoke(app, "NavigateTo", 'L', root, null, false);
            Invoke(app, "NavigateTo", 'R', root, null, false);
            session.PumpUntil(() => app.ListingsReady, System.TimeSpan.FromSeconds(30));
            session.PumpFrames(3);
            body(session, app, root);
        }
        finally
        {
            app?.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
