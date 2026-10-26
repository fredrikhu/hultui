using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.RichText;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A text area, a checkbox, a radio button, a switch and a rich text editor marked their layout
/// on every patch, whatever it copied: a rebuild of a pane around one laid it out with every box
/// above it up to the window (Meridian's 18,884-line response on each URL keystroke, Junction's
/// summary field beside its two checkboxes). They now leave it standing (PatchMarksOwnLayout),
/// compared against a twin themed the way the style pass themes them (PatchThemesTwin). These pin
/// both halves: a rebuild that changes nothing lays nothing out, under the default theme and under
/// one whose sizes differ from the controls' own defaults; and everything that does change what
/// they measure — a label, a style, another theme, a document in an editor as tall as its
/// document — is laid out again, through a rebuild or without one.
/// </summary>
public class ControlPatchLayoutTests
{
    private static readonly TextInputStyle Roomy = new() { Padding = new Thickness(4, 30, 4, 30) };

    /// <summary>Sizes other than every control's own defaults, so a twin left unthemed differs.</summary>
    private static readonly Theme OtherSizes = Theme.Light
        .With(new CheckboxTheme { Size = 22f, BorderWidth = 1f })
        .With(new RadioButtonTheme { Size = 21f })
        .With(new ToggleSwitchTheme { TrackWidth = 46f, TrackHeight = 24f, KnobSize = 18f })
        .With(new TextInputTheme { Padding = new Thickness(7, 11, 7, 11), BorderWidth = new Thickness(2) });

    /// <summary>A form of every control this concerns, rebuilt on demand. Each sits at the start of
    /// a row with a marker after it, so the marker's place is the control's laid-out width.</summary>
    private sealed class Screen : Component
    {
        public string CheckLabel = "Amend";
        public string SwitchLabel = "Push";
        public TextInputStyle? AreaStyle;
        public string AreaText = "first line\nsecond line";
        public RichDocument Grown = RichDocument.FromPlainText("one\ntwo");
        public readonly Ref<TextAreaElement> Area = new();
        public readonly Ref<CheckboxElement> Check = new();
        public readonly Ref<RadioButtonElement> Radio = new();
        public readonly Ref<ToggleSwitchElement> Switch = new();
        public readonly Ref<RichTextEditorElement> Editor = new();
        public readonly Ref<RichTextEditorElement> Growing = new();
        public readonly Ref<BoxElement> AfterArea = new(), AfterCheck = new(), AfterRadio = new(), AfterSwitch = new(),
            AfterEditor = new(), BelowGrowing = new();

        private static Element Row(Element control, Ref<BoxElement> marker) =>
            Flex.Row(gap: 0).AlignItems(AlignItems.FlexStart).Children(
                control, Box.New().Width(Dimension.Px(4)).Height(Dimension.Px(4)).WithRef(marker));

        public override Element Build() =>
            Flex.Column(gap: 4).AlignItems(AlignItems.FlexStart).Children(
                // No width of its own: a text area measures nothing, so it is as wide as its
                // padding and border, and the marker after it shows them.
                Row(TextArea.New(AreaText).Style(AreaStyle).Height(Dimension.Px(60)).WithRef(Area), AfterArea),
                Row(Checkbox.New(true, CheckLabel).WithRef(Check), AfterCheck),
                Row(RadioButton.New("g", true, "Hourly").WithRef(Radio), AfterRadio),
                Row(ToggleSwitch.New(true, SwitchLabel).WithRef(Switch), AfterSwitch),
                Row(RichTextEditor.New(RichDocument.FromPlainText("notes")).Width(Dimension.Px(200)).Height(Dimension.Px(80))
                    .WithRef(Editor), AfterEditor),
                RichTextEditor.New(Grown).Width(Dimension.Px(300)).AutoHeight(20f).WithRef(Growing),
                Box.New().Width(Dimension.Px(4)).Height(Dimension.Px(4)).WithRef(BelowGrowing));

        public void Rebuild() => MarkDirty();
    }

