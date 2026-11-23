using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;
using HultUI.Headless;
using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// A diff row's line numbers and sign are labels of the row itself, placed as the boxes that held
/// them placed them: each number at its column's left edge and the row's foot (the last line of a
/// wrapped row), the sign centred in its column, and a missing number (an added line's old one, a
/// removed line's new one) only a margin. Until plan 17 round 18 each was a label in a box: a row
/// was 9 elements unified and 11 split (8 and 9 wrapped); the frames are the same either way.
/// </summary>
[Collection("JunctionDiff")]
public class DiffRowCellTests
{
    /// <summary>Every code row on screen is its numbers, its sign (none for a wrapped context line),
    /// its code cell and nothing else: no box around a number or the sign, no label for a missing
    /// number.</summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    public void ARow_IsItsNumbersSignAndCode(bool commitView, bool split, bool wrap)
    {
        Hosted(commitView, split, wrap, 1400, (session, app) =>
        {
            var list = List(app);
            var items = (IReadOnlyList<object>)list.GetType().GetProperty("Items")!.GetValue(list)!;
            int checkedRows = 0, changed = 0;
            foreach (var row in Rows(app))
            {
                var code = Descendants(row).OfType<TextElement>().FirstOrDefault(t => t.Selectable && t.SelectionIndex >= 0);
                if (code is null) continue;   // a hunk header
                var model = items[code.SelectionIndex]!;
                int expected = split
                    ? 1 + Half(Prop<GitDiffLine?>(model, "Left"), left: true) + Half(Prop<GitDiffLine?>(model, "Right"), left: false)
                    : Unified(Prop<GitDiffLine>(model, "Line"));
                int actual = Descendants(row).Count();
                Assert.True(expected == actual,
                    $"row {code.SelectionIndex} ('{code.Content}') is {actual} elements, expected {expected}: "
                    + string.Join(" ", Descendants(row).Select(e => e.GetType().Name)));
                checkedRows++;
                if (split ? Prop<GitDiffLine?>(model, "Left") is not { Kind: DiffLineKind.Context } : Prop<GitDiffLine>(model, "Line").Kind != DiffLineKind.Context)
                    changed++;
            }
            Assert.True(checkedRows >= 10, $"only {checkedRows} code rows on screen");
            Assert.True(changed >= 2 && changed < checkedRows, $"{changed} changed rows of {checkedRows}: the test needs both kinds");

            int Unified(GitDiffLine line)
            {
                bool isChange = line.Kind is DiffLineKind.Addition or DiffLineKind.Deletion;
                int n = 1;
                if (!commitView) n += isChange ? 2 : 1;                       // the staging box, and its icon
                n += (line.OldLineNumber is null ? 0 : 1) + (line.NewLineNumber is null ? 0 : 1);
                n += wrap && !isChange ? 0 : 1;                               // the sign
                return n + (wrap ? 1 : 2);                                    // the code label, panned in a cell
            }

            int Half(GitDiffLine? line, bool left)
            {
                int? number = left ? line?.OldLineNumber : line?.NewLineNumber;
                return 1 + (number is null ? 0 : 1) + (wrap ? 1 : 2);
            }
        });
    }

