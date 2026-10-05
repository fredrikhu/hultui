using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace PerfSuite;

/// <summary>Building blocks the scenarios share, so their trees look like an app's, not a test's.</summary>
static class Ui
{
    // The js-framework-benchmark vocabulary, so labels repeat the way list labels do
    // (about 3,600 combinations: the text caches warm up, as they would in an app).
    private static readonly string[] Adjectives =
    [
        "pretty", "large", "big", "small", "tall", "short", "long", "handsome",
        "plain", "quaint", "clean", "elegant", "easy", "angry", "crazy", "helpful",
        "mushy", "odd", "unsightly", "adorable", "important", "inexpensive",
        "cheap", "expensive", "fancy",
    ];
    private static readonly string[] Colours =
        ["red", "yellow", "blue", "green", "pink", "brown", "purple", "white", "black", "orange"];
    private static readonly string[] Nouns =
        ["table", "chair", "house", "bbq", "desk", "car", "pony", "cookie", "sandwich", "burger", "pizza", "mouse", "keyboard"];

    public static string Label(Random rng)
        => $"{Adjectives[rng.Next(Adjectives.Length)]} {Colours[rng.Next(Colours.Length)]} {Nouns[rng.Next(Nouns.Length)]}";

    /// <summary>A paragraph of <paramref name="words"/> vocabulary words — wraps at any sensible width.</summary>
    public static string Paragraph(Random rng, int words)
    {
        var parts = new string[words];
        for (int i = 0; i < words; i++)
            parts[i] = (i % 3) switch
            {
                0 => Adjectives[rng.Next(Adjectives.Length)],
                1 => Colours[rng.Next(Colours.Length)],
                _ => Nouns[rng.Next(Nouns.Length)],
            };
        return string.Join(' ', parts);
    }

    public static readonly Color Muted = Color.FromHex("#94A3B8");
    public static readonly Color Accent = Color.FromHex("#4F46E5");
    public static readonly Color Selected = Color.FromHex("#DBEAFE");
    public static readonly Color CardFill = Color.FromHex("#F8FAFC");
    public static readonly Color Line = Color.FromHex("#E2E8F0");

    /// <summary>The js-framework-benchmark row: id, label, a remove glyph.</summary>
    public static BoxElement Row(int id, string label)
        => Flex.Row(gap: 12).FlexShrink(0)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(6, 16))
            .Background(Color.White)
            .Children(
                Text.New($"{id}").FontSize(13f).TextColor(Muted).Width(Dimension.Px(64)),
                Box.New().FlexGrow(1).Children(Text.New(label).FontSize(13f)),
                Box.New().Width(Dimension.Px(24)).Children(Text.New("✕").FontSize(13f).TextColor(Color.FromHex("#EF4444"))));

    public static BoxElement Rows(Random rng, int count, int firstId = 1)
    {
        var body = Flex.Column().FlexShrink(0);
        var rows = new Element[count];
        for (int i = 0; i < count; i++) rows[i] = Row(firstId + i, Label(rng));
        body.ReplaceChildren(rows);
        return body;
    }

    /// <summary>A small card: title, two lines of text, a footer row.</summary>
    public static BoxElement Card(Random rng, int i)
        => Flex.Column(gap: 6).Width(Dimension.Px(220)).FlexShrink(0)
            .Padding(14).Background(Color.White).CornerRadius(10f)
            .BorderColor(Line).BorderWidth(1f)
            .Children(
                Text.New($"Card {i}").FontSize(15f).FontWeight(FontWeight.SemiBold),
                Text.New(Paragraph(rng, 12)).FontSize(12f).TextColor(Color.FromHex("#475569")),
                Flex.Row(gap: 8).Children(
                    Text.New(Label(rng)).FontSize(11f).TextColor(Muted).FlexGrow(1),
                    Text.New($"#{i}").FontSize(11f).TextColor(Accent)));

    /// <summary>
    /// The window width at a step of a resize drag: 40 distinct widths, 4 px apart, so a
    /// step never lands on a width any measure cache still holds (the cache keeps four).
    /// Alternating between two widths would time cache hits, not a resize.
    /// </summary>
    public static int DragWidth(int step, int from = 1040) => from - 4 * (1 + step % 40);
}
