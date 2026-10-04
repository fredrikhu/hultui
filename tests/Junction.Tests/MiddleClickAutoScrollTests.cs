using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Middle-click autoscroll in Junction, and the thing it nearly broke.
///
/// <para>The gesture belongs to the framework, so what is worth pinning here is Junction's own
/// exposure to it. Its diff rows stage a line on click, and <c>OnClick</c> takes no button —
/// so before a click became the primary button's alone, a middle press over the diff staged
/// the line under the pointer. That is exactly the press someone now makes in order to scroll,
/// which is what turned a latent oddity into a live hazard.</para>
///
/// <para>The scroll runs from the frame loop rather than from pointer moves, so these hold the
/// pointer still and pump.</para>
/// </summary>
[Collection("JunctionDiff")]
public class MiddleClickAutoScrollTests
{
    private const float DeadZone = 14f;   // matches Window.AutoScrollDeadZone

    /// <summary>
    /// A middle press over a changed diff line does not stage it.
    /// <para>The left button is asserted alongside, so this cannot pass because the row
    /// stopped being stageable at all — which is how the first version of this probe fooled
    /// me, clicking context lines that were never stageable to begin with.</para>
    /// </summary>
    [Fact]
    public void AMiddlePressOverTheDiffDoesNotStageTheLineUnderIt()
    {
        int middle = StagedAfterClickingTheChangedLine(MouseButton.Middle);
        int left = StagedAfterClickingTheChangedLine(MouseButton.Left);

        Assert.Equal(1, left);      // the row really is stageable
        Assert.Equal(0, middle);    // and the middle button leaves it alone
    }

    /// <summary>Holding the middle button below the anchor scrolls the diff with no further
    /// input — the gesture's whole substance, and the reason this pumps without moving.</summary>
    [Fact]
    public void HoldingTheMiddleButtonBelowTheAnchorScrollsTheDiff()
    {
        WithTallDiff((session, app) =>
        {
            var list = Body(app);
            Assert.True(list.CanAutoScroll, "the fixture's diff has nothing to scroll");
            Assert.Equal(0f, list.ScrollOffsetY);

            var box = list.Layout.AbsoluteContentBox;
            float x = box.X + box.Width / 2f;
            float anchor = box.Y + 40f;

            session.Window.MouseDown(x, anchor, MouseButton.Middle);
            session.Window.MoveMouse(x, anchor + DeadZone + 40f);
            PumpFor(session, 0.5);

            Assert.True(Body(app).ScrollOffsetY > 0,
                "holding the middle button below the anchor did not scroll the diff");

            session.Window.MouseUp(x, anchor + DeadZone + 40f, MouseButton.Middle);
        });
    }