    /// <summary>The numbers start at their columns' left edges (46 px apart unified, 44 split, the
    /// column less 8 px of padding) and sit at the row's foot, on the last line of a wrapped row; the
    /// sign is centred in its 16 px column; a missing number leaves its column empty. The same places
    /// the boxes gave them.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TheNumbersAndSign_SitWhereTheirColumnsAre(bool split, bool wrap)
    {
        Hosted(commitView: true, split, wrap, 1000, (session, app) =>
        {
            var list = List(app);
            var items = (IReadOnlyList<object>)list.GetType().GetProperty("Items")!.GetValue(list)!;
            int numbers = 0, signs = 0, tall = 0, skipped = 0;
            foreach (var row in Rows(app))
            {
                var code = Descendants(row).OfType<TextElement>().FirstOrDefault(t => t.Selectable && t.SelectionIndex >= 0);
                if (code is null) continue;
                var model = items[code.SelectionIndex]!;
                // Found by what they say, not by where they are in the tree: the places are what is pinned.
                if (split)
                {
                    foreach (var (half, line, left) in new[] { (row.Children[0], Prop<GitDiffLine?>(model, "Left"), true), (row.Children[1], Prop<GitDiffLine?>(model, "Right"), false) })
                    {
                        float x0 = half.Layout.AbsoluteContentBox.X;
                        var cell = half.Children[^1];
                        int? n = left ? line?.OldLineNumber : line?.NewLineNumber;
                        Assert.Equal(x0 + 44f, cell.Layout.AbsoluteBorderBox.X, 3);
                        var numberLabels = Numbers(half);
                        if (n is null) { Assert.Empty(numberLabels); skipped++; continue; }
                        AssertNumber(Assert.Single(numberLabels), n.Value, x0, 36f, half);
                    }
                    continue;
                }

                var l = Prop<GitDiffLine>(model, "Line");
                float rx = row.Layout.AbsoluteContentBox.X;
                var nums = Numbers(row);
                int k = 0;
                if (l.OldLineNumber is { } o) AssertNumber(nums[k++], o, rx, 38f, row); else skipped++;
                if (l.NewLineNumber is { } nn) AssertNumber(nums[k++], nn, rx + 46f, 38f, row); else skipped++;
                Assert.Equal(k, nums.Count);
                bool isChange = l.Kind is DiffLineKind.Addition or DiffLineKind.Deletion;
                var codeCell = row.Children[^1];
                var sign = Descendants(row).OfType<TextElement>().SingleOrDefault(t => !t.Selectable && t.Content is "+" or "−" or " ");
                Assert.Equal(isChange, sign is { Content: "+" or "−" });
                if (sign is null)
                {
                    Assert.True(wrap && !isChange, "only a wrapped context line draws its blank sign as a margin");
                    Assert.Equal(rx + 108f, codeCell.Layout.AbsoluteBorderBox.X, 3);
                    continue;
                }
                Assert.Equal(l.Kind switch { DiffLineKind.Addition => "+", DiffLineKind.Deletion => "−", _ => " " }, sign.Content);
                var s = sign.Layout.AbsoluteBorderBox;
                var r = row.Layout.AbsoluteBorderBox;
                Assert.Equal(rx + 92f, s.X, 3);
                Assert.Equal(s.Y - r.Y, r.Y + r.Height - (s.Y + s.Height), 3);   // centred
                Assert.True(s.Height < r.Height, $"the sign fills its row ({s.Height} of {r.Height})");
                Assert.Equal(s.X + s.Width, codeCell.Layout.AbsoluteBorderBox.X, 3);
                if (wrap) Assert.Equal(16f, s.Width, 3);
                signs++;
            }
            Assert.True(numbers >= 10 && skipped >= 2, $"{numbers} numbers and {skipped} missing ones checked");
            if (!split) Assert.True(signs >= 3, $"{signs} signs checked");
            if (wrap) Assert.True(tall >= 1, "no row wrapped onto a second line, so the foot says nothing");

            static List<TextElement> Numbers(Element e) =>
                Descendants(e).OfType<TextElement>().Where(t => !t.Selectable && t.Content.Length > 0 && t.Content.All(char.IsAsciiDigit)).ToList();

            void AssertNumber(TextElement label, int n, float x, float width, Element row)
            {
                var c = label.Layout.AbsoluteContentBox;
                var b = label.Layout.AbsoluteBorderBox;
                var r = row.Layout.AbsoluteBorderBox;
                Assert.Equal(n.ToString(), label.Content);
                Assert.Equal(x, c.X, 3);                     // the text starts at the column's left edge
                Assert.Equal(width, c.Width, 3);
                Assert.Equal(TextAlign.Start, label.ResolvedInherited.TextAlign);
                Assert.Equal(r.Y + r.Height, b.Y + b.Height, 3);   // at the row's foot
                Assert.True(b.Height < 16f, $"number {n} is {b.Height} tall: it fills its row rather than sitting at its foot");
                if (r.Height > 30f && LayoutProbe.DrawnText(Descendants(row).OfType<TextElement>().First(t => t.Selectable)).LineCount >= 2)
                    tall++;
                numbers++;
            }
        });
    }

    /// <summary>A code selection dragged over four lines copies the four lines and nothing of the
    /// numbers or signs beside them, unified and either side of a split row.</summary>
    [Theory]
    [InlineData(false, false, "alpha 1\nalpha 2\nalpha 3\nbeta 3\nalpha 4")]
    [InlineData(true, false, "alpha 1\nalpha 2\nalpha 3\nalpha 4")]
    [InlineData(true, true, "alpha 1\nalpha 2\nbeta 3\nalpha 4")]
    public void CopyingACodeSelection_CopiesTheCodeOnly(bool split, bool right, string expected)
    {
        Hosted(commitView: true, split, wrap: false, 1400, (session, app) =>
        {
            var lane = Rows(app).Select(r => Descendants(r).OfType<TextElement>().Where(t => t.Selectable && t.SelectionIndex >= 0).ToList())
                .Where(c => c.Count > 0).Select(c => c[split && right ? 1 : 0]).ToList();
            var from = lane.First(t => t.Content == "alpha 1").Layout.AbsoluteContentBox;
            var to = lane.First(t => t.Content == "alpha 4").Layout.AbsoluteContentBox;
            session.Window.Drag(new Point(from.X + 0.5f, from.Y + from.Height / 2), new Point(to.X + to.Width - 2f, to.Y + to.Height / 2), steps: 8);
            session.PumpFrames(2);
            session.Window.KeyDown(KeyCode.C, KeyModifiers.Ctrl);
            session.PumpFrames(1);
            Assert.Equal(expected, session.Window.Clipboard.GetText());
        });
    }

