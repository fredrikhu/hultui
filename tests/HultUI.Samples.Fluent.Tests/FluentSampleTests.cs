using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Headless;
using Xunit;

namespace HultUI.Samples.Fluent.Tests;

/// <summary>
/// The sample is a look, so the tests are about the look: the details that make a window read
/// as WinUI rather than as a dark-grey app, and that a refactor would quietly lose. None of
/// them assert pixels — every one names the design decision it is holding in place.
/// </summary>
public class FluentSampleTests
{
    private static (HeadlessSession Session, FluentApp App, Element Root) Start()
    {
        var session = HeadlessSession.Create(1280, 860);
        var app = new FluentApp(session.Application, session.Window.Window);
        Element root = app.Mount();
        session.Application.SetRoot(root);
        session.PumpFrames(3);
        return (session, app, root);
    }

    private static void GoTo(HeadlessSession session, string page)
    {
        session.Window.FindByRole(AccessibilityRole.ListItem, page)!.Click();
        session.PumpFrames(3);
    }

    // ──────────────────────────────────────────────────────────────────────
    // The palette reaches the framework's own widgets
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Constructing the app themes the Application, not only <c>Program.cs</c>. The framework
    /// draws the text boxes, checkboxes and sliders on these pages, and it reads
    /// <c>Application.Theme</c> rather than the palette this sample paints with — so without
    /// this every one of them comes up in the stock light theme in the middle of a dark window.
    /// </summary>
    [Fact]
    public void ConstructingTheAppThemesTheApplication()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        Assert.Equal(app.Palette.Layer, session.Application.Theme.Background);
        Assert.Equal(app.Palette.Accent, session.Application.Theme.Primary);
        Assert.Equal(app.Palette.TextPrimary, session.Application.Theme.TextColor);
    }

    /// <summary>And the consequence, on a widget the sample did not draw: the gallery's text
    /// box is a framework <c>TextInputElement</c> drawing its own box, and that box must be
    /// wearing the Fluent control fill rather than the framework's own. Explicitly the *chromed*
    /// one — the nav pane's search field is chromeless so the sample can put a magnifier in it,
    /// and asserting on that would be asserting nothing.</summary>
    [Fact]
    public void AFrameworkWidgetTakesTheFluentColours()
    {
        var (session, app, root) = Start();
        using var _ = session;

        GoTo(session, "Controls gallery");
        var input = FirstOfType<TextInputElement>(root, i => i.DrawsChrome);

        Assert.NotNull(input);
        Assert.Equal(app.Palette.ControlFill, input!.BackgroundColor);
        Assert.Equal(app.Palette.ControlStroke, input.BorderColor);
    }

    /// <summary>
    /// The search box takes text, and keeps the caret while doing it.
    ///
    /// <para>Worth a test rather than a screenshot, because the magnifier costs something: the
    /// focus ring belongs to the wrapper box, an element only gets focus styles for its own
    /// focus, so focus has to be carried in app state — and that means focusing the field
    /// rebuilds the pane underneath it. If that rebuild replaced the input instead of patching
    /// it, focus would land nowhere and the typing would go missing. Asserting the same instance
    /// is the point; the text is the symptom.</para>
    /// </summary>
    [Fact]
    public void TheSearchBoxTakesTextAndKeepsItsCaretThroughTheRebuild()
    {
        var (session, app, root) = Start();
        using var _ = session;

        var input = FirstOfType<TextInputElement>(root)!;
        Assert.False(app.SearchFocused);

        session.Window.Click(input);
        session.PumpFrames(3);
        Assert.True(app.SearchFocused);

        session.Window.TypeText("display");
        session.PumpFrames(3);

        var live = FirstOfType<TextInputElement>(root)!;
        Assert.Same(input, live);
        Assert.Equal("display", live.Text);
        Assert.True(app.SearchFocused);
    }

    /// <summary>And the box around it is the sample's, not the framework's — which is what lets
    /// the magnifier sit inside it.</summary>
    [Fact]
    public void TheSearchInputDrawsNoBoxOfItsOwn()
    {
        var (session, app, root) = Start();
        using var _ = session;

        var input = FirstOfType<TextInputElement>(root)!;

        Assert.False(input.DrawsChrome);
        Assert.NotEqual(app.Palette.ControlFill, input.BackgroundColor);
        Assert.Equal(Thickness.Zero, input.Box.BorderWidth);
    }

    /// <summary>
    /// The content pane's scrollbar is the Windows 11 one: a 2px hairline with no track, that
    /// widens to a 12px bar with a track under the pointer.
    ///
    /// <para>The hairline is only defensible because the region that counts as "on the bar" is
    /// the expanded width — asserted here, because dropping it would leave a 2px target and the
    /// bar would look right in a screenshot and be unusable in the app.</para>
    /// </summary>
    [Fact]
    public void TheContentScrollbarRestsAsAHairlineAndCanStillBeHit()
    {
        var (session, app, root) = Start();
        using var _ = session;

        // The gallery overflows; Home does not, and a scroll view with nothing to scroll has no
        // bar to press.
        GoTo(session, "Controls gallery");
        var scroll = FirstOfType<ScrollViewElement>(root)!;

        Assert.Equal(2f, scroll.ScrollbarWidth);
        Assert.Equal(12f, scroll.ScrollbarExpandedWidth);
        Assert.True(scroll.ScrollbarExpandOnHover);
        Assert.Equal(Color.Transparent, scroll.ScrollbarColor);
        Assert.Equal(app.Palette.ControlFill, scroll.ScrollbarTrackHoverColor);
        Assert.Equal(app.Palette.ControlStrong, scroll.ScrollbarThumbColor);

        // Eight pixels in from the bar's own edge: nowhere near the 2px hairline, still a press
        // on the scrollbar.
        var box = scroll.ScreenContentBox;
        Assert.True(scroll.TryStartScrollbarDrag(new Point(box.Right - 8, box.Y + 40)));
    }

    // ──────────────────────────────────────────────────────────────────────
    // NavigationView
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void ANavItemSwitchesThePage()
    {
        var (session, _app, _root) = Start();
        using var s = session;

        Assert.NotNull(session.Window.FindByText("HULT-WORKSTATION"));

        GoTo(session, "Personalization");

        Assert.Null(session.Window.FindByText("HULT-WORKSTATION"));
        Assert.NotNull(session.Window.FindByText("Choose your mode"));
    }

    /// <summary>
    /// The selection indicator. WinUI marks the selected nav item with a 3x16 accent pill on
    /// its left edge and only a very faint fill behind it — a nav list that leans on the fill
    /// instead reads as some other toolkit however right the colours are. The pill is the
    /// first child of the item, and it is the assertion worth having.
    /// </summary>
    [Fact]
    public void OnlyTheSelectedNavItemHasAnAccentPill()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        GoTo(session, "Personalization");

        Assert.Equal(app.Palette.Accent, Pill(session, "Personalization").BackgroundColor);
        Assert.Equal(Color.Transparent, Pill(session, "Home").BackgroundColor);
        Assert.Equal(Color.Transparent, Pill(session, "About").BackgroundColor);
    }

    private static BoxElement Pill(HeadlessSession session, string item) =>
        (BoxElement)session.Window.FindByRole(AccessibilityRole.ListItem, item)!.Element.Children[0];

    // ──────────────────────────────────────────────────────────────────────
    // Controls
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The toggle switch flips, and its knob travels. The knob is moved by a transform rather
    /// than by relaying the row out, because a change of justification would snap where a
    /// transform can be transitioned — so the travel is worth asserting, not just the state.
    /// </summary>
    [Fact]
    public void TheToggleSwitchFlipsAndItsKnobTravels()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        GoTo(session, "Personalization");
        Assert.True(app.Transparency);
        Assert.Equal(20f, KnobOffset(session));

        session.Window.FindByRole(AccessibilityRole.Switch, "Transparency effects")!.Click();
        session.PumpFrames(40);   // past the 150ms transition, so the transform has settled

        Assert.False(app.Transparency);
        Assert.Equal(0f, KnobOffset(session));
    }

    private static float KnobOffset(HeadlessSession session)
    {
        var track = session.Window.FindByRole(AccessibilityRole.Switch, "Transparency effects")!.Element;
        return track.Children[0].Transform?.TranslateX ?? 0f;
    }

    /// <summary>
    /// Black text on a pale blue accent. In dark mode WinUI inverts the text on accent
    /// surfaces, and it is one of the most recognisable things about Windows 11 — an accent
    /// button with white text on it is the single fastest way to look like something else.
    /// </summary>
    [Fact]
    public void TheAccentButtonInvertsItsTextInDarkMode()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        GoTo(session, "Controls gallery");
        Assert.True(app.Dark);
        Assert.Equal(Color.Black, AccentButtonTextColor(session));

        app.SetDark(false);
        session.PumpFrames(3);

        Assert.Equal(Color.White, AccentButtonTextColor(session));
    }

    private static Color? AccentButtonTextColor(HeadlessSession session)
    {
        var button = session.Window.FindByRole(AccessibilityRole.Button, "Accent button")!.Element;
        return FirstOfType<TextElement>(button)!.Inherited.TextColor;
    }

    /// <summary>
    /// The two-tone control border. WinUI paints a control's 1px border as a vertical gradient
    /// that is darker along the bottom, and that one darker line is what makes a Fluent button
    /// look like it is resting on the page. A box takes a single border colour, so the button
    /// is two nested boxes — outer draws the bottom edge, inner draws the other three. Losing
    /// either half is invisible in a glance at a screenshot and obvious side by side with the
    /// real thing.
    /// </summary>
    [Fact]
    public void AStandardButtonKeepsItsTwoToneBorder()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        GoTo(session, "Controls gallery");

        var outer = (BoxElement)session.Window.FindByRole(AccessibilityRole.Button, "Standard button")!.Element;
        var inner = (BoxElement)outer.Children[0];

        Assert.Equal(new Thickness(0, 0, 1, 0), outer.Box.BorderWidth);
        Assert.Equal(app.Palette.ControlStrokeBottom, outer.BorderColor);

        Assert.Equal(new Thickness(1, 1, 0, 1), inner.Box.BorderWidth);
        Assert.Equal(app.Palette.ControlStroke, inner.BorderColor);

        // 1px in total either way — the two-tone must not have become a 2px border.
        Assert.Equal(1f, outer.Box.BorderWidth.Bottom + inner.Box.BorderWidth.Bottom);

        // And it runs the right way round. See TheBottomEdgeAlwaysRecedes.
        Assert.True(Luma(outer.BorderColor) < Luma(inner.BorderColor));
    }

    /// <summary>
    /// The bottom edge of a control is always the darker of the two, in both palettes.
    ///
    /// <para>Measured against the WinUI Gallery rather than reasoned about, because reasoning
    /// got it wrong: a dark-mode button there is fill #2D2D2D with a #353535 top and a #303030
    /// bottom — the <i>top</i> is the highlight. The sample had the bottom as the heavier edge
    /// in both modes, which is right for light and backwards for dark, and it is invisible until
    /// you put the two windows side by side.</para>
    /// </summary>
    [Fact]
    public void TheBottomEdgeAlwaysRecedes()
    {
        foreach (var dark in new[] { true, false })
        {
            var p = FluentPalette.For(dark, AccentPreset.Blue);
            Assert.True(Luma(p.ControlStrokeBottom) < Luma(p.ControlStroke),
                $"dark={dark}: bottom {p.ControlStrokeBottom} should be darker than top {p.ControlStroke}");
        }
    }

    /// <summary>The off switch, also measured: the knob is TextFillColorSecondary (78.6% white),
    /// brighter than the ControlStrongFillColorDefault ring around it, and the track carries a
    /// faint fill rather than being a hole cut in the card.</summary>
    [Fact]
    public void TheOffToggleKnobIsBrighterThanItsRing()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        GoTo(session, "Controls gallery");
        var track = (BoxElement)session.Window.FindByRole(AccessibilityRole.Switch, "Airplane mode")!.Element;
        var knob = (BoxElement)track.Children[0];

        Assert.Equal(app.Palette.TextSecondary, knob.BackgroundColor);
        Assert.Equal(app.Palette.ControlStrong, track.BorderColor);
        Assert.Equal(app.Palette.ControlAltFill, track.BackgroundColor);
        Assert.True(Luma(knob.BackgroundColor) > Luma(track.BorderColor));
    }

    /// <summary>Alpha-weighted, because half this palette is white or black at a percentage:
    /// the knob and the ring around it are both pure white and differ only in how much of it
    /// there is, and a luminance that ignores alpha calls them equal.</summary>
    private static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) * (c.A / 255.0);

    /// <summary>
    /// The expander opens, and its header gives up its bottom corners when it does. That is
    /// what makes the header and the panel below it read as one object rather than as two
    /// stacked cards — a header that keeps all four corners open looks like a bug.
    /// </summary>
    [Fact]
    public void TheExpanderJoinsItsPanelWhenOpen()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        GoTo(session, "Controls gallery");

        // The expander is the last thing on a long page. Scroll to it rather than growing the
        // window: a click aimed below the viewport hits nothing and reports nothing, so a test
        // that skipped this would pass with the header never having been pressed.
        session.Window.Wheel(800, 600, 0, -20);
        session.PumpFrames(6);

        var closed = (BoxElement)session.Window.FindByRole(AccessibilityRole.Button, "Notifications")!.Element;
        Assert.Equal(new CornerRadius(4f), closed.CornerRadius);
        Assert.Null(session.Window.FindByRole(AccessibilityRole.Switch, "Mail"));

        session.Window.FindByRole(AccessibilityRole.Button, "Notifications")!.Click();
        session.PumpFrames(3);

        var open = (BoxElement)session.Window.FindByRole(AccessibilityRole.Button, "Notifications")!.Element;
        Assert.True(app.ExpanderOpen);
        Assert.Equal(new CornerRadius(4f, 4f, 0f, 0f), open.CornerRadius);
        Assert.NotNull(session.Window.FindByRole(AccessibilityRole.Switch, "Mail"));
    }

    /// <summary>
    /// The switches inside the expander hold state, and the collapsed header counts them. The
    /// switch is controlled, so a row wired to a constant and a no-op handler draws fine and
    /// ignores every click — which is how these three shipped.
    /// </summary>
    [Fact]
    public void TheExpanderSwitchesFlipAndTheHeaderCountsThem()
    {
        var (session, app, root) = Start();
        using var _ = session;

        GoTo(session, "Controls gallery");
        session.Window.Wheel(800, 600, 0, -20);
        session.PumpFrames(6);
        session.Window.FindByRole(AccessibilityRole.Button, "Notifications")!.Click();
        session.PumpFrames(3);
        // The panel opened below the header; bring it into view before clicking in it.
        session.Window.Wheel(800, 600, 0, -20);
        session.PumpFrames(6);

        Assert.True(app.NotifyMail);
        Assert.False(app.NotifyStore);

        session.Window.FindByRole(AccessibilityRole.Switch, "Mail")!.Click();
        session.PumpFrames(3);
        session.Window.FindByRole(AccessibilityRole.Switch, "Store")!.Click();
        session.PumpFrames(3);

        Assert.False(app.NotifyMail);
        Assert.True(app.NotifyStore);

        session.Window.FindByRole(AccessibilityRole.Switch, "Calendar")!.Click();
        session.PumpFrames(3);
        session.Window.FindByRole(AccessibilityRole.Button, "Notifications")!.Click();
        session.PumpFrames(3);

        Assert.False(app.ExpanderOpen);
        Assert.NotNull(FirstOfType<TextElement>(root, t => t.Content == "1 app allowed to notify you"));
    }

    /// <summary>
    /// The composed controls are real controls: Tab reaches them, they ask for the focus
    /// visual, and Space works them.
    ///
    /// <para>They are boxes, and a box is neither focusable nor listening to the keyboard — so
    /// without this every button, switch and nav item in the sample is mouse-only, invisible to
    /// Tab and to anything driving the app through the accessibility tree. It looks identical in
    /// a screenshot either way, which is exactly why it needs a test.</para>
    /// </summary>
    [Fact]
    public void TabReachesTheComposedControlsAndSpaceWorksThem()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        GoTo(session, "Controls gallery");

        var button = session.Window.FindByRole(AccessibilityRole.Button, "Accent button")!.Element;
        Assert.True(button.IsFocusable);
        Assert.True(button.ShowsFocusVisual);

        // Tab along until it has focus, rather than assuming a position in the order.
        for (int i = 0; i < 60 && session.Window.Window.FocusedElement != button; i++)
        {
            session.Window.PressKey(KeyCode.Tab);
            session.PumpFrames(2);
        }
        Assert.Same(button, session.Window.Window.FocusedElement);

        int before = app.ClickCount;
        session.Window.PressKey(KeyCode.Space);
        session.PumpFrames(2);

        Assert.Equal(before + 1, app.ClickCount);
    }

    /// <summary>
    /// The InfoBar's severity mark is a filled disc with the symbol knocked out of it, not an
    /// outlined glyph.
    ///
    /// <para>Measured off the Gallery: a 16px disc in the severity colour with the symbol in
    /// <c>TextOnAccent</c> — black on dark, white on light. The obvious mistake is to reach for
    /// the outlined ⓘ-style glyph, which is what this sample did until the two were put side by
    /// side; the Segoe glyphs WinUI uses here (F13C–F13F) are the bare symbols with no ring of
    /// their own, which is the tell.</para>
    /// </summary>
    [Fact]
    public void TheInfoBarSeverityMarkIsAFilledDisc()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        GoTo(session, "Controls gallery");
        var bar = session.Window.FindByRole(
            AccessibilityRole.Alert, "Saved. Your changes were written to disk.")!.Element;

        if (!IconRenderer.UseSegoe)
        {
            // No icon font, no symbol to knock out — Phosphor has no bare "i". The fallback
            // keeps the outlined icon, so there is no disc to find and that is correct.
            Assert.Null(FirstOfType<BoxElement>(bar, b => b.BackgroundColor == app.Palette.Success));
            return;
        }

        var disc = FirstOfType<BoxElement>(bar, b => b.BackgroundColor == app.Palette.Success);
        Assert.NotNull(disc);
        Assert.Equal(new CornerRadius(8f), disc!.CornerRadius);

        var symbol = FirstOfType<TextElement>(disc);
        Assert.NotNull(symbol);
        Assert.Equal(app.Palette.TextOnAccent, symbol!.Inherited.TextColor);
    }

    /// <summary>
    /// The type ramp is four columns wide, not three.
    ///
    /// <para>The Gallery publishes each style as size, weight, <i>optical size</i> and
    /// <i>line height</i> — 12/16 on Small for Caption, 14/20 on Text for Body, 28/36 on Display
    /// for Title. Taking the size and the weight and letting the font's own metrics decide the
    /// rest is how a ramp ends up nearly right: it shows up the moment a description wraps, and
    /// in a page of settings cards most of them do.</para>
    /// </summary>
    [Fact]
    public void TheTypeRampCarriesItsOpticalSizesAndLineHeights()
    {
        var (session, app, _root) = Start();
        using var _ = session;
        var p = app.Palette;

        var caption = F.Caption(p, "x");
        Assert.Equal(Fluent.SmallFont, caption.Inherited.FontFamily);
        Assert.Equal(12f, caption.Inherited.FontSize);
        Assert.Equal(16f, caption.Inherited.LineHeight);

        var body = F.Body(p, "x");
        Assert.Equal(14f, body.Inherited.FontSize);
        Assert.Equal(20f, body.Inherited.LineHeight);

        var title = F.Title(p, "x");
        Assert.Equal(Fluent.DisplayFont, title.Inherited.FontFamily);
        Assert.Equal(28f, title.Inherited.FontSize);
        Assert.Equal(36f, title.Inherited.LineHeight);
    }

    /// <summary>
    /// Focus moves the text box's bottom edge and its fill, and leaves the other three sides
    /// exactly where they were.
    ///
    /// <para>Measured: a focused dark WinUI text box keeps <c>#303030</c> sides, takes a 2px
    /// accent along the bottom, and its fill goes <i>darker</i> to
    /// <c>ControlFillColorInputActive</c>. Lighting the whole ring is what this sample did, and
    /// it is what anyone reaches for when the theme offers one focus colour — so the assertion
    /// that matters is the negative one.</para>
    /// </summary>
    [Fact]
    public void FocusDoesNotLightTheTextBoxSides()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        var ti = session.Application.Theme.TextInput;
        Assert.Equal(ti.BorderColor, ti.FocusBorderColor);
        Assert.NotEqual(app.Palette.Accent, ti.FocusBorderColor);

        Assert.Equal(app.Palette.ControlStrong, ti.UnderlineColor);
        Assert.Equal(app.Palette.Accent, ti.FocusUnderlineColor);
        Assert.Equal(2f, ti.FocusUnderlineThickness);
        Assert.Equal(app.Palette.ControlInputActive, ti.FocusBackground);
    }

    /// <summary>
    /// A card's outline recedes into the page rather than standing off it.
    ///
    /// <para><c>CardStrokeColorDefault</c> is black at 10% in dark mode, so the line around a
    /// card is darker than both the card and the page behind it — measured on real Settings as
    /// page #1A2226, card #262D31, stroke #171F22. A lighter outline is the intuitive choice and
    /// makes a page of cards look like a page of buttons.</para>
    /// </summary>
    [Fact]
    public void ACardIsOutlinedByALineThatRecedes()
    {
        var (session, app, _root) = Start();
        using var _ = session;
        var p = app.Palette;

        Assert.True(Luma(p.CardStroke) < Luma(p.Card),
            $"stroke {p.CardStroke} should be darker than the card {p.Card}");
        Assert.True(Luma(p.CardStroke) < Luma(p.Layer),
            $"stroke {p.CardStroke} should be darker than the page {p.Layer}");
    }

    /// <summary>
    /// Both strokes are that 10% black, composited against the surface each one sits on.
    ///
    /// <para>Derived rather than compared to a remembered hex, because the whole point is that
    /// one token yields two colours. If someone retunes <c>Layer</c> or <c>Backdrop</c> and
    /// leaves the strokes alone, this fails and says by how much — which is better than a
    /// hairline drifting a level or two off and nobody noticing for a year.</para>
    ///
    /// <para>The alphas are measured, not recalled: on the Gallery a card border over the
    /// #272727 pane reads #232323 (35/39 = 0.897) and the pane border over the #202020 backdrop
    /// reads #1D1D1D (29/32 = 0.906), which bracket <c>#19000000</c>. Light Mica shows the
    /// wallpaper through, so the light theme was measured as ratios against its own neighbours
    /// instead: 236/251 and 232/247, both <c>#0F000000</c>.</para>
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AStrokeIsBlackCompositedAgainstWhateverIsBehindIt(bool dark)
    {
        var p = FluentPalette.For(dark, AccentPreset.Blue);
        byte alpha = dark ? (byte)0x19 : (byte)0x0F;

        Assert.Equal(BlackOver(alpha, p.Layer), p.CardStroke);
        Assert.Equal(BlackOver(alpha, p.Backdrop), p.LayerStroke);
    }

    /// <summary>Black at <paramref name="alpha"/> over an opaque surface, rounded the way a
    /// compositor rounds — truncating puts the dark pane stroke on #1C1C1C, one level under
    /// what the Gallery actually paints.</summary>
    private static Color BlackOver(byte alpha, Color surface)
    {
        float a = alpha / 255f;
        static byte Ch(byte s, float a) => (byte)MathF.Round(s * (1f - a));
        return new Color(Ch(surface.R, a), Ch(surface.G, a), Ch(surface.B, a));
    }

    /// <summary>
    /// The pane's own outline is darker than the backdrop it sits on.
    ///
    /// <para>The visible half of the rule above, and the one that was wrong: the content frame
    /// was drawn with the card's stroke, which is computed against the pane and therefore comes
    /// out <i>lighter</i> than the backdrop. A line lighter than the surface on both sides of it
    /// reads as a seam down the window rather than as an edge, and it is the first thing that
    /// separates from the Gallery in a side-by-side.</para>
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ThePaneIsOutlinedDarkerThanTheBackdrop(bool dark)
    {
        var p = FluentPalette.For(dark, AccentPreset.Blue);

        Assert.True(Luma(p.LayerStroke) < Luma(p.Backdrop),
            $"pane stroke {p.LayerStroke} should be darker than the backdrop {p.Backdrop}");
        Assert.True(Luma(p.LayerStroke) < Luma(p.CardStroke),
            $"and darker than a card's stroke {p.CardStroke}, which sits on the lighter pane");
    }

    /// <summary>And the content frame is the element that reaches for it — the palette being
    /// right is no use if the frame still asks for the card's stroke, which is what it did.</summary>
    [Fact]
    public void TheContentFrameDrawsThePaneStroke()
    {
        var (session, app, root) = Start();
        using var _ = session;

        var shell = root.Children[0];
        var frame = (BoxElement)shell.Children[1].Children[1];

        Assert.Equal(app.Palette.Layer, frame.BackgroundColor);      // the right element
        Assert.Equal(app.Palette.LayerStroke, frame.BorderColor);
    }

    /// <summary>
    /// Selecting a nav item and then pointing at it makes it <i>quieter</i>.
    ///
    /// <para><c>SubtleFillColorTertiary</c> is closer to the ground than Secondary — less white
    /// on a dark theme, less black on a light one — so WinUI's selected-and-hovered fill is
    /// darker than its hover fill, not brighter. Measured on the Gallery against a #202020
    /// ground: hover #2D2D2D, selected-hovered #292929. Reaching for a brighter fill for the
    /// second state is the intuitive move and is what this sample had.</para>
    /// </summary>
    [Fact]
    public void ASelectedNavItemGetsQuieterUnderThePointer()
    {
        var (session, app, _root) = Start();
        using var _ = session;
        var p = app.Palette;

        Assert.True(Luma(p.SubtlePressed) < Luma(p.SubtleHover),
            $"dark: selected-hovered {p.SubtlePressed} should be quieter than hover {p.SubtleHover}");

        // And in light, where "closer to the ground" means lighter rather than darker.
        var light = FluentPalette.For(false, AccentPreset.Blue);
        Assert.True(Luma(light.SubtlePressed) > Luma(light.SubtleHover),
            $"light: selected-hovered {light.SubtlePressed} should be quieter than hover {light.SubtleHover}");
    }

    /// <summary>
    /// The backdrop is one flat colour.
    ///
    /// <para>The title bar and the nav pane sit on it, and on the Gallery the whole top and left
    /// is a uniform #202020. This sample used to put a slight vertical gradient there on the
    /// theory that Mica fades; it does not, and the gradient was the most visible wrong thing in
    /// the window.</para>
    /// </summary>
    [Fact]
    public void TheBackdropIsFlat()
    {
        var (session, app, root) = Start();
        using var _ = session;

        var shell = (BoxElement)root.Children[0];
        Assert.Null(shell.BackgroundGradient);
        Assert.Equal(app.Palette.Backdrop, shell.BackgroundColor);
        Assert.Equal(Color.FromHex("#202020"), app.Palette.Backdrop);
    }

    /// <summary>
    /// The selected accent swatch is ringed, not shrunk.
    ///
    /// <para>It used to carry a 3px border in the <i>card</i> colour, and a border is drawn
    /// inside the box — so against a card background the ring was invisible and the only visible
    /// effect of selecting a swatch was its colour going from 38px to 34px. Measured off the
    /// rendered frame, which is the only place it showed: nothing about the element tree looked
    /// wrong.</para>
    ///
    /// <para>Now it is an outline, so the box does not move and the ring is the accent held off
    /// the square by a gap of the card behind it. Both halves are asserted — a ring that also
    /// resized the swatch would be half a fix.</para>
    /// </summary>
    [Fact]
    public void TheSelectedAccentSwatchIsRingedRatherThanShrunk()
    {
        var (session, app, _root) = Start();
        using var _ = session;
        GoTo(session, "Personalization");

        var selected = Swatch(session, AccentPreset.All[app.AccentIndex].Name);
        var other = Swatch(session, AccentPreset.All[(app.AccentIndex + 1) % AccentPreset.All.Length].Name);

        Assert.Equal(LayoutProbe.Painted(other).Width, LayoutProbe.Painted(selected).Width);
        Assert.Equal(other.Layout.Border, selected.Layout.Border);

        Assert.True(selected.Outline.IsVisible, "the selected swatch should carry a ring");
        Assert.False(other.Outline.IsVisible, "and an unselected one should not");

        // The accent, not the surface it sits on — the colour is what made the old ring vanish.
        Assert.NotEqual(app.Palette.Card, selected.Outline.Color);
        Assert.Equal(selected.BackgroundColor, selected.Outline.Color);
        Assert.True(selected.Outline.Offset > 0f, "and held off the square by a gap");
    }

    private static BoxElement Swatch(HeadlessSession session, string name) =>
        (BoxElement)(session.Window.FindByRole(AccessibilityRole.Button, name)
            ?? throw new InvalidOperationException($"no \"{name}\" swatch on screen")).Element;

    // ──────────────────────────────────────────────────────────────────────
    // Theming
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Picking an accent restyles the framework's widgets as well as the sample's own. The two
    /// are separate mechanisms — one is a palette this code paints from, the other is a
    /// <c>Theme</c> the framework reads — and a swatch that moves only one of them leaves a
    /// blue checkbox in a purple window.
    /// </summary>
    [Fact]
    public void AnAccentSwatchMovesBothPalettes()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        GoTo(session, "Personalization");
        session.Window.FindByRole(AccessibilityRole.Button, "Purple")!.Click();
        session.PumpFrames(3);

        Assert.Equal(AccentPreset.Purple.Dark, app.Palette.Accent);
        Assert.Equal(AccentPreset.Purple.Dark, session.Application.Theme.Primary);
        Assert.Equal(AccentPreset.Purple.Dark, session.Application.Theme.Checkbox.CheckedBackground);
        Assert.Equal(AccentPreset.Purple.Dark, session.Application.Theme.Slider.FillColor);
    }

    [Fact]
    public void SwitchingModeRepaintsEverything()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        var dark = app.Palette;
        app.SetDark(false);
        session.PumpFrames(3);
        var light = app.Palette;

        Assert.NotEqual(dark.TextPrimary, light.TextPrimary);
        Assert.Equal(light.TextPrimary, session.Application.Theme.TextColor);
        Assert.Equal(light.Layer, session.Application.Theme.Background);

        // Both accents are the same colour by name and a different colour on screen — that is
        // the whole reason AccentPreset carries two values rather than deriving one.
        Assert.Equal(AccentPreset.Blue.Dark, dark.Accent);
        Assert.Equal(AccentPreset.Blue.Light, light.Accent);
    }

    // ──────────────────────────────────────────────────────────────────────

    private static T? FirstOfType<T>(Element root, Func<T, bool>? where = null) where T : Element
    {
        if (root is T match && (where is null || where(match))) return match;
        foreach (var child in root.Children)
            if (FirstOfType(child, where) is { } found)
                return found;
        return null;
    }

    // ──────────────────────────────────────────────────────────────────────
    // The menu bar
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>The menu bar in the title bar opens from the keyboard as the first thing the
    /// window is asked, and its View menu checks what is in force.</summary>
    [Fact]
    public void AltVOpensTheViewMenu_WithDarkModeChecked()
    {
        var (session, app, _) = Start();
        using var _s = session;
        session.Window.PressKey(KeyCode.V, KeyModifiers.Alt);
        session.PumpFrames(3);

        var bar = MenuBarOf(app);
        Assert.Equal(1, bar.OpenIndex);
        var dark = Walk(session.Window.AccessibilityTree).Single(n => n.Name == "Dark mode");
        Assert.Equal(true, dark.IsToggled);

        session.Window.PressKey(KeyCode.Enter);
        session.PumpFrames(3);
        Assert.False(app.Dark);
        Assert.Equal(-1, bar.OpenIndex);
    }

    private static MenuBar MenuBarOf(FluentApp app) =>
        (MenuBar)typeof(FluentApp).GetField("_menus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(app)!;

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node) =>
        node.Children.SelectMany(Walk).Prepend(node);
}
