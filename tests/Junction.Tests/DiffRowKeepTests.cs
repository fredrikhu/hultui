using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Headless;
using Junction.Theme;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The diff's rows are flattened once and kept: a build that changes nothing they were made
/// from — a view-only build beside the diff, a toast frame — flattens nothing and makes no row,
/// and each thing the rows do depend on makes them again.
///
/// <para>Every build used to flatten the diff anew (a record per line, a word diff per changed
/// pair, merged spans) and the list made every row on screen again from the new records:
/// Changes, typing beside a 184-row diff, 744 KB of Build and 441 KB of rows a keystroke. The
/// flattened rows now live until the diff, the layout or the scheme is another object, their
/// colours are the highlight's when they are drawn (<see cref="DiffRowListTests"/>), and the list
/// is on item rows (<c>.RenderWith((row, selected) => …)</c>), told by
/// <c>InvalidateRows</c> when anything else a row shows moves.</para>
/// </summary>
[Collection("JunctionDiff")]
public class DiffRowKeepTests
{
    [Fact]
    public void AViewOnlyBuildBesideTheDiff_FlattensNothing_AndMakesNoRow()
    {
        Hosted((session, app) =>
        {
            var rows = Rows(app);
            int flattens = app.DiffFlattens, made = app.DiffRowsMade, builds = app.Builds;

            for (int i = 0; i < 6; i++)
            {
                Invoke(app, "RerenderView");
                session.PumpFrame();
            }

            Assert.True(app.Builds - builds >= 6, $"{app.Builds - builds} builds for six rebuilds");
            Assert.Equal(flattens, app.DiffFlattens);
            Assert.Equal(made, app.DiffRowsMade);
            Assert.Equal(rows, Rows(app));   // the same row elements, in the same places
        });
    }

    [Fact]
    public void SwitchingToSplit_FlattensAgain()
    {
        Hosted((session, app) =>
        {
            int flattens = app.DiffFlattens, made = app.DiffRowsMade;

            Invoke(app, "SetDiffSplit", true);
            session.PumpFrames(2);

            Assert.Equal(flattens + 1, app.DiffFlattens);
            Assert.True(app.DiffRowsMade - made >= Rows(app).Count, "the rows were not made again");
            // A changed pair is one row now: the old line on the left, the new on the right.
            var pair = Rows(app).First(r => Texts(r).Any(t => t.StartsWith("    int value4 = 4;", StringComparison.Ordinal)));
            Assert.Contains(Texts(pair), t => t.StartsWith("    int value4 = 40;", StringComparison.Ordinal));
        });
    }

    /// <summary>The first build asks for the colours and draws plain; the tokenize landing is a
    /// new highlight, and the rows on screen are made again with it. The rows of the list are not:
    /// they hold no colour.</summary>
    [Fact]
    public void TheHighlightLanding_MakesTheRowsOnScreenAgainInColour()
    {
        Hosted((session, app) =>
        {
            int flattens = app.DiffFlattens, made = app.DiffRowsMade;
            session.PumpUntil(() => Coloured(app), TimeSpan.FromSeconds(20));
            Assert.Equal(flattens, app.DiffFlattens);
            Assert.True(app.DiffRowsMade > made, "the rows were not made again");
        }, waitForColours: false);
    }

    /// <summary>The word diff's tint is the scheme's, baked into the flattened spans.</summary>
    [Fact]
    public void ASchemeSwitch_FlattensAgainWithItsWordTint()
    {
        Hosted((session, app) =>
        {
            try
            {
                var before = Nocturne.DiffAddHl;
                Assert.Contains(before, Backgrounds(app));

                Nocturne.Use("gruvbox-dark");
                Rebuild(app);
                session.PumpFrames(3);

                Assert.NotEqual(before, Nocturne.DiffAddHl);
                Assert.Contains(Nocturne.DiffAddHl, Backgrounds(app));
                Assert.DoesNotContain(before, Backgrounds(app));
            }
            finally
            {
                Nocturne.Use("nocturne");
            }
        });
    }

    [Fact]
    public void ANewDiff_FlattensAgain()
    {
        Hosted((session, app) =>
        {
            File.AppendAllText(Path.Combine(Get<string>(app, "_activeRepoPath"), "src", "Notes.cs"), "// appended after the class\n");
            Invoke(app, "RefreshAll");
            session.PumpUntil(() => Rows(app).Any(r => Texts(r).Contains("// appended after the class")),
                TimeSpan.FromSeconds(20));
        });
    }