    /// <summary>A press on a line number begins no selection, as it did when the number was in a
    /// box; in Changes a click on a changed line's number, or on the empty column of the number it
    /// lacks, picks the line for staging as a click anywhere else on the row does.</summary>
    [Fact]
    public void ANumberCell_TakesAClickAsTheRowDoes()
    {
        Hosted(commitView: true, split: false, wrap: false, 1400, (session, app) =>
        {
            var row = RowOf(app, "alpha 2");
            var number = Descendants(row).OfType<TextElement>().First(t => !t.Selectable && t.Content == "2");
            var at = LayoutProbe.Middle(number.Layout.AbsoluteBorderBox);
            session.Window.MouseDown(at.X, at.Y);
            session.PumpFrame();
            Assert.DoesNotContain(Descendants(List(app)).OfType<TextElement>(), t => t.IsDragging);
            session.Window.MouseUp(at.X, at.Y);
            session.PumpFrame();
        });

        Hosted(commitView: false, split: false, wrap: false, 1400, (session, app) =>
        {
            var picked = Field<HashSet<GitDiffLine>>(app, "_stageSel");
            var changed = RowOf(app, "beta 3");
            var number = Descendants(changed).OfType<TextElement>().First(t => !t.Selectable && t.Content == "3");
            session.Window.Click(number);
            session.PumpFrames(2);
            Assert.Equal(1, picked.Count);

            // The added line has no old number: the click lands in that empty column, on the row.
            var added = RowOf(app, "gamma");
            var r = added.Layout.AbsoluteBorderBox;
            session.Window.Click(r.X + 20f + 23f, r.Y + r.Height / 2);
            session.PumpFrames(2);
            Assert.Equal(2, picked.Count);
        });
    }

    // ── harness ──────────────────────────────────────────────────────

    private static string File(Func<int, string> line) =>
        string.Concat(Enumerable.Range(1, 30).Select(i => line(i) + "\n"));

    /// <summary>A file of thirty lines, "alpha 3" changed to "beta 3", "gamma" added after line 5 and
    /// "delta" after line 10, line 8 removed and line 12 long enough to wrap; the change committed for the
    /// Commit view (the newest commit) or left in the working tree for Changes.</summary>
    private static void Hosted(bool commitView, bool split, bool wrap, int width, Action<HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("notes.txt", File(i => $"alpha {i}"));
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "add notes");
        repo.Write("notes.txt", string.Concat(Enumerable.Range(1, 30).Select(i => i switch
        {
            3 => "beta 3\n",
            5 => "alpha 5\ngamma\n",
            8 => "",
            10 => "alpha 10\ndelta\n",
            12 => "alpha 12 " + string.Join(" ", Enumerable.Range(0, 40).Select(w => "word" + w)) + "\n",
            _ => $"alpha {i}\n",
        })));
        if (commitView) repo.Git("commit", "-am", "change notes");

        using var session = HeadlessSession.Create(width, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
            {
                RepositoryPaths = [repo.Path],
                ActiveRepoPath = repo.Path,
                Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
                ActiveWorkspaceId = "w1",
                DiffSplit = split,
                DiffWrap = wrap,
            });
            session.Application.SetRoot(app.Mount());
            session.PumpFrames(3);
            if (commitView)
            {
                session.Window.PressKey(KeyCode.Digit2);   // History selects the newest commit
                session.PumpUntil(() => Field<string?>(app!, "_selectedCommitHash") != null, TimeSpan.FromSeconds(30));
                session.Window.PressKey(KeyCode.Digit3);   // Commit
            }
            else
                session.Window.PressKey(KeyCode.Digit4);   // Changes
            session.PumpUntil(() => ListOrNull(app!) is { } l && l.Children.Count > 1, TimeSpan.FromSeconds(30));
            session.PumpUntil(() => app!.HighlightsPending == 0, TimeSpan.FromSeconds(30));
            DiffScrollTests.Quiet(session, app);
            session.PumpFrames(2);
            body(session, app);
        }
        finally { app?.Dispose(); }
    }

    private static T Field<T>(JunctionApp app, string name) =>
        (T)typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;

    private static T Prop<T>(object model, string name) =>
        (T)model.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(model)!;

    private static ScrollViewElement? ListOrNull(JunctionApp app) =>
        Field<Ref<Element>>(app, "_diffBodyRef").Current as ScrollViewElement;

    private static ScrollViewElement List(JunctionApp app) =>
        ListOrNull(app) ?? throw new Xunit.Sdk.XunitException("the diff list is not on screen");

    /// <summary>The realized rows, top to bottom (the list's first child is its spacer).</summary>
    private static List<Element> Rows(JunctionApp app) => List(app).Children.Skip(1).ToList();

    private static Element RowOf(JunctionApp app, string code) =>
        Rows(app).First(r => Descendants(r).OfType<TextElement>().Any(t => t.Selectable && t.Content == code));

    private static IEnumerable<Element> Descendants(Element element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var d in Descendants(child))
                yield return d;
    }
}
