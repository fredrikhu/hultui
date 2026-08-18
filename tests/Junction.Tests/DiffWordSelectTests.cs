using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Word and shift selection inside the diff. Dragging a range has always worked; these are
/// the two shortcuts that hang off the same selection — double-click for the word under the
/// pointer, Shift for extending what is already there.
/// </summary>
[Collection("JunctionDiff")]
public class DiffWordSelectTests
{
    private const string Indent = "    ";
    private const string Line = Indent + "alpha.gamma_delta(epsilon) omega";
    private static readonly string File40 = string.Join("\n", Enumerable.Repeat(Line, 40));

    // Word boundaries are what make the gesture useful over code: whitespace alone would hand
    // back "alpha.gamma_delta(epsilon)" where the user asked for one identifier, and the
    // indentation every diff line starts with would select nothing at all.
    [Theory]
    [InlineData("gamma_delta", "gamma_delta")]   // an identifier, punctuation on both sides
    [InlineData("alpha", "alpha")]               // the token before the dot, not the whole run
    public void DoubleClickSelectsTheWordUnderThePointer(string aimAt, string expected)
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            session.PumpUntil(() => LabelWith(session, Line) is { Layout.AbsoluteBorderBox.Height: > 0 },
                TimeSpan.FromSeconds(20));
            var label = LabelWith(session, Line)!;

            int at = Line.IndexOf(aimAt, StringComparison.Ordinal) + aimAt.Length / 2;
            var box = label.Layout.AbsoluteContentBox;
            float x = box.X + HultUI.Core.Rendering.TextMeasure.MeasureWidth(Line[..at], label.ResolvedInherited);
            session.Window.DoubleClick(x, box.Y + box.Height / 2f);
            session.PumpFrame();

            var group = Group(app, "_diffGroupUnified");
            Assert.Equal(expected, group.SelectedText);
        }, File40);
    }

    // Landing in the indentation selects the indentation, the way a browser does — not
    // nothing, which is what a whitespace-delimited word gives you there.
    [Fact]
    public void DoubleClickInTheIndentationSelectsTheIndentation()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            session.PumpUntil(() => LabelWith(session, Line) is { Layout.AbsoluteBorderBox.Height: > 0 },
                TimeSpan.FromSeconds(20));
            var label = LabelWith(session, Line)!;
            var box = label.Layout.AbsoluteContentBox;
            float x = box.X + HultUI.Core.Rendering.TextMeasure.MeasureWidth("  ", label.ResolvedInherited);
            session.Window.DoubleClick(x, box.Y + box.Height / 2f);
            session.PumpFrame();

            Assert.Equal(Indent, Group(app, "_diffGroupUnified").SelectedText);
        }, File40);
    }

    [Fact]
    public void ShiftClickExtendsFromTheFirstPress()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            session.PumpUntil(() => CodeLabels(session).Count >= 4, TimeSpan.FromSeconds(20));
            var labels = CodeLabels(session);
            var first = labels[0];
            var later = labels[3];

            float x = first.Layout.AbsoluteContentBox.X + 4f;
            session.Window.Click(x, Mid(first));
            session.PumpFrame();
            session.Window.MouseDown(x, Mid(later), HultUI.Headless.MouseButton.Left,
                HultUI.Core.Events.KeyModifiers.Shift);
            session.Window.MouseUp(x, Mid(later), HultUI.Headless.MouseButton.Left,
                HultUI.Core.Events.KeyModifiers.Shift);
            session.PumpFrame();

            var group = Group(app, "_diffGroupUnified");
            Assert.True(group.HasSelection, "shift-click extended nothing");
            Assert.Contains("\n", group.SelectedText);
        });
    }

    [Fact]
    public void ShiftDownExtendsTheSelectionByALine()
    {
        DiffScrollTests.Hosted((session, app) =>
        {
            session.PumpUntil(() => CodeLabels(session).Count >= 4, TimeSpan.FromSeconds(20));
            var first = CodeLabels(session)[0];
            session.Window.Click(first.Layout.AbsoluteContentBox.X + 4f, Mid(first));
            session.PumpFrame();

            session.Window.KeyDown(HultUI.Core.Events.KeyCode.Down, HultUI.Core.Events.KeyModifiers.Shift);
            session.PumpFrame();

            var group = Group(app, "_diffGroupUnified");
            Assert.True(group.HasSelection, "shift+down selected nothing");
        });
    }

    private static float Mid(TextElement e) =>
        e.Layout.AbsoluteContentBox.Y + e.Layout.AbsoluteContentBox.Height / 2f;

    private static TextSelectionGroup Group(JunctionApp app, string field) =>
        (TextSelectionGroup)typeof(JunctionApp)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;

    private static TextElement? LabelWith(HeadlessSession session, string content) =>
        CodeLabels(session).FirstOrDefault(l => l.Content == content);

    private static List<TextElement> CodeLabels(HeadlessSession session)
    {
        var found = new List<TextElement>();
        Walk(Root(session));
        return found;

        void Walk(Element? e)
        {
            if (e is null) return;
            if (e is TextElement { Selectable: true, SelectionIndex: >= 0, Content.Length: > 3 } te
                && te.Layout.AbsoluteBorderBox.Height > 0)
                found.Add(te);
            foreach (var c in e.Children) Walk(c);
        }
    }

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window);
}