    /// <summary>A commit's colours are keyed by its content, so reloading its diff (here with
    /// whitespace ignored) keeps the same highlight: only the diff is a new object, and that
    /// alone has to flatten again.</summary>
    [Fact]
    public void AReloadedCommitDiff_FlattensAgain_WithTheSameHighlight()
    {
        Hosted((session, app) =>
        {
            int Value20Rows() => Rows(app).Count(r => Texts(r).Any(t => t.TrimStart().StartsWith("int value20 = 20;", StringComparison.Ordinal)));
            Assert.Equal(2, Value20Rows());   // the re-indented line: removed and added
            int flattens = app.DiffFlattens;

            Invoke(app, "ToggleIgnoreWhitespace");
            session.PumpUntil(() => Value20Rows() == 0, TimeSpan.FromSeconds(20));   // no change left: no hunk
            Assert.True(app.DiffFlattens > flattens);
        }, commitView: true);
    }

    /// <summary>A line picked for staging draws ticked and tinted: state the list does not see.</summary>
    [Fact]
    public void PickingALineToStage_MakesTheRowsAgain()
    {
        Hosted((session, app) =>
        {
            var row = RowWith(app, "    int value4 = 40;");
            int flattens = app.DiffFlattens;

            session.Window.Click(row);
            session.PumpFrames(2);

            Assert.Equal(flattens, app.DiffFlattens);
            Assert.Equal(Nocturne.Alpha(Nocturne.Accent, 0.22), ((BoxElement)RowWith(app, "    int value4 = 40;")).BackgroundColor);
        });
    }

    [Fact]
    public void FindTintsTheKeptRows()
    {
        Hosted((session, app) =>
        {
            int flattens = app.DiffFlattens;
            var search = Get<HultUI.Core.Builders.FindBar>(app, "_diffFind").Search;

            search.SetQuery("gamma");
            session.PumpFrames(2);

            Assert.Equal(flattens, app.DiffFlattens);
            Assert.True(search.HasHits);
            Assert.Contains(Nocturne.FindCurrentBg, Backgrounds(app));
            Assert.Contains(Nocturne.FindHitBg, Backgrounds(app));
        });
    }

    /// <summary>The code face, size and tab width are settings the rows draw with.</summary>
    [Fact]
    public void ATabWidthOrCodeFaceChange_MakesTheRowsAgain()
    {
        Hosted((session, app) =>
        {
            var settings = Get<JunctionSettingsData>(app, "_settings");
            int width = settings.Appearance.TabWidth;
            Assert.Contains(new string(' ', width) + "int tabbed = 2;", AllTexts(app));

            settings.Appearance.TabWidth = width + 3;
            Invoke(app, "InvalidateCodeMetrics");
            Rebuild(app);
            session.PumpFrames(2);
            Assert.Contains(new string(' ', width + 3) + "int tabbed = 2;", AllTexts(app));

            // Not the default face (Consolas on Windows), or nothing changes.
            Invoke(app, "UseCodeFont", "Courier New");
            session.PumpFrames(2);
            var label = Rows(app).SelectMany(r => Descendants(r)).OfType<TextElement>()
                .First(t => t.Content.StartsWith("    int value4 = 40;", StringComparison.Ordinal));
            Assert.Equal("Courier New", label.Inherited.FontFamily);
        });
    }

    /// <summary>The staging checkboxes are glyphs cut at the display scale; a move to a screen
    /// of another scale retires them (and the next change frees them): the rows are made again.</summary>
    [Fact]
    public void ADisplayScaleChange_MakesTheRowsAgain()
    {
        Hosted((session, app) =>
        {
            try
            {
                int onScreen = Rows(app).Count;
                int made = app.DiffRowsMade;

                session.Window.SetScale(1.5f);
                session.PumpFrames(3);

                Assert.True(app.DiffRowsMade - made >= onScreen, $"{app.DiffRowsMade - made} rows made of {onScreen}");
            }
            finally
            {
                session.Window.SetScale(1f);
                session.PumpFrames(2);
            }
        });
    }

