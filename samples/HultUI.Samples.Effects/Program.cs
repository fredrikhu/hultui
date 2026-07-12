using HultUI;
using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

// Showcases the Tier B visual primitives: box-shadow and gradient fills.

var textSecondary = Color.FromHex("#64748B");
var card = Color.FromHex("#F8FAFC");
var border = Color.FromHex("#E2E8F0");
var accent = Color.FromHex("#4F46E5");

var root = ScrollView.New().Background(Color.FromHex("#EEF2F7")).Children(
    Flex.Column(gap: 26).Padding(32).Children(

        Text.New("Visual effects").FontSize(30f).FontWeight(FontWeight.Bold)
            .TextColor(Color.FromHex("#1E293B")).FlexShrink(0),

        // ── Box shadow ── elevation ramp + a colored shadow
        Section("BOX SHADOW",
            Flex.Row(gap: 28).FlexShrink(0).Children(
                ShadowCard("Subtle", new BoxShadow(0, 1, 4, new Color(15, 23, 42, 40)), textSecondary),
                ShadowCard("Medium", new BoxShadow(0, 4, 14, new Color(15, 23, 42, 55)), textSecondary),
                ShadowCard("Large", new BoxShadow(0, 12, 32, new Color(15, 23, 42, 65)), textSecondary),
                ShadowCard("Colored", new BoxShadow(0, 8, 24, new Color(79, 70, 229, 130)), textSecondary))),

        // ── Gradient ── linear at several angles + radial
        Section("GRADIENT",
            Flex.Row(gap: 28).FlexShrink(0).Children(
                GradientCard("Vertical", Gradient.Vertical(Color.FromHex("#818CF8"), Color.FromHex("#4338CA"))),
                GradientCard("Horizontal", Gradient.Horizontal(Color.FromHex("#FBBF24"), Color.FromHex("#EF4444"))),
                GradientCard("45°", Gradient.Linear(45f, Color.FromHex("#06B6D4"), Color.FromHex("#2563EB"))),
                GradientCard("Radial", Gradient.Radial(Color.FromHex("#FDE68A"), Color.FromHex("#B45309"))))),

        // ── Border ── single-side widths + dashed/dotted styles
        Section("BORDER",
            Flex.Row(gap: 28).FlexShrink(0).Children(
                BorderCard("Bottom", new Thickness(0, 0, 3, 0), accent, BorderStyle.Solid, 0f, textSecondary),
                BorderCard("Left accent", new Thickness(0, 0, 0, 4), Color.FromHex("#EF4444"), BorderStyle.Solid, 0f, textSecondary),
                BorderCard("Dashed", new Thickness(2), Color.FromHex("#94A3B8"), BorderStyle.Dashed, 10f, textSecondary),
                BorderCard("Dotted", new Thickness(2), Color.FromHex("#94A3B8"), BorderStyle.Dotted, 10f, textSecondary))),

        // ── Transform ── rotate / scale about the element center
        Section("TRANSFORM",
            Flex.Row(gap: 34).FlexShrink(0).AlignItems(AlignItems.Center).Children(
                TransformCard("Rotate -8°", Transform2D.Rotate(-8f)),
                TransformCard("Rotate 8°", Transform2D.Rotate(8f)),
                TransformCard("Scale 1.15", Transform2D.Scale(1.15f)),
                TransformCard("Both", new Transform2D(-6f, 1.1f, 1.1f, 0f, 0f)))),

        // ── Interaction ── reactive hover/press with declarative transitions
        Section("INTERACTION  (hover and press these)",
            Flex.Row(gap: 22).FlexShrink(0).AlignItems(AlignItems.Center).Children(
                // Hover fades the background.
                Btn("Hover fade")
                    .Background(Color.FromHex("#4F46E5"))
                    .OnHover(b => b.Background(Color.FromHex("#4338CA")))
                    .Transition(TransitionProperty.Background, 160f),
                // Press springs the scale down and back on release.
                Btn("Press me")
                    .Background(Color.FromHex("#0EA5E9"))
                    .OnHover(b => b.Background(Color.FromHex("#0284C7")))
                    .OnPressed(b => b.Scale(0.93f))
                    .Transition(TransitionProperty.Background, 140f)
                    .Transition(TransitionProperty.Transform, 340f, Easing.Spring()),
                // Hover lifts the button and shifts its color.
                Btn("Lift")
                    .Background(Color.FromHex("#8B5CF6"))
                    .OnHover(b => b.TranslateBy(0f, -4f).Background(Color.FromHex("#7C3AED")))
                    .Transition(TransitionProperty.Transform, 200f)
                    .Transition(TransitionProperty.Background, 160f))),

        // ── Combined ── gradient + rounded + colored shadow, the "modern card"
        Section("COMPOSITED",
            Flex.Row(gap: 28).FlexShrink(0).Children(
                Flex.Column(gap: 8).Width(320).FlexShrink(0).Padding(22)
                    .Background(Gradient.Linear(135f, Color.FromHex("#6366F1"), Color.FromHex("#8B5CF6")))
                    .CornerRadius(18f)
                    .BoxShadow(0, 14, 34, 2f, new Color(99, 102, 241, 110))
                    .Children(
                        Text.New("Elevated card").FontSize(19f).FontWeight(FontWeight.Bold).TextColor(Color.White),
                        Text.New("Gradient fill, rounded corners and a soft colored shadow — all composited by the renderer in one pass.")
                            .FontSize(13f).TextColor(new Color(255, 255, 255, 220)).LineHeight(20f))))
    )
);

