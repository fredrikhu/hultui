using System.Globalization;
using System.Reflection;
using System.Text;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Headless.Recording;
using Xunit;

namespace HultUI.Headless.Tests;

// Inside a HultUI.* namespace bare `Text` binds to the HultUI.Text namespace.
using Text = HultUI.Core.Builders.Text;

/// <summary>
/// A TextArea works its line starts out again only when something that moves a break changed:
/// the text, the width, what the text is measured with (font, size, weight, slant, spacing,
/// direction), the render context that measures it, or the UI culture (which picks Han faces).
///
/// <para>Every rebuild of the area's parent patches it, and the patch hands it its text again.
/// The Text setter used to forget the line starts even when that text was the one it had, so a
/// keystroke in Meridian's URL field, which rebuilds the whole app, wrapped the open 19,000-line
/// response again: 1.4 MB and every line measured, per keystroke. A list kept is the same
/// <see cref="List{T}"/> instance; one worked out again is a new one, which is what these
/// compare.</para>
/// </summary>
public class TextAreaPatchTests
{
    [Fact]
    public void ARebuildThatHandsTheAreaItsTextAgainKeepsItsLineStarts()
    {
        using var session = HeadlessSession.Create(500, 400);
        var viewer = Mount(session);
        var area = viewer.Area.Current!;
        var starts = Starts(area);
        Assert.True(starts.Count > Lines + 20, "the document did not wrap: it is no test of wrapped line starts");

        // An equal string, not the same one: a rebuild that formats its text anew hands over a
        // copy of what the area holds.
        viewer.Body = new StringBuilder(viewer.Body).ToString();
        Assert.NotSame(viewer.Body, area.Text);
        viewer.Rebuild();
        session.PumpFrames(2);

        Assert.Same(area, viewer.Area.Current);
        Assert.Same(starts, Starts(area));
    }

    [Fact]
    public void ARebuildThatChangesTheTextWrapsItAgain()
    {
        using var session = HeadlessSession.Create(500, 400);
        var viewer = Mount(session);
        var area = viewer.Area.Current!;
        var starts = Starts(area);

        viewer.Body = "one\ntwo\nthree";
        viewer.Rebuild();
        session.PumpFrames(2);

        Assert.Same(area, viewer.Area.Current);
        Assert.Equal([0, 4, 8], Starts(area));
        Assert.NotSame(starts, Starts(area));
    }

    /// <summary>A colour is paint only: no break moves, so nothing is measured again.</summary>
    [Fact]
    public void ARebuildThatOnlyRecoloursTheTextKeepsItsLineStarts()
    {
        using var session = HeadlessSession.Create(500, 400);
        var viewer = Mount(session);
        var area = viewer.Area.Current!;
        var starts = Starts(area);

        viewer.Ink = new Color(200, 40, 40, 255);
        viewer.Rebuild();
        session.PumpFrames(2);

        Assert.Equal(viewer.Ink, area.ResolvedInherited.TextColor);
        Assert.Same(starts, Starts(area));
    }

    /// <summary>
    /// A larger font wraps the text again, and its rows are as tall as the new font's. The row
    /// height was kept until the render context changed, so a rebuild with another font size
    /// spaced the new font's rows at the old font's height.
    /// </summary>
    [Fact]
    public void ARebuildWithAnotherFontSizeWrapsAgainAndSpacesTheRowsForIt()
    {
        using var session = HeadlessSession.Create(500, 400);
        var viewer = Mount(session);
        var area = viewer.Area.Current!;
        var starts = Starts(area);
        float small = RowHeight(area);
        Assert.Equal(starts.Count * small, ContentHeight(area), 0.01f);

        viewer.Size = 24f;
        viewer.Rebuild();
        session.PumpFrames(2);

        Assert.Equal(24f, area.ResolvedInherited.FontSize);
        var wider = Starts(area);
        Assert.NotSame(starts, wider);
        Assert.True(wider.Count > starts.Count, $"{starts.Count} rows at 12 px and {wider.Count} at 24 px: it did not wrap again");
        float large = RowHeight(area);
        Assert.True(large > small * 1.5f, $"a 24 px row measures {large} against {small} at 12 px");
        Assert.Equal(wider.Count * large, ContentHeight(area), 0.01f);
    }

