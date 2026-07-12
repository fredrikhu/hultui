using HultUI;
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