    /// <summary>
    /// The gesture survives the view rebuilding under it, keeping its place.
    /// <para>Junction's Changes view replaces the diff list part way through a gesture, and a
    /// replaced element is dropped from the tree without being marked detached — so the window
    /// went on scrolling a body nobody could see and the view sat at the top. This is the case
    /// that found it, so it is the one worth keeping.</para>
    /// </summary>
    [Fact]
    public void ItKeepsScrollingAcrossARebuildThatReplacesTheList()
    {
        WithTallDiff((session, app) =>
        {
            var box = Body(app).Layout.AbsoluteContentBox;
            float x = box.X + box.Width / 2f, anchor = box.Y + 40f;

            session.Window.MouseDown(x, anchor, MouseButton.Middle);
            session.Window.MoveMouse(x, anchor + DeadZone + 40f);

            var first = Body(app);
            PumpFor(session, 0.2);
            float early = Body(app).ScrollOffsetY;
            Assert.True(early > 0, "it never started scrolling");

            // Force the rebuild rather than waiting for one, so this is a statement about
            // surviving it rather than a race with whatever Junction happens to do.
            typeof(JunctionApp).GetMethod("Rerender", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(app, null);
            PumpFor(session, 0.4);

            Assert.True(Body(app).ScrollOffsetY > early,
                $"the scroll stopped at {Body(app).ScrollOffsetY:0} after the rebuild "
                + $"(it was {early:0} before) — the gesture lost its target");

            session.Window.MouseUp(x, anchor + DeadZone + 40f, MouseButton.Middle);
        });
    }

    /// <summary>Parks in the dead zone without ending the gesture, resumes on leaving it, and
    /// stops for good on release.</summary>
    [Fact]
    public void ItStopsInTheDeadZoneAndAgainOnRelease()
    {
        WithTallDiff((session, app) =>
        {
            var box = Body(app).Layout.AbsoluteContentBox;
            float x = box.X + box.Width / 2f;
            float anchor = box.Y + 40f;

            session.Window.MouseDown(x, anchor, MouseButton.Middle);
            session.Window.MoveMouse(x, anchor + DeadZone + 40f);
            PumpFor(session, 0.3);
            float ran = Body(app).ScrollOffsetY;
            Assert.True(ran > 0, "the first leg did not scroll");

            session.Window.MoveMouse(x, anchor + 2f);   // back inside the dead zone
            PumpFor(session, 0.4);
            Assert.Equal(ran, Body(app).ScrollOffsetY, 1);

            session.Window.MoveMouse(x, anchor + DeadZone + 40f);   // out again
            PumpFor(session, 0.3);
            float resumed = Body(app).ScrollOffsetY;
            Assert.True(resumed > ran, "leaving the dead zone again did not resume");

            session.Window.MouseUp(x, anchor + DeadZone + 40f, MouseButton.Middle);
            PumpFor(session, 0.5);
            Assert.Equal(resumed, Body(app).ScrollOffsetY, 1);
        });
    }

    /// <summary>
    /// Holding the middle button right of the anchor pans an unwrapped diff sideways, and
    /// holding it left of there brings it back.
    /// <para>An unwrapped diff scrolls sideways through <c>HorizontalPan</c> — a margin inside
    /// a clipping cell — because <c>VirtualListElement</c> pins its content width to the
    /// viewport and so can never hold a horizontal offset of its own. The framework's
    /// autoscroll used to move that offset, which had nowhere to go: the scrollbar under the
    /// diff worked and the gesture that looks exactly like using it did not. The list now
    /// names the pan as what scrolls it sideways, and the gesture goes there.</para>
    /// </summary>
    [Fact]
    public void HoldingTheMiddleButtonRightOfTheAnchorPansTheDiffSideways()
    {
        WithTallDiff((session, app) =>
        {
            var pan = Field<HultUI.Core.Builders.HorizontalPan>(app, "_diffPan");
            Assert.True(pan.CanPan, "the fixture's diff has nothing to pan");
            Assert.Equal(0f, pan.Offset);
            float codeAtRest = CodeLabelX(app);

            var box = Body(app).Layout.AbsoluteContentBox;
            float x = box.X + box.Width / 2f, y = box.Y + 40f;

            session.Window.MouseDown(x, y, MouseButton.Middle);
            session.Window.MoveMouse(x + DeadZone + 60f, y);
            PumpFor(session, 0.5);
            float panned = pan.Offset;
            Assert.True(panned > 0, "holding the middle button right of the anchor did not pan the diff");
            // The list's own offset stays where it always was: the pan is the whole story.
            Assert.Equal(0f, Body(app).ScrollOffsetX, 3);
            // And the code really moved: a panned cell shifts its text left by the offset.
            Assert.True(CodeLabelX(app) < codeAtRest - 1f,
                "the pan offset changed but the code on screen did not move");

            session.Window.MoveMouse(x - DeadZone - 60f, y);   // the other way
            PumpFor(session, 0.2);
            Assert.True(pan.Offset < panned, "holding it left of the anchor did not pan back");

            session.Window.MouseUp(x - DeadZone - 60f, y, MouseButton.Middle);
        });
    }

    /// <summary>
    /// A trackpad's sideways swipe, and Shift with the wheel, pan an unwrapped diff too.
    /// <para>Neither ever did. The app hung its pan on the list as a wheel handler, but the
    /// list's own handler ran first and stopped the event, and stopping also ends the
    /// element's remaining handlers — so the app's never ran. The list now sends the wheel's
    /// sideways motion to the pan it names, the same way the autoscroll's goes there, and
    /// Shift+wheel is sideways only rather than scrolling the diff down as well.</para>
    /// </summary>
    [Fact]
    public void ASidewaysSwipeAndShiftWheelPanTheDiff()
    {
        WithTallDiff((session, app) =>
        {
            var pan = Field<HultUI.Core.Builders.HorizontalPan>(app, "_diffPan");
            Assert.True(pan.CanPan, "the fixture's diff has nothing to pan");
            var box = Body(app).Layout.AbsoluteContentBox;
            float x = box.X + box.Width / 2f, y = box.Y + 40f;
            float top = Body(app).ScrollOffsetY;

            session.Window.Wheel(x, y, -1, 0);
            session.PumpFrame();
            float swiped = pan.Offset;
            Assert.True(swiped > 0, "a sideways swipe did not pan the diff");

            session.PumpFrame(TimeSpan.FromMilliseconds(400));   // a new gesture
            session.Window.Wheel(x, y, 0, -1, KeyModifiers.Shift);
            session.PumpFrame();
            Assert.True(pan.Offset > swiped, "Shift+wheel did not pan the diff");
            Assert.Equal(top, Body(app).ScrollOffsetY, 3);
        });
    }

    /// <summary>Wrapped, every line is visible in full and there is nothing sideways to reach:
    /// the gesture's horizontal half does nothing, and the vertical half still works.</summary>
    [Fact]
    public void SidewaysDoesNothingInAWrappedDiff()
    {
        WithTallDiff((session, app) =>
        {
            var pan = Field<HultUI.Core.Builders.HorizontalPan>(app, "_diffPan");
            Assert.False(pan.CanPan, "a wrapped diff should have nothing to pan");

            var box = Body(app).Layout.AbsoluteContentBox;
            float x = box.X + box.Width / 2f, y = box.Y + 40f;

            session.Window.MouseDown(x, y, MouseButton.Middle);
            session.Window.MoveMouse(x + DeadZone + 60f, y + DeadZone + 40f);
            PumpFor(session, 0.4);

            Assert.Equal(0f, pan.Offset);
            Assert.True(Body(app).ScrollOffsetY > 0, "the vertical half stopped working");

            session.Window.MouseUp(x + DeadZone + 60f, y + DeadZone + 40f, MouseButton.Middle);
        }, wrap: true);
    }

    /// <summary>The left edge of the first on-screen code label. A panned cell moves its text
    /// by a negative margin, so this is where the pan shows on screen.</summary>
    private static float CodeLabelX(JunctionApp app)
    {
        foreach (var row in Body(app).Children)
        {
            var label = LayoutProbe.Descendants<TextElement>(row,
                t => t is { Selectable: true, SelectionIndex: >= 0 }).FirstOrDefault();
            if (label is not null) return label.Layout.AbsoluteBorderBox.X;
        }
        throw new Xunit.Sdk.XunitException("no code label is on screen");
    }


    /// <summary>
    /// Every view with a list of its own scrolls under the gesture.
    /// <para>The diff is the surface the rest of this file is about, and it is not the only
    /// one: History, Branches and Tags each carry their own virtualized list, and the Changes
    /// view has a file list beside its diff. They are all the framework's scroll views, so
    /// this is really asking whether each view <i>has</i> one on screen with somewhere to go
    /// — which is the part a redesign can quietly change.</para>
    /// </summary>
    [Theory]
    [InlineData(KeyCode.Digit2, "History")]
    [InlineData(KeyCode.Digit4, "Changes")]
    [InlineData(KeyCode.Digit5, "Branches")]
    [InlineData(KeyCode.Digit6, "Tags")]
    public void EveryViewWithAListScrollsUnderTheGesture(KeyCode view, string name)
    {
        WithBusyRepo(view, (session, app) =>
        {
            var list = LargestScrollable(session)
                ?? throw new Xunit.Sdk.XunitException($"{name}: nothing on screen can scroll");

            var box = list.Layout.AbsoluteContentBox;
            float x = box.X + box.Width / 2f, anchor = box.Y + 40f;
            float before = list.ScrollOffsetY;

            session.Window.MouseDown(x, anchor, MouseButton.Middle);
            session.Window.MoveMouse(x, anchor + DeadZone + 40f);
            PumpFor(session, 0.5);

            var after = LargestScrollable(session)!;
            Assert.True(after.ScrollOffsetY > before,
                $"{name}: the list did not scroll under a held middle button");

            session.Window.MouseUp(x, anchor + DeadZone + 40f, MouseButton.Middle);
        });
    }

    /// <summary>The scrollable view with the most content — the view's own list rather than
    /// the sidebar, which also scrolls and is never what the pointer is over here.</summary>
    private static ScrollViewElement? LargestScrollable(HeadlessSession session)
    {
        ScrollViewElement? best = null;
        Walk(Root(session));
        return best;

        void Walk(Element? e)
        {
            if (e is null) return;
            if (e is ScrollViewElement { CanAutoScroll: true } sv
                && (best is null || sv.ContentHeight > best.ContentHeight))
                best = sv;
            foreach (var c in e.Children) Walk(c);
        }
    }

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window);