    private static (HeadlessSession Session, Screen Screen, Element Root) Mount(Theme? theme = null)
    {
        var session = HeadlessSession.Create(900, 700);
        if (theme is not null) session.Application.Theme = theme;
        var screen = new Screen();
        Element root = screen.Mount();
        session.Application.SetRoot(root);
        session.PumpFrames(3);
        return (session, screen, root);
    }

    private static List<string> LayoutDirty(Element e, List<string>? into = null)
    {
        into ??= [];
        if ((e.DirtyFlags & DirtyFlags.Layout) != 0) into.Add(e.GetType().Name);
        foreach (var c in e.Children) LayoutDirty(c, into);
        return into;
    }

    private static float X(Ref<BoxElement> marker) => marker.Current!.Layout.AbsoluteBorderBox.X;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARebuildThatChangesNothing_LaysNothingOut(bool otherSizes)
    {
        var (session, screen, root) = Mount(otherSizes ? OtherSizes : null);
        using var _ = session;
        var area = screen.Area.Current;

        screen.Rebuild();

        // Not one box marked: no layout pass at all.
        Assert.Empty(LayoutDirty(root));
        session.PumpFrames(2);
        Assert.Same(area, screen.Area.Current);
    }

    [Fact]
    public void AnotherTextFromARebuild_IsDrawn_AndLaysNothingOut()
    {
        var (session, screen, root) = Mount();
        using var _ = session;

        screen.AreaText = "a different text\nover two lines";
        screen.Rebuild();

        Assert.Empty(LayoutDirty(root));
        session.PumpFrames(2);
        Assert.Equal("a different text\nover two lines", screen.Area.Current!.Text);
    }

    [Fact]
    public void ALongerLabelFromARebuild_WidensTheCheckbox_AndTheSwitch()
    {
        var (session, screen, _) = Mount();
        using var __ = session;
        float check = X(screen.AfterCheck), toggle = X(screen.AfterSwitch);

        screen.CheckLabel = "Amend the last commit";
        screen.SwitchLabel = "Push after committing";
        screen.Rebuild();
        session.PumpFrames(2);

        Assert.True(X(screen.AfterCheck) > check + 40f, $"the checkbox kept its width: {check} → {X(screen.AfterCheck)}");
        Assert.True(X(screen.AfterSwitch) > toggle + 40f, $"the switch kept its width: {toggle} → {X(screen.AfterSwitch)}");
    }

    [Fact]
    public void AStyleOfMorePaddingFromARebuild_WidensTheTextArea()
    {
        var (session, screen, _) = Mount();
        using var __ = session;
        float before = X(screen.AfterArea);
        var ti = session.Application.Theme.TextInput;

        screen.AreaStyle = Roomy;
        screen.Rebuild();
        session.PumpFrames(2);

        Assert.Equal(before + 60f - ti.Padding.Left - ti.Padding.Right, X(screen.AfterArea), 2);
    }

    [Fact]
    public void AStyleSetOnTheLiveTextArea_WidensIt()
    {
        // No rebuild: only the style pass sees it, and the area marks its own layout.
        var (session, screen, _) = Mount();
        using var __ = session;
        float before = X(screen.AfterArea);
        var ti = session.Application.Theme.TextInput;

        screen.Area.Current!.Style = Roomy;
        session.PumpFrames(2);

        Assert.Equal(before + 60f - ti.Padding.Left - ti.Padding.Right, X(screen.AfterArea), 2);
    }