    /// <summary>Another render context may measure differently (another window, a recording
    /// wrapper): the line starts it did not measure are not its.</summary>
    [Fact]
    public void AnotherRenderContextWrapsAgain()
    {
        using var session = HeadlessSession.Create(500, 400);
        var viewer = Mount(session);
        var area = viewer.Area.Current!;
        var starts = Starts(area);

        var field = typeof(TextAreaElement).GetField("_renderCtx", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(area, new RecordingRenderContext((IRenderContext)field.GetValue(area)!));

        var again = Starts(area);
        Assert.NotSame(starts, again);
        Assert.Equal(starts, again);   // the wrapper measures as the context it wraps
    }

    /// <summary>The UI culture picks the face a Han character is measured in, so the breaks
    /// of a line holding one can move with it — the wrap memo is kept per culture for that.</summary>
    [Fact]
    public void AnotherUiCultureWrapsAgain()
    {
        using var session = HeadlessSession.Create(500, 400);
        var viewer = Mount(session);
        var area = viewer.Area.Current!;
        var starts = Starts(area);

        var was = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
            Assert.NotSame(starts, Starts(area));
        }
        finally
        {
            CultureInfo.CurrentUICulture = was;
        }
    }

    [Fact]
    public void SettingTheSameTextFromCodeKeepsTheLineStarts()
    {
        using var session = HeadlessSession.Create(500, 400);
        var viewer = Mount(session);
        var area = viewer.Area.Current!;
        var starts = Starts(area);

        area.Text = new StringBuilder(area.Text).ToString();
        Assert.Same(starts, Starts(area));

        area.Text += "\nmore";
        Assert.NotSame(starts, Starts(area));
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private const int Lines = 120;

    private static Viewer Mount(HeadlessSession session)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Lines; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append("line ").Append(i);
            // Every fifth line runs to three or four rows at 12 px in this width.
            if (i % 5 == 0)
                for (int w = 0; w < 30; w++) sb.Append(" word").Append(w);
        }
        var viewer = new Viewer { Body = sb.ToString() };
        session.Window.SetRoot(viewer.Mount());
        session.PumpFrames(2);
        return viewer;
    }

    // The list the area wraps, scrolls and draws by, for the width and style it has.
    private static List<int> Starts(TextAreaElement area) =>
        area.GetLineStarts(area.Layout.AbsoluteContentBox.Width, area.ResolvedInherited);

    private static float RowHeight(TextAreaElement area)
    {
        var ctx = (IRenderContext)typeof(TextAreaElement)
            .GetField("_renderCtx", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(area)!;
        return ctx.MeasureText("X", area.ResolvedInherited).Height;
    }

    // How tall the area takes its rows to be: the furthest it scrolls, plus the viewport.
    private static float ContentHeight(TextAreaElement area)
    {
        area.ScrollOffsetY = float.MaxValue;
        float height = area.ScrollOffsetY + area.Layout.AbsoluteContentBox.Height;
        area.ScrollOffsetY = 0;
        return height;
    }

    /// <summary>A real component, because only reconciliation runs <c>PatchFrom</c>.</summary>
    private sealed class Viewer : Component
    {
        public string Body = "";
        public float Size = 12f;
        public Color Ink = new(30, 30, 30, 255);
        public Ref<TextAreaElement> Area { get; } = new();

        public override Element Build()
        {
            var area = TextArea.New(Body).ReadOnly().FontFamily(FontFamilies.Monospace).FontSize(Size)
                .TextColor(Ink).FlexGrow(1);
            return Flex.Column(gap: 0).FlexGrow(1).Children(Text.New("header").FontSize(12f), area.WithRef(Area));
        }

        public void Rebuild() => MarkDirty();
    }
}
