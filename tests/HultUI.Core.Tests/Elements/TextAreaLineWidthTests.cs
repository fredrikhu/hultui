using System.Reflection;
using System.Text;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// A TextArea works its line starts out again on every edit and every resize, and a line that
/// fits is the one width it measures of it (<see cref="IRenderContext.MeasureTextWidth"/>). It
/// keeps that width for each hard line, and carries the widths of the lines an edit did not
/// touch to the edited text — those before the edit at the same number, those after it at a
/// number shifted by the lines the edit added or took away. These hold it to measuring only
/// the lines an edit touched, and to every width it keeps being its own line's: the widths here
/// depend on each character, so a width carried to another line is a wrong number.
/// </summary>
public class TextAreaLineWidthTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    // Widths that differ from line to line, and a record of what was measured.
    private sealed class Counting : NullRenderContext, IRenderContext
    {
        public readonly List<string> Measured = [];

        public static float Width(ReadOnlySpan<char> text, ResolvedInheritedStyle style)
        {
            float w = 0;
            foreach (char c in text) w += (c % 7 + 3) * style.FontSize * 0.07f;
            return w;
        }

        public override Size MeasureText(string text, ResolvedInheritedStyle style) =>
            new(Width(text, style), style.FontSize);

        public float MeasureTextWidth(ReadOnlySpan<char> text, ResolvedInheritedStyle style)
        {
            Measured.Add(text.ToString());
            return Width(text, style);
        }

        public List<string> Take()
        {
            var taken = Measured.ToList();
            Measured.Clear();
            return taken;
        }
    }

    private (TextAreaElement Area, BoxElement Root) Mount(string text, float width = 700)
    {
        var area = TextAreaElement.New(text);
        area.Box.Width = width;
        area.Box.Height = 300;
        var root = BoxElement.New();
        root.Box.Width = 900;
        root.Box.Height = 400;
        root.AddChild(area);
        StyleResolver.Resolve(root, Theme.Light.ToResolvedStyle());
        _layout.PerformLayout(root, 900, 400);
        EventDispatcher.DispatchDirect(new FocusEvent(), area);
        return (area, root);
    }

    private static string Lines(Random rng, int count)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            if (i > 0) sb.Append('\n');
            int words = rng.Next(5);
            for (int w = 0; w < words; w++)
                sb.Append(w > 0 ? " " : "").Append((char)('a' + rng.Next(26)), rng.Next(1, 7));
        }
        return sb.ToString();
    }

    private static string[] HardLines(string text) => text.Split('\n');

    private static float[] KeptWidths(TextAreaElement area)
    {
        var widths = (float[])typeof(TextAreaElement)
            .GetField("_lineWidths", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(area)!;
        int count = (int)typeof(TextAreaElement)
            .GetField("_lineWidthCount", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(area)!;
        return widths[..count];
    }

    private static void Type(TextAreaElement area, int at, string text)
    {
        area.CursorPosition = at;
        EventDispatcher.Dispatch(new TextInputEvent { Text = text }, area);
    }

    private static void Key(TextAreaElement area, KeyCode key, KeyModifiers modifiers = KeyModifiers.None) =>
        EventDispatcher.Dispatch(new KeyDownEvent { Key = key, Modifiers = modifiers }, area);

    // Every width kept is the width of the line with that number now, and there is one per line.
    private static void AssertWidthsAreTheLinesOwn(TextAreaElement area, string when)
    {
        var lines = HardLines(area.Text);
        var widths = KeptWidths(area);
        Assert.True(lines.Length == widths.Length, $"{when}: {widths.Length} widths for {lines.Length} lines");
        for (int i = 0; i < lines.Length; i++)
        {
            if (float.IsNaN(widths[i])) continue;
            float own = Counting.Width(lines[i], area.ResolvedInherited);
            Assert.True(BitConverter.SingleToInt32Bits(own) == BitConverter.SingleToInt32Bits(widths[i]),
                $"{when}: line {i} \"{lines[i]}\" kept {widths[i]}, its own width is {own}");
        }
    }

    [Fact]
    public void AnEdit_MeasuresOnlyTheLinesItTouched()
    {
        var ctx = new Counting();
        var doc = Lines(new Random(1), 400);
        var (area, root) = Mount(doc);
        area.Render(ctx);
        // Every line with text, once: they all fit, and each is short enough to be measured whole.
        Assert.Equal(HardLines(doc).Count(l => l.Length > 0), ctx.Take().Count);

        int Start(int line) => area.Text.Split('\n').Take(line).Sum(l => l.Length + 1);

        // The first line from `from` on with two characters or more.
        int With2(int from) => Array.FindIndex(HardLines(area.Text), from, l => l.Length >= 2);

        int typed = With2(200);
        Type(area, Start(typed) + 1, "xyz");
        area.Render(ctx);
        Assert.Equal([HardLines(area.Text)[typed]], ctx.Take());

        // A line split in two, then joined again: each half, then the whole.
        int split = With2(100);
        area.CursorPosition = Start(split) + 1;
        Key(area, KeyCode.Enter);
        area.Render(ctx);
        Assert.Equal(HardLines(area.Text)[split..(split + 2)].Order(), ctx.Take().Order());
        area.CursorPosition = Start(split + 1);
        Key(area, KeyCode.Backspace);
        area.Render(ctx);
        Assert.Equal([HardLines(area.Text)[split]], ctx.Take());

        // Undo takes back the join, the split and the typing — each one line or two again.
        Key(area, KeyCode.Z, KeyModifiers.Ctrl);
        area.Render(ctx);
        Assert.InRange(ctx.Take().Count, 1, 2);
        AssertWidthsAreTheLinesOwn(area, "after an undo");

        // Narrower and wider: the widths are the same numbers, nothing is measured.
        foreach (float width in new[] { 600f, 750f })
        {
            area.Box.Width = width;
            _layout.PerformLayout(root, 900, 400);
            area.Render(ctx);
            Assert.Empty(ctx.Take());
        }

        // Another font size measures every line again.
        area.Inherited.FontSize = 15;
        StyleResolver.Resolve(root, Theme.Light.ToResolvedStyle());
        area.Render(ctx);
        Assert.Equal(HardLines(area.Text).Count(l => l.Length > 0), ctx.Take().Count);
        AssertWidthsAreTheLinesOwn(area, "after a font size change");

        // A new text that starts and ends with the old one's lines: only the lines between.
        var lines = HardLines(area.Text);
        var replaced = string.Join('\n', lines[..150].Concat(["one new line", "and another"]).Concat(lines[^150..]));
        area.Text = replaced;
        area.Render(ctx);
        Assert.Equal(["one new line", "and another"], ctx.Take());
        AssertWidthsAreTheLinesOwn(area, "after a text that shares its ends");
    }

    [Fact]
    public void AfterRandomEdits_EveryKeptWidthIsItsOwnLines_AndTheLineStartsAreAFreshAreas()
    {
        var rng = new Random(20261010);
        var ctx = new Counting();
        var (area, _) = Mount(Lines(rng, 60), width: 260);
        area.Render(ctx);
        float wrapWidth = area.Layout.AbsoluteContentBox.Width;
        int measured = 0;
        for (int edit = 0; edit < 600; edit++)
        {
            var text = area.Text;
            int at = rng.Next(text.Length + 1);
            int end = Math.Min(text.Length, at + rng.Next(0, 9));
            string inserted = rng.Next(6) switch
            {
                0 => "\n",
                1 => "\n\n",
                2 => "",
                3 => "q\nr",
                _ => new string((char)('a' + rng.Next(26)), rng.Next(1, 5)),
            };
            switch (rng.Next(4))
            {
                case 0:
                    // Typed or pasted over a selection, through the area's own edit path.
                    area.CursorPosition = end;
                    if (inserted.Length > 0) Type(area, at, inserted);
                    else if (at > 0) { area.CursorPosition = at; Key(area, KeyCode.Backspace); }
                    break;
                case 1 when edit > 0:
                    Key(area, rng.Next(2) == 0 ? KeyCode.Z : KeyCode.Y, KeyModifiers.Ctrl);
                    break;
                default:
                    // Handed a new text from code, as a viewer is.
                    area.Text = string.Concat(text.AsSpan(0, at), inserted, text.AsSpan(end));
                    break;
            }
            area.Render(ctx);
            measured += ctx.Take().Count;
            AssertWidthsAreTheLinesOwn(area, $"edit {edit}");

            var fresh = TextAreaElement.New(area.Text);
            typeof(TextAreaElement).GetField("_renderCtx", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(fresh, new Counting());
            Assert.Equal(fresh.GetLineStarts(wrapWidth, area.ResolvedInherited), area.GetLineStarts(wrapWidth, area.ResolvedInherited));
        }
        // An edit touches a line or three: far fewer than measuring all of them each time.
        Assert.True(measured < 600 * 6, $"{measured} lines measured over 600 edits");
    }
}
