using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

// Showcases the Tier-A text styling features:
//   text-align, italic/oblique, underline/strike-through, line-height, letter-spacing.

var textPrimary = Color.FromHex("#1E293B");
var textSecondary = Color.FromHex("#64748B");
var card = Color.FromHex("#F8FAFC");
var border = Color.FromHex("#E2E8F0");
var accent = Color.FromHex("#4F46E5");

const string Sample = "The quick brown fox jumps over the lazy dog";
const string Paragraph = "Typography is the art\nof arranging type to make\nwritten language legible.";

var root = ScrollView.New().Background(Color.White).Children(
    Flex.Column(gap: 26).Padding(32).Children(

        Text.New("Typography").FontSize(30f).FontWeight(FontWeight.Bold).TextColor(textPrimary).FlexShrink(0),

        // ── Text alignment ──
        Section("TEXT ALIGN",
            AlignedBox("Left", TextAlign.Left),
            AlignedBox("Center", TextAlign.Center),
            AlignedBox("Right", TextAlign.Right)),

        // ── Font style ──
        Section("FONT STYLE",
            Labeled("Regular", Text.New(Sample).FontSize(16f).TextColor(textPrimary)),
            Labeled("Italic", Text.New(Sample).FontSize(16f).TextColor(textPrimary).Italic()),
            Labeled("Bold italic", Text.New(Sample).FontSize(16f).TextColor(textPrimary).FontWeight(FontWeight.Bold).Italic())),

        // ── Decoration ──
        Section("DECORATION",
            Labeled("Underline", Text.New(Sample).FontSize(16f).TextColor(textPrimary).Underline()),
            Labeled("Strikethrough", Text.New(Sample).FontSize(16f).TextColor(textPrimary).Strikethrough()),
            Labeled("Both", Text.New(Sample).FontSize(16f).TextColor(textPrimary).Underline().Strikethrough())),

        // ── Line height ── (multi-line so the spacing is visible)
        Section("LINE HEIGHT",
            Flex.Row(gap: 24).Children(
                LineHeightCard("Default", 0f, textPrimary, textSecondary, card, border),
                LineHeightCard("22px", 22f, textPrimary, textSecondary, card, border),
                LineHeightCard("36px", 36f, textPrimary, textSecondary, card, border))),

        // ── Letter spacing ──
        Section("LETTER SPACING",
            Labeled("0px", Text.New("LETTER SPACING").FontSize(16f).TextColor(textPrimary)),
            Labeled("2px", Text.New("LETTER SPACING").FontSize(16f).TextColor(textPrimary).LetterSpacing(2f)),
            Labeled("6px", Text.New("LETTER SPACING").FontSize(16f).TextColor(textPrimary).LetterSpacing(6f)))
    )
);

using var app = new Application("HultUI - Typography", 760, 900);
app.SetRoot(root);
app.Run();
return;

// ──── Helpers ────

// A titled card. The heading itself uses letter-spacing for a small-caps label look.
Element Section(string title, params Element[] items)
{
    var body = Flex.Column(gap: 14).Children(items);
    // FlexShrink(0): the sections live in a ScrollView, so they must keep their
    // natural height and let the view scroll rather than being compressed to fit
    // (which would overlap the rows and clip the taller line-height cards).
    return Flex.Column(gap: 12).FlexShrink(0).Children(
        Text.New(title).FontSize(12f).FontWeight(FontWeight.SemiBold).TextColor(accent).LetterSpacing(1.5f),
        Flex.Column(gap: 14).Padding(20).Background(card).CornerRadius(12f)
            .BorderColor(border).BorderWidth(1f).Children(body)
    );
}

// A label on the left, the example filling the rest of the row.
Element Labeled(string label, Element example)
{
    example.FlexGrow(1);
    return Flex.Row(gap: 20).AlignItems(AlignItems.Center).Children(
        Text.New(label).Width(110).FlexShrink(0).FontSize(13f).TextColor(textSecondary),
        example
    );
}

// A full-width bordered box whose text is aligned within it, so the alignment shows.
Element AlignedBox(string label, TextAlign align) =>
    Labeled(label,
        Flex.Column().FlexGrow(1).Padding(new Thickness(8, 12, 8, 12))
            .Background(Color.White).CornerRadius(6f).BorderColor(border).BorderWidth(1f)
            .Children(
                Text.New(Sample).Width(Dimension.Percent(100)).FontSize(15f).TextColor(textPrimary).TextAlign(align)
            ));

static Element LineHeightCard(string label, float lineHeight, Color textPrimary, Color textSecondary, Color card, Color border)
{
    var paragraph = Text.New(Paragraph).FontSize(14f).TextColor(textPrimary);
    if (lineHeight > 0f)
        paragraph.LineHeight(lineHeight);

    return Flex.Column(gap: 8).FlexGrow(1).Padding(14).Background(Color.White).CornerRadius(8f)
        .BorderColor(border).BorderWidth(1f).Children(
            Text.New(label).FontSize(12f).FontWeight(FontWeight.SemiBold).TextColor(textSecondary),
            paragraph
        );
}
