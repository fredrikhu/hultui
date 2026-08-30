using HultUI.Core.Builders;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The drop mark on a dropdown and a date picker: a solid triangle typed from the control's
/// font, or two strokes drawn by the renderer.
///
/// <para>The triangle stays the default because it is what these controls have always drawn,
/// and changing it would repaint every dropdown in every app that never asked. The stroked
/// shape is what current design systems use — Fluent among them — and being drawn rather than
/// typed it does not depend on the UI font shipping a glyph for it.</para>
/// </summary>
public class ChevronShapeTests
{
    private static string DrawDropdown(Theme theme)
    {
        using var session = HeadlessSession.Create(300, 120,
            options: new HeadlessOptions { RecordDrawCalls = true });
        session.Application.Theme = theme;
        session.Application.SetRoot(Box.New().Padding(20f).Children(Dropdown.New(["Alpha", "Beta"], 0)));
        session.PumpFrames(2);

        session.Window.RenderLog.Clear();
        session.PumpFrame(session.DefaultFrameDelta, forceRender: true);
        return session.Window.RenderLog.ToString();
    }

    [Fact]
    public void TheDefaultIsStillTheTriangleGlyph()
    {
        var log = DrawDropdown(Theme.Light);

        Assert.Contains("▾", log);
        Assert.DoesNotContain("DrawLine", log);
    }

    [Fact]
    public void TheStrokedShapeIsDrawnRatherThanTyped()
    {
        var log = DrawDropdown(Theme.Light.With(new DropdownTheme
        {
            ChevronShape = ChevronShape.Stroke,
        }));

        Assert.DoesNotContain("▾", log);
        // Two strokes meeting at a point.
        Assert.Equal(2, log.Split('\n').Count(l => l.Contains("DrawLine")));
    }

    /// <summary>The control keeps its height either way. The mark's shape is cosmetic, and a
    /// row that changes height when a theme swaps a glyph for two lines is not.</summary>
    [Fact]
    public void TheShapeDoesNotChangeTheControlHeight()
    {
        static float HeightUnder(Theme theme)
        {
            using var session = HeadlessSession.Create(300, 120);
            session.Application.Theme = theme;
            var dropdown = Dropdown.New(["Alpha", "Beta"], 0);
            session.Application.SetRoot(Box.New().Padding(20f).Children(dropdown));
            session.PumpFrames(2);
            return dropdown.Layout.Height;
        }

        Assert.Equal(
            HeightUnder(Theme.Light),
            HeightUnder(Theme.Light.With(new DropdownTheme { ChevronShape = ChevronShape.Stroke })));
    }
}
