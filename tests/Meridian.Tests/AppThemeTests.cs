using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Headless;
using Meridian.Theme;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// The app themes the <c>Application</c> from its own constructor, not only from
/// <c>Program.cs</c>.
///
/// <para>The framework's own widgets — text inputs, text areas — read the Application theme
/// rather than the colours Meridian paints with, and re-read it on every style pass. Setting
/// it in <c>Program.cs</c> alone is correct for the shipped binary and wrong for everything
/// else that hosts the app: tests and the screenshot driver construct <c>MeridianApp</c>
/// directly, so every field came up in the framework's light default — flat white boxes in an
/// otherwise dark window. That is not a bug the app has, but it is one a screenshot shows, and
/// a frame you cannot trust is worse than no frame. Junction and Vesper already set it in both
/// places for the same reason.</para>
/// </summary>
public class AppThemeTests
{
    [Fact]
    public void ConstructingTheAppThemesTheApplication()
    {
        using var session = HeadlessSession.Create(1400, 900);
        var app = new MeridianApp(session.Application, session.Window.Window);
        try
        {
            session.Application.SetRoot(app.Mount());
            session.PumpFrames(2);

            var expected = Azimuth.ToHultTheme();
            Assert.Equal(expected.Background, session.Application.Theme.Background);
            Assert.Equal(expected.TextColor, session.Application.Theme.TextColor);
        }
        finally { app.Dispose(); }
    }

    /// <summary>And the consequence, which is the part that was actually visible: a framework
    /// widget mounted under the app takes Azimuth's colours rather than the light default.
    /// Asserted on the widget rather than on the theme object, because the theme being right
    /// and the widget still coming up white is exactly the failure this guards.</summary>
    [Fact]
    public void AFrameworkWidgetUnderTheAppIsNotTheLightDefault()
    {
        using var session = HeadlessSession.Create(1400, 900);
        var app = new MeridianApp(session.Application, session.Window.Window);
        try
        {
            var area = TextArea.New("some text");
            session.Application.SetRoot(Flex.Column(gap: 0).Children(app.Mount(), area));
            session.PumpFrames(2);

            // Azimuth leaves inputs transparent so the bordered box each view wraps them in
            // shows through. The default theme fills them with its own light background,
            // which is what the white editor in the driver's frames was.
            Assert.Equal(Color.Transparent, area.BackgroundColor);
        }
        finally { app.Dispose(); }
    }
}