    /// <summary>Another theme's sizes, with and without a rebuild before the theme is set (a sample
    /// flipping its own light/dark flag rebuilds first).</summary>
    [Theory]
    [InlineData("area", false)]
    [InlineData("area", true)]
    [InlineData("editor", false)]
    [InlineData("editor", true)]
    [InlineData("check", false)]
    [InlineData("check", true)]
    [InlineData("radio", false)]
    [InlineData("radio", true)]
    [InlineData("switch", false)]
    [InlineData("switch", true)]
    public void AnotherThemesSize_LaysTheControlOutAgain_WithOrWithoutARebuild(string control, bool rebuildFirst)
    {
        var (session, screen, _) = Mount();
        using var __ = session;
        var light = Theme.Light;
        (Func<float> Measure, float Expected) c = control switch
        {
            // Width = padding + border: 11 + 11 + 2 + 2 against the light theme's own.
            "area" => (() => X(screen.AfterArea),
                X(screen.AfterArea) + 26f - light.TextInput.Padding.Left - light.TextInput.Padding.Right
                    - light.TextInput.BorderWidth.Left - light.TextInput.BorderWidth.Right),
            // A fixed-size editor's content box moves in by the new padding: its first block's
            // caret rectangle starts there, laid out again or not.
            "editor" => (() => screen.Editor.Current!.Layout.AbsoluteBorderBox.Width
                - screen.Editor.Current!.Layout.AbsoluteContentBox.Width, 26f),
            "check" => (() => X(screen.AfterCheck), X(screen.AfterCheck) + 22f - light.Checkbox.Size),
            "radio" => (() => X(screen.AfterRadio), X(screen.AfterRadio) + 21f - light.RadioButton.Size),
            _ => (() => X(screen.AfterSwitch), X(screen.AfterSwitch) + 46f - light.ToggleSwitch.TrackWidth),
        };

        if (rebuildFirst) screen.Rebuild();
        session.Application.Theme = OtherSizes;
        session.PumpFrames(2);

        Assert.Equal(c.Expected, c.Measure(), 2);
    }

    [Fact]
    public void ALongerDocumentFromARebuild_GrowsAnEditorAsTallAsItsDocument()
    {
        var (session, screen, _) = Mount();
        using var __ = session;
        float before = screen.BelowGrowing.Current!.Layout.AbsoluteBorderBox.Y;

        screen.Grown = RichDocument.FromPlainText("one\ntwo\nthree\nfour\nfive");
        screen.Rebuild();
        session.PumpFrames(2);

        Assert.True(screen.BelowGrowing.Current!.Layout.AbsoluteBorderBox.Y > before + 30f,
            $"the editor kept its height: the box below it at {before} → {screen.BelowGrowing.Current!.Layout.AbsoluteBorderBox.Y}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARichTextThemeWithMoreSpacing_GrowsAnEditorAsTallAsItsDocument(bool rebuildFirst)
    {
        // Only the rich text theme changes: no padding, no font metric, nothing the style pass sees.
        var (session, screen, _) = Mount();
        using var __ = session;
        float before = screen.BelowGrowing.Current!.Layout.AbsoluteBorderBox.Y;

        if (rebuildFirst) screen.Rebuild();
        session.Application.Theme = Theme.Light.With(new RichTextTheme { BlockSpacing = 40f });
        session.PumpFrames(2);

        Assert.True(screen.BelowGrowing.Current!.Layout.AbsoluteBorderBox.Y > before + 25f,
            $"the editor kept its height: the box below it at {before} → {screen.BelowGrowing.Current!.Layout.AbsoluteBorderBox.Y}");
    }

    [Fact]
    public void ARebuildThatTogglesTheSwitch_SlidesTheKnobAtTheThemesPace()
    {
        // The twin is themed before the state is copied, so the slide takes the theme's time, not
        // the element's own default.
        var (session, screen, _) = Mount(Theme.Light.With(new ToggleSwitchTheme { TransitionSeconds = 0f }));
        using var __ = session;
        var toggle = screen.Switch.Current!;
        Assert.Equal(1f, toggle.KnobPosition);

        // A zero transition snaps: the knob is at the off side straight after the patch. The
        // element's own default (0.15 s) would have started a slide from the on side.
        toggle.PatchFrom(ToggleSwitch.New(false, screen.SwitchLabel));
        Assert.Equal(0f, toggle.KnobPosition);
        session.PumpFrames(1);
    }
}
