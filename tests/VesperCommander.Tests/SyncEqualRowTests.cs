using System.IO;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Headless;
using VesperCommander.Theme;
using Xunit;

namespace VesperCommander.Tests;

public class SyncEqualRowTests
{
    /// <summary>An Equal row in Sync reads dimmer through its colours, not through its row: the row
    /// has no <c>Opacity</c> (one on an element with children draws it offscreen, so every Equal
    /// row on screen was a layer on every Sync frame: 26 at 1240×820, 36–38 % of a GL frame, plan
    /// 17 round 9 step 13), and each of its seven texts is the colour a differing row draws at 55 %
    /// of its alpha; the selected row is not faded. The frame draws no layer, hovered or not.</summary>
    [Fact]
    public void AnEqualRowFadesItsColours_NotItsRow()
    {
        using var t = new DiffViewTests.Fixture();
        var then = System.DateTime.Now.AddHours(-2);
        // a.txt and d.txt are newer and longer on the left (a is the first row, so selected);
        // b.txt and c.txt are the same on both sides: Equal.
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt", "d.txt" })
        {
            bool equal = name is "b.txt" or "c.txt";
            t.WriteLeft(name, equal ? "same" : "left, newer");
            t.WriteRight(name, "same");
            File.SetLastWriteTime(Path.Combine(t.Right, name), then);
            if (equal) File.SetLastWriteTime(Path.Combine(t.Left, name), then);
        }
        t.Open(options: new HeadlessOptions { RecordDrawCalls = true });
        t.Invoke("OpenSync");
        t.Session.PumpUntil(() => !t.Field<bool>("_syncComparing") && t.Field<System.Collections.IList>("_syncRows").Count == 4,
            System.TimeSpan.FromSeconds(20));
        t.SetField("_syncShowEqual", true);
        t.MarkDirty();
        t.Session.PumpFrames(2);

        Element Row(string path) => Descendants(t.Root).Single(e => e is BoxElement && e.Children.Count == 7
            && e.Children[0] is TextElement { Content: var c } && c == path);
        var selected = Row("a.txt");
        var equalRow = Row("b.txt");
        var differing = Row("d.txt");

        Assert.All(Descendants(equalRow).Prepend(equalRow), e => Assert.Equal(1f, e.Opacity));
        static Color Fade(Color c) => new(c.R, c.G, c.B, (byte)System.Math.Round(c.A * 0.55));
        static Color Ink(Element cell) => (cell as TextElement ?? (TextElement)cell.Children[0]).Inherited.TextColor!.Value;
        for (int i = 0; i < 7; i++)
        {
            // The rail (column 3) draws its own colour for each kind of row: "=" in Neutral500 for Equal.
            var full = i == 3 ? Nocturne.Neutral500 : Ink(differing.Children[i]);
            Assert.Equal(Fade(full), Ink(equalRow.Children[i]));
        }
        Assert.Equal(Nocturne.Text, Ink(selected.Children[0]));
        Assert.Equal(Nocturne.Text, Ink(differing.Children[0]));

        var log = t.Session.Window.RenderLog;
        log.Clear();
        t.MarkDirty();
        t.Session.PumpFrame();
        Assert.Contains(log.Named("DrawText"), op => op.ToString().Contains("b.txt", System.StringComparison.Ordinal));
        Assert.Empty(log.Named("PushOpacity"));

        // Hovered, the same: the row has no hover tint of its own, and still no layer.
        log.Clear();
        var box = equalRow.WindowBorderBox;
        t.Session.Window.MoveMouse(box.X + box.Width / 2, box.Y + box.Height / 2);
        t.Session.PumpFrames(2);
        Assert.Equal(Fade(Nocturne.Text), Ink(Row("b.txt").Children[0]));
        Assert.Empty(log.Named("PushOpacity"));
    }

    private static IEnumerable<Element> Descendants(Element e)
    {
        foreach (var c in e.Children)
        {
            yield return c;
            foreach (var d in Descendants(c)) yield return d;
        }
    }
}