    /// <summary>Unwrapped and with no review every row is one line high at any width, so a width
    /// change keeps the rows on screen: a window resize, and the split between the file list and
    /// the diff dragged (plan 17 round 10: a drag move made every row on screen again, 159.7 KB).
    /// Wrapped, the rows are measured at the width, and a change makes them again.</summary>
    [Fact]
    public void AWidthChange_KeepsTheRows_UnlessTheyWrap()
    {
        Hosted((session, app) =>
        {
            var rows = Rows(app);
            int made = app.DiffRowsMade;
            foreach (var w in new[] { 1396, 1392, 1300, 1180, 1396, 1400 })
                Resize(session, app, w);
            Assert.True(app.DiffRowsMade == made, $"{app.DiffRowsMade - made} rows made over six widths");
            Assert.Equal(rows, Rows(app));

            var handle = Descendants(RootOf(session)).OfType<SplitterHandleElement>().First(h => h.Layout.AbsoluteBorderBox.Height > 0);
            var box = handle.ScreenBorderBox;
            float x = box.X + box.Width / 2, y = box.Y + box.Height / 2;
            int builds = app.Builds;
            session.Window.MouseDown(x, y);
            session.PumpFrames(2);
            foreach (var d in new[] { -4f, -40f, 60f, 4f })
            {
                x += d;
                session.Window.MoveMouse(x, y);
                session.PumpFrames(2);
            }
            session.Window.MouseUp(x, y);
            session.PumpFrames(2);
            // The press and the release build; the moves size the list on screen (round 10 step 13).
            Assert.True(app.Builds - builds == 2, $"{app.Builds - builds} builds over a press, four drag moves and a release");
            Assert.True(app.DiffRowsMade == made, $"{app.DiffRowsMade - made} rows made over a split drag");
            Assert.Equal(rows, Rows(app));

            // Wrapped: measured at the width, made again when it moves.
            var body = Get<Ref<Element>>(app, "_diffBodyRef").Current!;
            body.Focus();
            session.Window.PressKey(KeyCode.W);
            session.PumpFrames(3);
            Assert.True(Get<bool>(app, "_diffWrap"));
            int wrapped = app.DiffRowsMade;
            Resize(session, app, 1200);
            Assert.True(app.DiffRowsMade - wrapped >= Rows(app).Count, $"{app.DiffRowsMade - wrapped} rows made of {Rows(app).Count} after a wrapped width change");
        });
    }

    /// <summary>The sideways offset is baked into the code cells: a width change that keeps it keeps
    /// the rows, and one that clamps it (the column widened past what is left to scroll to) makes
    /// them again at the offset the pan now has.</summary>
    [Fact]
    public void APannedDiff_KeepsItsRowsOverAWidthChange_UntilTheOffsetClamps()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            var pan = Get<HultUI.Core.Builders.HorizontalPan>(app, "_diffPan");
            Assert.True(pan.CanPan, "the fixture's long lines left nothing to scroll");
            // Twice: the first is clamped to what the pan knew before the build it sets off
            // measured the column, the second to the column as laid out.
            pan.PanTo(float.MaxValue);
            session.PumpFrames(3);
            pan.PanTo(float.MaxValue);
            session.PumpFrames(3);
            float panned = pan.Offset;
            Assert.True(panned > 0);

            // Narrower: more to scroll to, the offset stays, and so do the rows.
            int made = app.DiffRowsMade;
            var rows = Rows(app);
            Resize(session, app, 1360);
            Assert.Equal(panned, pan.Offset);
            Assert.True(app.DiffRowsMade == made, $"{app.DiffRowsMade - made} rows made at a kept offset");
            Assert.Equal(rows, Rows(app));

            // Wider than when it was panned: the offset clamps, and the rows are made at it.
            Resize(session, app, 1440);
            Assert.True(pan.Offset < panned, $"the offset {pan.Offset} did not clamp below {panned}");
            Assert.Equal(pan.MaxOffset, pan.Offset);
            Assert.True(app.DiffRowsMade - made >= Rows(app).Count, $"{app.DiffRowsMade - made} rows made of {Rows(app).Count} after the offset clamped");
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    /// <summary>A window resize, then a view build at the width it laid out to: a build that runs
    /// before the new layout reads the old one, so the next one is what sees the change.</summary>
    private static void Resize(HeadlessSession session, JunctionApp app, int width)
    {
        session.Window.SetClientSize(width, 900);
        session.PumpFrames(2);
        Invoke(app, "RerenderView");
        session.PumpFrames(2);
    }