using var app = new Application("HultUI - Visual Effects", 820, 720);
app.SetRoot(root);
app.Run();
return;

// ──── Helpers ────

Element Section(string title, params Element[] items)
{
    var body = Flex.Column(gap: 14).Children(items);
    return Flex.Column(gap: 12).FlexShrink(0).Children(
        Text.New(title).FontSize(12f).FontWeight(FontWeight.SemiBold).TextColor(accent).LetterSpacing(1.5f),
        Flex.Column(gap: 14).Padding(26).Background(card).CornerRadius(12f)
            .BorderColor(border).BorderWidth(1f).Children(body)
    );
}

static Element ShadowCard(string label, BoxShadow shadow, Color labelColor) =>
    Flex.Column().Width(150).Height(92).FlexShrink(0)
        .Background(Color.White).CornerRadius(12f).BoxShadow(shadow)
        .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
        .Children(Text.New(label).FontSize(14f).FontWeight(FontWeight.SemiBold).TextColor(labelColor));

static Element GradientCard(string label, Gradient gradient) =>
    Flex.Column().Width(150).Height(92).FlexShrink(0)
        .Background(gradient).CornerRadius(12f)
        .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
        .Children(Text.New(label).FontSize(14f).FontWeight(FontWeight.Bold).TextColor(Color.White));

static BoxElement Btn(string label) =>
    Flex.Column().Width(150).Height(56).FlexShrink(0)
        .CornerRadius(10f).BoxShadow(0, 4, 12, new Color(15, 23, 42, 55))
        .Cursor(CursorType.Pointer)
        .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
        .Children(Text.New(label).FontSize(14f).FontWeight(FontWeight.Bold).TextColor(Color.White));

static Element TransformCard(string label, Transform2D transform) =>
    Flex.Column().Width(150).Height(92).FlexShrink(0)
        .Background(Gradient.Linear(135f, Color.FromHex("#6366F1"), Color.FromHex("#8B5CF6")))
        .CornerRadius(12f).BoxShadow(0, 6, 18, new Color(15, 23, 42, 60))
        .Transform(transform)
        .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
        .Children(Text.New(label).FontSize(13f).FontWeight(FontWeight.Bold).TextColor(Color.White));

static Element BorderCard(string label, Thickness widths, Color color, BorderStyle style, float radius, Color labelColor) =>
    Flex.Column().Width(150).Height(92).FlexShrink(0)
        .Background(Color.White).CornerRadius(radius)
        .Border(widths, color, style)
        .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
        .Children(Text.New(label).FontSize(13f).FontWeight(FontWeight.SemiBold).TextColor(labelColor));