    /// <summary>A repository with enough commits, branches and tags that every view has a list
    /// worth scrolling, opened on <paramref name="view"/>.</summary>
    private static void WithBusyRepo(KeyCode view, System.Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        for (int i = 0; i < 60; i++)
        {
            repo.Write($"file-{i:00}.txt", $"content {i}\n");
            repo.Git("add", "-A");
            repo.Git("commit", "-m", $"commit number {i}");
            if (i % 2 == 0) repo.Git("branch", $"topic/branch-{i:00}");
            if (i % 2 == 0) repo.Git("tag", $"v0.0.{i}");
        }
        // Something uncommitted, so the Changes view has a list of its own.
        for (int i = 0; i < 40; i++) repo.Write($"pending-{i:00}.txt", "x\n");

        using var session = HeadlessSession.Create(1400, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
            {
                RepositoryPaths = [repo.Path],
                ActiveRepoPath = repo.Path,
                Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
                ActiveWorkspaceId = "w1",
            });
            session.Application.SetRoot(app.Mount());
            session.PumpFrames(3);
            session.Window.PressKey(view);
            session.PumpUntil(() => LargestScrollable(session) is not null, System.TimeSpan.FromSeconds(30));
            session.PumpFrames(6);
            body(session, app);
        }
        finally { app?.Dispose(); }
    }

    /// <summary>
    /// Scrolling a <i>wrapped</i> diff leaves every row still holding its own text.
    /// <para>The two features meet here and nowhere else. Wrapped, each row is a different
    /// height, measured in the build and handed to the virtualized list; the autoscroll then
    /// walks the file continuously, realizing rows the whole way. Every wrapping fault this
    /// month came down to a row measured at one width and laid out at another, and a gesture
    /// that scrolls through hundreds of rows is the thing most likely to find the next one —
    /// the static sweeps only ever looked at what happened to be on screen at rest.</para>
    /// </summary>
    [Fact]
    public void ScrollingAWrappedDiffKeepsEveryRowHoldingItsOwnText()
    {
        WithTallDiff((session, app) =>
        {
            var list = Body(app);
            Assert.True(list.CanAutoScroll, "the fixture's diff has nothing to scroll");

            var box = list.Layout.AbsoluteContentBox;
            float x = box.X + box.Width / 2f, anchor = box.Y + 40f;

            session.Window.MouseDown(x, anchor, MouseButton.Middle);
            session.Window.MoveMouse(x, anchor + DeadZone + 90f);   // well out, so it covers ground

            int checkedRows = 0;
            for (int step = 0; step < 40; step++)
            {
                PumpFor(session, 0.05);
                foreach (var row in Body(app).Children)
                {
                    var label = LayoutProbe.Descendants<TextElement>(row,
                        t => t is { Selectable: true, SelectionIndex: >= 0 }).FirstOrDefault();
                    if (label is null) continue;
                    // The label's own line boxes, not its laid-out box: the row forces its
                    // height on the label, so the box reads back as the row's either way.
                    float drawn = LayoutProbe.DrawnText(label).Height;
                    if (drawn <= 0) continue;
                    checkedRows++;
                    float rowHeight = row.Layout.AbsoluteBorderBox.Height;
                    Assert.True(drawn <= rowHeight + 0.5f,
                        $"while autoscrolling a wrapped diff, {drawn:0}px of text sat in a "
                        + $"{rowHeight:0}px row at offset {Body(app).ScrollOffsetY:0}");
                }
            }

            Assert.True(Body(app).ScrollOffsetY > 0, "the wrapped diff never scrolled");
            Assert.True(checkedRows > 50, $"only {checkedRows} row observations — this proves nothing");

            session.Window.MouseUp(x, anchor + DeadZone + 90f, MouseButton.Middle);
        }, wrap: true);
    }

    /// <summary>
    /// The Files view's body scrolls too.
    /// <para>Every other surface in this file is a virtualized list, and that is what the
    /// gesture used to look for. A file's text is a <c>TextAreaElement</c>, which scrolls its
    /// own wrapped rows instead of child elements — so the sidebar's tree scrolled under a
    /// held middle button and the file beside it did not.</para>
    /// </summary>
    [Fact]
    public void HoldingTheMiddleButtonScrollsAFilesViewBody()
    {
        WithLongFile((session, app) =>
        {
            var body = FileBody(app);
            Assert.True(body.CanAutoScroll, "the fixture's file fits the pane");

            var box = body.Layout.AbsoluteContentBox;
            float x = box.X + box.Width / 2f, anchor = box.Y + 40f;

            session.Window.MouseDown(x, anchor, MouseButton.Middle);
            session.Window.MoveMouse(x, anchor + DeadZone + 40f);
            PumpFor(session, 0.5);

            Assert.True(FileBody(app).ScrollOffsetY > 0,
                "holding the middle button over a file's text did not scroll it");

            session.Window.MouseUp(x, anchor + DeadZone + 40f, MouseButton.Middle);
        });
    }

    /// <summary>And the press leaves the reader's place in the text alone — a middle press on
    /// a body is a scroll, never a caret move or the start of a selection.</summary>
    [Fact]
    public void TheGestureDoesNotDisturbTheSelectionInAFile()
    {
        WithLongFile((session, app) =>
        {
            var body = FileBody(app);
            var box = body.Layout.AbsoluteContentBox;
            float x = box.X + box.Width / 2f, anchor = box.Y + 40f;

            session.Window.MouseDown(x, anchor, MouseButton.Middle);
            session.Window.MoveMouse(x, anchor + DeadZone + 40f);
            PumpFor(session, 0.4);
            session.Window.MouseUp(x, anchor + DeadZone + 40f, MouseButton.Middle);

            var after = FileBody(app);
            Assert.True(after.ScrollOffsetY > 0, "the gesture did not run, so this proves nothing");
            Assert.Equal("", after.SelectedText);
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private static TextAreaElement FileBody(JunctionApp app) =>
        ((HultUI.Core.Reactivity.Ref<TextAreaElement>)typeof(JunctionApp)
            .GetField("_fileBodyRef", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(app)!).Current
        ?? throw new Xunit.Sdk.XunitException("the Files view never mounted a body");

    /// <summary>Mounts the app on the Files view with a four-hundred-line file open, which is
    /// the state a reader is in when they reach for the middle button there.</summary>
    private static void WithLongFile(System.Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("sample.cs", string.Join("\n",
            Enumerable.Range(1, 400).Select(i => $"// line {i} of a file worth scrolling")) + "\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "add a sample file");

        using var session = HeadlessSession.Create(1400, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
            {
                RepositoryPaths = [repo.Path],
                ActiveRepoPath = repo.Path,
                Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
                ActiveWorkspaceId = "w1",
            });
            session.Application.SetRoot(app.Mount());
            session.PumpFrames(3);
            session.Window.PressKey(KeyCode.Digit7);   // Files
            typeof(JunctionApp).GetField("_filesSelPath", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(app, "sample.cs");
            session.PumpUntil(
                () => ((HultUI.Core.Reactivity.Ref<TextAreaElement>)typeof(JunctionApp)
                    .GetField("_fileBodyRef", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(app)!).Current is { Text.Length: > 1000 },
                System.TimeSpan.FromSeconds(30));
            session.PumpFrames(3);
            body(session, app);
        }
        finally { app?.Dispose(); }
    }


    /// <summary>Clicks the fixture's one changed line — the trailing <c>EDIT</c> — with the
    /// given button in a fresh app, and returns how many lines ended up staged.</summary>
    private static int StagedAfterClickingTheChangedLine(MouseButton button)
    {
        int staged = -1;
        DiffScrollTests.Hosted((session, app) =>
        {
            Assert.Equal(0, StagedCount(app));

            var row = Body(app).Children.FirstOrDefault(r =>
                LayoutProbe.Descendants<TextElement>(r, t => t.Content.Contains("EDIT")).Any())
                ?? throw new Xunit.Sdk.XunitException("the changed line is not on screen");

            var b = row.Layout.AbsoluteBorderBox;
            session.Window.Click(b.X + b.Width * 0.7f, b.Y + b.Height / 2f, button);
            session.PumpFrames(3);
            staged = StagedCount(app);
        });
        return staged;
    }

    private static int StagedCount(JunctionApp app) =>
        Field<System.Collections.IEnumerable>(app, "_stageSel").Cast<object>().Count();

    /// <summary>Pumps <paramref name="seconds"/> of virtual time. PumpFrame advances the clock
    /// itself, so nothing else here may — that would run the scroll at double speed while the
    /// frame count stayed the same.</summary>
    private static void PumpFor(HeadlessSession session, double seconds)
    {
        int frames = (int)System.Math.Round(seconds * 60);
        for (int i = 0; i < frames; i++) session.PumpFrame();
    }

    /// <summary>The diff body, re-read each time: a rebuild replaces it, and a reference from
    /// before one describes a detached tree.</summary>
    private static ScrollViewElement Body(JunctionApp app) =>
        BodyOrNull(app) ?? throw new Xunit.Sdk.XunitException("the diff body is not mounted");

    private static ScrollViewElement? BodyOrNull(JunctionApp app) =>
        Field<HultUI.Core.Reactivity.Ref<Element>>(app, "_diffBodyRef").Current as ScrollViewElement;

    /// <summary>
    /// Mounts the app on a repository whose working copy adds three hundred lines to a
    /// committed file, so the Changes view's diff is tall enough to scroll and wide enough
    /// that the sideways pan has somewhere to go.
    /// </summary>
    /// <remarks>Its own repository rather than the shared diff fixture: that one's working
    /// copy differs by a single line, so its diff is four rows tall with nothing to scroll —
    /// the gesture correctly does nothing there, which reads as a failure and is not one.</remarks>
    private static void WithTallDiff(System.Action<HeadlessSession, JunctionApp> body,
        bool wrap = false)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        // Every length between 45 and 200 characters, cycling. Uniform lines are no good for
        // the wrapped case: a measurement a few pixels too generous only shows on a line whose
        // length sits on the wrap boundary, and with every line the same length the whole file
        // misses it together — the first draft of this fixture did exactly that and its wrapped
        // test was green against the padding fault. They stay wide enough for the sideways pan.
        var words = "a line long enough that no plausible pane width contains all of it and then some".Split(' ');
        string LineOf(int length)
        {
            var sb = new System.Text.StringBuilder("        ");
            for (int w = 0; sb.Length < length; w++) sb.Append(words[w % words.Length]).Append(' ');
            return sb.ToString(0, length).TrimEnd();
        }
        repo.Write("wide.txt", "first\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "a starting point");
        repo.Write("wide.txt", "first\n" + string.Join("\n",
            Enumerable.Range(0, 300).Select(i => LineOf(45 + i * 7 % 156))) + "\n");

        using var session = HeadlessSession.Create(1400, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
            {
                RepositoryPaths = [repo.Path],
                ActiveRepoPath = repo.Path,
                Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
                ActiveWorkspaceId = "w1",
                DiffWrap = wrap,
            });
            session.Application.SetRoot(app.Mount());
            session.PumpFrames(3);
            session.Window.PressKey(KeyCode.Digit4);   // Changes
            session.PumpUntil(() => BodyOrNull(app) is { Children.Count: > 1 }, System.TimeSpan.FromSeconds(30));
            SettleUntilTheListStopsBeingReplaced(session, app);
            body(session, app);
        }
        finally { app?.Dispose(); }
    }

    /// <summary>
    /// Pumps until the diff list is the same element two frames running.
    /// <para>Opening the view leaves loaders landing for several frames, and each rebuild
    /// replaces the list and resets its scroll offset. A gesture started inside that window
    /// scrolls a body that is thrown away before the next frame, which looks exactly like the
    /// autoscroll not working — it took a per-frame trace showing a new element each time to
    /// see that it was the fixture and not the feature. Waiting for the tree to settle is the
    /// fixture's job.</para>
    /// </summary>
    private static void SettleUntilTheListStopsBeingReplaced(HeadlessSession session, JunctionApp app)
    {
        // Ten consecutive frames on the same element, not two. The repository is written to
        // moments before the app mounts, so the change watcher's events arrive late and
        // scattered: each one bumps the data generation, reloads, and replaces the list.
        // A two-frame quiet run is satisfied between two of them, which made this pass and
        // fail on alternate runs.
        const int Quiet = 10;
        var previous = BodyOrNull(app);
        int stable = 0;
        for (int i = 0; i < 600; i++)
        {
            session.PumpFrame();
            var current = BodyOrNull(app);
            stable = current is not null && ReferenceEquals(current, previous) ? stable + 1 : 0;
            previous = current;
            if (stable >= Quiet) return;
        }
        throw new Xunit.Sdk.XunitException("the diff list never stopped being rebuilt");
    }

    private static T Field<T>(JunctionApp app, string name) =>
        (T)typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;
}
