using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Styling;
using HultUI.Headless;

namespace PerfSuite;

static partial class Scenarios
{
    // A second window opened beside a busy first one, as an inspector, a settings window or a
    // second WinForms control is. A step times the open: the platform window, its surface and
    // the tree under "build", the new window's first frame under style, layout and render.
    // Closing it is the untimed reset.
    private static IEnumerable<Scenario> WindowScenarios()
    {
        yield return new Scenario("window/open a second window",
            "Open a 640x480 window of 40 labels, a heading and two buttons beside a 1,000-row window, and render it",
            ctx =>
            {
                ctx.Mount(Busy(ctx, Text.New("Main window").FontSize(13f)));
                HeadlessWindow? second = null;
                return new Op
                {
                    Reset = _ => second?.Window.Close(),
                    Run = _ =>
                    {
                        second = ctx.Session.CreateWindow("Second window", 640, 480);
                        second.SetRoot(SecondWindow(ctx.Rng));
                    },
                    TimedWindow = () => second!.Window,
                    Check = _ => second is { Window.IsOpen: true } && ctx.Session.Application.Windows.Count == 2
                        ? null
                        : $"{ctx.Session.Application.Windows.Count} windows open, not the first and the new one",
                };
            });
    }

    private static Element SecondWindow(Random rng)
    {
        var labels = new Element[40];
        for (int i = 0; i < labels.Length; i++)
            labels[i] = Text.New(Ui.Label(rng)).FontSize(13f);
        return Flex.Column(gap: 6).Padding(16).Children(
            Text.New("Settings").FontSize(18f).FontWeight(FontWeight.SemiBold),
            ScrollView.New().FlexGrow(1).Children(Flex.Column(gap: 4).Children(labels)),
            Flex.Row(gap: 8).Children(Button.New("OK"), Button.New("Cancel", ButtonVariant.Secondary)));
    }
}