    private static Element RootOf(HeadlessSession session) =>
        (Element)typeof(HultUI.Window).GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session.Window.Window)!;

    private static T Get<T>(JunctionApp app, string field) =>
        (T)typeof(JunctionApp).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;

    private static void Invoke(JunctionApp app, string name, params object?[] args) =>
        typeof(JunctionApp).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(app, args);

    private static void Rebuild(JunctionApp app)
    {
        for (var t = typeof(JunctionApp); t != null; t = t.BaseType)
            if (t.GetMethod("MarkDirty", BindingFlags.NonPublic | BindingFlags.Instance) is { } m)
            { m.Invoke(app, null); return; }
        throw new Xunit.Sdk.XunitException("no MarkDirty to call");
    }

    private static ScrollViewElement? ListOrNull(JunctionApp app) =>
        Get<Ref<Element>>(app, "_diffBodyRef").Current as ScrollViewElement;

    /// <summary>The realized diff rows, top to bottom (the list's first child is its spacer).</summary>
    private static List<Element> Rows(JunctionApp app) =>
        (ListOrNull(app) ?? throw new Xunit.Sdk.XunitException("the diff list is not on screen")).Children.Skip(1).ToList();

    private static List<string> Texts(Element row) =>
        Descendants(row).OfType<TextElement>().Select(t => t.Content).ToList();

    private static List<string> AllTexts(JunctionApp app) => Rows(app).SelectMany(Texts).ToList();

    private static Element RowWith(JunctionApp app, string code) =>
        Rows(app).First(r => Texts(r).Any(t => t.StartsWith(code, StringComparison.Ordinal)));

    /// <summary>Every span background the rows on screen draw.</summary>
    private static List<Color> Backgrounds(JunctionApp app) =>
        Rows(app).SelectMany(Descendants).OfType<TextElement>()
            .SelectMany(t => t.Spans ?? []).Select(s => s.Background).Where(c => c.A > 0).ToList();

    private static bool Coloured(JunctionApp app) =>
        ListOrNull(app) is { } list && list.Children.Skip(1).SelectMany(Descendants).OfType<TextElement>()
            .Any(t => t.Content.StartsWith("class Notes", StringComparison.Ordinal) && t.Spans is { Count: > 0 } s && s.Any(x => x.Color.A > 0));

    private static IEnumerable<Element> Descendants(Element element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var d in Descendants(child))
                yield return d;
    }

    private static string Notes(Func<int, string> line, string tabbed) =>
        "class Notes\n{\n" + tabbed + "\n" + string.Concat(Enumerable.Range(4, 30).Select(i => line(i) + "\n")) + "}\n";

    /// <summary>Changes (or, with <paramref name="commitView"/>, the Commit tab on the commit
    /// that makes the change), on a C# file with a line holding a tab and seven more changed
    /// word-wise, and one re-indented further down; its diff on screen and (by default)
    /// coloured.</summary>
    private static void Hosted(Action<HeadlessSession, JunctionApp> body, bool waitForColours = true, bool commitView = false)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("src/Notes.cs", Notes(i => $"    int value{i} = {i}; // alpha beta gamma {i}", "\tint tabbed = 1;"));
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "add notes");
        repo.Write("src/Notes.cs", Notes(i => i switch
        {
            <= 10 => $"    int value{i} = {i * 10}; // alpha BETA gamma {i}",
            20 => $"        int value{i} = {i}; // alpha beta gamma {i}",
            _ => $"    int value{i} = {i}; // alpha beta gamma {i}",
        }, "\tint tabbed = 2;"));
        if (commitView) repo.Git("commit", "-am", "change notes");

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

            if (commitView)
            {
                session.Window.PressKey(KeyCode.Digit2);   // History selects the newest commit
                session.PumpUntil(() => Get<string?>(app!, "_selectedCommitHash") != null, TimeSpan.FromSeconds(30));
                session.Window.PressKey(KeyCode.Digit3);   // Commit
            }
            else
                session.Window.PressKey(KeyCode.Digit4);   // Changes
            session.PumpUntil(() => ListOrNull(app!) is { } l && l.Children.Count > 1, TimeSpan.FromSeconds(30));
            if (waitForColours) session.PumpUntil(() => Coloured(app!), TimeSpan.FromSeconds(20));
            session.PumpFrames(3);

            body(session, app);
        }
        finally { app?.Dispose(); }
    }
}
